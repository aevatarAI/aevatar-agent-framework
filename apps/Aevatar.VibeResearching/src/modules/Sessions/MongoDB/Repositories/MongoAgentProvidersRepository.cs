using Microsoft.Extensions.Logging;
using System.Text;
using System.Text.Json;
using Aevatar.VibeResearching.Infrastructure.MongoDB.Workspace;
using Aevatar.VibeResearching.Sessions.Repositories;
using Aevatar.VibeResearching.Sessions.ValueObjects;

namespace Aevatar.VibeResearching.Sessions.MongoDB.Repositories;

/// <summary>
/// File-based repository implementation for agent providers configuration.
/// Persists per-agent LLM provider selection for a session.
/// File: workspace/sessions/{id}/artifacts/ui/agent_providers.json
/// Note: Best-effort - storage failures must not break runs.
/// </summary>
public sealed class MongoAgentProvidersRepository : IAgentProvidersRepository
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly WorkspaceService _workspace;
    private readonly ILogger<MongoAgentProvidersRepository> _logger;

    public MongoAgentProvidersRepository(WorkspaceService workspace, ILogger<MongoAgentProvidersRepository> logger)
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

    public async Task<AgentProvidersSnapshot?> LoadAsync(string sessionId, CancellationToken ct)
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

    public async Task UpsertAsync(string sessionId, string agentName, string providerName, CancellationToken ct)
    {
        agentName = (agentName ?? string.Empty).Trim();
        if (agentName.Length == 0)
            throw new ArgumentException("agent is required", nameof(agentName));

        var snap = await LoadAsync(sessionId, ct);
        var map = new Dictionary<string, string>(snap?.Map ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase);

        var p = (providerName ?? string.Empty).Trim();
        if (p.Length == 0 || string.Equals(p, "default", StringComparison.OrdinalIgnoreCase))
        {
            map.Remove(agentName);
        }
        else
        {
            map[agentName] = p;
        }

        var next = new AgentProvidersSnapshot(
            Version: Math.Max(0, snap?.Version ?? 0) + 1,
            UpdatedAt: DateTimeOffset.UtcNow.ToString("O"),
            Map: map);

        await SaveAsync(sessionId, next, ct);
    }
}
