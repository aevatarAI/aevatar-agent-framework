using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.Extensions;
using Aevatar.Agents.AI;
using Aevatar.Agents.AI.Abstractions.Configuration;
using Aevatar.Agents.AI.Core;
using Microsoft.Extensions.Options;
using Aevatar.Agents.AI.WithTool.Abstractions;
using ScientificResearchAssistant.Vibe;
using ScientificResearchAssistant.Api.Infrastructure;

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
    private readonly SkillPacksSyncService _skillPacksSync;

    // Per-process retry throttle (best-effort). We don't want to run `git pull` on every request.
    private DateTimeOffset _lastSkillPacksRetryKickoffUtc = DateTimeOffset.MinValue;
    private static readonly TimeSpan SkillPacksRetryMinInterval = TimeSpan.FromSeconds(60);

    private readonly Dictionary<string, SessionEntry> _sessions = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _sessionsLock = new(1, 1);

    public ResearchRuntime(
        IGAgentActorFactory actorFactory,
        ILogger<ResearchRuntime> logger,
        IOptions<LLMProvidersConfig> llm,
        SkillPacksSyncService skillPacksSync)
    {
        _actorFactory = actorFactory;
        _logger = logger;
        _llm = llm;
        _skillPacksSync = skillPacksSync;
    }

    public async Task<(ResearchAgent Agent, string AgentId)> GetAgentAsync(
        string sessionId,
        string? providerName,
        CancellationToken ct)
    {
        var entry = await GetOrCreateEntryAsync(sessionId, ct);
        await EnsureMainInitializedAsync(entry, providerName, ct);
        if (entry.MainAgent == null || entry.MainActor == null)
            throw new InvalidOperationException(entry.MainLastError ?? "agent not initialized");
        return (entry.MainAgent, entry.AgentId);
    }

    public async Task<(VibePlannerAgent Agent, string AgentId)> GetPlannerAgentAsync(
        string sessionId,
        string? providerName,
        CancellationToken ct)
    {
        var entry = await GetOrCreateEntryAsync(sessionId, ct);
        await EnsurePlannerInitializedAsync(entry, providerName, ct);
        if (entry.PlannerAgent == null || entry.PlannerActor == null)
            throw new InvalidOperationException(entry.PlannerLastError ?? "planner not initialized");
        return (entry.PlannerAgent, entry.PlannerAgentId);
    }

    public async Task<(VibeReasonerAgent Agent, string AgentId)> GetReasonerAgentAsync(
        string sessionId,
        string? providerName,
        CancellationToken ct)
    {
        var entry = await GetOrCreateEntryAsync(sessionId, ct);
        await EnsureReasonerInitializedAsync(entry, providerName, ct);
        if (entry.ReasonerAgent == null || entry.ReasonerActor == null)
            throw new InvalidOperationException(entry.ReasonerLastError ?? "reasoner not initialized");
        return (entry.ReasonerAgent, entry.ReasonerAgentId);
    }

    public async Task<AevatarAIAgentState?> TryGetAgentStateAsync(string sessionId, CancellationToken ct)
    {
        var entry = await GetOrCreateEntryAsync(sessionId, ct);
        await EnsureMainInitializedAsync(entry, providerName: null, ct);

        try
        {
            if (entry.MainActor == null)
                return null;

            return await entry.MainActor.InvokeAsync<AevatarAIAgentState>("GetState");
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
        await EnsureMainInitializedAsync(entry, providerName, ct);

        await entry.Lock.WaitAsync(ct);
        try
        {
            if (entry.ToolsSnapshot is { Count: > 0 })
            {
                return (entry.ToolsSnapshot, entry.McpToolNames);
            }

            if (entry.MainAgent == null)
                return (Array.Empty<object>(), new HashSet<string>(StringComparer.OrdinalIgnoreCase));

            IReadOnlyList<ToolDefinition> tools;
            try
            {
                tools = await entry.MainAgent.ListToolsAsync(ct);
            }
            catch (Exception ex)
            {
                entry.MainLastError = ex.Message;
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
        await EnsureMainInitializedAsync(entry, providerName, ct);

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

        // Best-effort: if skill packs are configured but sync failed earlier, retry on session access.
        // This makes the system "eventually consistent" without requiring manual clicks.
        TryKickoffSkillPacksSyncRetryBestEffort();

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

    private void TryKickoffSkillPacksSyncRetryBestEffort()
    {
        try
        {
            if (_skillPacksSync == null)
                return;

            if (!_skillPacksSync.HasEnabledPacks)
                return;

            // If we've synced successfully, no need to retry.
            if (_skillPacksSync.LastSyncOk)
                return;

            var now = DateTimeOffset.UtcNow;
            if (_lastSkillPacksRetryKickoffUtc != DateTimeOffset.MinValue &&
                now - _lastSkillPacksRetryKickoffUtc < SkillPacksRetryMinInterval)
            {
                return;
            }

            _lastSkillPacksRetryKickoffUtc = now;

            // Fire-and-forget: never block the request path.
            _ = _skillPacksSync
                .TryEnsureSyncedAsync(SkillPackSyncMode.Manual, CancellationToken.None)
                .ContinueWith(t =>
                {
                    _logger.LogDebug(t.Exception, "[SkillPacksSync] Session-triggered retry failed (best-effort).");
                }, TaskContinuationOptions.OnlyOnFaulted);
        }
        catch
        {
            // best-effort only
        }
    }

    private async Task EnsureInitializedAsync(SessionEntry entry, string? providerName, CancellationToken ct)
    {
        // Backward compatibility shim: keep old method name for existing call sites.
        await EnsureMainInitializedAsync(entry, providerName, ct);
    }

    private async Task EnsureMainInitializedAsync(SessionEntry entry, string? providerName, CancellationToken ct)
    {
        if (entry.MainIsReady)
            return;

        await entry.Lock.WaitAsync(ct);
        try
        {
            if (entry.MainIsReady)
                return;

            entry.MainLastError = null;
            EnsureProviderName(entry, providerName);

            _logger.LogInformation("[ResearchRuntime] Creating agent actor: {AgentId} (session={SessionId})",
                entry.AgentId, entry.SessionId);

            entry.MainActor = await _actorFactory.CreateGAgentActorAsync<ResearchAgent>(entry.AgentId, ct);
            entry.MainAgent = (ResearchAgent)entry.MainActor.GetAgent();

            _logger.LogInformation("[ResearchRuntime] Initializing LLM provider: {Provider} (session={SessionId})",
                entry.ProviderName, entry.SessionId);

            await entry.MainAgent.InitializeAsync(entry.ProviderName, _ => { }, ct);

            entry.MainIsReady = true;
        }
        catch (Exception ex)
        {
            entry.MainLastError = ex.Message;
            _logger.LogError(ex, "[ResearchRuntime] Main agent init failed: {Message}", ex.Message);
            entry.MainIsReady = false;
        }
        finally
        {
            entry.Lock.Release();
        }
    }

    private async Task EnsurePlannerInitializedAsync(SessionEntry entry, string? providerName, CancellationToken ct)
    {
        if (entry.PlannerIsReady)
            return;

        await entry.Lock.WaitAsync(ct);
        try
        {
            if (entry.PlannerIsReady)
                return;

            entry.PlannerLastError = null;
            EnsureProviderName(entry, providerName);

            _logger.LogInformation("[ResearchRuntime] Creating planner agent actor: {AgentId} (session={SessionId})",
                entry.PlannerAgentId, entry.SessionId);

            entry.PlannerActor = await _actorFactory.CreateGAgentActorAsync<VibePlannerAgent>(entry.PlannerAgentId, ct);
            entry.PlannerAgent = (VibePlannerAgent)entry.PlannerActor.GetAgent();

            await entry.PlannerAgent.InitializeAsync(entry.ProviderName, cfg =>
            {
                cfg.Temperature = 0.2f;
                cfg.MaxOutputTokens = 1000;
            }, ct);

            entry.PlannerIsReady = true;
        }
        catch (Exception ex)
        {
            entry.PlannerLastError = ex.Message;
            _logger.LogError(ex, "[ResearchRuntime] Planner init failed: {Message}", ex.Message);
            entry.PlannerIsReady = false;
        }
        finally
        {
            entry.Lock.Release();
        }
    }

    private async Task EnsureReasonerInitializedAsync(SessionEntry entry, string? providerName, CancellationToken ct)
    {
        if (entry.ReasonerIsReady)
            return;

        await entry.Lock.WaitAsync(ct);
        try
        {
            if (entry.ReasonerIsReady)
                return;

            entry.ReasonerLastError = null;
            EnsureProviderName(entry, providerName);

            _logger.LogInformation("[ResearchRuntime] Creating reasoner agent actor: {AgentId} (session={SessionId})",
                entry.ReasonerAgentId, entry.SessionId);

            entry.ReasonerActor = await _actorFactory.CreateGAgentActorAsync<VibeReasonerAgent>(entry.ReasonerAgentId, ct);
            entry.ReasonerAgent = (VibeReasonerAgent)entry.ReasonerActor.GetAgent();

            await entry.ReasonerAgent.InitializeAsync(entry.ProviderName, cfg =>
            {
                cfg.Temperature = 0.1f;
                cfg.MaxOutputTokens = 1600;
            }, ct);

            entry.ReasonerIsReady = true;
        }
        catch (Exception ex)
        {
            entry.ReasonerLastError = ex.Message;
            _logger.LogError(ex, "[ResearchRuntime] Reasoner init failed: {Message}", ex.Message);
            entry.ReasonerIsReady = false;
        }
        finally
        {
            entry.Lock.Release();
        }
    }

    private void EnsureProviderName(SessionEntry entry, string? providerName)
    {
        if (!string.IsNullOrWhiteSpace(entry.ProviderName))
            return;

        var provider = string.IsNullOrWhiteSpace(providerName)
            ? (string.IsNullOrWhiteSpace(_llm.Value.Default) ? "default" : _llm.Value.Default)
            : providerName.Trim();

        entry.ProviderName = provider;
    }

    private sealed class SessionEntry
    {
        public required string SessionId { get; init; }
        public required string AgentId { get; init; }
        public string PlannerAgentId => $"{AgentId}-planner";
        public string ReasonerAgentId => $"{AgentId}-reasoner";

        public readonly SemaphoreSlim Lock = new(1, 1);
        public IGAgentActor? MainActor { get; set; }
        public ResearchAgent? MainAgent { get; set; }

        public bool MainIsReady { get; set; }
        public string? MainLastError { get; set; }
        public string ProviderName { get; set; } = string.Empty;

        public IGAgentActor? PlannerActor { get; set; }
        public VibePlannerAgent? PlannerAgent { get; set; }
        public bool PlannerIsReady { get; set; }
        public string? PlannerLastError { get; set; }

        public IGAgentActor? ReasonerActor { get; set; }
        public VibeReasonerAgent? ReasonerAgent { get; set; }
        public bool ReasonerIsReady { get; set; }
        public string? ReasonerLastError { get; set; }

        public IReadOnlyList<object>? ToolsSnapshot { get; set; }
        public HashSet<string> McpToolNames { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }
}


