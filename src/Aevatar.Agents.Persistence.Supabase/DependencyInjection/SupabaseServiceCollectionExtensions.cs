using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;

namespace Aevatar.Agents.Persistence.Supabase.DependencyInjection;

/// <summary>
/// Supabase(Postgres) base infrastructure DI extensions.
///
/// This project only owns:
/// - NpgsqlDataSource registration (connection pool)
/// - shared SQL utilities (identifier validation)
///
/// Stores are split into:
/// - Aevatar.Agents.Persistence.Supabase.GAgent (State/Config/EventRouter)
/// - Aevatar.Agents.Persistence.Supabase.Memory (IMemoryStore/IMemoryVectorIndex)
/// </summary>
public static class SupabaseServiceCollectionExtensions
{
    /// <summary>
    /// Register Supabase(Postgres) infrastructure (NpgsqlDataSource).
    /// </summary>
    public static IServiceCollection AddAevatarSupabase(
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

        // Create does not connect immediately; first connection happens on first OpenConnection.
        services.TryAddSingleton(_ => NpgsqlDataSource.Create(connectionString));

        return services;
    }
}


