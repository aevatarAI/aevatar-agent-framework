using System.Collections.Concurrent;
using Aevatar.Agents;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.Helpers;
using Aevatar.Agents.AGUI;
using Aevatar.Agents.AI;
using Aevatar.Agents.AI.Abstractions.Configuration;
using Aevatar.Agents.AI.Core;
using Aevatar.Agents.AI.Core.Configuration;
using Aevatar.Agents.AI.Core.Messages;
using Aevatar.Agents.Core.Hierarchy;
using Aevatar.Agents.Workspaces.Hubs;
using Aevatar.Agents.Workspaces.Models;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aevatar.Agents.Workspaces.Core;

public sealed class RoleWorkspaceService
{
    // ============================================================
    // 中文 + ASCII:
    // Role Workspace: 负责 YAML 角色加载、实例化与层级编排。
    // ============================================================
    private readonly object _gate = new();
    private readonly IGAgentActorManager _actorManager;
    private readonly RoleAgentFactory _roleAgentFactory;
    private readonly GlobalAgentYamlRegistry _registry;
    private readonly RoleAgUiHub _hub;
    private readonly IOptionsMonitor<LLMProvidersConfig> _llmProviders;
    private readonly IOptionsMonitor<RoleWorkspaceOptions> _options;
    private readonly ILogger<RoleWorkspaceService> _logger;

    private readonly Dictionary<string, RoleInstance> _instances = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<RoleEdge> _edges = new();
    private readonly ConcurrentDictionary<string, bool> _initializedAgents = new(StringComparer.Ordinal);

    public string RootRole { get; private set; }

