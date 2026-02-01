using System.Collections.Concurrent;
using System.Text.Json;
using System.Linq;
using System.Reflection;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.Extensions;
using Aevatar.Agents.Abstractions.Helpers;
using Aevatar.Agents.Abstractions.Memory;
using Aevatar.Agents.Abstractions.Tracing;
using Aevatar.Agents.AI;
using Aevatar.Agents.AI.Core;
using Aevatar.Agents.AI.Core.Configuration;
using Aevatar.Agents.Cognitive.Engine;
using Aevatar.Agents.Cognitive.Primitives;
using Aevatar.CognitiveMesh.Dsl;
using Aevatar.CognitiveMesh.Dsl.Models;
using Aevatar.CognitiveMesh.Dsl.Options;
using Aevatar.CognitiveMesh.Dsl.Validation;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aevatar.Agents.Sessions;

public sealed class CognitiveSessionService
{
    private readonly IGAgentActorManager _actorManager;
    private readonly ISessionStore _sessionStore;
    private readonly RoleAgentFactory _roleAgentFactory;
    private readonly GlobalAgentYamlRegistry _roleRegistry;
    private readonly IMemoryStore? _memoryStore;
    private readonly IExecutionTraceStore? _executionTraceStore;
    private readonly CognitiveSessionOptions _options;
    private readonly ILogger<CognitiveSessionService> _logger;
    private readonly WorkflowParser _workflowParser = new();

    private readonly ConcurrentDictionary<string, SemaphoreSlim> _sessionGates = new(StringComparer.Ordinal);

    public CognitiveSessionService(
        IGAgentActorManager actorManager,
        ISessionStore sessionStore,
        RoleAgentFactory roleAgentFactory,
        GlobalAgentYamlRegistry roleRegistry,
        IMemoryStore? memoryStore,
        IExecutionTraceStore? executionTraceStore,
        IOptions<CognitiveSessionOptions> options,
        ILogger<CognitiveSessionService> logger)
    {
        _actorManager = actorManager ?? throw new ArgumentNullException(nameof(actorManager));
        _sessionStore = sessionStore ?? throw new ArgumentNullException(nameof(sessionStore));
        _roleAgentFactory = roleAgentFactory ?? throw new ArgumentNullException(nameof(roleAgentFactory));
        _roleRegistry = roleRegistry ?? throw new ArgumentNullException(nameof(roleRegistry));
        _memoryStore = memoryStore;
        _executionTraceStore = executionTraceStore;
        _options = options?.Value ?? new CognitiveSessionOptions();
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public WorkflowListResponse ListWorkflows()
    {
        var response = new WorkflowListResponse();
        var dir = ResolveWorkflowsDirectory();
        if (!Directory.Exists(dir))
            return response;

        var list = Directory.EnumerateFiles(dir, "*.*", SearchOption.TopDirectoryOnly)
            .Where(IsWorkflowFile)
            .Select(path => Path.GetFileNameWithoutExtension(path))
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var name in list)
        {
            response.Workflows.Add(new WorkflowInfo
            {
                Name = name,
                Version = string.Empty,
                Description = string.Empty
            });
        }

        return response;
    }

