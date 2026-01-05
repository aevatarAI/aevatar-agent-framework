using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using MongoDB.Driver;

namespace Aevatar.Agents.Persistence.MongoDB.GAgent;

/// <summary>
/// Centralized MongoDB index manager.
/// Ensures indexes are created once per collection per process lifetime.
/// </summary>
internal static class MongoDBIndexManager
{
    // Track which collections have been initialized (per collection full name).
    private static readonly ConcurrentDictionary<string, bool> InitializedCollections = new();

    /// <summary>
    /// Ensure indexes for AgentStateDocument collection.
    /// </summary>
    public static async Task EnsureStateStoreIndexesAsync(
        IMongoCollection<AgentStateDocument> collection,
        CancellationToken ct = default)
    {
        var collectionKey = GetCollectionKey(collection);
        if (!InitializedCollections.TryAdd(collectionKey, true))
        {
            return;
        }

        try
        {
            var indexKeys = Builders<AgentStateDocument>.IndexKeys;
            var indexes = new[]
            {
                // AgentId is [BsonId] so MongoDB creates _id index automatically.
                // We add UpdatedAt index for TTL cleanup and time-range queries.
                new CreateIndexModel<AgentStateDocument>(
                    indexKeys.Descending(x => x.UpdatedAt),
                    new CreateIndexOptions { Name = "idx_updated_at", Background = true }),

                // Version index for optimistic concurrency queries.
                new CreateIndexModel<AgentStateDocument>(
                    indexKeys.Combine(
                        indexKeys.Ascending(x => x.AgentId),
                        indexKeys.Ascending(x => x.Version)),
                    new CreateIndexOptions { Name = "idx_agent_version", Background = true })
            };

            await collection.Indexes.CreateManyAsync(indexes, ct).ConfigureAwait(false);
        }
        catch (MongoCommandException ex) when (ex.Code == 85 || ex.Code == 86)
        {
            // Index already exists - OK.
        }
    }

    /// <summary>
    /// Ensure indexes for AgentConfigDocument collection.
    /// </summary>
    public static async Task EnsureConfigStoreIndexesAsync<TConfig>(
        IMongoCollection<AgentConfigDocument<TConfig>> collection,
        CancellationToken ct = default)
    {
        var collectionKey = GetCollectionKey(collection);
        if (!InitializedCollections.TryAdd(collectionKey, true))
        {
            return;
        }

        try
        {
            var indexKeys = Builders<AgentConfigDocument<TConfig>>.IndexKeys;
            var indexes = new[]
            {
                // Compound unique index on AgentType + AgentId.
                new CreateIndexModel<AgentConfigDocument<TConfig>>(
                    indexKeys.Combine(
                        indexKeys.Ascending(x => x.AgentType),
                        indexKeys.Ascending(x => x.AgentId)),
                    new CreateIndexOptions { Name = "idx_agent_type_id", Unique = true, Background = true }),

                // UpdatedAt index for TTL cleanup.
                new CreateIndexModel<AgentConfigDocument<TConfig>>(
                    indexKeys.Descending(x => x.UpdatedAt),
                    new CreateIndexOptions { Name = "idx_updated_at", Background = true })
            };

            await collection.Indexes.CreateManyAsync(indexes, ct).ConfigureAwait(false);
        }
        catch (MongoCommandException ex) when (ex.Code == 85 || ex.Code == 86)
        {
            // Index already exists - OK.
        }
    }

    /// <summary>
    /// Ensure indexes for EventRouterHierarchyDocument collection.
    /// </summary>
    public static async Task EnsureEventRouterStoreIndexesAsync(
        IMongoCollection<EventRouterHierarchyDocument> collection,
        CancellationToken ct = default)
    {
        var collectionKey = GetCollectionKey(collection);
        if (!InitializedCollections.TryAdd(collectionKey, true))
        {
            return;
        }

        try
        {
            var indexKeys = Builders<EventRouterHierarchyDocument>.IndexKeys;
            var indexes = new[]
            {
                // AgentId is [BsonId] so _id index is automatic.
                // ParentId index for finding all children of a parent.
                new CreateIndexModel<EventRouterHierarchyDocument>(
                    indexKeys.Ascending(x => x.ParentId),
                    new CreateIndexOptions { Name = "idx_parent_id", Background = true }),

                // UpdatedAt index for TTL cleanup.
                new CreateIndexModel<EventRouterHierarchyDocument>(
                    indexKeys.Descending(x => x.UpdatedAt),
                    new CreateIndexOptions { Name = "idx_updated_at", Background = true })
            };

            await collection.Indexes.CreateManyAsync(indexes, ct).ConfigureAwait(false);
        }
        catch (MongoCommandException ex) when (ex.Code == 85 || ex.Code == 86)
        {
            // Index already exists - OK.
        }
    }

