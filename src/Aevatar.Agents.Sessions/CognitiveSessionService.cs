using Aevatar.Agents;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.Extensions;
using Aevatar.Agents.Abstractions.Helpers;
using Aevatar.Agents.Abstractions.Memory;
using Aevatar.Agents.Abstractions.Tracing;
using Aevatar.Agents.AI;
using Aevatar.Agents.AI.Core;
using Aevatar.Agents.Cognitive.Agents;
using Aevatar.Agents.Cognitive.Execution;
using Aevatar.Agents.Cognitive.Messages;
using Aevatar.Agents.Cognitive.Primitives;
using Aevatar.Agents.Cognitive.Utilities;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WorkflowDefinition = Aevatar.Agents.Cognitive.Primitives.WorkflowDefinition;

namespace Aevatar.Agents.Sessions;

public sealed class CognitiveSessionService
{
    private readonly IGAgentActorManager _actorManager;
    private readonly IWorkflowRegistry _workflowRegistry;
    private readonly ISessionStore _sessionStore;
    private readonly IMemoryStore? _memoryStore;
    private readonly IExecutionTraceStore? _executionTraceStore;
    private readonly CognitiveSessionOptions _options;
    private readonly ILogger<CognitiveSessionService> _logger;

    public CognitiveSessionService(
        IGAgentActorManager actorManager,
        IWorkflowRegistry workflowRegistry,
        ISessionStore sessionStore,
        IMemoryStore? memoryStore,
        IExecutionTraceStore? executionTraceStore,
        IOptions<CognitiveSessionOptions> options,
        ILogger<CognitiveSessionService> logger)
    {
        _actorManager = actorManager ?? throw new ArgumentNullException(nameof(actorManager));
        _workflowRegistry = workflowRegistry ?? throw new ArgumentNullException(nameof(workflowRegistry));
        _sessionStore = sessionStore ?? throw new ArgumentNullException(nameof(sessionStore));
        _memoryStore = memoryStore;
        _executionTraceStore = executionTraceStore;
        _options = options?.Value ?? new CognitiveSessionOptions();
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public WorkflowListResponse ListWorkflows()
    {
        var list = new WorkflowListResponse();
        foreach (var name in _workflowRegistry.List())
        {
            var wf = _workflowRegistry.Get(name);
            if (wf == null) continue;
            list.Workflows.Add(new WorkflowInfo
            {
                Name = wf.Name ?? string.Empty,
                Version = wf.Version ?? string.Empty,
                Description = wf.Description ?? string.Empty
            });
        }

        return list;
    }

    public async Task<SessionState> StartSessionAsync(
        StartSessionRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var workflowName = (request.WorkflowName ?? string.Empty).Trim();
        if (workflowName.Length == 0)
            throw new ArgumentException("workflow_name is required.", nameof(request));

        var workflow = _workflowRegistry.Get(workflowName);
        if (workflow == null)
        {
            var available = string.Join(", ", _workflowRegistry.List());
            throw new InvalidOperationException($"Workflow '{workflowName}' not found. Available: {available}");
        }

        var sessionId = NormalizeSessionId(request.SessionId);
        if (await _sessionStore.ExistsAsync(sessionId, ct))
            throw new InvalidOperationException($"Session '{sessionId}' already exists.");

        var providerName = NormalizeProviderName(request.ProviderName);
        var workerCount = request.WorkerCount > 0 ? request.WorkerCount : _options.DefaultWorkerCount;

        var enableAgentMemory = request.EnableAgentMemory || _options.EnableAgentMemory;
        var enableSessionMemory = request.EnableSessionMemory || _options.EnableSessionMemory;

        var coordinatorRawId = BuildCoordinatorRawId(sessionId);
        var coordinatorActor = await _actorManager.CreateAndRegisterAsync<CognitiveCoordinatorGAgent>(
            coordinatorRawId,
            ct);

        var coordinator = RequireAgent<CognitiveCoordinatorGAgent>(coordinatorActor);

        await coordinator.InitializeAsync(providerName, cancellationToken: ct);
        coordinator.SetActorManager(_actorManager);
        coordinator.ConfigureSessionContext(sessionId, enableSessionMemory, enableAgentMemory);

        RegisterWorkflows(coordinator, workflow);

        var workerGuids = BuildWorkerIds(sessionId, workerCount);
        await coordinator.CreateWorkerPoolAsync(workerCount, workerGuids);

        var workerActorIds = await ConfigureWorkersAsync(
            workerGuids,
            sessionId,
            enableSessionMemory,
            enableAgentMemory,
            ct);

        var variables = ConvertVariables(request);
        variables["session_id"] = sessionId;

        var startEvent = new StartWorkflowRequestEvent { WorkflowName = workflowName };
        foreach (var (key, value) in variables)
        {
            startEvent.Variables[key] = ProtoValueConverter.ToProto(value);
        }

        await coordinatorActor.PublishEventAsync(startEvent, EventDirection.Down, ct);

        var executionId = await ResolveExecutionIdWithRetryAsync(coordinatorActor.Id, ct);
        var now = Timestamp.FromDateTime(DateTime.UtcNow);
        var state = new SessionState
        {
            SessionId = sessionId,
            WorkflowName = workflowName,
            ProviderName = providerName,
            WorkerCount = workerCount,
            CoordinatorId = coordinatorActor.Id,
            ExecutionId = string.IsNullOrWhiteSpace(executionId) ? string.Empty : executionId,
            Status = ExecutionStatus.EsPending,
            CreatedAt = now,
            UpdatedAt = now
        };

        state.WorkerIds.Add(workerActorIds);
        state.AgentIds.Add(coordinatorActor.Id);
        state.AgentIds.Add(workerActorIds);

        await _sessionStore.SaveAsync(state, ct);
        if (enableSessionMemory)
        {
            await AppendSessionIndexEntryAsync(state, ct);
        }

        await EmitSessionStartTraceAsync(state, executionId, ct);
        return state;
    }

    public async Task<SessionState?> GetSessionStateAsync(string sessionId, CancellationToken ct = default)
    {
        var state = await _sessionStore.GetAsync(sessionId, ct);
        if (state == null)
            return null;

        await TryRefreshSessionStateAsync(state, ct);
        return state;
    }

    public async Task<SessionAgentsResponse?> GetSessionAgentsAsync(string sessionId, CancellationToken ct = default)
    {
        var state = await GetSessionStateAsync(sessionId, ct);
        if (state == null)
            return null;

        var response = new SessionAgentsResponse
        {
            SessionId = state.SessionId,
            CoordinatorId = state.CoordinatorId
        };
        response.WorkerIds.Add(state.WorkerIds);
        response.AgentIds.Add(state.AgentIds);
        return response;
    }

    public async Task<SessionAgentStatesResponse?> GetSessionAgentStatesAsync(
        string sessionId,
        bool includeHistory,
        int historyLimit,
        CancellationToken ct = default)
    {
        var state = await GetSessionStateAsync(sessionId, ct);
        if (state == null)
            return null;

        var response = new SessionAgentStatesResponse();
        var actors = await _actorManager.GetActorsAsync(state.AgentIds);

        foreach (var actor in actors)
        {
            var agentState = await actor.InvokeAsync<AevatarAIAgentState>("GetState");
            var normalized = NormalizeHistory(agentState, includeHistory, historyLimit);
            response.Agents.Add(new SessionAgentStateBundle
            {
                AgentId = actor.Id,
                State = normalized
            });
        }

        return response;
    }

    public async Task<SessionAgentHistoriesResponse?> GetSessionAgentHistoriesAsync(
        string sessionId,
        int historyLimit,
        CancellationToken ct = default)
    {
        var state = await GetSessionStateAsync(sessionId, ct);
        if (state == null)
            return null;

        var response = new SessionAgentHistoriesResponse();
        var actors = await _actorManager.GetActorsAsync(state.AgentIds);

        foreach (var actor in actors)
        {
            var agentState = await actor.InvokeAsync<AevatarAIAgentState>("GetState");
            var history = TrimHistory(agentState.History, historyLimit);
            var bundle = new SessionAgentHistory
            {
                AgentId = actor.Id
            };
            bundle.History.Add(history);
            response.Agents.Add(bundle);
        }

        return response;
    }

    public async Task<SessionMemoryEntriesResponse?> GetSessionMemoryEntriesAsync(
        string sessionId,
        int limit,
        CancellationToken ct = default)
    {
        if (_memoryStore == null)
            return null;

        var response = new SessionMemoryEntriesResponse();
        var service = new SessionHistoryService(_memoryStore);
        var entries = await service.GetSessionEntriesAsync(sessionId, limit, ct);
        response.Entries.Add(entries);
        return response;
    }

    public async Task<SessionMemoryEntriesResponse?> GetAgentMemoryEntriesAsync(
        string sessionId,
        string agentId,
        int limit,
        CancellationToken ct = default)
    {
        if (_memoryStore == null)
            return null;

        if (!await IsAgentInSessionAsync(sessionId, agentId, ct))
            return null;

        var response = new SessionMemoryEntriesResponse();
        var memoryId = BuildMemoryId(MemoryScopeType.PrivateAgent, agentId);
        var entries = await _memoryStore.ListEntriesAsync(memoryId, limit, ct);
        response.Entries.Add(entries);
        return response;
    }

    public async Task<SessionMemoryResourcesResponse?> ListSessionMemoryResourcesAsync(
        string sessionId,
        MemoryScopeType scopeType,
        int limit,
        CancellationToken ct = default)
    {
        if (_memoryStore == null)
            return null;

        var response = new SessionMemoryResourcesResponse();
        var resources = await _memoryStore.ListResourcesAsync(scopeType, limit, ct);
        resources = await FilterResourcesBySessionAsync(sessionId, scopeType, resources, ct);
        response.Resources.Add(resources);
        return response;
    }

    public async Task<SessionTraceResponse?> GetSessionTraceAsync(string sessionId, CancellationToken ct = default)
    {
        if (_executionTraceStore == null)
            return null;

        var state = await GetSessionStateAsync(sessionId, ct);
        if (state == null)
            return null;

        var executionId = !string.IsNullOrWhiteSpace(state.ExecutionId)
            ? state.ExecutionId
            : await TryResolveExecutionIdAsync(state.CoordinatorId, ct);

        if (string.IsNullOrWhiteSpace(executionId))
            return null;

        var trace = await _executionTraceStore.LoadAsync(executionId, ct);
        if (trace == null)
            return null;

        return new SessionTraceResponse { Trace = trace };
    }

    public async Task<SessionMemoryResourcesResponse?> ListSessionsAsync(int limit, CancellationToken ct = default)
    {
        if (_memoryStore == null)
            return null;

        return await ListSessionMemoryResourcesAsync(string.Empty, MemoryScopeType.Session, limit, ct);
    }

    private string NormalizeProviderName(string? providerName)
    {
        var candidate = string.IsNullOrWhiteSpace(providerName)
            ? _options.DefaultProviderName
            : providerName.Trim();
        return string.IsNullOrWhiteSpace(candidate)
            ? AevatarAgentsConstants.DefaultProviderName
            : candidate;
    }

    private static string NormalizeSessionId(string? sessionId)
    {
        if (!string.IsNullOrWhiteSpace(sessionId))
            return sessionId.Trim();

        return Guid.NewGuid().ToString("N");
    }

    private static string BuildCoordinatorRawId(string sessionId)
        => DeterministicGuid.FromString($"cognitive:{sessionId}:coordinator").ToString("D");

    private static List<Guid> BuildWorkerIds(string sessionId, int count)
    {
        var ids = new List<Guid>(capacity: Math.Max(0, count));
        for (var i = 0; i < count; i++)
        {
            ids.Add(DeterministicGuid.FromString($"cognitive:{sessionId}:worker:{i}"));
        }
        return ids;
    }

    private async Task<List<string>> ConfigureWorkersAsync(
        IReadOnlyList<Guid> workerIds,
        string sessionId,
        bool enableSessionMemory,
        bool enableAgentMemory,
        CancellationToken ct)
    {
        var result = new List<string>(workerIds.Count);
        foreach (var id in workerIds)
        {
            var raw = id.ToString("D");
            var actorId = AgentId.Normalize<RoleAIGAgent>(raw);
            result.Add(actorId);

            var actor = await _actorManager.GetActorAsync(actorId);
            if (actor == null)
                continue;

            try
            {
                if (actor.GetAgent() is RoleAIGAgent worker)
                {
                    worker.ConfigureSessionContext(sessionId, enableSessionMemory, enableAgentMemory);
                    worker.SetStepExecutionHandler(new CognitiveStepExecutionHandler());
                }
            }
            catch (NotSupportedException)
            {
                _logger.LogWarning(
                    "Session configuration requires local runtime to access worker agent {AgentId}",
                    actorId);
            }
        }

        return result;
    }

    private void RegisterWorkflows(CognitiveCoordinatorGAgent coordinator, WorkflowDefinition workflow)
    {
        if (_options.RegisterAllWorkflows)
        {
            foreach (var name in _workflowRegistry.List())
            {
                var wf = _workflowRegistry.Get(name);
                if (wf != null)
                {
                    coordinator.RegisterWorkflow(wf);
                }
            }

            return;
        }

        coordinator.RegisterWorkflow(workflow);
    }

    private static Dictionary<string, object> ConvertVariables(StartSessionRequest request)
    {
        var variables = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (var (key, value) in request.Variables)
        {
            variables[key] = ProtoValueConverter.FromProto(value);
        }

        return variables;
    }

    private async Task TryRefreshSessionStateAsync(SessionState state, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(state.CoordinatorId))
            return;

        var actor = await _actorManager.GetActorAsync(state.CoordinatorId);
        if (actor == null)
            return;

        try
        {
            var agentState = await actor.InvokeAsync<AevatarAIAgentState>("GetState");
            var coordinatorState = TryUnpackCoordinatorState(agentState);
            if (coordinatorState == null)
                return;

            state.ExecutionId = coordinatorState.ExecutionId ?? string.Empty;
            state.Status = coordinatorState.Status;
            state.UpdatedAt = Timestamp.FromDateTime(DateTime.UtcNow);
            await _sessionStore.SaveAsync(state, ct);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to refresh session state for {SessionId}", state.SessionId);
        }
    }

