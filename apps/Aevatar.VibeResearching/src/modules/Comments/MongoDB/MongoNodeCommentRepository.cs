using Aevatar.VibeResearching.Comments.Entities;
using Aevatar.VibeResearching.Comments.Repositories;
using MongoDB.Driver;
using Volo.Abp.DependencyInjection;

namespace Aevatar.VibeResearching.Comments.MongoDB;

/// <summary>
/// MongoDB implementation of INodeCommentRepository.
/// Uses IMongoDatabase directly (registered by AddAevatarMongoDB).
/// </summary>
internal sealed class MongoNodeCommentRepository : INodeCommentRepository, ITransientDependency
{
    private const string CollectionName = "NodeComments";

    private readonly IMongoCollection<NodeComment> _collection;
    private static volatile bool _indexesCreated;

    public MongoNodeCommentRepository(IMongoDatabase database)
    {
        _collection = database.GetCollection<NodeComment>(CollectionName);
    }

    /// <inheritdoc/>
    public async Task<List<NodeComment>> GetListByNodeIdAsync(
        string sessionId, string nodeId, int skip, int take, CancellationToken ct = default)
    {
        await EnsureIndexesAsync(ct);

        var filter = Builders<NodeComment>.Filter.And(
            Builders<NodeComment>.Filter.Eq(c => c.SessionId, sessionId),
            Builders<NodeComment>.Filter.Eq(c => c.NodeId, nodeId),
            Builders<NodeComment>.Filter.Eq(c => c.IsDeleted, false));

        return await _collection
            .Find(filter)
            .SortBy(c => c.CreatedAt)
            .Skip(skip)
            .Limit(take)
            .ToListAsync(ct);
    }

    /// <inheritdoc/>
    public async Task<List<NodeComment>> GetPreviewByNodeIdAsync(
        string sessionId, string nodeId, int count, CancellationToken ct = default)
    {
        await EnsureIndexesAsync(ct);

        var filter = Builders<NodeComment>.Filter.And(
            Builders<NodeComment>.Filter.Eq(c => c.SessionId, sessionId),
            Builders<NodeComment>.Filter.Eq(c => c.NodeId, nodeId),
            Builders<NodeComment>.Filter.Eq(c => c.IsDeleted, false));

        return await _collection
            .Find(filter)
            .SortByDescending(c => c.CreatedAt)
            .Limit(count)
            .ToListAsync(ct);
    }

    /// <inheritdoc/>
    public async Task<long> GetCountByNodeIdAsync(
        string sessionId, string nodeId, CancellationToken ct = default)
    {
        await EnsureIndexesAsync(ct);

        var filter = Builders<NodeComment>.Filter.And(
            Builders<NodeComment>.Filter.Eq(c => c.SessionId, sessionId),
            Builders<NodeComment>.Filter.Eq(c => c.NodeId, nodeId),
            Builders<NodeComment>.Filter.Eq(c => c.IsDeleted, false));

        return await _collection.CountDocumentsAsync(filter, cancellationToken: ct);
    }

    /// <inheritdoc/>
    public async Task<NodeComment?> GetAsync(Guid id, CancellationToken ct = default)
    {
        var filter = Builders<NodeComment>.Filter.Eq(c => c.Id, id);
        return await _collection.Find(filter).FirstOrDefaultAsync(ct);
    }

    /// <inheritdoc/>
    public async Task InsertAsync(NodeComment comment, CancellationToken ct = default)
    {
        await EnsureIndexesAsync(ct);
        await _collection.InsertOneAsync(comment, cancellationToken: ct);
    }

    /// <inheritdoc/>
    public async Task UpdateAsync(NodeComment comment, CancellationToken ct = default)
    {
        var filter = Builders<NodeComment>.Filter.Eq(c => c.Id, comment.Id);
        await _collection.ReplaceOneAsync(filter, comment, cancellationToken: ct);
    }

    /// <summary>
    /// Creates compound indexes on first access.
    /// </summary>
    private async Task EnsureIndexesAsync(CancellationToken ct)
    {
        if (_indexesCreated) return;

        var indexKeys = Builders<NodeComment>.IndexKeys
            .Ascending(c => c.SessionId)
            .Ascending(c => c.NodeId)
            .Ascending(c => c.IsDeleted)
            .Ascending(c => c.CreatedAt);

        var indexModel = new CreateIndexModel<NodeComment>(
            indexKeys,
            new CreateIndexOptions { Name = "IX_NodeComment_Session_Node_Deleted_Created" });

        await _collection.Indexes.CreateOneAsync(indexModel, cancellationToken: ct);
        _indexesCreated = true;
    }
}
