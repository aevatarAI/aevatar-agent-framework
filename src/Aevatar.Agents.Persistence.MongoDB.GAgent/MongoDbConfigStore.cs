using System;
using System.Threading;
using System.Threading.Tasks;
using Aevatar.Agents.Abstractions.Persistence;
using MongoDB.Driver;

namespace Aevatar.Agents.Persistence.MongoDB.GAgent;

/// <summary>
/// MongoDB configuration store implementation.
/// Stores agent configurations in MongoDB collections with agent type isolation.
/// </summary>
/// <typeparam name="TConfig">Configuration type</typeparam>
public class MongoDbConfigStore<TConfig> : IConfigStore<TConfig>
    where TConfig : class, new()
{
    private readonly IMongoCollection<AgentConfigDocument<TConfig>> _collection;

    /// <summary>
    /// Create MongoDB configuration store.
    /// </summary>
    /// <param name="database">MongoDB database instance</param>
    /// <param name="collectionName">Optional custom collection name</param>
    public MongoDbConfigStore(
        IMongoDatabase database,
        string? collectionName = null)
    {
        var name = collectionName ?? $"agent_configs_{typeof(TConfig).Name}";
        _collection = database.GetCollection<AgentConfigDocument<TConfig>>(name);

        // Ensure indexes are created (idempotent).
        MongoDBIndexManager.EnsureConfigStoreIndexes(_collection);
    }

    /// <summary>
    /// Load configuration from MongoDB.
    /// </summary>
    public async Task<TConfig?> LoadAsync(Type agentType, string agentId, CancellationToken ct = default)
    {
        var agentTypeName = agentType.FullName ?? agentType.Name;
        var doc = await _collection.Find(x => x.AgentType == agentTypeName && x.AgentId == agentId)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
        return doc?.Config;
    }

    /// <summary>
    /// Save configuration to MongoDB (upsert).
    /// </summary>
    public async Task SaveAsync(Type agentType, string agentId, TConfig config, CancellationToken ct = default)
    {
        var agentTypeName = agentType.FullName ?? agentType.Name;
        var doc = new AgentConfigDocument<TConfig>
        {
            AgentType = agentTypeName,
            AgentId = agentId,
            Config = config,
            UpdatedAt = DateTime.UtcNow
        };

        await _collection.ReplaceOneAsync(
            x => x.AgentType == agentTypeName && x.AgentId == agentId,
            doc,
            new ReplaceOptions { IsUpsert = true },
            ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Delete configuration from MongoDB.
    /// </summary>
    public async Task DeleteAsync(Type agentType, string agentId, CancellationToken ct = default)
    {
        var agentTypeName = agentType.FullName ?? agentType.Name;
        await _collection.DeleteOneAsync(
            x => x.AgentType == agentTypeName && x.AgentId == agentId,
            ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Check if configuration exists.
    /// </summary>
    public async Task<bool> ExistsAsync(Type agentType, string agentId, CancellationToken ct = default)
    {
        var agentTypeName = agentType.FullName ?? agentType.Name;
        var count = await _collection.CountDocumentsAsync(
            x => x.AgentType == agentTypeName && x.AgentId == agentId,
            cancellationToken: ct).ConfigureAwait(false);
        return count > 0;
    }
}


