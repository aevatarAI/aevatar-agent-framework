using Aevatar.Agents;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.AGUI;
using Aevatar.Agents.AI;
using Aevatar.Agents.AI.Core;
using Aevatar.Agents.AI.Core.Configuration;
using Aevatar.Agents.AI.Core.Messages;
using Aevatar.Agents.Core.Hierarchy;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;

namespace Aevatar.Workshop.RoleWorkspace;

public sealed class WorkshopRoleWorkspace
{
    // ============================================================
    // 中文 + ASCII:
    // Role Workspace: 负责 YAML 角色加载、实例化与层级编排。
    // ============================================================
    private const string DefaultRootRole = "sisyphus";
    private const string WorkspaceSessionId = "role_workspace";

    private readonly object _gate = new();
    private readonly IGAgentActorManager _actorManager;
    private readonly RoleAgentFactory _roleAgentFactory;
    private readonly GlobalAgentYamlRegistry _registry;
    private readonly WorkshopGroupAgUiHub _hub;
    private readonly ILogger<WorkshopRoleWorkspace> _logger;

    private readonly Dictionary<string, RoleInstance> _instances = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<RoleEdge> _edges = new();

    public string RootRole { get; private set; } = DefaultRootRole;

    public WorkshopRoleWorkspace(
        IGAgentActorManager actorManager,
        RoleAgentFactory roleAgentFactory,
        GlobalAgentYamlRegistry registry,
        WorkshopGroupAgUiHub hub,
        ILogger<WorkshopRoleWorkspace> logger)
    {
        _actorManager = actorManager ?? throw new ArgumentNullException(nameof(actorManager));
        _roleAgentFactory = roleAgentFactory ?? throw new ArgumentNullException(nameof(roleAgentFactory));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _hub = hub ?? throw new ArgumentNullException(nameof(hub));
        _logger = logger;
    }

    public IReadOnlyList<string> GetKnownRoles()
        => _registry.GetKnownRoles().OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();

