using System.Text;
using System.Text.RegularExpressions;
using Google.Protobuf;
using Google.Protobuf.Reflection;
using Google.Protobuf.WellKnownTypes;
using VibeResearching.Contracts.Collab;

namespace Aevatar.Agents.Cognitive.Researching.Workspace;

// ============================================================
//  FileMailboxService
//
//  Implements "file-only agent communication" as a durable queue:
//    mailbox/{agent}/in -> processing -> archive
//    failures -> mailbox/_dead
//
//  Key properties:
//  - Atomic send (tmp -> rename/move)
//  - Single-consumer processing lock (in -> processing move)
//  - Idempotency via archive
// ============================================================
public sealed class FileMailboxService
{
    private static readonly Regex SafeName = new(@"^[a-zA-Z0-9_-]{2,64}$", RegexOptions.Compiled);

    // Protobuf JSON formatting/parsing with Any support.
    private static readonly TypeRegistry MailboxTypeRegistry = TypeRegistry.FromFiles(
        WrappersReflection.Descriptor,
        SraCollabReflection.Descriptor);

    private static readonly JsonFormatter Formatter = new(
        JsonFormatter.Settings.Default
            .WithFormatDefaultValues(true)
            .WithPreserveProtoFieldNames(true)
            .WithTypeRegistry(MailboxTypeRegistry));

    private static readonly JsonParser Parser = new(
        JsonParser.Settings.Default
            .WithIgnoreUnknownFields(true)
            .WithTypeRegistry(MailboxTypeRegistry));

    private readonly WorkspaceService _workspace;
    private readonly ILogger<FileMailboxService> _logger;

