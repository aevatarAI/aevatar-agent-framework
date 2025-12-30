using System;
using System.Threading;
using System.Threading.Tasks;
using Aevatar.Agents.Abstractions.Persistence;
using Aevatar.Agents.Persistence.MongoDB;
using Google.Protobuf;
using MongoDB.Driver;

namespace Aevatar.Agents.Persistence.MongoDB.GAgent;

/// <summary>
/// MongoDB state store implementation with Protobuf serialization.
/// </summary>
/// <typeparam name="TState">State type (must be Protobuf IMessage)</typeparam>
public class MongoDBStateStore<TState> : IVersionedStateStore<TState>
    where TState : class, IMessage<TState>, new()
{
    private readonly IMongoCollection<AgentStateDocument> _collection;
    private readonly string _stateTypeName;

    // Static constructor ensures BSON serializers are configured once per type.
    static MongoDBStateStore()
    {
        MongoDBServiceCollectionExtensions.ConfigureBsonSerializers();
    }

    /// <summary>
    /// Create MongoDB state store with Protobuf serialization.
    /// </summary>
    /// <param name="database">MongoDB database instance</param>
    /// <param name="collectionName">Optional custom collection name (default: agent_states_{StateTypeName})</param>
    public MongoDBStateStore(
        IMongoDatabase database,
        string? collectionName = null)
    {
        var name = collectionName ?? $"agent_states_{typeof(TState).Name}";
        _collection = database.GetCollection<AgentStateDocument>(name);
        _stateTypeName = typeof(TState).FullName ?? typeof(TState).Name;

        // Ensure indexes are created (idempotent).
        MongoDBIndexManager.EnsureStateStoreIndexes(_collection);
    }

    /// <summary>
    /// Load state from MongoDB and deserialize from Protobuf bytes.
    /// </summary>
    public async Task<TState?> LoadAsync(string agentId, CancellationToken ct = default)
    {
        var doc = await _collection.Find(x => x.AgentId == agentId)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        if (doc == null || doc.StateData == null || doc.StateData.Length == 0)
            return null;

        var state = new TState();
        state.MergeFrom(doc.StateData);
        return state;
    }

    /// <summary>
    /// Serialize state to Protobuf bytes and save to MongoDB (upsert).
    /// </summary>
    public async Task SaveAsync(string agentId, TState state, CancellationToken ct = default)
    {
        var doc = new AgentStateDocument
        {
            AgentId = agentId,
            StateData = state.ToByteArray(),
            StateType = _stateTypeName,
            Version = 1,
            UpdatedAt = DateTime.UtcNow
        };

        await _collection.ReplaceOneAsync(
            x => x.AgentId == agentId,
            doc,
            new ReplaceOptions { IsUpsert = true },
            ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Save with version control (for EventSourcing snapshots).
    /// </summary>
    public async Task SaveAsync(string agentId, TState state, long version, CancellationToken ct = default)
    {
        var doc = new AgentStateDocument
        {
            AgentId = agentId,
            StateData = state.ToByteArray(),
            StateType = _stateTypeName,
            Version = version,
            UpdatedAt = DateTime.UtcNow
        };

        await _collection.ReplaceOneAsync(
            x => x.AgentId == agentId,
            doc,
            new ReplaceOptions { IsUpsert = true },
            ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Get current version.
    /// </summary>
    public async Task<long> GetCurrentVersionAsync(string agentId, CancellationToken ct = default)
    {
        var doc = await _collection.Find(x => x.AgentId == agentId)
            .Project(x => x.Version)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
        return doc;
    }

    /// <summary>
    /// Delete state from MongoDB.
    /// </summary>
    public async Task DeleteAsync(string agentId, CancellationToken ct = default)
    {
        await _collection.DeleteOneAsync(x => x.AgentId == agentId, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Check if state exists.
    /// </summary>
    public async Task<bool> ExistsAsync(string agentId, CancellationToken ct = default)
    {
        var count = await _collection.CountDocumentsAsync(x => x.AgentId == agentId, cancellationToken: ct)
            .ConfigureAwait(false);
        return count > 0;
    }
}

/// <summary>
/// MongoDB state store factory for DI.
/// </summary>
public static class MongoDBStateStoreFactory
{
    /// <summary>
    /// Create MongoDB state store factory function using DI-registered IMongoDatabase.
    /// </summary>
    public static Func<IServiceProvider, object> Create<TState>(string? collectionName = null)
        where TState : class, IMessage<TState>, new()
    {
        return sp =>
        {
            var database = sp.GetService(typeof(IMongoDatabase)) as IMongoDatabase
                ?? throw new InvalidOperationException(
                    "IMongoDatabase not registered. Call services.AddAevatarMongoDB() first.");
            return new MongoDBStateStore<TState>(database, collectionName);
        };
    }
}