    public IReadOnlyList<RoleInstanceSnapshot> GetInstances()
    {
        lock (_gate)
        {
            return _instances.Values
                .Select(instance => new RoleInstanceSnapshot(
                    instance.Role,
                    instance.ActorId,
                    instance.CreatedAt,
                    string.Equals(instance.Role, RootRole, StringComparison.OrdinalIgnoreCase)))
                .OrderBy(x => x.Role, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }

    public RoleGraphSnapshot GetGraphSnapshot()
    {
        lock (_gate)
        {
            return new RoleGraphSnapshot(
                RootRole,
                _instances.Values
                    .Select(instance => new RoleGraphNode(
                        instance.Role,
                        instance.ActorId,
                        string.Equals(instance.Role, RootRole, StringComparison.OrdinalIgnoreCase)))
                    .OrderBy(x => x.Role, StringComparer.OrdinalIgnoreCase)
                    .ToList(),
                _edges
                    .Select(edge => new RoleGraphEdge(edge.ParentRole, edge.ChildRole))
                    .OrderBy(x => x.ParentRole, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(x => x.ChildRole, StringComparer.OrdinalIgnoreCase)
                    .ToList());
        }
    }

    public async Task<RoleInstanceSnapshot> EnsureRoleAsync(
        string role,
        bool linkToRoot,
        CancellationToken ct)
    {
        var key = NormalizeRole(role);
        if (key.Length == 0)
            throw new ArgumentException("role is required", nameof(role));

        RoleInstance? existing;
        lock (_gate)
        {
            if (_instances.TryGetValue(key, out existing))
            {
                return new RoleInstanceSnapshot(
                    existing.Role,
                    existing.ActorId,
                    existing.CreatedAt,
                    string.Equals(existing.Role, RootRole, StringComparison.OrdinalIgnoreCase));
            }
        }

        var yamlPath = AgentYamlConfigLoader.GetConfigFilePath(key);
        if (!_registry.HasRole(key) && !File.Exists(yamlPath))
            throw new InvalidOperationException($"role yaml not found: {key}");

        var actor = await _actorManager.CreateAndRegisterAsync<WorkshopRoleAgent>(key, ct);
        var agent = (WorkshopRoleAgent)actor.GetAgent();
        agent.InitializeRole(key);
        await _roleAgentFactory.ApplyYamlAsync(agent, key, ct);

        var created = new RoleInstance(key, actor.Id, actor, DateTimeOffset.UtcNow);

        lock (_gate)
        {
            _instances[key] = created;
        }

        if (linkToRoot && !string.Equals(key, RootRole, StringComparison.OrdinalIgnoreCase))
        {
            await EnsureRootAsync(ct);
            await LinkAsync(RootRole, key, ct);
        }

        return new RoleInstanceSnapshot(
            created.Role,
            created.ActorId,
            created.CreatedAt,
            string.Equals(created.Role, RootRole, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<RoleInstanceSnapshot> EnsureRootAsync(CancellationToken ct)
        => await EnsureRoleAsync(RootRole, linkToRoot: false, ct);

    public async Task SetRootAsync(string role, CancellationToken ct)
    {
        var key = NormalizeRole(role);
        if (key.Length == 0)
            throw new ArgumentException("role is required", nameof(role));

        await EnsureRoleAsync(key, linkToRoot: false, ct);
        RootRole = key;
    }

    public async Task LinkAsync(string parentRole, string childRole, CancellationToken ct)
    {
        var parentKey = NormalizeRole(parentRole);
        var childKey = NormalizeRole(childRole);
        if (parentKey.Length == 0 || childKey.Length == 0)
            throw new ArgumentException("parent/child role is required");

        var parent = await EnsureRoleAsync(parentKey, linkToRoot: false, ct);
        var child = await EnsureRoleAsync(childKey, linkToRoot: false, ct);

        var parentActor = await GetActorAsync(parent.ActorId, ct);
        var childActor = await GetActorAsync(child.ActorId, ct);

        await ActorHierarchyCoordinator.LinkAsync(parentActor, childActor, _logger, ct);

        lock (_gate)
        {
            _edges.Add(new RoleEdge(parentKey, childKey));
        }
    }

    public async Task UnlinkAsync(string parentRole, string childRole, CancellationToken ct)
    {
        var parentKey = NormalizeRole(parentRole);
        var childKey = NormalizeRole(childRole);
        if (parentKey.Length == 0 || childKey.Length == 0)
            throw new ArgumentException("parent/child role is required");

        var child = await EnsureRoleAsync(childKey, linkToRoot: false, ct);
        var childActor = await GetActorAsync(child.ActorId, ct);
        var parentActor = TryGetActorByRole(parentKey);

        await ActorHierarchyCoordinator.UnlinkAsync(childActor, parentActor, _logger, ct);

        lock (_gate)
        {
            _edges.Remove(new RoleEdge(parentKey, childKey));
        }
    }

    public async Task<bool> RemoveInstanceAsync(string role, CancellationToken ct)
    {
        var key = NormalizeRole(role);
        if (key.Length == 0)
            throw new ArgumentException("role is required", nameof(role));

        if (string.Equals(key, RootRole, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("root role cannot be removed");

        RoleInstance? instance;
        List<RoleEdge> edges;
        lock (_gate)
        {
            if (!_instances.TryGetValue(key, out instance))
                return false;

            edges = _edges
                .Where(e => string.Equals(e.ParentRole, key, StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(e.ChildRole, key, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        foreach (var edge in edges)
        {
            RoleInstance? parentInstance;
            RoleInstance? childInstance;
            lock (_gate)
            {
                _instances.TryGetValue(edge.ParentRole, out parentInstance);
                _instances.TryGetValue(edge.ChildRole, out childInstance);
            }

            if (childInstance == null)
                continue;

            await ActorHierarchyCoordinator.UnlinkAsync(
                childInstance.Actor,
                parentInstance?.Actor,
                _logger,
                ct);
        }

        lock (_gate)
        {
            _edges.RemoveWhere(e => string.Equals(e.ParentRole, key, StringComparison.OrdinalIgnoreCase) ||
                                   string.Equals(e.ChildRole, key, StringComparison.OrdinalIgnoreCase));
            _instances.Remove(key);
        }

        await _actorManager.DeactivateAndUnregisterAsync(instance.ActorId, ct);
        return true;
    }

    public async Task<string> PublishChatAsync(ChatRequestEvent request, CancellationToken ct)
    {
        if (request == null)
            throw new ArgumentNullException(nameof(request));

        var message = (request.Message ?? string.Empty).Replace("\r", "").Trim();
        if (message.Length == 0)
            throw new ArgumentException("message is required", nameof(request));

        var root = await EnsureRootAsync(ct);
        var rootActor = await GetActorAsync(root.ActorId, ct);

        request.RequestId = NormalizeRequestId(request.RequestId);
        request.Message = message;
        request.Timestamp ??= Timestamp.FromDateTime(DateTime.UtcNow);
        request.Context[ChatRequest.SessionIdKey] = WorkspaceSessionId;

        _hub.PublishUserMessage(request.RequestId, message);

        await rootActor.PublishEventAsync(
            request,
            EventDirection.Down,
            ct,
            isInternalCall: false);

        return request.RequestId;
    }

    public IReadOnlyList<AgUiMessage> GetMessagesSnapshot(int maxMessages)
        => _hub.GetMessagesSnapshot(maxMessages);

    public IAsyncEnumerable<AgUiEvent> SubscribeAsync(CancellationToken ct)
        => _hub.SubscribeAsync(ct);

    private async Task<IGAgentActor> GetActorAsync(string actorId, CancellationToken ct)
    {
        var actor = await _actorManager.GetActorAsync(actorId);
        if (actor == null)
            throw new InvalidOperationException($"actor not found: {actorId}");
        return actor;
    }

    private IGAgentActor? TryGetActorByRole(string role)
    {
        lock (_gate)
        {
            if (!_instances.TryGetValue(role, out var instance))
                return null;
            return instance.Actor;
        }
    }

    private static string NormalizeRole(string? role)
        => GlobalAgentYamlRegistry.NormalizeRoleKey(role);

    private static string NormalizeRequestId(string? requestId)
    {
        var id = (requestId ?? string.Empty).Trim();
        return id.Length == 0 ? Guid.NewGuid().ToString("N") : id;
    }

    private sealed record RoleInstance(
        string Role,
        string ActorId,
        IGAgentActor Actor,
        DateTimeOffset CreatedAt);

    private readonly record struct RoleEdge(string ParentRole, string ChildRole);
}

public sealed record RoleInstanceSnapshot(
    string Role,
    string ActorId,
    DateTimeOffset CreatedAt,
    bool IsRoot);

public sealed record RoleGraphSnapshot(
    string RootRole,
    IReadOnlyList<RoleGraphNode> Nodes,
    IReadOnlyList<RoleGraphEdge> Edges);

public sealed record RoleGraphNode(
    string Role,
    string ActorId,
    bool IsRoot);

public sealed record RoleGraphEdge(
    string ParentRole,
    string ChildRole);