    public FileMailboxService(WorkspaceService workspace, ILogger<FileMailboxService> logger)
    {
        _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<string> SendAsync(string sessionId, string toAgent, SraMailboxMessage message, CancellationToken ct)
    {
        var ws = _workspace.EnsureSessionWorkspace(sessionId);
        return await SendAsync(ws, toAgent, message, ct);
    }

    public async Task<string> SendAsync(WorkspacePaths ws, string toAgent, SraMailboxMessage message, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(ws);
        ArgumentNullException.ThrowIfNull(message);
        ct.ThrowIfCancellationRequested();

        toAgent = NormalizeName(toAgent, nameof(toAgent));
        EnsureAgentMailboxDirs(ws, toAgent);

        if (string.IsNullOrWhiteSpace(message.SessionId))
            message.SessionId = ws.SessionId;

        if (string.IsNullOrWhiteSpace(message.MessageId))
            throw new ArgumentException("message_id is required", nameof(message));

        var from = string.IsNullOrWhiteSpace(message.FromAgent) ? "unknown" : NormalizeName(message.FromAgent, "from_agent");
        message.FromAgent = from;

        if (string.IsNullOrWhiteSpace(message.ToAgent))
        {
            message.ToAgent = toAgent;
        }
        else
        {
            var to = NormalizeName(message.ToAgent, "to_agent");
            if (!string.Equals(to, toAgent, StringComparison.Ordinal))
                throw new ArgumentException("to_agent does not match toAgent parameter", nameof(message));
            message.ToAgent = to;
        }

        if (message.CreatedAt == null)
            message.CreatedAt = Timestamp.FromDateTime(DateTime.UtcNow);

        // Stable-ish filename for deterministic ordering.
        var stamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd'T'HHmmss'Z'");
        var fileName = $"{stamp}_{SanitizeFileToken(message.MessageId)}_{message.FromAgent}->{message.ToAgent}.json";

        var inDir = Path.Combine(ws.MailboxDir, toAgent, "in");
        var target = Path.Combine(inDir, fileName);

        // Atomic write: write to session tmp dir then move into inbox.
        Directory.CreateDirectory(ws.TmpDir);
        var tmp = Path.Combine(ws.TmpDir, $"{Guid.NewGuid():N}.tmp");

        var json = FormatSafe(message);
        await File.WriteAllTextAsync(tmp, json, Encoding.UTF8, ct);

        // Move is atomic on same volume.
        File.Move(tmp, target);

        return target;
    }

    public async Task<MailboxDequeue?> TryDequeueAsync(string sessionId, string agentName, CancellationToken ct)
    {
        var ws = _workspace.EnsureSessionWorkspace(sessionId);
        return await TryDequeueAsync(ws, agentName, ct);
    }

    public async Task<MailboxDequeue?> TryDequeueAsync(WorkspacePaths ws, string agentName, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(ws);
        ct.ThrowIfCancellationRequested();

        agentName = NormalizeName(agentName, nameof(agentName));
        EnsureAgentMailboxDirs(ws, agentName);

        var inDir = Path.Combine(ws.MailboxDir, agentName, "in");
        var processingDir = Path.Combine(ws.MailboxDir, agentName, "processing");

        if (!Directory.Exists(inDir))
            return null;

        // Deterministic order: file name ascending (timestamp prefix).
        foreach (var file in Directory.EnumerateFiles(inDir, "*.json", SearchOption.TopDirectoryOnly)
                     .OrderBy(p => p, StringComparer.Ordinal))
        {
            ct.ThrowIfCancellationRequested();

            var fileName = Path.GetFileName(file);
            var processingPath = Path.Combine(processingDir, fileName);

            try
            {
                // Lock: in -> processing
                File.Move(file, processingPath);
            }
            catch
            {
                // Someone else took it, or transient fs issue. Continue.
                continue;
            }

            try
            {
                var json = await File.ReadAllTextAsync(processingPath, Encoding.UTF8, ct);
                var msg = Parser.Parse<SraMailboxMessage>(json);
                return new MailboxDequeue
                {
                    SessionId = ws.SessionId,
                    AgentName = agentName,
                    ProcessingPath = processingPath,
                    Message = msg
                };
            }
            catch (Exception ex)
            {
                await DeadLetterAsync(ws, agentName, processingPath, $"parse failed: {ex.Message}", ct);
                continue;
            }
        }

        return null;
    }

    public Task AckAsync(MailboxDequeue dequeue, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(dequeue);
        return AckAsync(dequeue.SessionId, dequeue.AgentName, dequeue.ProcessingPath, ct);
    }

    public Task AckAsync(string sessionId, string agentName, string processingPath, CancellationToken ct)
    {
        var ws = _workspace.EnsureSessionWorkspace(sessionId);
        return AckAsync(ws, agentName, processingPath, ct);
    }

    public Task AckAsync(WorkspacePaths ws, string agentName, string processingPath, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(ws);
        ct.ThrowIfCancellationRequested();

        agentName = NormalizeName(agentName, nameof(agentName));
        EnsureAgentMailboxDirs(ws, agentName);

        var archiveDir = Path.Combine(ws.MailboxDir, agentName, "archive");
        Directory.CreateDirectory(archiveDir);

        return MoveFileSafeAsync(processingPath, archiveDir, ct);
    }

    public Task DeadLetterAsync(MailboxDequeue dequeue, string error, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(dequeue);
        return DeadLetterAsync(dequeue.SessionId, dequeue.AgentName, dequeue.ProcessingPath, error, ct);
    }

    public Task DeadLetterAsync(string sessionId, string agentName, string processingPath, string error, CancellationToken ct)
    {
        var ws = _workspace.EnsureSessionWorkspace(sessionId);
        return DeadLetterAsync(ws, agentName, processingPath, error, ct);
    }

    public async Task DeadLetterAsync(WorkspacePaths ws, string agentName, string processingPath, string error, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(ws);
        ct.ThrowIfCancellationRequested();

        agentName = NormalizeName(agentName, nameof(agentName));
        EnsureAgentMailboxDirs(ws, agentName);

        var deadDir = ws.MailboxDeadDir;
        Directory.CreateDirectory(deadDir);

        try
        {
            var name = Path.GetFileName(processingPath);
            var target = Path.Combine(deadDir, $"{agentName}__{name}");

            // Attach an error sidecar (best-effort).
            var errorPath = target + ".error.txt";
            await File.WriteAllTextAsync(errorPath, error ?? "unknown error", Encoding.UTF8, ct);

            File.Move(processingPath, target, overwrite: true);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to dead-letter mailbox message (best-effort).");
        }
    }

    private static void EnsureAgentMailboxDirs(WorkspacePaths ws, string agentName)
    {
        var baseDir = Path.Combine(ws.MailboxDir, agentName);
        Directory.CreateDirectory(Path.Combine(baseDir, "in"));
        Directory.CreateDirectory(Path.Combine(baseDir, "processing"));
        Directory.CreateDirectory(Path.Combine(baseDir, "archive"));
    }

    private static string NormalizeName(string name, string arg)
    {
        name = (name ?? string.Empty).Trim();
        if (!SafeName.IsMatch(name))
            throw new ArgumentException("invalid agent name format", arg);
        return name;
    }

    private static string SanitizeFileToken(string token)
    {
        token = (token ?? string.Empty).Replace(" ", "").Trim();
        if (token.Length == 0)
            return "msg";
        return Regex.Replace(token, @"[^a-zA-Z0-9_-]+", "_");
    }

    private static string FormatSafe(IMessage message)
    {
        try
        {
            return Formatter.Format(message);
        }
        catch
        {
            return "{}";
        }
    }

    private static async Task MoveFileSafeAsync(string path, string targetDir, CancellationToken ct)
    {
        try
        {
            Directory.CreateDirectory(targetDir);
            var name = Path.GetFileName(path);
            var dest = Path.Combine(targetDir, name);
            File.Move(path, dest, overwrite: true);
        }
        catch
        {
            // best-effort only
            await Task.CompletedTask;
        }
    }
}

public sealed record MailboxDequeue
{
    public required string SessionId { get; init; }
    public required string AgentName { get; init; }
    public required string ProcessingPath { get; init; }
    public required SraMailboxMessage Message { get; init; }
}
