using System.Text;
using Google.Protobuf;
using Google.Protobuf.Reflection;
using Google.Protobuf.WellKnownTypes;
using VibeResearching.Api.Workspace;
using VibeResearching.Contracts.Collab;

namespace VibeResearching.Api.Paper;

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
                await WriteFileAtomicAsync(ws, path, json, ct);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to write paper patch log");
            }
        }
    }

    // ============================================================
    //  Patch implementations
    // ============================================================

    private async Task ApplyReplaceSpanPatchAsync(
        WorkspacePaths ws,
        string targetPath,
        PaperPatchProposal proposal,
        CancellationToken ct)
    {
        // Validate payload bounds first.
        if ((proposal.ReplaceText?.Length ?? 0) > MaxPatchChars)
            throw new ArgumentException($"replace_text too large (max {MaxPatchChars})", nameof(proposal));

        var startLine = proposal.ReplaceStartLine;
        var endLineExcl = proposal.ReplaceEndLineExclusive;
        if (startLine <= 0 || endLineExcl <= 0)
            throw new ArgumentException("replace_start_line and replace_end_line_exclusive are required for REPLACE_SPAN", nameof(proposal));

        // Read lines (normalize to LF only).
        var lines = (await File.ReadAllLinesAsync(targetPath, Encoding.UTF8, ct))
            .Select(l => (l ?? string.Empty).Replace("\r", ""))
            .ToList();

        // 1-based to 0-based indices.
        if (startLine < 1 || startLine > lines.Count + 1)
            throw new ArgumentOutOfRangeException(nameof(proposal), "replace_start_line out of range");
        if (endLineExcl < startLine || endLineExcl > lines.Count + 1)
            throw new ArgumentOutOfRangeException(nameof(proposal), "replace_end_line_exclusive out of range");

        var startIdx = startLine - 1;
        var endIdx = endLineExcl - 1; // exclusive

        var replacementLines = (proposal.ReplaceText ?? string.Empty)
            .Replace("\r", "")
            .Split('\n', StringSplitOptions.None)
            .ToList();

        // If replace_text ends with a trailing newline, Split gives an extra empty item.
        // That is fine; we'll normalize final output to end with newline anyway.

        lines.RemoveRange(startIdx, endIdx - startIdx);
        lines.InsertRange(startIdx, replacementLines);

        // Normalize output: always end with newline for diff-friendliness.
        var newText = string.Join("\n", lines) + "\n";
        await WriteFileAtomicAsync(ws, targetPath, newText, ct);
    }

    private static string ResolveTargetPath(WorkspacePaths ws, PaperTargetFile target)
    {
        return target switch
        {
            PaperTargetFile.Outline => ws.PaperOutlinePath,
            PaperTargetFile.Draft => ws.PaperDraftPath,
            _ => throw new ArgumentOutOfRangeException(nameof(target), target, "unknown paper target")
        };
    }

    private static async Task WriteFileAtomicAsync(WorkspacePaths ws, string targetPath, string content, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        Directory.CreateDirectory(ws.TmpDir);

        var tmp = Path.Combine(ws.TmpDir, $"{Guid.NewGuid():N}.tmp");
        await File.WriteAllTextAsync(tmp, content ?? string.Empty, Encoding.UTF8, ct);

        // Replace in-place (same volume). Best-effort atomicity.
        File.Move(tmp, targetPath, overwrite: true);
    }

    private static string SanitizeFileToken(string token)
    {
        token = (token ?? string.Empty).Trim();
        if (token.Length == 0) token = "id";

        var sb = new StringBuilder(token.Length);
        foreach (var ch in token)
            sb.Append(char.IsLetterOrDigit(ch) ? ch : '_');
        return sb.ToString();
    }
}