    public async Task<SessionState> StartSessionAsync(
        StartSessionRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var workflowName = (request.WorkflowName ?? string.Empty).Trim();
        if (workflowName.Length == 0)
            throw new ArgumentException("workflow_name is required.", nameof(request));

        var sessionId = NormalizeSessionId(request.SessionId);
        if (await _sessionStore.ExistsAsync(sessionId, ct))
            throw new InvalidOperationException($"Session '{sessionId}' already exists.");

        var workflowPath = ResolveWorkflowPath(workflowName);
        string raw;
        try
        {
            raw = await File.ReadAllTextAsync(workflowPath, ct);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to read workflow '{workflowName}': {ex.Message}", ex);
        }

        var now = Timestamp.FromDateTime(DateTime.UtcNow);
        var lazy = _options.LazyLoadRoles;
        var state = new SessionState
        {
            SessionId = sessionId,
            WorkflowName = workflowName,
            WorkflowPath = workflowPath,
            CreatedAt = now,
            UpdatedAt = now
        };
        state.Tags["workflow_name"] = workflowName;
        state.Tags["workflow_path"] = workflowPath;

        if (TryParseCognitiveWorkflow(raw, out var cognitive))
        {
            state.Tags["workflow_kind"] = "cognitive";
            state.Roles.Add(BuildSessionRoles(cognitive, sessionId, loaded: !lazy));
        }
        else
        {
            MeshDefinition def;
            try
            {
                def = CompileWorkflow(raw);
            }
            catch (DslCompilationException ex)
            {
                var errors = ex.Errors?.Select(e => $"{e.Code}:{e.Message}") ?? Array.Empty<string>();
                var message = string.Join("; ", errors);
                throw new InvalidOperationException($"Workflow compile failed: {message}", ex);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Workflow compile failed: {ex.Message}", ex);
            }

            state.Tags["workflow_kind"] = "mesh";
            state.Roles.Add(BuildSessionRoles(def, sessionId, loaded: !lazy));
        }

        await _sessionStore.SaveAsync(state, ct);

        if (!lazy)
        {
            state = await EnsureRoleAgentsLoadedAsync(state, ct);
        }

        await AppendSessionIndexEntryAsync(state, ct);
        return state;
    }

    public Task<SessionState?> GetSessionStateAsync(string sessionId, CancellationToken ct = default)
        => _sessionStore.GetAsync(sessionId, ct);

    public async Task<SessionRole?> EnsureRoleAgentLoadedAsync(
        string sessionId,
        string nodeId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
            return null;

        var state = await GetSessionStateAsync(sessionId, ct);
        if (state == null)
            return null;

        return await EnsureRoleAgentLoadedAsync(state, nodeId, ct);
    }

