using Aevatar.Agents.Abstractions.Memory;
using Aevatar.Agents.Persistence.MongoDB.Memory.Options;
using Aevatar.Agents.Persistence.MongoDB.Memory.Stores;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace Aevatar.Agents.Persistence.MongoDB.Memory.DependencyInjection;

/// <summary>
/// DI extension methods for MongoDB-backed AI Memory.
/// </summary>
public static class MongoDbMemoryServiceCollectionExtensions
{
    /// <summary>
    /// Register MongoDB AI Memory implementations (IMemoryStore + IMemoryVectorIndex).
    ///
    /// Notes:
    /// - This requires base infra services.AddAevatarMongoDB(...) to be called first.
    /// </summary>
    public static IServiceCollection AddAevatarMongoDBMemory(
        this IServiceCollection services,
        Action<MongoDbMemoryOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddOptions<MongoDbMemoryOptions>()
            .Configure(o => configure?.Invoke(o));

        services.AddSingleton<IMemoryStore>(sp =>
        {
            var db = sp.GetRequiredService<IMongoDatabase>();
            var opt = sp.GetRequiredService<IOptions<MongoDbMemoryOptions>>();
            return new MongoDbMemoryStore(db, opt);
        });

        services.AddSingleton<IMemoryVectorIndex>(sp =>
        {
            var db = sp.GetRequiredService<IMongoDatabase>();
            var opt = sp.GetRequiredService<IOptions<MongoDbMemoryOptions>>();
            return new MongoDbMemoryVectorIndex(db, opt);
        });

        return services;
    }
}




