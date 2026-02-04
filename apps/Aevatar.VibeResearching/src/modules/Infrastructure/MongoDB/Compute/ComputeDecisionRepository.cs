using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Aevatar.VibeResearching.Infrastructure.MongoDB.Workspace;
using Aevatar.VibeResearching.Infrastructure;

namespace Aevatar.VibeResearching.Infrastructure.MongoDB.Compute;

// ============================================================
//  ComputeDecisionRepository (File-SSoT, MVP)
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

public sealed class ComputeDecisionRepository : IComputeDecisionRepository
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };

    private readonly WorkspaceService _workspace;
    private readonly ILogger<ComputeDecisionRepository> _logger;

    public ComputeDecisionRepository(WorkspaceService workspace, ILogger<ComputeDecisionRepository> logger)
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

    public async Task RecordDecisionAsync(
        string sessionId,
        string planId,
        string decision,
        CancellationToken ct)
    {
        await SaveAsync(sessionId, planId, decision, comment: null, ct);
    }

    public Task<object> GetPlanAsync(
        string sessionId,
        string planId,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var ws = _workspace.EnsureSessionWorkspace(sessionId);
        var dir = Path.Combine(ws.ArtifactsDir, "compute", "plans");
        var file = Path.Combine(dir, $"{Sanitize(planId)}.json");
        if (!File.Exists(file))
            return Task.FromResult<object>(new { sessionId, planId, status = "not_found" });
        // MVP: return raw JSON as dynamic object
        var json = File.ReadAllText(file);
        var doc = JsonSerializer.Deserialize<JsonElement>(json);
        return Task.FromResult<object>(doc);
    }

    public Task<object> GetJobStatusAsync(
        string sessionId,
        string jobId,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        // MVP: compute runner not yet implemented — return placeholder status
        return Task.FromResult<object>(new { sessionId, jobId, status = "not_started" });
    }

    public Task<IReadOnlyList<object>> GetRequestsAsync(
        string sessionId,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var ws = _workspace.EnsureSessionWorkspace(sessionId);
        var dir = Path.Combine(ws.ArtifactsDir, "compute", "decisions");
        if (!Directory.Exists(dir))
            return Task.FromResult<IReadOnlyList<object>>(Array.Empty<object>());

        var files = Directory.GetFiles(dir, "*.json");
        var results = new List<object>(files.Length);
        foreach (var f in files)
        {
            try
            {
                var json = File.ReadAllText(f);
                results.Add(JsonSerializer.Deserialize<JsonElement>(json));
            }
            catch
            {
                // skip corrupted files
            }
        }
        return Task.FromResult<IReadOnlyList<object>>(results);
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
