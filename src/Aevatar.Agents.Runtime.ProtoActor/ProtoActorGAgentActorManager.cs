using System.Collections.Concurrent;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Core.Hierarchy;
using Microsoft.Extensions.Logging;
using Proto;

namespace Aevatar.Agents.Runtime.ProtoActor;

/// <summary>
/// ProtoActor runtime Agent Actor manager
/// </summary>
public class ProtoActorGAgentActorManager : IGAgentActorManager
{
    private readonly IGAgentActorFactory _factory;
    private readonly IRootContext _rootContext;
    private readonly ILogger<ProtoActorGAgentActorManager> _logger;
    private readonly ConcurrentDictionary<string, IGAgentActor> _actors = new();

    public ProtoActorGAgentActorManager(
        IGAgentActorFactory factory,
        IRootContext rootContext,
        ILogger<ProtoActorGAgentActorManager> logger)
    {
        _factory = factory;
        _rootContext = rootContext;
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

        // Register using actor.Id (full format) to avoid collisions
        // when same raw ID is used for different Agent types
        _actors[actor.Id] = actor;

        _logger.LogInformation("Agent actor {ActorId} created and registered (input: {InputId})", 
            actor.Id, id);

        return actor;
    }

    public Task<IGAgentActor?> GetActorAsync(string id)
    {
        _actors.TryGetValue(id, out var actor);
        return Task.FromResult(actor);
    }

    public Task<IReadOnlyList<IGAgentActor>> GetAllActorsAsync()
    {
        return Task.FromResult<IReadOnlyList<IGAgentActor>>(_actors.Values.ToList());
    }

    public async Task DeactivateAndUnregisterAsync(string id, CancellationToken ct = default)
    {
        if (!_actors.TryRemove(id, out var actor))
        {
            _logger.LogWarning("Actor {Id} not found for deactivation", id);
            return;
        }

        _logger.LogInformation("Deactivating and unregistering actor {Id}", id);
        await actor.DeactivateAsync(ct);
    }

    public async Task DeactivateAllAsync(CancellationToken ct = default)
    {
        var actorsToDeactivate = _actors.Values.ToList();
        _actors.Clear();

        _logger.LogInformation("Deactivating all {Count} actors", actorsToDeactivate.Count);

        await Task.WhenAll(actorsToDeactivate.Select(a => a.DeactivateAsync(ct)));
    }

    public Task<bool> ExistsAsync(string id)
    {
        return Task.FromResult(_actors.ContainsKey(id));
    }

    public Task<int> GetCountAsync()
    {
        return Task.FromResult(_actors.Count);
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
        
        foreach (var id in ids)
        {
            if (_actors.TryGetValue(id, out var actor))
            {
                actors.Add(actor);
            }
        }
        
        return Task.FromResult<IReadOnlyList<IGAgentActor>>(actors);
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

    public Task<int> GetCountByTypeAsync<TAgent>()
        where TAgent : IGAgent
    {
        var count = _actors.Values.Count(a => a.GetAgent() is TAgent);
        return Task.FromResult(count);
    }

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

        return Task.FromResult(new ActorHealthStatus
        {
            Id = id,
            IsHealthy = true,
            LastActivityTime = DateTimeOffset.UtcNow
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
            ActiveActors = _actors.Count,
            ActorsByType = actorsByType
        });
    }

    #endregion

    private IGAgentActor GetRequiredActor(string id)
    {
        if (_actors.TryGetValue(id, out var actor))
        {
            return actor;
        }

        throw new InvalidOperationException($"Actor {id} is not registered in ProtoActor runtime.");
    }
}