    public async Task<SessionAgentsResponse?> GetSessionAgentsAsync(string sessionId, CancellationToken ct = default)
    {
        var state = await GetSessionStateAsync(sessionId, ct);
        if (state == null)
            return null;

        var response = new SessionAgentsResponse
        {
            SessionId = state.SessionId
        };
        response.Roles.Add(state.Roles);
        response.AgentIds.Add(state.Roles
            .Select(r => r.AgentId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal));
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

        state = await EnsureRoleAgentsLoadedAsync(state, ct);

        var response = new SessionAgentStatesResponse();
        var agentIds = CollectAgentIds(state);
        var actors = await _actorManager.GetActorsAsync(agentIds);

        foreach (var actor in actors)
        {
            try
            {
                var agentState = await actor.InvokeAsync<AevatarAIAgentState>("GetState");
                var normalized = NormalizeHistory(agentState, includeHistory, historyLimit);
                response.Agents.Add(new SessionAgentStateBundle
                {
                    AgentId = actor.Id,
                    State = normalized
                });
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to read agent state for {AgentId}", actor.Id);
            }
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

        state = await EnsureRoleAgentsLoadedAsync(state, ct);

        var response = new SessionAgentHistoriesResponse();
        var agentIds = CollectAgentIds(state);
        var actors = await _actorManager.GetActorsAsync(agentIds);

        foreach (var actor in actors)
        {
            try
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
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to read agent history for {AgentId}", actor.Id);
            }
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

        if (!state.Tags.TryGetValue("execution_id", out var executionId))
            return null;

        if (string.IsNullOrWhiteSpace(executionId))
            return null;

        var trace = await _executionTraceStore.LoadAsync(executionId, ct);
        return trace == null ? null : new SessionTraceResponse { Trace = trace };
    }

    public async Task<SessionMemoryResourcesResponse?> ListSessionsAsync(int limit, CancellationToken ct = default)
    {
        if (_memoryStore == null)
            return null;

        return await ListSessionMemoryResourcesAsync(string.Empty, MemoryScopeType.Session, limit, ct);
    }

    private static string NormalizeSessionId(string? sessionId)
    {
        if (!string.IsNullOrWhiteSpace(sessionId))
            return sessionId.Trim();

        return Guid.NewGuid().ToString("N");
    }

    private static bool IsWorkflowFile(string path)
    {
        var ext = Path.GetExtension(path);
        return ext.Equals(".yaml", StringComparison.OrdinalIgnoreCase)
               || ext.Equals(".yml", StringComparison.OrdinalIgnoreCase)
               || ext.Equals(".json", StringComparison.OrdinalIgnoreCase);
    }

    private string ResolveWorkflowsDirectory(string? workflowName = null)
    {
        if (!string.IsNullOrWhiteSpace(_options.WorkflowsDirectory))
            return ExpandHome(_options.WorkflowsDirectory!.Trim());

        var cwd = Directory.GetCurrentDirectory();
        var appName = Assembly.GetEntryAssembly()?.GetName().Name ?? string.Empty;
        var resolved = CognitiveSessionWorkflows.ResolveWorkflowsDirectory(cwd, appName, workflowName);
        // #region agent log
        System.IO.File.AppendAllText("/Users/zhaoyiqi/Code/aevatar-agent-framework/.cursor/debug.log",
            System.Text.Json.JsonSerializer.Serialize(new
            {
                sessionId = string.Empty,
                runId = string.Empty,
                hypothesisId = "H40",
                location = "CognitiveSessionService.cs:ResolveWorkflowsDirectory",
                message = "workflows_dir_resolved",
                data = new
                {
                    cwd,
                    appName,
                    workflowName = workflowName ?? string.Empty,
                    resolved,
                    exists = !string.IsNullOrWhiteSpace(resolved) && Directory.Exists(resolved)
                },
                timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            }) + Environment.NewLine);
        // #endregion

        if (!string.IsNullOrWhiteSpace(resolved) && Directory.Exists(resolved))
            return resolved!;

        var configDir = ResolveAevatarConfigDirectory();
        return Path.Combine(configDir, "workflows");
    }

    private string ResolveWorkflowPath(string workflowName)
    {
        var name = (workflowName ?? string.Empty).Trim();
        if (name.Length == 0)
            throw new ArgumentException("workflow_name is required.", nameof(workflowName));

        if (File.Exists(name))
            return Path.GetFullPath(name);

        var dir = ResolveWorkflowsDirectory(name);
        var direct = Path.Combine(dir, name);
        if (File.Exists(direct))
            return Path.GetFullPath(direct);

        if (!Path.HasExtension(name))
        {
            var yaml = Path.Combine(dir, $"{name}.yaml");
            if (File.Exists(yaml))
                return Path.GetFullPath(yaml);

            var yml = Path.Combine(dir, $"{name}.yml");
            if (File.Exists(yml))
                return Path.GetFullPath(yml);

            var json = Path.Combine(dir, $"{name}.json");
            if (File.Exists(json))
                return Path.GetFullPath(json);
        }

        var fallback = CognitiveSessionWorkflows.TryResolveWorkflowPath(
            name,
            Directory.GetCurrentDirectory(),
            Assembly.GetEntryAssembly()?.GetName().Name);
        if (!string.IsNullOrWhiteSpace(fallback))
            return fallback!;

        // #region agent log
        System.IO.File.AppendAllText("/Users/zhaoyiqi/Code/aevatar-agent-framework/.cursor/debug.log",
            System.Text.Json.JsonSerializer.Serialize(new
            {
                sessionId = string.Empty,
                runId = string.Empty,
                hypothesisId = "H41",
                location = "CognitiveSessionService.cs:ResolveWorkflowPath",
                message = "workflow_path_not_found",
                data = new
                {
                    workflowName = workflowName ?? string.Empty,
                    dir,
                    directExists = File.Exists(direct),
                    yamlExists = File.Exists(Path.Combine(dir, $"{name}.yaml")),
                    ymlExists = File.Exists(Path.Combine(dir, $"{name}.yml")),
                    jsonExists = File.Exists(Path.Combine(dir, $"{name}.json")),
                    cwd = Directory.GetCurrentDirectory(),
                    appName = Assembly.GetEntryAssembly()?.GetName().Name ?? string.Empty
                },
                timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            }) + Environment.NewLine);
        // #endregion

        throw new FileNotFoundException($"Workflow '{workflowName}' not found.", workflowName);
    }

    private MeshDefinition CompileWorkflow(string raw)
    {
        raw = (raw ?? string.Empty).Replace("\r", "").Trim();
        if (raw.Length == 0)
            throw new ArgumentException("workflow YAML is empty.");

        var (json, nodeTypes) = MeshInputCoercer.CoerceToJson(raw);

        var options = CognitiveDslOptions.Default.With(
            allowedAgentTypes: MergeAllowedAgentTypes(nodeTypes),
            allowedConstraintTypes: CognitiveDslOptions.Default.AllowedConstraintTypes,
            metaAgentTypeName: "meta");

        var compiler = new CognitiveDslCompiler(options);
        return compiler.Compile(json);
    }

    private IReadOnlySet<string> MergeAllowedAgentTypes(IReadOnlySet<string> nodeTypes)
    {
        var set = new HashSet<string>(CognitiveDslOptions.Default.AllowedAgentTypes, StringComparer.OrdinalIgnoreCase);
        foreach (var role in _roleRegistry.GetKnownRoles())
            set.Add(role);
        foreach (var t in nodeTypes)
            set.Add(t);
        set.Add("meta");
        return set;
    }

    private List<SessionRole> BuildSessionRoles(MeshDefinition def, string sessionId, bool loaded)
    {
        var list = new List<SessionRole>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in def.Nodes)
        {
            var nodeId = (node.Id ?? string.Empty).Trim();
            if (nodeId.Length == 0 || !seen.Add(nodeId))
                continue;

            var role = ResolveRole(node);
            var agentId = BuildRoleActorId(sessionId, nodeId);
            list.Add(new SessionRole
            {
                NodeId = nodeId,
                Role = role,
                AgentId = agentId,
                Loaded = loaded
            });
        }

        return list;
    }

