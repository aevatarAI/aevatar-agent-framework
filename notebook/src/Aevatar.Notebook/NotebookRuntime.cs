using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.Extensions;
using Aevatar.Agents.AI;
using Aevatar.Agents.AI.Abstractions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Aevatar.Notebook.Agents;
using Aevatar.Agents.AI.Core;

namespace Aevatar.Notebook;

// ============================================================
//  NotebookRuntime
//
//  Purpose:
//  - Manage NotebookAgent instances per "session" (thread).
//  - Keep legacy MVP behavior via DefaultSessionId.
// ============================================================
public sealed class NotebookRuntime
{
    public const string DefaultSessionId = "default";

    private readonly IGAgentActorFactory _actorFactory;
    private readonly ILogger<NotebookRuntime> _logger;
    private readonly IOptions<LLMProvidersConfig> _llm;

    private readonly Dictionary<string, SessionEntry> _sessions = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _sessionsLock = new(1, 1);

    public NotebookRuntime(
        IGAgentActorFactory actorFactory,
        ILogger<NotebookRuntime> logger,
        IOptions<LLMProvidersConfig> llm)
    {
        _actorFactory = actorFactory;
        _logger = logger;
        _llm = llm;
    }

    public Task<(NotebookAgent Agent, string AgentId)> GetAgentAsync(CancellationToken ct) =>
        GetAgentAsync(DefaultSessionId, providerName: null, ct);

    public async Task<(NotebookAgent Agent, string AgentId)> GetAgentAsync(
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

    public Task<NotebookStatus> GetStatusAsync(CancellationToken ct) =>
        GetStatusAsync(DefaultSessionId, ct);

    public async Task<NotebookStatus> GetStatusAsync(string sessionId, CancellationToken ct)
    {
        var entry = await GetOrCreateEntryAsync(sessionId, ct);
        await EnsureInitializedAsync(entry, providerName: null, ct);
        return new NotebookStatus
        {
            AgentId = entry.AgentId,
            IsReady = entry.IsReady,
            LastError = entry.LastError
        };
    }

    public Task<NotebookStatus> ResetAsync(CancellationToken ct) =>
        ResetAsync(DefaultSessionId, ct);

    public async Task<NotebookStatus> ResetAsync(string sessionId, CancellationToken ct)
    {
        sessionId = NormalizeSessionId(sessionId);

        await _sessionsLock.WaitAsync(ct);
        try
        {
            if (_sessions.TryGetValue(sessionId, out var entry))
            {
                await entry.Lock.WaitAsync(ct);
                try
                {
                    entry.Actor = null;
                    entry.Agent = null;
                    entry.IsReady = false;
                    entry.LastError = null;
                    entry.ProviderName = null;
                }
                finally
                {
                    entry.Lock.Release();
                }
            }
        }
        finally
        {
            _sessionsLock.Release();
        }

        return await GetStatusAsync(sessionId, ct);
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
            _logger.LogDebug(ex, "[Notebook] GetState failed (best-effort).");
            return null;
        }
    }

    private static string NormalizeSessionId(string? sessionId)
    {
        var s = (sessionId ?? string.Empty).Trim();
        return s.Length == 0 ? DefaultSessionId : s;
    }

    private static string BuildAgentId(string sessionId) => $"notebook-{sessionId}";

    private async Task<SessionEntry> GetOrCreateEntryAsync(string? sessionId, CancellationToken ct)
    {
        var sid = NormalizeSessionId(sessionId);

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

            _logger.LogInformation("[Notebook] Creating agent actor: {AgentId} (session={SessionId})", entry.AgentId, entry.SessionId);
            entry.Actor = await _actorFactory.CreateGAgentActorAsync<NotebookAgent>(entry.AgentId);
            entry.Agent = (NotebookAgent)entry.Actor.GetAgent();

            // Initialize LLM provider:
            // - Prefer explicit providerName for the first initialization of this session.
            // - Otherwise fall back to LLMProviders:Default (or "default").
            var provider = string.IsNullOrWhiteSpace(providerName)
                ? (string.IsNullOrWhiteSpace(_llm.Value.Default) ? "default" : _llm.Value.Default)
                : providerName.Trim();

            entry.ProviderName = provider;
            _logger.LogInformation("[Notebook] Initializing LLM provider: {Provider} (session={SessionId})", provider, entry.SessionId);

            await entry.Agent.InitializeAsync(
                provider,
                cfg =>
                {
                    cfg.Temperature = 0.2f;
                    cfg.MaxOutputTokens = 1200;
                },
                ct);

            entry.IsReady = true;
        }
        catch (Exception ex)
        {
            entry.LastError = ex.Message;
            _logger.LogError(ex, "[Notebook] Initialization failed: {Message}", ex.Message);
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
        public NotebookAgent? Agent { get; set; }
        public bool IsReady { get; set; }
        public string? LastError { get; set; }
        public string? ProviderName { get; set; }
    }
}

public sealed class NotebookStatus
{
    public required string AgentId { get; init; }
    public required bool IsReady { get; init; }
    public string? LastError { get; init; }
}