    private async Task<string> TryResolveExecutionIdAsync(string coordinatorId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(coordinatorId))
            return string.Empty;

        var actor = await _actorManager.GetActorAsync(coordinatorId);
        if (actor == null)
            return string.Empty;

        try
        {
            var agentState = await actor.InvokeAsync<AevatarAIAgentState>("GetState");
            var coordinatorState = TryUnpackCoordinatorState(agentState);
            return coordinatorState?.ExecutionId ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private async Task<string> ResolveExecutionIdWithRetryAsync(string coordinatorId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(coordinatorId))
            return string.Empty;

        const int maxAttempts = 10;
        const int delayMs = 50;
        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            var executionId = await TryResolveExecutionIdAsync(coordinatorId, ct);
            if (!string.IsNullOrWhiteSpace(executionId))
                return executionId;

            if (attempt < maxAttempts - 1)
                await Task.Delay(delayMs, ct);
        }

        _logger.LogDebug("ExecutionId not ready for coordinator {CoordinatorId}", coordinatorId);
        return string.Empty;
    }

    private async Task EmitSessionStartTraceAsync(
        SessionState state,
        string? executionId,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(state.SessionId) || string.IsNullOrWhiteSpace(state.CoordinatorId))
            return;

        var evt = SessionTraceEventFactory.CreateSessionStart(
            sessionId: state.SessionId,
            executionId: string.IsNullOrWhiteSpace(executionId) ? null : executionId,
            agentId: state.CoordinatorId,
            workflowName: state.WorkflowName);

        await PublishSessionTraceAsync(state.CoordinatorId, evt, ct);
    }

    private async Task PublishSessionTraceAsync(
        string coordinatorId,
        ExecutionTraceEvent evt,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(coordinatorId))
            return;

        var actor = await _actorManager.GetActorAsync(coordinatorId);
        if (actor == null)
            return;

        try
        {
            await actor.PublishEventAsync(evt, EventDirection.Down, ct);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to publish session trace for {CoordinatorId}", coordinatorId);
        }
    }

    private static CognitiveCoordinatorState? TryUnpackCoordinatorState(AevatarAIAgentState state)
    {
        if (state.CustomState == null)
            return null;

        try
        {
            return state.CustomState.Unpack<CognitiveCoordinatorState>();
        }
        catch
        {
            return null;
        }
    }

    private static AevatarAIAgentState NormalizeHistory(
        AevatarAIAgentState state,
        bool includeHistory,
        int historyLimit)
    {
        var copy = state.Clone();
        if (!includeHistory)
        {
            copy.History.Clear();
            return copy;
        }

        var trimmed = TrimHistory(copy.History, historyLimit);
        copy.History.Clear();
        copy.History.Add(trimmed);
        return copy;
    }

    private static IEnumerable<AevatarChatMessage> TrimHistory(
        IEnumerable<AevatarChatMessage> history,
        int limit)
    {
        if (limit <= 0)
            return history;

        var list = history.ToList();
        if (list.Count <= limit)
            return list;

        return list.Skip(list.Count - limit);
    }

    private static string BuildMemoryId(MemoryScopeType scopeType, string scopeId)
    {
        var type = scopeType.ToString().ToLowerInvariant();
        var id = (scopeId ?? string.Empty).Trim();
        return $"{type}::{id}";
    }

    private async Task<bool> IsAgentInSessionAsync(
        string sessionId,
        string agentId,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(sessionId) || string.IsNullOrWhiteSpace(agentId))
            return false;

        var session = await _sessionStore.GetAsync(sessionId, ct);
        if (session == null)
            return false;

        return session.AgentIds.Contains(agentId);
    }

    private async Task<List<MemoryResourceSummary>> FilterResourcesBySessionAsync(
        string sessionId,
        MemoryScopeType scopeType,
        IReadOnlyList<MemoryResourceSummary> resources,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
            return resources.ToList();

        if (scopeType == MemoryScopeType.Session)
        {
            return resources
                .Where(r => string.Equals(r.Scope?.ScopeId, sessionId, StringComparison.Ordinal))
                .ToList();
        }

        if (scopeType == MemoryScopeType.PrivateAgent)
        {
            var session = await _sessionStore.GetAsync(sessionId, ct);
            if (session == null || session.AgentIds.Count == 0)
                return [];

            var agentSet = new HashSet<string>(session.AgentIds, StringComparer.Ordinal);
            return resources
                .Where(r => !string.IsNullOrWhiteSpace(r.Scope?.ScopeId) && agentSet.Contains(r.Scope.ScopeId))
                .ToList();
        }

        return resources.ToList();
    }

    private async Task AppendSessionIndexEntryAsync(SessionState state, CancellationToken ct)
    {
        if (_memoryStore == null)
            return;

        if (string.IsNullOrWhiteSpace(state.SessionId))
            return;

        var scope = new MemoryScope
        {
            Type = MemoryScopeType.Session,
            ScopeId = state.SessionId
        };
        var memoryId = SessionHistoryService.BuildSessionMemoryId(state.SessionId);

        var entry = new MemoryEntry
        {
            EntryId = Guid.NewGuid().ToString("N"),
            MemoryId = memoryId,
            Scope = scope,
            AgentId = state.CoordinatorId ?? string.Empty,
            Role = "system",
            Content = $"session:{state.WorkflowName}",
            CreatedAt = Timestamp.FromDateTime(DateTime.UtcNow)
        };

        entry.Tags["workflow_name"] = state.WorkflowName ?? string.Empty;
        entry.Tags["provider_name"] = state.ProviderName ?? string.Empty;
        entry.Tags["coordinator_id"] = state.CoordinatorId ?? string.Empty;

        try
        {
            await _memoryStore.AppendAsync(entry, ct);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to append session index entry for {SessionId}", state.SessionId);
        }
    }

    private static TAgent RequireAgent<TAgent>(IGAgentActor actor) where TAgent : class
    {
        try
        {
            if (actor.GetAgent() is TAgent typed)
                return typed;
        }
        catch (NotSupportedException ex)
        {
            throw new InvalidOperationException(
                "Session API requires local runtime to access agent instances.",
                ex);
        }

        throw new InvalidOperationException("Failed to access agent instance.");
    }
}