    private static List<SessionRole> BuildSessionRoles(WorkflowDefinition workflow, string sessionId, bool loaded)
    {
        var ordered = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var step in workflow.Steps)
        {
            CollectWorkflowAgents(step, ordered, seen);
        }

        var list = new List<SessionRole>();
        foreach (var role in ordered)
        {
            var nodeId = role.Trim();
            if (nodeId.Length == 0)
                continue;

            var agentId = BuildRoleActorId(sessionId, nodeId);
            list.Add(new SessionRole
            {
                NodeId = nodeId,
                Role = nodeId,
                AgentId = agentId,
                Loaded = loaded
            });
        }

        return list;
    }

    private static void CollectWorkflowAgents(StepDefinition step, List<string> ordered, ISet<string> seen)
    {
        if (step.Parameters.TryGetValue("agent", out var agentObj))
        {
            var agent = (agentObj?.ToString() ?? string.Empty).Trim();
            if (agent.Length > 0 && seen.Add(agent))
                ordered.Add(agent);
        }

        if (step.IfTrue != null)
        {
            foreach (var child in step.IfTrue)
                CollectWorkflowAgents(child, ordered, seen);
        }

        if (step.IfFalse != null)
        {
            foreach (var child in step.IfFalse)
                CollectWorkflowAgents(child, ordered, seen);
        }

        if (step.Generator != null)
            CollectWorkflowAgents(step.Generator, ordered, seen);

        if (step.Step != null)
            CollectWorkflowAgents(step.Step, ordered, seen);

        if (step.Parameters.TryGetValue("steps", out var stepsObj) &&
            stepsObj is IEnumerable<StepDefinition> steps)
        {
            foreach (var child in steps)
                CollectWorkflowAgents(child, ordered, seen);
        }
    }

    private bool TryParseCognitiveWorkflow(string raw, out WorkflowDefinition workflow)
    {
        workflow = null!;
        try
        {
            workflow = _workflowParser.Parse(raw);
        }
        catch
        {
            return false;
        }

        return workflow.Steps.Count > 0;
    }

    private static string ResolveRole(NodeSpec node)
    {
        if (node.Params != null &&
            node.Params.TryGetValue("role", out var roleElem) &&
            roleElem.ValueKind == JsonValueKind.String)
        {
            var role = (roleElem.GetString() ?? string.Empty).Trim();
            if (role.Length > 0) return role;
        }

        var type = (node.Type ?? string.Empty).Trim();
        return type.Length == 0 ? "role" : type;
    }

    private async Task<SessionState> EnsureRoleAgentsLoadedAsync(SessionState state, CancellationToken ct)
    {
        if (state.Roles.Count == 0)
            return state;

        var gate = _sessionGates.GetOrAdd(state.SessionId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            var updated = state.Clone();
            var changed = false;

            foreach (var role in updated.Roles)
            {
                if (await EnsureRoleAgentLoadedInternalAsync(updated.SessionId, role, ct))
                    changed = true;
            }

            if (changed)
            {
                updated.UpdatedAt = Timestamp.FromDateTime(DateTime.UtcNow);
                await _sessionStore.SaveAsync(updated, ct);
            }

            return updated;
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<SessionRole?> EnsureRoleAgentLoadedAsync(
        SessionState state,
        string nodeId,
        CancellationToken ct)
    {
        var key = NormalizeNodeId(nodeId);
        if (key.Length == 0 || state.Roles.Count == 0)
            return null;

        var gate = _sessionGates.GetOrAdd(state.SessionId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            var updated = state.Clone();
            var role = updated.Roles.FirstOrDefault(r => string.Equals(r.NodeId, key, StringComparison.Ordinal));
            if (role == null)
                return null;

            var changed = await EnsureRoleAgentLoadedInternalAsync(updated.SessionId, role, ct);
            if (changed)
            {
                updated.UpdatedAt = Timestamp.FromDateTime(DateTime.UtcNow);
                await _sessionStore.SaveAsync(updated, ct);
            }

            return role;
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<bool> EnsureRoleAgentLoadedInternalAsync(
        string sessionId,
        SessionRole role,
        CancellationToken ct)
    {
        var nodeId = NormalizeNodeId(role.NodeId);
        if (nodeId.Length == 0)
            return false;

        var rawId = BuildRoleRawId(sessionId, nodeId);
        var expectedAgentId = AgentId.Normalize<RoleAIGAgent>(rawId);
        var changed = UpdateRoleAgentId(role, expectedAgentId);

        if (role.Loaded)
            return changed;

        var actor = await GetOrCreateRoleActorAsync(rawId, expectedAgentId, ct);
        await TryConfigureRoleAgentAsync(actor, role, expectedAgentId, ct);

        role.Loaded = true;
        return true;
    }

    private static bool UpdateRoleAgentId(SessionRole role, string expectedAgentId)
    {
        if (string.Equals(role.AgentId, expectedAgentId, StringComparison.Ordinal))
            return false;

        role.AgentId = expectedAgentId;
        return true;
    }

    private async Task<IGAgentActor> GetOrCreateRoleActorAsync(
        string rawId,
        string expectedAgentId,
        CancellationToken ct)
    {
        var actor = await _actorManager.GetActorAsync(expectedAgentId);
        return actor ?? await _actorManager.CreateAndRegisterAsync<RoleAIGAgent>(rawId, ct);
    }

    private async Task TryConfigureRoleAgentAsync(
        IGAgentActor actor,
        SessionRole role,
        string expectedAgentId,
        CancellationToken ct)
    {
        try
        {
            if (actor.GetAgent() is RoleAIGAgent agent)
            {
                agent.InitializeRole(role.Role);
                await _roleAgentFactory.ApplyYamlAsync(agent, role.Role, ct);
            }
        }
        catch (NotSupportedException ex)
        {
            _logger.LogWarning(ex, "Role agent configuration requires local runtime for {AgentId}", expectedAgentId);
        }
    }

    private static string NormalizeNodeId(string? nodeId)
        => (nodeId ?? string.Empty).Trim();

    private static IReadOnlyList<string> CollectAgentIds(SessionState state)
    {
        return state.Roles
            .Select(r => r.AgentId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .ToList();
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

        foreach (var role in session.Roles)
        {
            if (string.Equals(role.AgentId, agentId, StringComparison.Ordinal))
                return true;

            if (string.IsNullOrWhiteSpace(role.AgentId))
            {
                var expected = BuildRoleActorId(sessionId, role.NodeId);
                if (string.Equals(expected, agentId, StringComparison.Ordinal))
                    return true;
            }
        }

        return false;
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
            if (session == null || session.Roles.Count == 0)
                return [];

            var agentSet = new HashSet<string>(StringComparer.Ordinal);
            foreach (var role in session.Roles)
            {
                var id = string.IsNullOrWhiteSpace(role.AgentId)
                    ? BuildRoleActorId(sessionId, role.NodeId)
                    : role.AgentId;
                if (!string.IsNullOrWhiteSpace(id))
                    agentSet.Add(id);
            }

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

        // #region agent log
        System.IO.File.AppendAllText("/Users/zhaoyiqi/Code/aevatar-agent-framework/.cursor/debug.log",
            JsonSerializer.Serialize(new
            {
                sessionId = state.SessionId,
                runId = string.Empty,
                hypothesisId = "H22",
                location = "CognitiveSessionService.cs:AppendSessionIndexEntryAsync",
                message = "session_index_append",
                data = new
                {
                    memoryStoreType = _memoryStore.GetType().FullName ?? _memoryStore.GetType().Name,
                    workflowName = state.WorkflowName ?? string.Empty
                },
                timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            }) + Environment.NewLine);
        // #endregion

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
            AgentId = string.Empty,
            Role = "system",
            Content = $"session:{state.WorkflowName}",
            CreatedAt = Timestamp.FromDateTime(DateTime.UtcNow)
        };

        entry.Tags["workflow_name"] = state.WorkflowName ?? string.Empty;
        entry.Tags["workflow_path"] = state.WorkflowPath ?? string.Empty;
        entry.Tags["role_count"] = state.Roles.Count.ToString();

        try
        {
            await _memoryStore.AppendAsync(entry, ct);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to append session index entry for {SessionId}", state.SessionId);
        }
    }

    private static string BuildRoleRawId(string sessionId, string nodeId)
    {
        var safeSession = SanitizeRawId(sessionId);
        var safeNode = SanitizeRawId(nodeId);
        if (safeSession.Length == 0) safeSession = "session";
        if (safeNode.Length == 0) safeNode = "node";
        return $"{safeSession}__{safeNode}";
    }

    private static string BuildRoleActorId(string sessionId, string nodeId)
        => AgentId.Normalize<RoleAIGAgent>(BuildRoleRawId(sessionId, nodeId));

    private static string SanitizeRawId(string value)
    {
        var trimmed = (value ?? string.Empty).Trim();
        return trimmed.Replace(AgentId.Separator, '_');
    }

    private static string ResolveAevatarConfigDirectory()
    {
        var fromEnv = (Environment.GetEnvironmentVariable("AEVATAR_CONFIG_DIR") ?? string.Empty).Trim();
        if (fromEnv.Length > 0)
            return ExpandHome(fromEnv);

        var secretsDir = (Environment.GetEnvironmentVariable("AEVATAR_SECRETS_DIR") ?? string.Empty).Trim();
        if (secretsDir.Length > 0)
            return ExpandHome(secretsDir);

        var secretsPath = (Environment.GetEnvironmentVariable("AEVATAR_SECRETS_PATH") ?? string.Empty).Trim();
        if (secretsPath.Length > 0)
            return Path.GetDirectoryName(ExpandHome(secretsPath)) ?? ExpandHome(secretsPath);

        var legacySecrets = (Environment.GetEnvironmentVariable("AEVATAR_SECRETS") ?? string.Empty).Trim();
        if (legacySecrets.Length > 0)
            return Path.GetDirectoryName(ExpandHome(legacySecrets)) ?? ExpandHome(legacySecrets);

        var configPath = (Environment.GetEnvironmentVariable("AEVATAR_CONFIG") ?? string.Empty).Trim();
        if (configPath.Length > 0)
            return Path.GetDirectoryName(ExpandHome(configPath)) ?? ExpandHome(configPath);

        var configPath2 = (Environment.GetEnvironmentVariable("AEVATAR_CONFIG_PATH") ?? string.Empty).Trim();
        if (configPath2.Length > 0)
            return Path.GetDirectoryName(ExpandHome(configPath2)) ?? ExpandHome(configPath2);

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, ".aevatar");
    }

    private static string ExpandHome(string path)
    {
        var p = (path ?? string.Empty).Trim().Replace('\\', '/');
        if (!p.StartsWith("~/", StringComparison.Ordinal))
            return p;

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, p[2..]);
    }
}
