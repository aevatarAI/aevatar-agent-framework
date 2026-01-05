using System.Collections.Concurrent;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Core.Hierarchy;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.Runtime.Local;

/// <summary>
/// Local runtime Agent Actor manager
/// </summary>
public class LocalGAgentActorManager : IGAgentActorManager
{
    private readonly IGAgentActorFactory _factory;
    private readonly ILogger<LocalGAgentActorManager> _logger;
    private readonly ConcurrentDictionary<string, IGAgentActor> _actors = new();
    private readonly ConcurrentDictionary<string, DateTimeOffset> _lastActivityTime = new();

    public LocalGAgentActorManager(
        IGAgentActorFactory factory,
        ILogger<LocalGAgentActorManager> logger)
    {
        _factory = factory;
        _logger = logger;
    }

    #region Lifecycle Management

    public async Task<IGAgentActor> CreateAndRegisterAsync<TAgent>(
        string id,
        CancellationToken ct = default)
        where TAgent : IGAgent
    {
        _logger.LogDebug("Creating and registering agent {AgentType} with id {Id}",
            typeof(TAgent).Name, id);

        // Create Actor
        var actor = await _factory.CreateGAgentActorAsync<TAgent>(id, ct);

        // Register using actor.Id (full format) to avoid collisions
        // when same raw ID is used for different Agent types
        _actors[actor.Id] = actor;
        _lastActivityTime[actor.Id] = DateTimeOffset.UtcNow;

        _logger.LogInformation("Agent actor {ActorId} created and registered (input: {InputId})", 
            actor.Id, id);
        return actor;
    }

    public async Task<IReadOnlyList<IGAgentActor>> CreateBatchAsync<TAgent>(
        IEnumerable<string> ids,
        CancellationToken ct = default)
        where TAgent : IGAgent
    {
        var idList = ids.ToList();
        _logger.LogDebug("Batch creating {Count} agents of type {AgentType}",
            idList.Count, typeof(TAgent).Name);

        var tasks = idList.Select(id => CreateAndRegisterAsync<TAgent>(id, ct));
        var actors = await Task.WhenAll(tasks);

        return actors;
    }

    public async Task DeactivateAndUnregisterAsync(string id, CancellationToken ct = default)
    {
        if (!_actors.TryRemove(id, out var actor))
        {
            _logger.LogWarning("Actor {Id} not found for deactivation", id);
            return;
        }

        _lastActivityTime.TryRemove(id, out _);

        _logger.LogInformation("Deactivating and unregistering actor {Id}", id);
        await actor.DeactivateAsync(ct);
    }

    public async Task DeactivateBatchAsync(IEnumerable<string> ids, CancellationToken ct = default)
    {
        var idList = ids.ToList();
        _logger.LogDebug("Batch deactivating {Count} actors", idList.Count);

        var tasks = idList.Select(id => DeactivateAndUnregisterAsync(id, ct));
        await Task.WhenAll(tasks);
    }

    public async Task DeactivateAllAsync(CancellationToken ct = default)
    {
        var actorsToDeactivate = _actors.Values.ToList();
        _actors.Clear();
        _lastActivityTime.Clear();

        _logger.LogInformation("Deactivating all {Count} actors", actorsToDeactivate.Count);

        // Deactivate all actors concurrently
        await Task.WhenAll(actorsToDeactivate.Select(a => a.DeactivateAsync(ct)));
    }

    #endregion

    #region Query and Get

    public Task<IGAgentActor?> GetActorAsync(string id)
    {
        if (_actors.TryGetValue(id, out var actor))
        {
            _lastActivityTime[id] = DateTimeOffset.UtcNow;
            return Task.FromResult<IGAgentActor?>(actor);
        }

        return Task.FromResult<IGAgentActor?>(null);
    }

    public Task<IReadOnlyList<IGAgentActor>> GetActorsAsync(IEnumerable<string> ids)
    {
        var actors = new List<IGAgentActor>();

        foreach (var id in ids)
        {
            if (_actors.TryGetValue(id, out var actor))
            {
                actors.Add(actor);
                _lastActivityTime[id] = DateTimeOffset.UtcNow;
            }
        }

        return Task.FromResult<IReadOnlyList<IGAgentActor>>(actors);
    }

