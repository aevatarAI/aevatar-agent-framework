using Aevatar.Agents.Abstractions.Memory;
using Aevatar.Agents.Persistence.SQLite;
using Aevatar.Agents.Persistence.SQLite.Memory.Options;
using Aevatar.Agents.Persistence.SQLite.Memory.Stores;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Aevatar.Agents.Persistence.SQLite.Memory.DependencyInjection;

/// <summary>
/// DI extension methods for SQLite-backed AI Memory.
/// </summary>
public static class SQLiteMemoryServiceCollectionExtensions
{
    /// <summary>
    /// Register SQLite AI Memory implementations (IMemoryStore + IMemoryVectorIndex).
    ///
    /// Notes:
    /// - This requires base infra services.AddAevatarSQLite(...) to be called first.
    /// </summary>
    public static IServiceCollection AddAevatarSQLiteMemory(
        this IServiceCollection services,
        Action<SQLiteMemoryOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddOptions<SQLiteMemoryOptions>()
            .Configure(o => configure?.Invoke(o));

        services.AddSingleton<IMemoryStore>(sp =>
        {
            var factory = sp.GetRequiredService<SQLiteConnectionFactory>();
            var opt = sp.GetRequiredService<IOptions<SQLiteMemoryOptions>>();
            return new SQLiteMemoryStore(factory, opt);
        });

        services.AddSingleton<IMemoryVectorIndex>(sp =>
        {
            var factory = sp.GetRequiredService<SQLiteConnectionFactory>();
            var opt = sp.GetRequiredService<IOptions<SQLiteMemoryOptions>>();
            return new SQLiteMemoryVectorIndex(factory, opt);
        });

        return services;
    }
}
