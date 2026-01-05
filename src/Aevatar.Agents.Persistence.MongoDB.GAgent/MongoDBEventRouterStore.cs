using System;
using System.Threading;
using System.Threading.Tasks;
using Aevatar.Agents.Abstractions.EventRouting;
using MongoDB.Driver;

namespace Aevatar.Agents.Persistence.MongoDB.GAgent;

/// <summary>
/// MongoDB EventRouter hierarchy store implementation.
/// Stores agent hierarchies in MongoDB collections.
/// </summary>
public class MongoDBEventRouterStore : IEventRouterStore
{
    private readonly IMongoCollection<EventRouterHierarchyDocument> _collection;

    /// <summary>
    /// Create MongoDB EventRouter store.
    /// </summary>
    /// <param name="database">MongoDB database instance</param>
    /// <param name="collectionName">Optional custom collection name</param>
    public MongoDBEventRouterStore(
        IMongoDatabase database,
        string? collectionName = null)
    {
        var name = collectionName ?? "agent_event_router_hierarchies";
        _collection = database.GetCollection<EventRouterHierarchyDocument>(name);

        // Ensure indexes are created (idempotent).
        MongoDBIndexManager.EnsureEventRouterStoreIndexes(_collection);
    }

    /// <summary>
    /// Load hierarchy from MongoDB.
    /// </summary>
    public async Task<EventRouterHierarchy?> LoadAsync(string agentId, CancellationToken ct = default)
    {
        var doc = await _collection.Find(x => x.AgentId == agentId)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        if (doc == null)
            return null;

        return new EventRouterHierarchy
        {
            ParentId = doc.ParentId,
            ChildrenIds = [..doc.ChildrenIds]
        };
    }

    /// <summary>
    /// Save hierarchy to MongoDB (upsert).
    /// </summary>
    public async Task SaveAsync(string agentId, EventRouterHierarchy hierarchy, CancellationToken ct = default)
    {
        var doc = new EventRouterHierarchyDocument
        {
            AgentId = agentId,
            ParentId = hierarchy.ParentId,
            ChildrenIds = [..hierarchy.ChildrenIds],
            UpdatedAt = DateTime.UtcNow
        };

        await _collection.ReplaceOneAsync(
            x => x.AgentId == agentId,
            doc,
            new ReplaceOptions { IsUpsert = true },
            ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Delete hierarchy from MongoDB.
    /// </summary>
    public async Task DeleteAsync(string agentId, CancellationToken ct = default)
    {
        await _collection.DeleteOneAsync(x => x.AgentId == agentId, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Check if hierarchy exists.
    /// </summary>
    public async Task<bool> ExistsAsync(string agentId, CancellationToken ct = default)
    {
        var count = await _collection.CountDocumentsAsync(x => x.AgentId == agentId, cancellationToken: ct)
            .ConfigureAwait(false);
        return count > 0;
    }
}

/// <summary>
/// MongoDB EventRouter store factory for DI.
/// </summary>
public static class MongoDBEventRouterStoreFactory
{
    /// <summary>
    /// Create MongoDB EventRouter store factory function using DI-registered IMongoDatabase.
    /// </summary>
    /// <param name="collectionName">Optional custom collection name</param>
    public static Func<IServiceProvider, IEventRouterStore> Create(string? collectionName = null)
    {
        return sp =>
        {
            var database = sp.GetService(typeof(IMongoDatabase)) as IMongoDatabase
                ?? throw new InvalidOperationException(
                    "IMongoDatabase not registered. Call services.AddAevatarMongoDB() first.");
            return new MongoDBEventRouterStore(database, collectionName);
        };
    }
}


