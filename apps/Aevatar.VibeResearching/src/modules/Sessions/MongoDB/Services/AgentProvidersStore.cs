using Microsoft.Extensions.Logging;
using System.Text;
using System.Text.Json;
using Aevatar.VibeResearching.Infrastructure.MongoDB.Workspace;
using Aevatar.VibeResearching.Sessions.ValueObjects;

namespace Aevatar.VibeResearching.Sessions.MongoDB.Services;

// ============================================================
//  AgentProvidersStore (File-SSoT, per-session)
//
//  Purpose:
//  - Persist per-agent LLM provider selection for a session.
//  - Allows "each agent uses its own provider" without forcing the Composer
//    to send a providerName per message.
//
//  File:
//  - workspace/sessions/{id}/artifacts/ui/agent_providers.json
//
//  Notes:
//  - Best-effort: storage failures must not break runs.
//  - Provider values are names under LLMProviders:Providers:{name}.
// ============================================================

/// <summary>
/// File-backed store for per-agent LLM provider configuration.
/// Persists agent-to-provider mappings in workspace/sessions/{id}/artifacts/ui/agent_providers.json.
/// All operations are best-effort and do not break runs on failure.
/// </summary>
public sealed class AgentProvidersStore
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly WorkspaceService _workspace;
    private readonly ILogger<AgentProvidersStore> _logger;

    public AgentProvidersStore(WorkspaceService workspace, ILogger<AgentProvidersStore> logger)
    {
        _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public string GetPath(string sessionId)
    {
        var ws = _workspace.EnsureSessionWorkspace(sessionId);
        var dir = Path.Combine(ws.ArtifactsDir, "ui");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "agent_providers.json");
    }

    public async Task<AgentProvidersSnapshot> LoadAsync(string sessionId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var path = GetPath(sessionId);
        try
        {
            if (!File.Exists(path))
                return new AgentProvidersSnapshot(0, "", new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));

            var json = await File.ReadAllTextAsync(path, Encoding.UTF8, ct);
            if (string.IsNullOrWhiteSpace(json))
                return new AgentProvidersSnapshot(0, "", new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));

            var snap = JsonSerializer.Deserialize<AgentProvidersSnapshot>(json, Json);
            if (snap == null)
                return new AgentProvidersSnapshot(0, "", new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));

            return new AgentProvidersSnapshot(
                Version: snap.Version,
                UpdatedAt: snap.UpdatedAt ?? "",
                Map: snap.Map ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load agent_providers.json (best-effort).");
            return new AgentProvidersSnapshot(0, "", new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
        }
    }

    public async Task SaveAsync(string sessionId, AgentProvidersSnapshot snapshot, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ct.ThrowIfCancellationRequested();

        var ws = _workspace.EnsureSessionWorkspace(sessionId);
        var path = GetPath(ws.SessionId);

        // Atomic write via tmp
        Directory.CreateDirectory(ws.TmpDir);
        var tmp = Path.Combine(ws.TmpDir, $"{Guid.NewGuid():N}.tmp");

        var json = JsonSerializer.Serialize(snapshot, Json);
        await File.WriteAllTextAsync(tmp, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), ct);
        File.Move(tmp, path, overwrite: true);
    }

    public async Task<AgentProvidersSnapshot> UpsertAsync(string sessionId, string agent, string? providerName, CancellationToken ct)
    {
        agent = (agent ?? string.Empty).Trim();
        if (agent.Length == 0)
            throw new ArgumentException("agent is required", nameof(agent));

        var snap = await LoadAsync(sessionId, ct);
        var map = new Dictionary<string, string>(snap.Map ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase);

        var p = (providerName ?? string.Empty).Trim();
        if (p.Length == 0 || string.Equals(p, "default", StringComparison.OrdinalIgnoreCase))
        {
            map.Remove(agent);
        }
        else
        {
            map[agent] = p;
        }

        var next = new AgentProvidersSnapshot(
            Version: Math.Max(0, snap.Version) + 1,
            UpdatedAt: DateTimeOffset.UtcNow.ToString("O"),
            Map: map);

        await SaveAsync(sessionId, next, ct);
        return next;
    }
}
