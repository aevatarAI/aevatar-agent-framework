using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.Extensions;
using Aevatar.Agents.AI;
using Aevatar.Agents.AI.Abstractions.Configuration;
using Aevatar.Agents.AI.Core;
using Microsoft.Extensions.Options;
using Aevatar.Agents.AI.WithTool.Abstractions;

namespace ScientificResearchAssistant.Api;

// ============================================================
//  ResearchRuntime (MVP)
//
//  Purpose:
//  - Manage ResearchAgent instances per "session" (thread).
//  - Keep agent state bounded so AG-UI can snapshot on reconnect.
// ============================================================

public sealed class ResearchRuntime
{
    private readonly IGAgentActorFactory _actorFactory;
    private readonly ILogger<ResearchRuntime> _logger;
    private readonly IOptions<LLMProvidersConfig> _llm;

    private readonly Dictionary<string, SessionEntry> _sessions = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _sessionsLock = new(1, 1);

    public ResearchRuntime(
        IGAgentActorFactory actorFactory,
        ILogger<ResearchRuntime> logger,
        IOptions<LLMProvidersConfig> llm)
    {
        _actorFactory = actorFactory;
        _logger = logger;
        _llm = llm;
    }

    public async Task<(ResearchAgent Agent, string AgentId)> GetAgentAsync(
        string sessionId,
        string? providerName,
        CancellationToken ct)
    {
        var entry = await GetOrCreateEntryAsync(sessionId, ct);
        await EnsureInitializedAsync(entry, providerName, ct);
        if (entry.Agent == null || entry.Actor == null)
            throw new InvalidOperationException(entry.LastError ?? "agent not initialized");
        return (entry.Agent, entry.AgentId);
    }

