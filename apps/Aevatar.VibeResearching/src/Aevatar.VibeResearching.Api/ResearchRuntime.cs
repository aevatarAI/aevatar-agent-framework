using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.Extensions;
using Aevatar.Agents.AI;
using Aevatar.Agents.AI.Abstractions.Configuration;
using Aevatar.Agents.AI.Core;
using Aevatar.Agents.AI.Core.Configuration;
using Microsoft.Extensions.Options;
using Aevatar.Agents.AI.Tool.Abstractions;
using Google.Protobuf.WellKnownTypes;
using VibeResearching.Vibe;
using VibeResearching.Api.Infrastructure;
using VibeResearching.Api.Sessions;
using VibeResearching.Api.Vibe.Mesh;
using VibeResearching.Contracts.Sessions;

namespace VibeResearching.Api;

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
    private readonly IOptionsMonitor<LLMProvidersConfig> _llm;
    private readonly SkillPacksSyncService _skillPacksSync;
    private readonly TimeSpan _skillPacksRetryMinInterval;
    private readonly GlobalAgentYamlRegistry _roles;
    private readonly IVibeSessionStore? _sessionStore;

    // Per-process retry throttle (best-effort). We don't want to run `git pull` on every request.
    private DateTimeOffset _lastSkillPacksRetryKickoffUtc = DateTimeOffset.MinValue;

    private readonly Dictionary<string, SessionEntry> _sessions = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _sessionsLock = new(1, 1);

    public ResearchRuntime(
        IGAgentActorFactory actorFactory,
        ILogger<ResearchRuntime> logger,
        IOptionsMonitor<LLMProvidersConfig> llm,
        SkillPacksSyncService skillPacksSync,
        IOptions<SkillPacksOptions> skillPacksOptions,
        GlobalAgentYamlRegistry roles,
        IVibeSessionStore? sessionStore = null)
    {
        _actorFactory = actorFactory;
        _logger = logger;
        _llm = llm;
        _skillPacksSync = skillPacksSync;
        _roles = roles ?? throw new ArgumentNullException(nameof(roles));
        _sessionStore = sessionStore;

        var seconds = skillPacksOptions?.Value?.RetryMinIntervalSeconds ?? 60;
        // Keep it sane: prevent accidental zero/negative or extremely spammy values.
        seconds = Math.Clamp(seconds, 5, 3600);
        _skillPacksRetryMinInterval = TimeSpan.FromSeconds(seconds);
    }

    // ============================================================
    //  YAML role overrides (global, cross-app)
    //
    //  Convention:
    //  - ~/.aevatar/agents/{role}.yaml
    //
    //  Scope:
    //  - Provider/model/prompt/tools/skills for both:
    //    - built-in roles (planner/reasoner/...)
    //    - dynamic roles (VibeRoleAgent)
    //
    //  Philosophy:
    //  - No YAML => preserve current hard-coded behavior (fallback).
    //  - YAML exists => override only what is explicitly provided.
    // ============================================================

    // NOTE: YAML application logic moved to framework-level AgentYamlConfigApplier.

    private static string ResolveProviderNamePreferYaml(
        string? requestedProviderName,
        string currentProviderName,
        string role,
        GlobalAgentYamlRegistry roles)
    {
        var requested = (requestedProviderName ?? string.Empty).Trim();
        if (string.Equals(requested, "default", StringComparison.OrdinalIgnoreCase))
            requested = string.Empty;

        // 1) Request-level explicit provider wins (matches orchestrator semantics).
        if (!string.IsNullOrWhiteSpace(requested))
            return requested;

        // 2) YAML provider for role (if any).
        var yamlProvider = roles.TryGetProvider(role);
        if (!string.IsNullOrWhiteSpace(yamlProvider))
            return yamlProvider!;

        // 3) Keep current if set.
        var cur = (currentProviderName ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(cur))
            return cur;

        return string.Empty;
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
        return (entry.MainAgent, entry.MainAgentId);
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

    public async Task<(VibeResearchAssistantAgent Agent, string AgentId)> GetResearchAssistantAgentAsync(
        string sessionId,
        string? providerName,
        CancellationToken ct)
    {
        var entry = await GetOrCreateEntryAsync(sessionId, ct);
        await EnsureResearchAssistantInitializedAsync(entry, providerName, ct);
        if (entry.ResearchAssistantAgent == null || entry.ResearchAssistantActor == null)
            throw new InvalidOperationException(entry.ResearchAssistantLastError ?? "research_assistant not initialized");
        return (entry.ResearchAssistantAgent, entry.ResearchAssistantAgentId);
    }

    public async Task<(VibeLibrarianAgent Agent, string AgentId)> GetLibrarianAgentAsync(
        string sessionId,
        string? providerName,
        CancellationToken ct)
    {
        var entry = await GetOrCreateEntryAsync(sessionId, ct);
        await EnsureLibrarianInitializedAsync(entry, providerName, ct);
        if (entry.LibrarianAgent == null || entry.LibrarianActor == null)
            throw new InvalidOperationException(entry.LibrarianLastError ?? "librarian not initialized");
        return (entry.LibrarianAgent, entry.LibrarianAgentId);
    }

    public async Task<(VibeVerifierAgent Agent, string AgentId)> GetVerifierAgentAsync(
        string sessionId,
        string? providerName,
        CancellationToken ct)
    {
        var entry = await GetOrCreateEntryAsync(sessionId, ct);
        await EnsureVerifierInitializedAsync(entry, providerName, ct);
        if (entry.VerifierAgent == null || entry.VerifierActor == null)
            throw new InvalidOperationException(entry.VerifierLastError ?? "verifier not initialized");
        return (entry.VerifierAgent, entry.VerifierAgentId);
    }

    public async Task<(VibeVerifierAgent Agent, string AgentId)> GetVerifierAgentAsync(
        string sessionId,
        string? providerName,
        string verifierKey,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(verifierKey))
            return await GetVerifierAgentAsync(sessionId, providerName, ct);

        var entry = await GetOrCreateEntryAsync(sessionId, ct);
        await EnsureVerifierInstanceInitializedAsync(entry, providerName, verifierKey, ct);

        var resolvedProvider = ResolveProviderName(providerName, entry.VerifierProviderName);
        var key = $"{SanitizeToken(verifierKey)}__{SanitizeProviderKey(resolvedProvider)}";
        if (!entry.VerifierInstances.TryGetValue(key, out var inst) || inst.Agent == null || inst.Actor == null)
            throw new InvalidOperationException(inst?.LastError ?? "verifier not initialized");

        return (inst.Agent, inst.AgentId);
    }

    public async Task<(VibeDagBuilderAgent Agent, string AgentId)> GetDagBuilderAgentAsync(
        string sessionId,
        string? providerName,
        CancellationToken ct)
    {
        var entry = await GetOrCreateEntryAsync(sessionId, ct);
        await EnsureDagBuilderInitializedAsync(entry, providerName, ct);
        if (entry.DagBuilderAgent == null || entry.DagBuilderActor == null)
            throw new InvalidOperationException(entry.DagBuilderLastError ?? "dag_builder not initialized");
        return (entry.DagBuilderAgent, entry.DagBuilderAgentId);
    }

    public async Task<(AIGAgentBase Agent, string AgentId)> GetRoleAgentAsync(
        string sessionId,
        string? providerName,
        string role,
        CancellationToken ct)
    {
        role = (role ?? string.Empty).Trim();
        if (role.Length == 0)
            throw new ArgumentException("role is required", nameof(role));

        var entry = await GetOrCreateEntryAsync(sessionId, ct);
        await EnsureRoleInstanceInitializedAsync(entry, providerName, role, ct);

        var key = $"{SanitizeToken(role)}__{SanitizeProviderKey(ResolveProviderName(providerName, string.Empty))}";
        if (!entry.RoleInstances.TryGetValue(key, out var inst) || inst.Agent == null || inst.Actor == null)
            throw new InvalidOperationException(inst?.LastError ?? "role agent not initialized");

        return (inst.Agent, inst.AgentId);
    }

    public async Task<(VibePaperEditorAgent Agent, string AgentId)> GetPaperEditorAgentAsync(
        string sessionId,
        string? providerName,
        CancellationToken ct)
    {
        var entry = await GetOrCreateEntryAsync(sessionId, ct);
        await EnsurePaperEditorInitializedAsync(entry, providerName, ct);
        if (entry.PaperEditorAgent == null || entry.PaperEditorActor == null)
            throw new InvalidOperationException(entry.PaperEditorLastError ?? "paper_editor not initialized");
        return (entry.PaperEditorAgent, entry.PaperEditorAgentId);
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

    private static string SanitizeToken(string s)
    {
        var t = (s ?? string.Empty).Trim();
        if (t.Length == 0) return string.Empty;

        var sb = new System.Text.StringBuilder(t.Length);
        foreach (var ch in t)
            sb.Append(char.IsLetterOrDigit(ch) ? ch : '_');
        return sb.ToString();
    }

    private static string BuildAgentId(string sessionId) => $"sra-{sessionId}";

    private async Task TrackAgentAsync(
        SessionEntry entry,
        string agentId,
        string? providerName,
        bool isWorker,
        bool isCoordinator,
        CancellationToken ct)
    {
        if (_sessionStore == null)
            return;

        agentId = (agentId ?? string.Empty).Trim();
        if (agentId.Length == 0)
            return;

        try
        {
            var record = await _sessionStore.GetAsync(entry.SessionId, ct)
                         ?? new VibeSessionRecord { SessionId = entry.SessionId };

            if (string.IsNullOrWhiteSpace(record.ProviderName) && !string.IsNullOrWhiteSpace(providerName))
                record.ProviderName = providerName!.Trim();

            if (isCoordinator && string.IsNullOrWhiteSpace(record.CoordinatorId))
                record.CoordinatorId = agentId;

            if (!record.AgentIds.Contains(agentId))
                record.AgentIds.Add(agentId);

            if (isWorker && !record.WorkerIds.Contains(agentId))
                record.WorkerIds.Add(agentId);

            if (IsDefaultTimestamp(record.CreatedAt))
                record.CreatedAt = Timestamp.FromDateTime(DateTime.UtcNow);
            record.UpdatedAt = Timestamp.FromDateTime(DateTime.UtcNow);

            await _sessionStore.SaveAsync(record, ct);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex,
                "[ResearchRuntime] Failed to persist session agent list for {SessionId}",
                entry.SessionId);
        }
    }

    private static bool IsDefaultTimestamp(Timestamp? ts)
        => ts == null || (ts.Seconds == 0 && ts.Nanos == 0);

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
                now - _lastSkillPacksRetryKickoffUtc < _skillPacksRetryMinInterval)
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
        var resolvedProvider = ResolveProviderName(providerName, entry.MainProviderName);
        if (entry.MainIsReady &&
            entry.MainActor != null &&
            entry.MainAgent != null &&
            string.Equals(entry.MainProviderName, resolvedProvider, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        await entry.Lock.WaitAsync(ct);
        try
        {
            resolvedProvider = ResolveProviderName(providerName, entry.MainProviderName);
            if (entry.MainIsReady &&
                entry.MainActor != null &&
                entry.MainAgent != null &&
                string.Equals(entry.MainProviderName, resolvedProvider, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            entry.MainLastError = null;
            entry.MainProviderName = resolvedProvider;

            _logger.LogInformation("[ResearchRuntime] Creating agent actor: {AgentId} (session={SessionId})",
                entry.MainAgentId, entry.SessionId);

            entry.MainActor = await _actorFactory.CreateGAgentActorAsync<ResearchAgent>(entry.MainAgentId, ct);
            entry.MainAgent = (ResearchAgent)entry.MainActor.GetAgent();

            _logger.LogInformation("[ResearchRuntime] Initializing LLM provider: {Provider} (session={SessionId})",
                entry.MainProviderName, entry.SessionId);

            var providerCfg = BuildProviderConfigOrThrow(entry.MainProviderName);
            await entry.MainAgent.InitializeAsync(providerCfg, _ => { }, ct);

            // Provider-bound init => tools snapshot may need refresh.
            entry.ToolsSnapshot = null;
            entry.McpToolNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            entry.MainIsReady = true;
            await TrackAgentAsync(
                entry,
                entry.MainAgentId,
                entry.MainProviderName,
                isWorker: false,
                isCoordinator: true,
                ct);
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
        var role = "planner";
        var yaml = _roles.TryLoad(role);
        var resolvedProvider = ResolveProviderNamePreferYaml(providerName, entry.PlannerProviderName, role, _roles);
        resolvedProvider = ResolveProviderName(resolvedProvider, entry.PlannerProviderName);
        if (entry.PlannerIsReady &&
            entry.PlannerActor != null &&
            entry.PlannerAgent != null &&
            string.Equals(entry.PlannerProviderName, resolvedProvider, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        await entry.Lock.WaitAsync(ct);
        try
        {
            yaml = _roles.TryLoad(role);
            resolvedProvider = ResolveProviderNamePreferYaml(providerName, entry.PlannerProviderName, role, _roles);
            resolvedProvider = ResolveProviderName(resolvedProvider, entry.PlannerProviderName);
            if (entry.PlannerIsReady &&
                entry.PlannerActor != null &&
                entry.PlannerAgent != null &&
                string.Equals(entry.PlannerProviderName, resolvedProvider, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            entry.PlannerLastError = null;
            entry.PlannerProviderName = resolvedProvider;

            _logger.LogInformation("[ResearchRuntime] Creating planner agent actor: {AgentId} (session={SessionId})",
                entry.PlannerAgentId, entry.SessionId);

            entry.PlannerActor = await _actorFactory.CreateGAgentActorAsync<VibePlannerAgent>(entry.PlannerAgentId, ct);
            entry.PlannerAgent = (VibePlannerAgent)entry.PlannerActor.GetAgent();

            var providerCfg = BuildProviderConfigOrThrow(entry.PlannerProviderName);
            await entry.PlannerAgent.InitializeAsync(providerCfg, cfg =>
            {
                if (yaml != null)
                    AgentYamlConfigApplier.ApplyModelKnobs(yaml, cfg);

                // Fallback defaults (preserve existing behavior).
                if (yaml?.Temperature is null)
                    cfg.Temperature = 0.2f;
                if (yaml?.MaxTokens is null)
                    cfg.MaxOutputTokens = 1000;
            }, ct);

            if (yaml != null)
                await AgentYamlConfigApplier.ApplyAsync(entry.PlannerAgent, yaml, role, ct);

            entry.PlannerIsReady = true;
            await TrackAgentAsync(
                entry,
                entry.PlannerAgentId,
                entry.PlannerProviderName,
                isWorker: true,
                isCoordinator: false,
                ct);
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
        var role = "reasoner";
        var yaml = _roles.TryLoad(role);
        var resolvedProvider = ResolveProviderNamePreferYaml(providerName, entry.ReasonerProviderName, role, _roles);
        resolvedProvider = ResolveProviderName(resolvedProvider, entry.ReasonerProviderName);
        if (entry.ReasonerIsReady &&
            entry.ReasonerActor != null &&
            entry.ReasonerAgent != null &&
            string.Equals(entry.ReasonerProviderName, resolvedProvider, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        await entry.Lock.WaitAsync(ct);
        try
        {
            yaml = _roles.TryLoad(role);
            resolvedProvider = ResolveProviderNamePreferYaml(providerName, entry.ReasonerProviderName, role, _roles);
            resolvedProvider = ResolveProviderName(resolvedProvider, entry.ReasonerProviderName);
            if (entry.ReasonerIsReady &&
                entry.ReasonerActor != null &&
                entry.ReasonerAgent != null &&
                string.Equals(entry.ReasonerProviderName, resolvedProvider, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            entry.ReasonerLastError = null;
            entry.ReasonerProviderName = resolvedProvider;

            _logger.LogInformation("[ResearchRuntime] Creating reasoner agent actor: {AgentId} (session={SessionId})",
                entry.ReasonerAgentId, entry.SessionId);

            entry.ReasonerActor = await _actorFactory.CreateGAgentActorAsync<VibeReasonerAgent>(entry.ReasonerAgentId, ct);
            entry.ReasonerAgent = (VibeReasonerAgent)entry.ReasonerActor.GetAgent();

            var providerCfg = BuildProviderConfigOrThrow(entry.ReasonerProviderName);
            await entry.ReasonerAgent.InitializeAsync(providerCfg, cfg =>
            {
                if (yaml != null)
                    AgentYamlConfigApplier.ApplyModelKnobs(yaml, cfg);

                if (yaml?.Temperature is null)
                    cfg.Temperature = 0.1f;
                if (yaml?.MaxTokens is null)
                    cfg.MaxOutputTokens = 1600;
            }, ct);

            if (yaml != null)
                await AgentYamlConfigApplier.ApplyAsync(entry.ReasonerAgent, yaml, role, ct);

            entry.ReasonerIsReady = true;
            await TrackAgentAsync(
                entry,
                entry.ReasonerAgentId,
                entry.ReasonerProviderName,
                isWorker: true,
                isCoordinator: false,
                ct);
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

    private async Task EnsureResearchAssistantInitializedAsync(SessionEntry entry, string? providerName, CancellationToken ct)
    {
        var resolvedProvider = ResolveProviderName(providerName, entry.ResearchAssistantProviderName);
        if (entry.ResearchAssistantIsReady &&
            entry.ResearchAssistantActor != null &&
            entry.ResearchAssistantAgent != null &&
            string.Equals(entry.ResearchAssistantProviderName, resolvedProvider, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        await entry.Lock.WaitAsync(ct);
        try
        {
            resolvedProvider = ResolveProviderName(providerName, entry.ResearchAssistantProviderName);
            if (entry.ResearchAssistantIsReady &&
                entry.ResearchAssistantActor != null &&
                entry.ResearchAssistantAgent != null &&
                string.Equals(entry.ResearchAssistantProviderName, resolvedProvider, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            entry.ResearchAssistantLastError = null;
            entry.ResearchAssistantProviderName = resolvedProvider;

            _logger.LogInformation("[ResearchRuntime] Creating research_assistant agent actor: {AgentId} (session={SessionId})",
                entry.ResearchAssistantAgentId, entry.SessionId);

            entry.ResearchAssistantActor = await _actorFactory.CreateGAgentActorAsync<VibeResearchAssistantAgent>(entry.ResearchAssistantAgentId, ct);
            entry.ResearchAssistantAgent = (VibeResearchAssistantAgent)entry.ResearchAssistantActor.GetAgent();

            var providerCfg = BuildProviderConfigOrThrow(entry.ResearchAssistantProviderName);
            await entry.ResearchAssistantAgent.InitializeAsync(providerCfg, cfg =>
            {
                cfg.Temperature = 0.2f;
                cfg.MaxOutputTokens = 2000;
            }, ct);

            entry.ResearchAssistantIsReady = true;
            await TrackAgentAsync(
                entry,
                entry.ResearchAssistantAgentId,
                entry.ResearchAssistantProviderName,
                isWorker: true,
                isCoordinator: false,
                ct);
        }
        catch (Exception ex)
        {
            entry.ResearchAssistantLastError = ex.Message;
            _logger.LogError(ex, "[ResearchRuntime] ResearchAssistant init failed: {Message}", ex.Message);
            entry.ResearchAssistantIsReady = false;
        }
        finally
        {
            entry.Lock.Release();
        }
    }

    private async Task EnsureLibrarianInitializedAsync(SessionEntry entry, string? providerName, CancellationToken ct)
    {
        var role = "librarian";
        var yaml = _roles.TryLoad(role);
        var resolvedProvider = ResolveProviderNamePreferYaml(providerName, entry.LibrarianProviderName, role, _roles);
        resolvedProvider = ResolveProviderName(resolvedProvider, entry.LibrarianProviderName);
        if (entry.LibrarianIsReady &&
            entry.LibrarianActor != null &&
            entry.LibrarianAgent != null &&
            string.Equals(entry.LibrarianProviderName, resolvedProvider, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        await entry.Lock.WaitAsync(ct);
        try
        {
            yaml = _roles.TryLoad(role);
            resolvedProvider = ResolveProviderNamePreferYaml(providerName, entry.LibrarianProviderName, role, _roles);
            resolvedProvider = ResolveProviderName(resolvedProvider, entry.LibrarianProviderName);
            if (entry.LibrarianIsReady &&
                entry.LibrarianActor != null &&
                entry.LibrarianAgent != null &&
                string.Equals(entry.LibrarianProviderName, resolvedProvider, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            entry.LibrarianLastError = null;
            entry.LibrarianProviderName = resolvedProvider;

            _logger.LogInformation("[ResearchRuntime] Creating librarian agent actor: {AgentId} (session={SessionId})",
                entry.LibrarianAgentId, entry.SessionId);

            entry.LibrarianActor = await _actorFactory.CreateGAgentActorAsync<VibeLibrarianAgent>(entry.LibrarianAgentId, ct);
            entry.LibrarianAgent = (VibeLibrarianAgent)entry.LibrarianActor.GetAgent();

            var providerCfg = BuildProviderConfigOrThrow(entry.LibrarianProviderName);
            await entry.LibrarianAgent.InitializeAsync(providerCfg, cfg =>
            {
                if (yaml != null)
                    AgentYamlConfigApplier.ApplyModelKnobs(yaml, cfg);

                if (yaml?.Temperature is null)
                    cfg.Temperature = 0.1f;
                if (yaml?.MaxTokens is null)
                    cfg.MaxOutputTokens = 1200;
            }, ct);

            if (yaml != null)
                await AgentYamlConfigApplier.ApplyAsync(entry.LibrarianAgent, yaml, role, ct);

            entry.LibrarianIsReady = true;
            await TrackAgentAsync(
                entry,
                entry.LibrarianAgentId,
                entry.LibrarianProviderName,
                isWorker: true,
                isCoordinator: false,
                ct);
        }
        catch (Exception ex)
        {
            entry.LibrarianLastError = ex.Message;
            _logger.LogError(ex, "[ResearchRuntime] Librarian init failed: {Message}", ex.Message);
            entry.LibrarianIsReady = false;
        }
        finally
        {
            entry.Lock.Release();
        }
    }

    private async Task EnsureVerifierInitializedAsync(SessionEntry entry, string? providerName, CancellationToken ct)
    {
        var role = "verifier";
        var yaml = _roles.TryLoad(role);
        var resolvedProvider = ResolveProviderNamePreferYaml(providerName, entry.VerifierProviderName, role, _roles);
        resolvedProvider = ResolveProviderName(resolvedProvider, entry.VerifierProviderName);
        if (entry.VerifierIsReady &&
            entry.VerifierActor != null &&
            entry.VerifierAgent != null &&
            string.Equals(entry.VerifierProviderName, resolvedProvider, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        await entry.Lock.WaitAsync(ct);
        try
        {
            yaml = _roles.TryLoad(role);
            resolvedProvider = ResolveProviderNamePreferYaml(providerName, entry.VerifierProviderName, role, _roles);
            resolvedProvider = ResolveProviderName(resolvedProvider, entry.VerifierProviderName);
            if (entry.VerifierIsReady &&
                entry.VerifierActor != null &&
                entry.VerifierAgent != null &&
                string.Equals(entry.VerifierProviderName, resolvedProvider, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            entry.VerifierLastError = null;
            entry.VerifierProviderName = resolvedProvider;

            _logger.LogInformation("[ResearchRuntime] Creating verifier agent actor: {AgentId} (session={SessionId})",
                entry.VerifierAgentId, entry.SessionId);

            entry.VerifierActor = await _actorFactory.CreateGAgentActorAsync<VibeVerifierAgent>(entry.VerifierAgentId, ct);
            entry.VerifierAgent = (VibeVerifierAgent)entry.VerifierActor.GetAgent();

            var providerCfg = BuildProviderConfigOrThrow(entry.VerifierProviderName);
            await entry.VerifierAgent.InitializeAsync(providerCfg, cfg =>
            {
                if (yaml != null)
                    AgentYamlConfigApplier.ApplyModelKnobs(yaml, cfg);

                if (yaml?.Temperature is null)
                    cfg.Temperature = 0.0f;
                if (yaml?.MaxTokens is null)
                    cfg.MaxOutputTokens = 1400;
            }, ct);

            if (yaml != null)
                await AgentYamlConfigApplier.ApplyAsync(entry.VerifierAgent, yaml, role, ct);

            entry.VerifierIsReady = true;
            await TrackAgentAsync(
                entry,
                entry.VerifierAgentId,
                entry.VerifierProviderName,
                isWorker: true,
                isCoordinator: false,
                ct);
        }
        catch (Exception ex)
        {
            entry.VerifierLastError = ex.Message;
            _logger.LogError(ex, "[ResearchRuntime] Verifier init failed: {Message}", ex.Message);
            entry.VerifierIsReady = false;
        }
        finally
        {
            entry.Lock.Release();
        }
    }

    private async Task EnsureVerifierInstanceInitializedAsync(
        SessionEntry entry,
        string? providerName,
        string verifierKey,
        CancellationToken ct)
    {
        verifierKey = SanitizeToken(verifierKey);
        if (verifierKey.Length == 0)
        {
            await EnsureVerifierInitializedAsync(entry, providerName, ct);
            return;
        }

        await entry.Lock.WaitAsync(ct);
        try
        {
            var resolvedProvider = ResolveProviderName(providerName, entry.VerifierProviderName);
            var providerKey = SanitizeProviderKey(resolvedProvider);
            var instKey = $"{verifierKey}__{providerKey}";

            if (!entry.VerifierInstances.TryGetValue(instKey, out var inst))
            {
                inst = new VerifierInstance
                {
                    AgentId = $"{entry.AgentId}-verifier-{verifierKey}-{providerKey}",
                    ProviderName = resolvedProvider
                };
                entry.VerifierInstances[instKey] = inst;
            }

            if (inst.IsReady &&
                inst.Actor != null &&
                inst.Agent != null &&
                string.Equals(inst.ProviderName, resolvedProvider, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            inst.LastError = null;
            inst.ProviderName = resolvedProvider;

            _logger.LogInformation("[ResearchRuntime] Creating verifier agent actor: {AgentId} (session={SessionId})",
                inst.AgentId, entry.SessionId);

            inst.Actor = await _actorFactory.CreateGAgentActorAsync<VibeVerifierAgent>(inst.AgentId, ct);
            inst.Agent = (VibeVerifierAgent)inst.Actor.GetAgent();

            var providerCfg = BuildProviderConfigOrThrow(resolvedProvider);
            await inst.Agent.InitializeAsync(providerCfg, cfg =>
            {
                cfg.Temperature = 0.0f;
                cfg.MaxOutputTokens = 1400;
            }, ct);

            inst.IsReady = true;
            await TrackAgentAsync(
                entry,
                inst.AgentId,
                inst.ProviderName,
                isWorker: true,
                isCoordinator: false,
                ct);
        }
        catch (Exception ex)
        {
            // Preserve the record so callers can see the error without rethrowing internal details.
            var resolvedProvider = ResolveProviderName(providerName, entry.VerifierProviderName);
            var providerKey = SanitizeProviderKey(resolvedProvider);
            var instKey = $"{verifierKey}__{providerKey}";

            if (!entry.VerifierInstances.TryGetValue(instKey, out var inst))
            {
                inst = new VerifierInstance { AgentId = $"{entry.AgentId}-verifier-{verifierKey}-{providerKey}", ProviderName = resolvedProvider };
                entry.VerifierInstances[instKey] = inst;
            }

            inst.LastError = ex.Message;
            inst.IsReady = false;
            _logger.LogError(ex, "[ResearchRuntime] Verifier({Key}) init failed: {Message}", verifierKey, ex.Message);
        }
        finally
        {
            entry.Lock.Release();
        }
    }

    private async Task EnsureDagBuilderInitializedAsync(SessionEntry entry, string? providerName, CancellationToken ct)
    {
        var role = "dag_builder";
        var yaml = _roles.TryLoad(role);
        var resolvedProvider = ResolveProviderNamePreferYaml(providerName, entry.DagBuilderProviderName, role, _roles);
        resolvedProvider = ResolveProviderName(resolvedProvider, entry.DagBuilderProviderName);
        if (entry.DagBuilderIsReady &&
            entry.DagBuilderActor != null &&
            entry.DagBuilderAgent != null &&
            string.Equals(entry.DagBuilderProviderName, resolvedProvider, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        await entry.Lock.WaitAsync(ct);
        try
        {
            yaml = _roles.TryLoad(role);
            resolvedProvider = ResolveProviderNamePreferYaml(providerName, entry.DagBuilderProviderName, role, _roles);
            resolvedProvider = ResolveProviderName(resolvedProvider, entry.DagBuilderProviderName);
            if (entry.DagBuilderIsReady &&
                entry.DagBuilderActor != null &&
                entry.DagBuilderAgent != null &&
                string.Equals(entry.DagBuilderProviderName, resolvedProvider, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            entry.DagBuilderLastError = null;
            entry.DagBuilderProviderName = resolvedProvider;

            _logger.LogInformation("[ResearchRuntime] Creating dag_builder agent actor: {AgentId} (session={SessionId})",
                entry.DagBuilderAgentId, entry.SessionId);

            entry.DagBuilderActor = await _actorFactory.CreateGAgentActorAsync<VibeDagBuilderAgent>(entry.DagBuilderAgentId, ct);
            entry.DagBuilderAgent = (VibeDagBuilderAgent)entry.DagBuilderActor.GetAgent();

            var providerCfg = BuildProviderConfigOrThrow(entry.DagBuilderProviderName);
            await entry.DagBuilderAgent.InitializeAsync(providerCfg, cfg =>
            {
                if (yaml != null)
                    AgentYamlConfigApplier.ApplyModelKnobs(yaml, cfg);

                if (yaml?.Temperature is null)
                    cfg.Temperature = 0.1f;
                if (yaml?.MaxTokens is null)
                    cfg.MaxOutputTokens = 2000;
            }, ct);

            if (yaml != null)
                await AgentYamlConfigApplier.ApplyAsync(entry.DagBuilderAgent, yaml, role, ct);

            entry.DagBuilderIsReady = true;
            await TrackAgentAsync(
                entry,
                entry.DagBuilderAgentId,
                entry.DagBuilderProviderName,
                isWorker: true,
                isCoordinator: false,
                ct);
        }
        catch (Exception ex)
        {
            entry.DagBuilderLastError = ex.Message;
            _logger.LogError(ex, "[ResearchRuntime] DagBuilder init failed: {Message}", ex.Message);
            entry.DagBuilderIsReady = false;
        }
        finally
        {
            entry.Lock.Release();
        }
    }

    private async Task EnsurePaperEditorInitializedAsync(SessionEntry entry, string? providerName, CancellationToken ct)
    {
        var role = "paper_editor";
        var yaml = _roles.TryLoad(role);
        var resolvedProvider = ResolveProviderNamePreferYaml(providerName, entry.PaperEditorProviderName, role, _roles);
        resolvedProvider = ResolveProviderName(resolvedProvider, entry.PaperEditorProviderName);
        if (entry.PaperEditorIsReady &&
            entry.PaperEditorActor != null &&
            entry.PaperEditorAgent != null &&
            string.Equals(entry.PaperEditorProviderName, resolvedProvider, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        await entry.Lock.WaitAsync(ct);
        try
        {
            yaml = _roles.TryLoad(role);
            resolvedProvider = ResolveProviderNamePreferYaml(providerName, entry.PaperEditorProviderName, role, _roles);
            resolvedProvider = ResolveProviderName(resolvedProvider, entry.PaperEditorProviderName);
            if (entry.PaperEditorIsReady &&
                entry.PaperEditorActor != null &&
                entry.PaperEditorAgent != null &&
                string.Equals(entry.PaperEditorProviderName, resolvedProvider, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            entry.PaperEditorLastError = null;
            entry.PaperEditorProviderName = resolvedProvider;

            _logger.LogInformation("[ResearchRuntime] Creating paper_editor agent actor: {AgentId} (session={SessionId})",
                entry.PaperEditorAgentId, entry.SessionId);

            entry.PaperEditorActor = await _actorFactory.CreateGAgentActorAsync<VibePaperEditorAgent>(entry.PaperEditorAgentId, ct);
            entry.PaperEditorAgent = (VibePaperEditorAgent)entry.PaperEditorActor.GetAgent();

            var providerCfg = BuildProviderConfigOrThrow(entry.PaperEditorProviderName);
            await entry.PaperEditorAgent.InitializeAsync(providerCfg, cfg =>
            {
                if (yaml != null)
                    AgentYamlConfigApplier.ApplyModelKnobs(yaml, cfg);

                if (yaml?.Temperature is null)
                    cfg.Temperature = 0.2f;
                if (yaml?.MaxTokens is null)
                    cfg.MaxOutputTokens = 2200;
            }, ct);

            if (yaml != null)
                await AgentYamlConfigApplier.ApplyAsync(entry.PaperEditorAgent, yaml, role, ct);

            entry.PaperEditorIsReady = true;
            await TrackAgentAsync(
                entry,
                entry.PaperEditorAgentId,
                entry.PaperEditorProviderName,
                isWorker: true,
                isCoordinator: false,
                ct);
        }
        catch (Exception ex)
        {
            entry.PaperEditorLastError = ex.Message;
            _logger.LogError(ex, "[ResearchRuntime] PaperEditor init failed: {Message}", ex.Message);
            entry.PaperEditorIsReady = false;
        }
        finally
        {
            entry.Lock.Release();
        }
    }

    private async Task EnsureRoleInstanceInitializedAsync(
        SessionEntry entry,
        string? providerName,
        string role,
        CancellationToken ct)
    {
        role = SanitizeToken(role);
        if (role.Length == 0)
            throw new ArgumentException("role is empty", nameof(role));

        await entry.Lock.WaitAsync(ct);
        try
        {
            var resolvedProvider = ResolveProviderName(providerName, string.Empty);
            var providerKey = SanitizeProviderKey(resolvedProvider);
            var instKey = $"{role}__{providerKey}";

            if (!entry.RoleInstances.TryGetValue(instKey, out var inst))
            {
                inst = new RoleInstance
                {
                    Role = role,
                    ProviderName = resolvedProvider,
                    AgentId = $"{entry.AgentId}-role-{role}-{providerKey}"
                };
                entry.RoleInstances[instKey] = inst;
            }

            if (inst.IsReady &&
                inst.Actor != null &&
                inst.Agent != null &&
                string.Equals(inst.ProviderName, resolvedProvider, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            inst.LastError = null;
            inst.ProviderName = resolvedProvider;

            _logger.LogInformation("[ResearchRuntime] Creating role agent actor: {AgentId} (role={Role}, session={SessionId})",
                inst.AgentId, role, entry.SessionId);

            inst.Actor = await _actorFactory.CreateGAgentActorAsync<VibeRoleAgent>(inst.AgentId, ct);
            inst.Agent = (VibeRoleAgent)inst.Actor.GetAgent();

            var yaml = _roles.TryLoad(role);
            var yamlProvider = yaml != null ? (yaml.Provider ?? string.Empty).Trim() : string.Empty;
            if (!string.IsNullOrWhiteSpace(yamlProvider) && !string.Equals(yamlProvider, "default", StringComparison.OrdinalIgnoreCase))
            {
                // Provider defined by role yaml wins over caller request.
                inst.ProviderName = yamlProvider;
                resolvedProvider = yamlProvider;
            }

            var providerCfg = BuildProviderConfigOrThrow(resolvedProvider);
            await inst.Agent.InitializeAsync(providerCfg, cfg =>
            {
                // Apply YAML model/runtime knobs (if any). Keep it best-effort and explicit.
                if (yaml != null)
                    AgentYamlConfigApplier.ApplyModelKnobs(yaml, cfg);
            }, ct);

            // Apply YAML prompt/tools/skills (best-effort).
            if (yaml != null)
                await AgentYamlConfigApplier.ApplyAsync(inst.Agent, yaml, role, ct);

            inst.IsReady = true;
            await TrackAgentAsync(
                entry,
                inst.AgentId,
                inst.ProviderName,
                isWorker: true,
                isCoordinator: false,
                ct);
        }
        catch (Exception ex)
        {
            var resolvedProvider = ResolveProviderName(providerName, string.Empty);
            var providerKey = SanitizeProviderKey(resolvedProvider);
            var instKey = $"{role}__{providerKey}";

            if (!entry.RoleInstances.TryGetValue(instKey, out var inst))
            {
                inst = new RoleInstance
                {
                    Role = role,
                    ProviderName = resolvedProvider,
                    AgentId = $"{entry.AgentId}-role-{role}-{providerKey}"
                };
                entry.RoleInstances[instKey] = inst;
            }

            inst.LastError = ex.Message;
            inst.IsReady = false;
            _logger.LogError(ex, "[ResearchRuntime] Role({Role}) init failed: {Message}", role, ex.Message);
        }
        finally
        {
            entry.Lock.Release();
        }
    }


    private string ResolveProviderName(string? requestedProviderName, string currentProviderName)
    {
        var requested = (requestedProviderName ?? string.Empty).Trim();
        if (string.Equals(requested, "default", StringComparison.OrdinalIgnoreCase))
            requested = string.Empty;

        // 1) Explicit request (if configured).
        if (!string.IsNullOrWhiteSpace(requested) && IsProviderConfigured(requested))
            return requested;

        // 2) Keep current if configured.
        var cur = (currentProviderName ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(cur) && IsProviderConfigured(cur))
            return cur;

        // 3) Fallback to a runnable default.
        return ResolveEffectiveDefaultProviderName();
    }

    private bool IsProviderConfigured(string? providerName)
    {
        var name = (providerName ?? string.Empty).Trim();
        if (name.Length == 0 || string.Equals(name, "default", StringComparison.OrdinalIgnoreCase))
            return false;

        var root = _llm.CurrentValue;
        return root.Providers.TryGetValue(name, out var p) && p != null && !string.IsNullOrWhiteSpace(p.ApiKey);
    }

    private static string SanitizeProviderKey(string providerName)
    {
        var key = SanitizeToken((providerName ?? string.Empty).Trim().ToLowerInvariant());
        return key.Length == 0 ? "default" : key;
    }

    private string ResolveEffectiveDefaultProviderName()
    {
        var root = _llm.CurrentValue;

        // 1) Config default (if set and present)
        var def = (root.Default ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(def) && IsProviderConfigured(def))
            return def;

        // 2) Prefer providers that actually have an API key.
        var withKey = root.Providers
            .Where(kv => kv.Value != null && !string.IsNullOrWhiteSpace(kv.Value.ApiKey))
            .Select(kv => kv.Key)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(withKey))
            return withKey;

        // 3) Fallback: any configured provider key.
        var any = root.Providers.Keys
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        return string.IsNullOrWhiteSpace(any) ? "default" : any;
    }

    private LLMProviderConfig BuildProviderConfigOrThrow(string providerName)
    {
        var name = (providerName ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidOperationException("ProviderName is empty.");

        var root = _llm.CurrentValue;
        if (!root.Providers.TryGetValue(name, out var src) || src == null)
        {
            throw new InvalidOperationException(
                $"LLM provider '{name}' is not configured. Check LLMProviders:Providers:{name} in configuration.");
        }

        var copy = CloneProviderConfig(src);
        if (string.IsNullOrWhiteSpace(copy.Name))
            copy.Name = name;

        // Embeddings fallback:
        // - If provider doesn't define embeddings -> inherit global embeddings.
        // - If provider defines embeddings -> partial-merge missing fields from global embeddings.
        if (root.Embeddings != null)
        {
            if (copy.Embeddings == null)
            {
                copy.Embeddings = CloneEmbeddingConfig(root.Embeddings);
            }
            else
            {
                copy.Embeddings = MergeEmbeddingConfig(root.Embeddings, copy.Embeddings);
            }
        }

        return copy;
    }

    private static LLMProviderConfig CloneProviderConfig(LLMProviderConfig src)
    {
        return new LLMProviderConfig
        {
            Name = src.Name,
            ProviderType = src.ProviderType,
            ApiKey = src.ApiKey,
            Model = src.Model,
            Endpoint = src.Endpoint,
            DeploymentName = src.DeploymentName,
            Temperature = src.Temperature,
            MaxTokens = src.MaxTokens,
            TimeoutMilliseconds = src.TimeoutMilliseconds,
            EnableStreaming = src.EnableStreaming,
            ProviderSpecificSettings = src.ProviderSpecificSettings != null
                ? new Dictionary<string, object>(src.ProviderSpecificSettings)
                : new Dictionary<string, object>(),
            Embeddings = src.Embeddings != null ? CloneEmbeddingConfig(src.Embeddings) : null
        };
    }

    private static LLMEmbeddingConfig CloneEmbeddingConfig(LLMEmbeddingConfig src)
    {
        return new LLMEmbeddingConfig
        {
            ProviderType = src.ProviderType,
            Model = src.Model,
            DeploymentName = src.DeploymentName,
            Endpoint = src.Endpoint,
            ApiKey = src.ApiKey,
            Dimensions = src.Dimensions,
            ProviderSpecificSettings = src.ProviderSpecificSettings != null
                ? new Dictionary<string, object>(src.ProviderSpecificSettings)
                : new Dictionary<string, object>()
        };
    }

    private static LLMEmbeddingConfig MergeEmbeddingConfig(LLMEmbeddingConfig global, LLMEmbeddingConfig provider)
    {
        // Provider overrides explicitly set fields; missing fields fall back to global.
        var merged = CloneEmbeddingConfig(provider);

        if (string.IsNullOrWhiteSpace(merged.ProviderType))
            merged.ProviderType = global.ProviderType;
        if (string.IsNullOrWhiteSpace(merged.Model))
            merged.Model = global.Model;
        if (string.IsNullOrWhiteSpace(merged.DeploymentName))
            merged.DeploymentName = global.DeploymentName;
        if (string.IsNullOrWhiteSpace(merged.Endpoint))
            merged.Endpoint = global.Endpoint;
        if (string.IsNullOrWhiteSpace(merged.ApiKey))
            merged.ApiKey = global.ApiKey;
        if (!merged.Dimensions.HasValue)
            merged.Dimensions = global.Dimensions;

        // ProviderSpecificSettings: merge global -> provider (provider wins on key conflicts).
        var mergedSettings = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        if (global.ProviderSpecificSettings != null)
        {
            foreach (var kv in global.ProviderSpecificSettings)
                mergedSettings[kv.Key] = kv.Value;
        }
        if (provider.ProviderSpecificSettings != null)
        {
            foreach (var kv in provider.ProviderSpecificSettings)
                mergedSettings[kv.Key] = kv.Value;
        }
        merged.ProviderSpecificSettings = mergedSettings;

        return merged;
    }

    private sealed class SessionEntry
    {
        public required string SessionId { get; init; }
        public required string AgentId { get; init; }
        public string MainAgentId => $"{AgentId}-main-{ResearchRuntime.SanitizeProviderKey(MainProviderName)}";
        public string PlannerAgentId => $"{AgentId}-planner-{ResearchRuntime.SanitizeProviderKey(PlannerProviderName)}";
        public string ReasonerAgentId => $"{AgentId}-reasoner-{ResearchRuntime.SanitizeProviderKey(ReasonerProviderName)}";
        public string ResearchAssistantAgentId => $"{AgentId}-research_assistant-{ResearchRuntime.SanitizeProviderKey(ResearchAssistantProviderName)}";
        public string LibrarianAgentId => $"{AgentId}-librarian-{ResearchRuntime.SanitizeProviderKey(LibrarianProviderName)}";
        public string VerifierAgentId => $"{AgentId}-verifier-{ResearchRuntime.SanitizeProviderKey(VerifierProviderName)}";
        public string DagBuilderAgentId => $"{AgentId}-dag_builder-{ResearchRuntime.SanitizeProviderKey(DagBuilderProviderName)}";
        public string PaperEditorAgentId => $"{AgentId}-paper_editor-{ResearchRuntime.SanitizeProviderKey(PaperEditorProviderName)}";

        public readonly SemaphoreSlim Lock = new(1, 1);
        public IGAgentActor? MainActor { get; set; }
        public ResearchAgent? MainAgent { get; set; }

        public bool MainIsReady { get; set; }
        public string? MainLastError { get; set; }
        public string MainProviderName { get; set; } = string.Empty;

        public IGAgentActor? PlannerActor { get; set; }
        public VibePlannerAgent? PlannerAgent { get; set; }
        public bool PlannerIsReady { get; set; }
        public string? PlannerLastError { get; set; }
        public string PlannerProviderName { get; set; } = string.Empty;

        public IGAgentActor? ReasonerActor { get; set; }
        public VibeReasonerAgent? ReasonerAgent { get; set; }
        public bool ReasonerIsReady { get; set; }
        public string? ReasonerLastError { get; set; }
        public string ReasonerProviderName { get; set; } = string.Empty;

        public IGAgentActor? ResearchAssistantActor { get; set; }
        public VibeResearchAssistantAgent? ResearchAssistantAgent { get; set; }
        public bool ResearchAssistantIsReady { get; set; }
        public string? ResearchAssistantLastError { get; set; }
        public string ResearchAssistantProviderName { get; set; } = string.Empty;

        public IGAgentActor? LibrarianActor { get; set; }
        public VibeLibrarianAgent? LibrarianAgent { get; set; }
        public bool LibrarianIsReady { get; set; }
        public string? LibrarianLastError { get; set; }
        public string LibrarianProviderName { get; set; } = string.Empty;

        public IGAgentActor? VerifierActor { get; set; }
        public VibeVerifierAgent? VerifierAgent { get; set; }
        public bool VerifierIsReady { get; set; }
        public string? VerifierLastError { get; set; }
        public string VerifierProviderName { get; set; } = string.Empty;

        // Extra verifier instances keyed by a small token (e.g., "dag_consensus_v1").
        // Used by lightweight quorum-based DAG consensus.
        public Dictionary<string, VerifierInstance> VerifierInstances { get; } = new(StringComparer.Ordinal);

        // Dynamic roles keyed by role + provider.
        // Used by Mesh DSL (Option B) for user-defined roles in ~/.aevatar/agents/*.yaml.
        public Dictionary<string, RoleInstance> RoleInstances { get; } = new(StringComparer.Ordinal);

        public IGAgentActor? DagBuilderActor { get; set; }
        public VibeDagBuilderAgent? DagBuilderAgent { get; set; }
        public bool DagBuilderIsReady { get; set; }
        public string? DagBuilderLastError { get; set; }
        public string DagBuilderProviderName { get; set; } = string.Empty;

        public IGAgentActor? PaperEditorActor { get; set; }
        public VibePaperEditorAgent? PaperEditorAgent { get; set; }
        public bool PaperEditorIsReady { get; set; }
        public string? PaperEditorLastError { get; set; }
        public string PaperEditorProviderName { get; set; } = string.Empty;

        public IReadOnlyList<object>? ToolsSnapshot { get; set; }
        public HashSet<string> McpToolNames { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class VerifierInstance
    {
        public required string AgentId { get; init; }
        public string ProviderName { get; set; } = string.Empty;
        public IGAgentActor? Actor { get; set; }
        public VibeVerifierAgent? Agent { get; set; }
        public bool IsReady { get; set; }
        public string? LastError { get; set; }
    }

    private sealed class RoleInstance
    {
        public required string AgentId { get; init; }
        public required string Role { get; init; }
        public string ProviderName { get; set; } = string.Empty;
        public IGAgentActor? Actor { get; set; }
        public VibeRoleAgent? Agent { get; set; }
        public bool IsReady { get; set; }
        public string? LastError { get; set; }
    }
}