    /// <summary>
    /// Synchronous version for constructor usage (not recommended for hot paths).
    /// </summary>
    public static void EnsureStateStoreIndexes(IMongoCollection<AgentStateDocument> collection)
    {
        var collectionKey = GetCollectionKey(collection);
        if (!InitializedCollections.TryAdd(collectionKey, true))
        {
            return;
        }

        try
        {
            var indexKeys = Builders<AgentStateDocument>.IndexKeys;
            var indexes = new[]
            {
                new CreateIndexModel<AgentStateDocument>(
                    indexKeys.Descending(x => x.UpdatedAt),
                    new CreateIndexOptions { Name = "idx_updated_at", Background = true }),

                new CreateIndexModel<AgentStateDocument>(
                    indexKeys.Combine(
                        indexKeys.Ascending(x => x.AgentId),
                        indexKeys.Ascending(x => x.Version)),
                    new CreateIndexOptions { Name = "idx_agent_version", Background = true })
            };

            collection.Indexes.CreateMany(indexes);
        }
        catch (MongoCommandException ex) when (ex.Code == 85 || ex.Code == 86)
        {
            // Index already exists - OK.
        }
    }

    /// <summary>
    /// Synchronous version for constructor usage.
    /// </summary>
    public static void EnsureConfigStoreIndexes<TConfig>(IMongoCollection<AgentConfigDocument<TConfig>> collection)
    {
        var collectionKey = GetCollectionKey(collection);
        if (!InitializedCollections.TryAdd(collectionKey, true))
        {
            return;
        }

        try
        {
            var indexKeys = Builders<AgentConfigDocument<TConfig>>.IndexKeys;
            var indexes = new[]
            {
                new CreateIndexModel<AgentConfigDocument<TConfig>>(
                    indexKeys.Combine(
                        indexKeys.Ascending(x => x.AgentType),
                        indexKeys.Ascending(x => x.AgentId)),
                    new CreateIndexOptions { Name = "idx_agent_type_id", Unique = true, Background = true }),

                new CreateIndexModel<AgentConfigDocument<TConfig>>(
                    indexKeys.Descending(x => x.UpdatedAt),
                    new CreateIndexOptions { Name = "idx_updated_at", Background = true })
            };

            collection.Indexes.CreateMany(indexes);
        }
        catch (MongoCommandException ex) when (ex.Code == 85 || ex.Code == 86)
        {
            // Index already exists - OK.
        }
    }

    /// <summary>
    /// Synchronous version for constructor usage.
    /// </summary>
    public static void EnsureEventRouterStoreIndexes(IMongoCollection<EventRouterHierarchyDocument> collection)
    {
        var collectionKey = GetCollectionKey(collection);
        if (!InitializedCollections.TryAdd(collectionKey, true))
        {
            return;
        }

        try
        {
            var indexKeys = Builders<EventRouterHierarchyDocument>.IndexKeys;
            var indexes = new[]
            {
                new CreateIndexModel<EventRouterHierarchyDocument>(
                    indexKeys.Ascending(x => x.ParentId),
                    new CreateIndexOptions { Name = "idx_parent_id", Background = true }),

                new CreateIndexModel<EventRouterHierarchyDocument>(
                    indexKeys.Descending(x => x.UpdatedAt),
                    new CreateIndexOptions { Name = "idx_updated_at", Background = true })
            };

            collection.Indexes.CreateMany(indexes);
        }
        catch (MongoCommandException ex) when (ex.Code == 85 || ex.Code == 86)
        {
            // Index already exists - OK.
        }
    }

    private static string GetCollectionKey<T>(IMongoCollection<T> collection)
    {
        return $"{collection.Database.DatabaseNamespace.DatabaseName}:{collection.CollectionNamespace.CollectionName}";
    }

    internal static void ResetForTesting()
    {
        InitializedCollections.Clear();
    }
}


