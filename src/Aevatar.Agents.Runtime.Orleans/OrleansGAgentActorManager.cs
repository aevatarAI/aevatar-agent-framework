using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Core.Hierarchy;
using Microsoft.Extensions.Logging;
using Orleans;

namespace Aevatar.Agents.Runtime.Orleans;

/// <summary>
/// Orleans runtime Agent Actor manager
/// </summary>
public class OrleansGAgentActorManager : IGAgentActorManager
{
    private readonly IGAgentActorFactory _factory;
    private readonly IGrainFactory _grainFactory;
    private readonly ILogger<OrleansGAgentActorManager> _logger;
    private readonly Dictionary<string, IGAgentActor> _actors = new();
    private readonly object _lock = new();

    public OrleansGAgentActorManager(
        IGAgentActorFactory factory,
        IGrainFactory grainFactory,
        ILogger<OrleansGAgentActorManager> logger)
    {
        _factory = factory;
        _grainFactory = grainFactory;
        _logger = logger;
    }

    public async Task<IGAgentActor> CreateAndRegisterAsync<TAgent>(
        string id,
        CancellationToken ct = default)
        where TAgent : IGAgent
    {
        _logger.LogDebug("Creating and registering agent {AgentType} with id {Id}",
            typeof(TAgent).Name, id);

        var actor = await _factory.CreateGAgentActorAsync<TAgent>(id, ct);

        // Use actor.Id (full GrainKey format) as storage key to avoid collisions
        // when same raw Guid is used for different Agent types
        lock (_lock)
        {
            _actors[actor.Id] = actor;
        }

        _logger.LogInformation("Agent actor {ActorId} created and registered (input: {InputId})", 
            actor.Id, id);

        return actor;
    }

    public Task<IGAgentActor?> GetActorAsync(string id)
    {
        lock (_lock)
        {
            _actors.TryGetValue(id, out var actor);
            return Task.FromResult(actor);
        }
    }

    public Task<IReadOnlyList<IGAgentActor>> GetAllActorsAsync()
    {
        lock (_lock)
        {
            return Task.FromResult<IReadOnlyList<IGAgentActor>>(_actors.Values.ToList());
        }
    }

    public async Task DeactivateAndUnregisterAsync(string id, CancellationToken ct = default)
    {
        IGAgentActor? actor;

        lock (_lock)
        {
            if (!_actors.TryGetValue(id, out actor))
            {
                _logger.LogWarning("Actor {Id} not found for deactivation", id);
                return;
            }

            _actors.Remove(id);
        }

        _logger.LogInformation("Deactivating and unregistering actor {Id}", id);
        await actor.DeactivateAsync(ct);
    }

    public async Task DeactivateAllAsync(CancellationToken ct = default)
    {
        List<IGAgentActor> actorsToDeactivate;

        lock (_lock)
        {
            actorsToDeactivate = _actors.Values.ToList();
            _actors.Clear();
        }

        _logger.LogInformation("Deactivating all {Count} actors", actorsToDeactivate.Count);

        await Task.WhenAll(actorsToDeactivate.Select(a => a.DeactivateAsync(ct)));
    }

    public Task<bool> ExistsAsync(string id)
    {
        lock (_lock)
        {
            return Task.FromResult(_actors.ContainsKey(id));
        }
    }

    public Task<int> GetCountAsync()
    {
        lock (_lock)
        {
            return Task.FromResult(_actors.Count);
        }
    }

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

        var resolvedParentId = parentId;
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

    #region New Interface Implementation

    public async Task<IReadOnlyList<IGAgentActor>> CreateBatchAsync<TAgent>(
        IEnumerable<string> ids,
        CancellationToken ct = default)
        where TAgent : IGAgent
    {
        var tasks = ids.Select(id => CreateAndRegisterAsync<TAgent>(id, ct));
        var actors = await Task.WhenAll(tasks);
        return actors;
    }

    public async Task DeactivateBatchAsync(IEnumerable<string> ids, CancellationToken ct = default)
    {
        var tasks = ids.Select(id => DeactivateAndUnregisterAsync(id, ct));
        await Task.WhenAll(tasks);
    }

    public Task<IReadOnlyList<IGAgentActor>> GetActorsAsync(IEnumerable<string> ids)
    {
        var actors = new List<IGAgentActor>();

        lock (_lock)
        {
            foreach (var id in ids)
            {
                if (_actors.TryGetValue(id, out var actor))
                {
                    actors.Add(actor);
                }
            }
        }

        return Task.FromResult<IReadOnlyList<IGAgentActor>>(actors);
    }

    public Task<IReadOnlyList<IGAgentActor>> GetActorsByTypeAsync<TAgent>()
        where TAgent : IGAgent
    {
        lock (_lock)
        {
            var actors = _actors.Values
                .Where(a => a.GetAgent() is TAgent)
                .ToList();

            return Task.FromResult<IReadOnlyList<IGAgentActor>>(actors);
        }
    }

    public Task<IReadOnlyList<IGAgentActor>> GetActorsByTypeNameAsync(string typeName)
    {
        lock (_lock)
        {
            var actors = _actors.Values
                .Where(a => a.GetAgent().GetType().Name == typeName)
                .ToList();

            return Task.FromResult<IReadOnlyList<IGAgentActor>>(actors);
        }
    }

    public Task<int> GetCountByTypeAsync<TAgent>()
        where TAgent : IGAgent
    {
        lock (_lock)
        {
            var count = _actors.Values.Count(a => a.GetAgent() is TAgent);
            return Task.FromResult(count);
        }
    }

    public Task<ActorHealthStatus> GetHealthStatusAsync(string id)
    {
        lock (_lock)
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

            // Orleans can provide more detailed health status
            return Task.FromResult(new ActorHealthStatus
            {
                Id = id,
                IsHealthy = true,
                LastActivityTime = DateTimeOffset.UtcNow
            });
        }
    }

    public Task<ActorManagerStatistics> GetStatisticsAsync()
    {
        lock (_lock)
        {
            var actorsByType = _actors.Values
                .GroupBy(a => a.GetAgent().GetType().Name)
                .ToDictionary(g => g.Key, g => g.Count());

            return Task.FromResult(new ActorManagerStatistics
            {
                TotalActors = _actors.Count,
                ActiveActors = _actors.Count,
                ActorsByType = actorsByType
            });
        }
    }

    #endregion

    private IGAgentActor GetRequiredActor(string id)
    {
        lock (_lock)
        {
            if (_actors.TryGetValue(id, out var actor))
            {
                return actor;
            }
        }

        throw new InvalidOperationException($"Actor {id} is not registered in Orleans runtime.");
    }
}