    public Task<IReadOnlyList<IGAgentActor>> GetAllActorsAsync()
    {
        return Task.FromResult<IReadOnlyList<IGAgentActor>>(_actors.Values.ToList());
    }

    public Task<IReadOnlyList<IGAgentActor>> GetActorsByTypeAsync<TAgent>()
        where TAgent : IGAgent
    {
        var actors = _actors.Values
            .Where(a => a.GetAgent() is TAgent)
            .ToList();

        return Task.FromResult<IReadOnlyList<IGAgentActor>>(actors);
    }

    public Task<IReadOnlyList<IGAgentActor>> GetActorsByTypeNameAsync(string typeName)
    {
        var actors = _actors.Values
            .Where(a => a.GetAgent().GetType().Name == typeName)
            .ToList();

        return Task.FromResult<IReadOnlyList<IGAgentActor>>(actors);
    }

    public Task<bool> ExistsAsync(string id)
    {
        return Task.FromResult(_actors.ContainsKey(id));
    }

    public Task<int> GetCountAsync()
    {
        return Task.FromResult(_actors.Count);
    }

    public Task<int> GetCountByTypeAsync<TAgent>()
        where TAgent : IGAgent
    {
        var count = _actors.Values.Count(a => a.GetAgent() is TAgent);
        return Task.FromResult(count);
    }

    #endregion

    #region Hierarchy Relationship Coordination

    public async Task LinkParentChildAsync(string parentId, string childId, CancellationToken ct = default)
    {
        var parent = GetRequiredActor(parentId);
        var child = GetRequiredActor(childId);

        _logger.LogInformation("Linking parent {ParentId} with child {ChildId}", parentId, childId);
        await ActorHierarchyCoordinator.LinkAsync(parent, child, _logger, ct);
    }

    public async Task UnlinkParentChildAsync(string childId, string? parentId = null, CancellationToken ct = default)
    {
        var child = GetRequiredActor(childId);

        string? resolvedParentId = parentId;
        if (string.IsNullOrEmpty(resolvedParentId))
        {
            resolvedParentId = await child.GetParentAsync();
        }

        IGAgentActor? parent = null;
        if (!string.IsNullOrEmpty(resolvedParentId))
        {
            parent = await GetActorAsync(resolvedParentId);
            if (parent == null)
            {
                _logger.LogWarning("Parent actor {ParentId} not found when unlinking child {ChildId}",
                    resolvedParentId, childId);
            }
        }

        await ActorHierarchyCoordinator.UnlinkAsync(child, parent, _logger, ct);
    }

    #endregion

    #region Monitoring and Diagnostics

    public Task<ActorHealthStatus> GetHealthStatusAsync(string id)
    {
        if (!_actors.TryGetValue(id, out var actor))
        {
            return Task.FromResult(new ActorHealthStatus
            {
                Id = id,
                IsHealthy = false,
                ErrorMessage = "Actor not found"
            });
        }

        _lastActivityTime.TryGetValue(id, out var lastActivity);

        return Task.FromResult(new ActorHealthStatus
        {
            Id = id,
            IsHealthy = true,
            LastActivityTime = lastActivity
        });
    }

    public Task<ActorManagerStatistics> GetStatisticsAsync()
    {
        var actorsByType = _actors.Values
            .GroupBy(a => a.GetAgent().GetType().Name)
            .ToDictionary(g => g.Key, g => g.Count());

        return Task.FromResult(new ActorManagerStatistics
        {
            TotalActors = _actors.Count,
            ActiveActors = _actors.Count, // In Local runtime, all Actors are active
            ActorsByType = actorsByType
        });
    }

    #endregion

    private IGAgentActor GetRequiredActor(string id)
    {
        if (_actors.TryGetValue(id, out var actor))
        {
            _lastActivityTime[id] = DateTimeOffset.UtcNow;
            return actor;
        }

        throw new InvalidOperationException($"Actor {id} is not registered in Local runtime.");
    }
}
