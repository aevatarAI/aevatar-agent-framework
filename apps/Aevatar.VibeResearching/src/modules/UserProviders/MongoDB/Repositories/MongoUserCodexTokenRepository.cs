using Aevatar.VibeResearching.UserProviders.Entities;
using Aevatar.VibeResearching.UserProviders.Repositories;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Aevatar.VibeResearching.UserProviders.MongoDB.Repositories;

/// <summary>
/// MongoDB implementation of the user Codex token repository.
/// </summary>
public sealed class MongoUserCodexTokenRepository : IUserCodexTokenRepository
{
    private readonly UserProvidersMongoDbContext _context;

    public MongoUserCodexTokenRepository(UserProvidersMongoDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<UserCodexToken?> GetByUserAsync(Guid userId, CancellationToken ct = default)
    {
        var filter = Builders<UserCodexToken>.Filter.Eq(x => x.UserId, userId);
        return await _context.CodexTokens.Find(filter).FirstOrDefaultAsync(ct);
    }

    public async Task<UserCodexToken> UpsertAsync(UserCodexToken entity, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(entity.Id))
            entity.Id = ObjectId.GenerateNewId().ToString();

        var filter = Builders<UserCodexToken>.Filter.Eq(x => x.UserId, entity.UserId);
        var options = new ReplaceOptions { IsUpsert = true };

        await _context.CodexTokens.ReplaceOneAsync(filter, entity, options, ct);
        return entity;
    }

    public async Task DeleteByUserAsync(Guid userId, CancellationToken ct = default)
    {
        var filter = Builders<UserCodexToken>.Filter.Eq(x => x.UserId, userId);
        await _context.CodexTokens.DeleteOneAsync(filter, ct);
    }
}
