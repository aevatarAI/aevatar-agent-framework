using System.Text;
using Google.Protobuf;
using Google.Protobuf.Reflection;
using Google.Protobuf.WellKnownTypes;
using Aevatar.Agents.Cognitive.Researching.Workspace;
using VibeResearching.Contracts.Collab;

namespace Aevatar.Agents.Cognitive.Researching.Paper;

// ============================================================
//  PaperService (Markdown) - Single Writer
//
//  - paper/* are the canonical manuscript files (File-SSoT).
//  - Non-writer agents MUST NOT edit paper files directly.
//    They send patch proposals via mailbox to "paper_editor".
//  - The writer applies patches deterministically and logs the change.
// ============================================================
public sealed class PaperService
{
    private const string PaperEditorAgentName = "paper_editor";
    private const int MaxPatchChars = 50_000; // bounded, MVP

    private static readonly TypeRegistry PaperTypeRegistry = TypeRegistry.FromFiles(SraCollabReflection.Descriptor);
    private static readonly JsonFormatter PaperJsonFormatter =
        new(new JsonFormatter.Settings(formatDefaultValues: true, typeRegistry: PaperTypeRegistry));

    private readonly WorkspaceService _workspace;
    private readonly FileMailboxService _mailbox;
    private readonly ILogger<PaperService> _logger;

    public PaperService(WorkspaceService workspace, FileMailboxService mailbox, ILogger<PaperService> logger)
    {
        _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        _mailbox = mailbox ?? throw new ArgumentNullException(nameof(mailbox));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<WorkspacePaths> EnsurePaperFilesAsync(string sessionId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var ws = _workspace.EnsureSessionWorkspace(sessionId);

        if (!File.Exists(ws.PaperOutlinePath))
            await File.WriteAllTextAsync(ws.PaperOutlinePath, string.Empty, Encoding.UTF8, ct);

        if (!File.Exists(ws.PaperDraftPath))
            await File.WriteAllTextAsync(ws.PaperDraftPath, string.Empty, Encoding.UTF8, ct);

        return ws;
    }

    // ------------------------------------------------------------
    //  Propose patch (non-writer path)
    // ------------------------------------------------------------

    public async Task<string> ProposePatchAsync(string sessionId, PaperPatchProposal proposal, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(proposal);
        ct.ThrowIfCancellationRequested();

        var ws = await EnsurePaperFilesAsync(sessionId, ct);

        if (string.IsNullOrWhiteSpace(proposal.PatchId))
            proposal.PatchId = Guid.NewGuid().ToString("N");

        if (proposal.CreatedAt == null)
            proposal.CreatedAt = Timestamp.FromDateTime(DateTime.UtcNow);

        // Envelope for mailbox routing.
        var envelope = new SraMailboxMessage
        {
            SessionId = ws.SessionId,
            MessageId = $"paper_patch:{proposal.PatchId}",
            FromAgent = string.IsNullOrWhiteSpace(proposal.AuthorAgent) ? "unknown" : proposal.AuthorAgent.Trim(),
            ToAgent = PaperEditorAgentName,
            Type = "paper.patch",
            CorrelationId = proposal.CorrelationId ?? "",
            CreatedAt = proposal.CreatedAt,
            Payload = Any.Pack(proposal)
        };

        return await _mailbox.SendAsync(ws, PaperEditorAgentName, envelope, ct);
    }

    // ------------------------------------------------------------
    //  Apply patch (writer path)
    // ------------------------------------------------------------

    public async Task ApplyPatchAsync(string sessionId, string? runId, PaperPatchProposal proposal, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(proposal);
        ct.ThrowIfCancellationRequested();

        var ws = await EnsurePaperFilesAsync(sessionId, ct);

        var targetFile = ResolveTargetPath(ws, proposal.TargetFile);

        // Deterministic, bounded patch.
        switch (proposal.Format)
        {
            case PaperPatchFormat.ReplaceSpan:
                await ApplyReplaceSpanPatchAsync(ws, targetFile, proposal, ct);
                break;

            default:
                throw new NotSupportedException($"unsupported patch format: {proposal.Format}");
        }

        // Best-effort: log patch application under runs/{runId}/
        runId = (runId ?? string.Empty).Trim();
        if (runId.Length > 0)
        {
            try
            {
                var runDir = Path.Combine(ws.RunsDir, runId);
                Directory.CreateDirectory(runDir);

                var json = PaperJsonFormatter.Format(proposal);
                var fileName = $"paper_patch_{SanitizeFileToken(proposal.PatchId)}.json";
                var path = Path.Combine(runDir, fileName);
                await File.WriteAllTextAsync(path, json, Encoding.UTF8, ct);
            }
            catch
            {
                // best-effort only
            }
        }
    }

    private static string ResolveTargetPath(WorkspacePaths ws, PaperTargetFile targetFile)
    {
        return targetFile switch
        {
            PaperTargetFile.Outline => ws.PaperOutlinePath,
            PaperTargetFile.Draft => ws.PaperDraftPath,
            _ => ws.PaperDraftPath
        };
    }

    private static async Task ApplyReplaceSpanPatchAsync(
        WorkspacePaths ws,
        string targetPath,
        PaperPatchProposal proposal,
        CancellationToken ct)
    {
        var content = string.Empty;
        if (File.Exists(targetPath))
            content = await File.ReadAllTextAsync(targetPath, Encoding.UTF8, ct);

        var lines = (content ?? string.Empty).Replace("\r", "").Split('\n').ToList();

        var start = Math.Max(0, proposal.ReplaceStartLine);
        var end = Math.Max(start, proposal.ReplaceEndLineExclusive);

        if (start > lines.Count) start = lines.Count;
        if (end > lines.Count) end = lines.Count;

        var replaceText = (proposal.ReplaceText ?? string.Empty).Replace("\r", "");
        if (replaceText.Length > MaxPatchChars)
            replaceText = replaceText[..MaxPatchChars];

        var replacementLines = replaceText.Split('\n').ToList();
        lines.RemoveRange(start, end - start);
        lines.InsertRange(start, replacementLines);

        Directory.CreateDirectory(ws.TmpDir);
        var tmp = Path.Combine(ws.TmpDir, $"{Guid.NewGuid():N}.tmp");
        await File.WriteAllTextAsync(tmp, string.Join("\n", lines), Encoding.UTF8, ct);
        File.Move(tmp, targetPath, overwrite: true);
    }

    private static string SanitizeFileToken(string? token)
    {
        token = (token ?? string.Empty).Trim();
        if (token.Length == 0) token = "patch";
        token = token.Replace(" ", "");
        if (token.Length > 60) token = token[..60];
        var sb = new StringBuilder(token.Length);
        foreach (var ch in token)
            sb.Append(char.IsLetterOrDigit(ch) ? char.ToLowerInvariant(ch) : '_');
        var s = sb.ToString().Trim('_');
        return s.Length == 0 ? "patch" : s;
    }
}