    public RoleWorkspaceService(
        IGAgentActorManager actorManager,
        RoleAgentFactory roleAgentFactory,
        GlobalAgentYamlRegistry registry,
        RoleAgUiHub hub,
        IOptionsMonitor<LLMProvidersConfig> llmProviders,
        IOptionsMonitor<RoleWorkspaceOptions> options,
        ILogger<RoleWorkspaceService> logger)
    {
        _actorManager = actorManager ?? throw new ArgumentNullException(nameof(actorManager));
        _roleAgentFactory = roleAgentFactory ?? throw new ArgumentNullException(nameof(roleAgentFactory));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _hub = hub ?? throw new ArgumentNullException(nameof(hub));
        _llmProviders = llmProviders ?? throw new ArgumentNullException(nameof(llmProviders));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger;

        RootRole = NormalizeRole(_options.CurrentValue.RootRole);
        if (string.IsNullOrWhiteSpace(RootRole))
            RootRole = "sisyphus";
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

        var actor = await _actorManager.CreateAndRegisterAsync<RoleWorkspaceAgent>(key, ct);
        var agent = (RoleWorkspaceAgent)actor.GetAgent();
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

    public async Task<RoleInstanceSnapshot> RebuildRoleAsync(
        string role,
        bool linkToRoot,
        bool setAsRoot,
        CancellationToken ct)
    {
        var key = NormalizeRole(role);
        if (key.Length == 0)
            throw new ArgumentException("role is required", nameof(role));

        List<RoleEdge> edges;
        lock (_gate)
        {
            edges = _edges
                .Where(e => string.Equals(e.ParentRole, key, StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(e.ChildRole, key, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        if (edges.Count > 0)
        {
            await DetachRoleEdgesAsync(edges, ct);
        }

        await RemoveInstanceAsync(key, ct);
        var snapshot = await EnsureRoleAsync(key, linkToRoot, ct);

        if (setAsRoot)
        {
            await SetRootAsync(key, ct);
        }

        if (edges.Count > 0)
        {
            await ReattachRoleEdgesAsync(edges, ct);
        }

        return snapshot;
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
        request.Context[ChatRequest.SessionIdKey] = ResolveWorkspaceSessionId();

        _hub.PublishUserMessage(root.Role, request.RequestId, message);

        try
        {
            await EnsureAgentInitializedAsync(root.Role, root.ActorId, (AIGAgentBase)rootActor.GetAgent(), ct);
            await rootActor.PublishEventAsync(
                request,
                EventDirection.Down,
                ct,
                isInternalCall: false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            _hub.PublishAssistantEnd(root.Role, request.RequestId, BuildAssistantError("request cancelled"));
        }
        catch (Exception ex)
        {
            _hub.PublishAssistantEnd(root.Role, request.RequestId, BuildAssistantError(ex.Message));
        }

        return request.RequestId;
    }

    public async Task<string> PublishRoleChatAsync(string role, ChatRequestEvent request, CancellationToken ct)
    {
        if (request == null)
            throw new ArgumentNullException(nameof(request));

        var key = NormalizeRole(role);
        if (key.Length == 0)
            throw new ArgumentException("role is required", nameof(role));

        var message = (request.Message ?? string.Empty).Replace("\r", "").Trim();
        if (message.Length == 0)
            throw new ArgumentException("message is required", nameof(request));

        var instance = await EnsureRoleAsync(key, linkToRoot: false, ct);
        var actor = await GetActorAsync(instance.ActorId, ct);

        request.RequestId = NormalizeRequestId(request.RequestId);
        request.Message = message;
        request.Timestamp ??= Timestamp.FromDateTime(DateTime.UtcNow);
        request.Context[ChatRequest.SessionIdKey] = ResolveWorkspaceSessionId();

        _hub.PublishUserMessage(key, request.RequestId, message);

        try
        {
            await EnsureAgentInitializedAsync(instance.Role, instance.ActorId, (AIGAgentBase)actor.GetAgent(), ct);
            await actor.PublishEventAsync(
                request,
                EventDirection.Down,
                ct,
                isInternalCall: false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            _hub.PublishAssistantEnd(key, request.RequestId, BuildAssistantError("request cancelled"));
        }
        catch (Exception ex)
        {
            _hub.PublishAssistantEnd(key, request.RequestId, BuildAssistantError(ex.Message));
        }

        return request.RequestId;
    }

    public IReadOnlyList<AgUiMessage> GetMessagesSnapshot(int maxMessages)
        => _hub.GetMessagesSnapshot(maxMessages);

    public IReadOnlyList<AgUiMessage> GetMessagesSnapshot(string role, int maxMessages)
        => _hub.GetMessagesSnapshot(role, maxMessages);

    public IAsyncEnumerable<AgUiEvent> SubscribeAsync(CancellationToken ct)
        => _hub.SubscribeAsync(ct);

    public void ClearRoleMessages(string role)
        => _hub.ClearRoleMessages(role);

    public async Task<AIGAgentBase> GetRoleAgentAsync(string role, CancellationToken ct)
    {
        var key = NormalizeRole(role);
        if (key.Length == 0)
            throw new ArgumentException("role is required", nameof(role));

        var instance = await EnsureRoleAsync(key, linkToRoot: false, ct);
        var actor = await GetActorAsync(instance.ActorId, ct);
        return (AIGAgentBase)actor.GetAgent();
    }

    private async Task EnsureAgentInitializedAsync(
        string role,
        string actorId,
        AIGAgentBase agent,
        CancellationToken ct)
    {
        if (_initializedAgents.ContainsKey(actorId))
            return;

        var providerName = ResolveProviderName(_llmProviders.CurrentValue);
        if (string.IsNullOrWhiteSpace(providerName))
        {
            throw new InvalidOperationException(
                "LLMProviders.default 未配置。请先通过 Aevatar.Config 或 appsettings.secrets.json 设置默认 provider。");
        }

        var options = _options.CurrentValue ?? new RoleWorkspaceOptions();
        if (string.IsNullOrWhiteSpace(agent.SystemPrompt))
            agent.SystemPrompt = options.SystemPrompt ?? string.Empty;

        var yamlConfig = BuildYamlConfigAction(options, role);
        await agent.InitializeAsync(
            providerName,
            config =>
            {
                config.Temperature = (float)options.Temperature;
                config.MaxOutputTokens = options.MaxOutputTokens;
                yamlConfig?.Invoke(config);
            },
            ct);

        _initializedAgents.TryAdd(actorId, true);
    }

    private Action<AevatarAIAgentConfig>? BuildYamlConfigAction(RoleWorkspaceOptions options, string role)
    {
        if (!options.EnableAgentYaml)
            return null;

        var key = (role ?? string.Empty).Trim();
        if (key.Length == 0)
            return null;

        return _roleAgentFactory.BuildYamlConfigAction(key);
    }

    private static string ResolveProviderName(LLMProvidersConfig config)
    {
        var fallback = config.Providers.Keys
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault() ?? string.Empty;

        return string.IsNullOrWhiteSpace(config.Default)
            ? fallback.Trim()
            : config.Default.Trim();
    }

    private string ResolveWorkspaceSessionId()
    {
        var id = (_options.CurrentValue.WorkspaceSessionId ?? string.Empty).Trim();
        return id.Length == 0 ? "role_workspace" : id;
    }

    private static string BuildAssistantError(string message)
    {
        var trimmed = (message ?? string.Empty).Trim();
        if (trimmed.Length == 0)
            trimmed = "request failed";
        return $"\n[workspace] {trimmed}\n";
    }

    private async Task<IGAgentActor> GetActorAsync(string actorId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(actorId))
            throw new ArgumentException("actor id is required", nameof(actorId));

        RoleInstance? instance;
        lock (_gate)
        {
            instance = _instances.Values.FirstOrDefault(i => i.ActorId == actorId);
        }

        if (instance != null)
            return instance.Actor;

        var actor = await _actorManager.GetActorAsync(actorId);
        if (actor == null)
            throw new InvalidOperationException($"actor not found: {actorId}");

        return actor;
    }

    private IGAgentActor? TryGetActorByRole(string role)
    {
        lock (_gate)
        {
            return _instances.TryGetValue(role, out var instance) ? instance.Actor : null;
        }
    }

    private async Task DetachRoleEdgesAsync(IReadOnlyList<RoleEdge> edges, CancellationToken ct)
    {
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
    }

    private async Task ReattachRoleEdgesAsync(IReadOnlyList<RoleEdge> edges, CancellationToken ct)
    {
        foreach (var edge in edges)
        {
            var parent = await EnsureRoleAsync(edge.ParentRole, linkToRoot: false, ct);
            var child = await EnsureRoleAsync(edge.ChildRole, linkToRoot: false, ct);
            var parentActor = await GetActorAsync(parent.ActorId, ct);
            var childActor = await GetActorAsync(child.ActorId, ct);

            await ActorHierarchyCoordinator.LinkAsync(parentActor, childActor, _logger, ct);
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
