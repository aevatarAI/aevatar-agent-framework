using Aevatar.VibeResearching.UserProviders.Entities;
using Aevatar.VibeResearching.UserProviders.Repositories;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Aevatar.VibeResearching.UserProviders.MongoDB.Repositories;

/// <summary>
/// MongoDB implementation of the user LLM provider repository.
/// </summary>
public sealed class MongoUserLlmProviderRepository : IUserLlmProviderRepository
{
    private readonly UserProvidersMongoDbContext _context;

    public MongoUserLlmProviderRepository(UserProvidersMongoDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<UserLlmProvider?> GetByIdAsync(string id, CancellationToken ct = default)
    {
        var filter = Builders<UserLlmProvider>.Filter.Eq(x => x.Id, id);
        return await _context.Providers.Find(filter).FirstOrDefaultAsync(ct);
    }

    public async Task<UserLlmProvider?> GetByIdAndUserAsync(string id, Guid userId, CancellationToken ct = default)
    {
        var filter = Builders<UserLlmProvider>.Filter.And(
            Builders<UserLlmProvider>.Filter.Eq(x => x.Id, id),
            Builders<UserLlmProvider>.Filter.Eq(x => x.UserId, userId));
        return await _context.Providers.Find(filter).FirstOrDefaultAsync(ct);
    }

    public async Task<IReadOnlyList<UserLlmProvider>> GetByUserAsync(Guid userId, CancellationToken ct = default)
    {
        var filter = Builders<UserLlmProvider>.Filter.Eq(x => x.UserId, userId);
        var list = await _context.Providers.Find(filter)
            .SortBy(x => x.CreatedAt)
            .ToListAsync(ct);
        return list;
    }

    public async Task<UserLlmProvider?> GetDefaultByUserAsync(Guid userId, CancellationToken ct = default)
    {
        var filter = Builders<UserLlmProvider>.Filter.And(
            Builders<UserLlmProvider>.Filter.Eq(x => x.UserId, userId),
            Builders<UserLlmProvider>.Filter.Eq(x => x.IsDefault, true));
        return await _context.Providers.Find(filter).FirstOrDefaultAsync(ct);
    }

    public async Task<UserLlmProvider?> GetByUserAndNameAsync(Guid userId, string name, CancellationToken ct = default)
    {
        var filter = Builders<UserLlmProvider>.Filter.And(
            Builders<UserLlmProvider>.Filter.Eq(x => x.UserId, userId),
            Builders<UserLlmProvider>.Filter.Regex(x => x.Name,
                new BsonRegularExpression($"^{System.Text.RegularExpressions.Regex.Escape(name)}$", "i")));
        return await _context.Providers.Find(filter).FirstOrDefaultAsync(ct);
    }

    public async Task<int> GetCountByUserAsync(Guid userId, CancellationToken ct = default)
    {
        var filter = Builders<UserLlmProvider>.Filter.Eq(x => x.UserId, userId);
        return (int)await _context.Providers.CountDocumentsAsync(filter, cancellationToken: ct);
    }

    public async Task<UserLlmProvider> InsertAsync(UserLlmProvider entity, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(entity.Id))
            entity.Id = ObjectId.GenerateNewId().ToString();

        await _context.Providers.InsertOneAsync(entity, cancellationToken: ct);
        return entity;
    }

    public async Task<UserLlmProvider> UpdateAsync(UserLlmProvider entity, CancellationToken ct = default)
    {
        var filter = Builders<UserLlmProvider>.Filter.Eq(x => x.Id, entity.Id);
        await _context.Providers.ReplaceOneAsync(filter, entity, cancellationToken: ct);
        return entity;
    }

    public async Task DeleteAsync(string id, CancellationToken ct = default)
    {
        var filter = Builders<UserLlmProvider>.Filter.Eq(x => x.Id, id);
        await _context.Providers.DeleteOneAsync(filter, ct);
    }

    public async Task UnsetDefaultByUserAsync(Guid userId, CancellationToken ct = default)
    {
        var filter = Builders<UserLlmProvider>.Filter.And(
            Builders<UserLlmProvider>.Filter.Eq(x => x.UserId, userId),
            Builders<UserLlmProvider>.Filter.Eq(x => x.IsDefault, true));
        var update = Builders<UserLlmProvider>.Update.Set(x => x.IsDefault, false);
        await _context.Providers.UpdateManyAsync(filter, update, cancellationToken: ct);
    }

    public async Task<UserLlmProvider?> GetOldestByUserAsync(Guid userId, CancellationToken ct = default)
    {
        var filter = Builders<UserLlmProvider>.Filter.Eq(x => x.UserId, userId);
        return await _context.Providers.Find(filter)
            .SortBy(x => x.CreatedAt)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<UserLlmProvider?> GetCodexByUserAsync(Guid userId, CancellationToken ct = default)
    {
        var filter = Builders<UserLlmProvider>.Filter.And(
            Builders<UserLlmProvider>.Filter.Eq(x => x.UserId, userId),
            Builders<UserLlmProvider>.Filter.Eq(x => x.IsCodexOAuth, true));
        return await _context.Providers.Find(filter).FirstOrDefaultAsync(ct);
    }
}
