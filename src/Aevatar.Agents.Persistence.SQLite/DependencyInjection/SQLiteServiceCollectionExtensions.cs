using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Aevatar.Agents.Persistence.SQLite.DependencyInjection;

/// <summary>
/// SQLite base infrastructure DI extensions.
/// </summary>
public static class SQLiteServiceCollectionExtensions
{
    /// <summary>
    /// Register SQLite infrastructure (connection factory).
    /// </summary>
    public static IServiceCollection AddAevatarSQLite(
        this IServiceCollection services,
        string connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(connectionString);

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException("connectionString cannot be empty.", nameof(connectionString));
        }

        connectionString = connectionString.Trim();
        services.TryAddSingleton(_ => new SQLiteConnectionFactory(connectionString));
        return services;
    }

    /// <summary>
    /// Register SQLite infrastructure via connection string builder.
    /// </summary>
    public static IServiceCollection AddAevatarSQLite(
        this IServiceCollection services,
        SqliteConnectionStringBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(builder);

        return services.AddAevatarSQLite(builder.ToString());
    }
}