    public async Task<AevatarAIAgentState?> TryGetAgentStateAsync(string sessionId, CancellationToken ct)
    {
        var entry = await GetOrCreateEntryAsync(sessionId, ct);
        await EnsureInitializedAsync(entry, providerName: null, ct);

        try
        {
            if (entry.Actor == null)
                return null;

            return await entry.Actor.InvokeAsync<AevatarAIAgentState>("GetState");
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "[ResearchRuntime] GetState failed (best-effort).");
            return null;
        }
    }

    public async Task<(IReadOnlyList<object> Tools, IReadOnlySet<string> McpToolNames)> GetToolsSnapshotAsync(
        string sessionId,
        string? providerName,
        CancellationToken ct)
    {
        var entry = await GetOrCreateEntryAsync(sessionId, ct);
        await EnsureInitializedAsync(entry, providerName, ct);

        await entry.Lock.WaitAsync(ct);
        try
        {
            if (entry.ToolsSnapshot is { Count: > 0 })
            {
                return (entry.ToolsSnapshot, entry.McpToolNames);
            }

            if (entry.Agent == null)
                return (Array.Empty<object>(), new HashSet<string>(StringComparer.OrdinalIgnoreCase));

            IReadOnlyList<ToolDefinition> tools;
            try
            {
                tools = await entry.Agent.ListToolsAsync(ct);
            }
            catch (Exception ex)
            {
                entry.LastError = ex.Message;
                _logger.LogWarning(ex, "[ResearchRuntime] ListTools failed (best-effort).");
                return (Array.Empty<object>(), new HashSet<string>(StringComparer.OrdinalIgnoreCase));
            }

            var list = tools
                .OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
                .Select(t => new
                {
                    name = t.Name,
                    description = t.Description,
                    category = t.Category.ToString(),
                    source = t.Metadata != null && t.Metadata.TryGetValue("Source", out var s) ? s?.ToString() : null,
                    tags = t.Tags
                })
                .Cast<object>()
                .ToList();

            var mcp = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var t in tools)
            {
                if (t.Metadata != null &&
                    t.Metadata.TryGetValue("Source", out var src) &&
                    string.Equals(src?.ToString(), "MCP", StringComparison.OrdinalIgnoreCase))
                {
                    mcp.Add(t.Name);
                }
            }

            entry.ToolsSnapshot = list;
            entry.McpToolNames = mcp;

            return (entry.ToolsSnapshot, entry.McpToolNames);
        }
        finally
        {
            entry.Lock.Release();
        }
    }

    public async Task<(IReadOnlyList<object> Tools, IReadOnlySet<string> McpToolNames)> RefreshToolsSnapshotAsync(
        string sessionId,
        string? providerName,
        CancellationToken ct)
    {
        var entry = await GetOrCreateEntryAsync(sessionId, ct);
        await EnsureInitializedAsync(entry, providerName, ct);

        await entry.Lock.WaitAsync(ct);
        try
        {
            // Invalidate cached snapshot so next call re-reads from ToolManager.
            entry.ToolsSnapshot = null;
            entry.McpToolNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
        finally
        {
            entry.Lock.Release();
        }

        return await GetToolsSnapshotAsync(sessionId, providerName, ct);
    }

    public async Task<bool> IsMcpToolAsync(string sessionId, string toolName, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(sessionId) || string.IsNullOrWhiteSpace(toolName))
            return false;

        var entry = await GetOrCreateEntryAsync(sessionId, ct);
        await entry.Lock.WaitAsync(ct);
        try
        {
            return entry.McpToolNames.Contains(toolName.Trim());
        }
        finally
        {
            entry.Lock.Release();
        }
    }

    private static string NormalizeSessionId(string? sessionId)
    {
        var s = (sessionId ?? string.Empty).Trim();
        return s;
    }

    private static string BuildAgentId(string sessionId) => $"sra-{sessionId}";

    private async Task<SessionEntry> GetOrCreateEntryAsync(string? sessionId, CancellationToken ct)
    {
        var sid = NormalizeSessionId(sessionId);
        if (sid.Length == 0)
            throw new ArgumentException("sessionId is required", nameof(sessionId));

        await _sessionsLock.WaitAsync(ct);
        try
        {
            if (_sessions.TryGetValue(sid, out var existing))
                return existing;

            var entry = new SessionEntry
            {
                SessionId = sid,
                AgentId = BuildAgentId(sid)
            };
            _sessions[sid] = entry;
            return entry;
        }
        finally
        {
            _sessionsLock.Release();
        }
    }

    private async Task EnsureInitializedAsync(SessionEntry entry, string? providerName, CancellationToken ct)
    {
        if (entry.IsReady)
            return;

        await entry.Lock.WaitAsync(ct);
        try
        {
            if (entry.IsReady)
                return;

            entry.LastError = null;

            _logger.LogInformation("[ResearchRuntime] Creating agent actor: {AgentId} (session={SessionId})",
                entry.AgentId, entry.SessionId);

            entry.Actor = await _actorFactory.CreateGAgentActorAsync<ResearchAgent>(entry.AgentId, ct);
            entry.Agent = (ResearchAgent)entry.Actor.GetAgent();

            var provider = string.IsNullOrWhiteSpace(providerName)
                ? (string.IsNullOrWhiteSpace(_llm.Value.Default) ? "default" : _llm.Value.Default)
                : providerName.Trim();

            entry.ProviderName = provider;
            _logger.LogInformation("[ResearchRuntime] Initializing LLM provider: {Provider} (session={SessionId})",
                provider, entry.SessionId);

            await entry.Agent.InitializeAsync(provider, _ => { }, ct);

            entry.IsReady = true;
        }
        catch (Exception ex)
        {
            entry.LastError = ex.Message;
            _logger.LogError(ex, "[ResearchRuntime] Initialization failed: {Message}", ex.Message);
            entry.IsReady = false;
        }
        finally
        {
            entry.Lock.Release();
        }
    }

    private sealed class SessionEntry
    {
        public required string SessionId { get; init; }
        public required string AgentId { get; init; }

        public readonly SemaphoreSlim Lock = new(1, 1);
        public IGAgentActor? Actor { get; set; }
        public ResearchAgent? Agent { get; set; }

        public bool IsReady { get; set; }
        public string? LastError { get; set; }
        public string? ProviderName { get; set; }

        public IReadOnlyList<object>? ToolsSnapshot { get; set; }
        public HashSet<string> McpToolNames { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }
}


