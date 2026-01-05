using MongoDB.Driver;

namespace Aevatar.Agents.Persistence.MongoDB.GAgent;

/// <summary>
/// MongoDB configuration store factory for DI.
/// </summary>
public static class MongoDBConfigurationStoreFactory
{
    /// <summary>
    /// Create MongoDB configuration store factory function using DI-registered IMongoDatabase.
    ///
    /// Usage:
    /// services.AddAevatarMongoDB("mongodb://localhost:27017");
    /// services.AddMongoDBConfigStore&lt;MyConfig&gt;();
    /// </summary>
    public static Func<IServiceProvider, object> Create<TConfig>(string? collectionName = null)
        where TConfig : class, new()
    {
        return sp =>
        {
            var database = sp.GetService(typeof(IMongoDatabase)) as IMongoDatabase
                ?? throw new InvalidOperationException(
                    "IMongoDatabase not registered. Call services.AddAevatarMongoDB() first.");

            return new MongoDbConfigStore<TConfig>(database, collectionName);
        };
    }
}


