using System.Text;
using System.Text.Json;
using VibeResearching.Api.Workspace;

namespace VibeResearching.Api.Vibe.Compute;

// ============================================================
//  ComputeDecisionStore (File-SSoT, MVP)
//
//  What:
//  - Persist user decisions for compute execution (execute/degrade/skip).
//
//  Where:
//  - workspace/sessions/{sessionId}/artifacts/compute/decisions/*.json
//
//  Notes:
//  - This is an MVP store (JSON, bounded). Compute runner is added later.
// ============================================================

public sealed class ComputeDecisionStore
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };

    private readonly WorkspaceService _workspace;
    private readonly ILogger<ComputeDecisionStore> _logger;

    public ComputeDecisionStore(WorkspaceService workspace, ILogger<ComputeDecisionStore> logger)
    {
        _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<string> SaveAsync(
        string sessionId,
        string planId,
        string action,
        string? comment,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var ws = _workspace.EnsureSessionWorkspace(sessionId);

        planId = (planId ?? string.Empty).Trim();
        action = (action ?? string.Empty).Trim().ToLowerInvariant();
        comment = (comment ?? string.Empty).Replace("\r", "").Trim();
        if (comment.Length > 2000) comment = comment[..2000];

        if (planId.Length == 0)
            planId = "plan";

        if (action is not ("execute" or "degrade" or "skip"))
            action = "skip";

        var dir = Path.Combine(ws.ArtifactsDir, "compute", "decisions");
        Directory.CreateDirectory(dir);
        Directory.CreateDirectory(ws.TmpDir);

        var stamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd_HHmmss");
        var fileName = $"decision_{stamp}_{Sanitize(planId)}.json";
        var full = Path.Combine(dir, fileName);

        var payload = new
        {
            sessionId = ws.SessionId,
            planId,
            action,
            comment,
            createdAt = DateTimeOffset.UtcNow.ToString("O")
        };

        try
        {
            var tmp = Path.Combine(ws.TmpDir, $"{Guid.NewGuid():N}.tmp");
            var json = JsonSerializer.Serialize(payload, Json);
            await File.WriteAllTextAsync(tmp, json, Encoding.UTF8, ct);
            File.Move(tmp, full, overwrite: true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to write compute decision file (best-effort).");
            throw;
        }

        // Return a session-relative path for UI references.
        var rel = Path.GetRelativePath(ws.SessionRoot, full).Replace('\\', '/');
        return rel.TrimStart('/');
    }

    public Task<string> WriteDecisionAsync(
        string sessionId,
        string planId,
        string action,
        string? comment,
        CancellationToken ct)
    {
        return SaveAsync(sessionId, planId, action, comment, ct);
    }

    private static string Sanitize(string token)
    {
        token = (token ?? string.Empty).Trim();
        if (token.Length == 0) token = "id";
        if (token.Length > 48) token = token[..48];

        var sb = new StringBuilder(token.Length);
        foreach (var ch in token)
        {
            sb.Append(char.IsLetterOrDigit(ch) ? char.ToLowerInvariant(ch) : '_');
        }

        var s = sb.ToString().Trim('_');
        return s.Length == 0 ? "id" : s;
    }
}


