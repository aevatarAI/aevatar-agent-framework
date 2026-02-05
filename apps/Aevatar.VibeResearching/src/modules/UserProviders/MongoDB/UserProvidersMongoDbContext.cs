using Aevatar.VibeResearching.UserProviders.Constants;
using Aevatar.VibeResearching.UserProviders.Entities;
using MongoDB.Driver;

namespace Aevatar.VibeResearching.UserProviders.MongoDB;

/// <summary>
/// MongoDB context for UserProviders module.
/// Provides typed collection accessors.
/// </summary>
public sealed class UserProvidersMongoDbContext
{
    private readonly IMongoDatabase _database;

    public UserProvidersMongoDbContext(IMongoDatabase database)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
    }

    public IMongoCollection<UserLlmProvider> Providers =>
        _database.GetCollection<UserLlmProvider>(UserProviderConsts.ProvidersCollectionName);

    public IMongoCollection<UserCodexToken> CodexTokens =>
        _database.GetCollection<UserCodexToken>(UserProviderConsts.CodexTokensCollectionName);
}
