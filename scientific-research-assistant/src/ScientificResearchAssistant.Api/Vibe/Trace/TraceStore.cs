using System.Text;
using Google.Protobuf;
using Google.Protobuf.Reflection;
using ScientificResearchAssistant.Api.Workspace;
using ScientificResearchAssistant.Contracts.Collab;

namespace ScientificResearchAssistant.Api.Vibe.Trace;

// ============================================================
//  TraceStore (Derivation Trace, File-SSoT)
//
//  Files:
//  - artifacts/trace/trace.jsonl      (append-only proto-json lines)
//  - runs/{runId}/summary.md         (human readable, bounded)
//
//  Design:
//  - Keep writes atomic where possible (append best-effort).
//  - Reads are bounded (latest N) and do NOT scan whole history.
// ============================================================

public sealed class TraceStore
{
    private const int MaxSummariesPerSnapshot = 60;
    private const int MaxMarkdownChars = 30_000;
    private const int TailReadBytes = 512 * 1024; // 512KB tail scan for latest N

    private static readonly TypeRegistry Registry = TypeRegistry.FromFiles(SraCollabReflection.Descriptor);
    private static readonly JsonFormatter Formatter =
        new(new JsonFormatter.Settings(formatDefaultValues: true, typeRegistry: Registry));
    private static readonly JsonParser Parser =
        new(JsonParser.Settings.Default.WithIgnoreUnknownFields(true).WithTypeRegistry(Registry));

    // IMPORTANT:
    // Use UTF-8 without BOM for JSONL. A BOM at the start of the first line can break protobuf JsonParser.
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private readonly WorkspaceService _workspace;
    private readonly ILogger<TraceStore> _logger;

    public TraceStore(WorkspaceService workspace, ILogger<TraceStore> logger)
    {
        _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public string GetTracePath(string sessionId)
    {
        var ws = _workspace.EnsureSessionWorkspace(sessionId);
        var dir = Path.Combine(ws.ArtifactsDir, "trace");
        return Path.Combine(dir, "trace.jsonl");
    }

    public async Task AppendAsync(string sessionId, SraRoundSummary summary, string? summaryMarkdown, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(summary);
        ct.ThrowIfCancellationRequested();

        var ws = _workspace.EnsureSessionWorkspace(sessionId);
        Directory.CreateDirectory(Path.Combine(ws.ArtifactsDir, "trace"));

        // 1) Append JSONL (best-effort)
        try
        {
            var path = GetTracePath(ws.SessionId);
            var line = Formatter.Format(summary).Replace("\r", "").Trim();
            if (line.Length > 0)
            {
                await File.AppendAllTextAsync(path, line + "\n", Utf8NoBom, ct);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to append trace jsonl (best-effort).");
        }

        // 2) Write human summary.md (bounded, best-effort)
        try
        {
            var runId = (summary.RunId ?? string.Empty).Trim();
            if (runId.Length == 0)
                return;

            var dir = Path.Combine(ws.RunsDir, runId);
            Directory.CreateDirectory(dir);

            var md = (summaryMarkdown ?? string.Empty).Replace("\r", "").Trim();
            if (md.Length > MaxMarkdownChars)
                md = md[..MaxMarkdownChars];

            var path = Path.Combine(dir, "summary.md");
            await WriteFileAtomicAsync(ws, path, md + "\n", ct);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to write summary.md (best-effort).");
        }
    }

    public async Task<List<SraRoundSummary>> LoadLatestAsync(string sessionId, int max, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        max = Math.Clamp(max, 0, MaxSummariesPerSnapshot);
        if (max == 0)
            return [];

        var ws = _workspace.EnsureSessionWorkspace(sessionId);
        var path = GetTracePath(ws.SessionId);
        if (!File.Exists(path))
            return [];

        try
        {
            // Read tail bytes only to avoid scanning huge files.
            await using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var len = fs.Length;
            var read = (int)Math.Min(TailReadBytes, len);
            fs.Seek(-read, SeekOrigin.End);

            var buf = new byte[read];
            var n = await fs.ReadAsync(buf.AsMemory(0, read), ct);
            var text = Encoding.UTF8.GetString(buf, 0, n).Replace("\r", "");

            // Split into lines; parse from end until max.
            var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            var list = new List<SraRoundSummary>(capacity: Math.Min(max, lines.Length));

            for (var i = lines.Length - 1; i >= 0 && list.Count < max; i--)
            {
                ct.ThrowIfCancellationRequested();
                var line = lines[i].Trim();
                if (line.Length == 0) continue;

                // Strip BOM if present (legacy files may have been written with UTF8 BOM).
                if (line.Length > 0 && line[0] == '\uFEFF')
                    line = line.TrimStart('\uFEFF');

                try
                {
                    var msg = Parser.Parse<SraRoundSummary>(line);
                    // Best-effort: keep only summaries for this session.
                    if (!string.Equals(msg.SessionId, ws.SessionId, StringComparison.Ordinal))
                        continue;
                    list.Add(msg);
                }
                catch
                {
                    // Ignore unparsable lines in tail (best-effort)
                }
            }

            list.Reverse(); // chronological order
            return list;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to load latest trace summaries (best-effort).");
            return [];
        }
    }

    private static async Task WriteFileAtomicAsync(WorkspacePaths ws, string targetPath, string content, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        Directory.CreateDirectory(ws.TmpDir);

        var tmp = Path.Combine(ws.TmpDir, $"{Guid.NewGuid():N}.tmp");
        await File.WriteAllTextAsync(tmp, content ?? string.Empty, Utf8NoBom, ct);
        File.Move(tmp, targetPath, overwrite: true);
    }
}


