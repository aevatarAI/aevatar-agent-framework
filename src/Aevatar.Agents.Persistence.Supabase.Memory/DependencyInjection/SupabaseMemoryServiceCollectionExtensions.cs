using Aevatar.Agents.Persistence.Supabase.DependencyInjection;
using Aevatar.Agents.Persistence.Supabase.Memory.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Aevatar.Agents.Persistence.Supabase.Memory.DependencyInjection;

// ============================================================
//  SupabaseMemoryServiceCollectionExtensions
//
//  Design:
//  - Manage connection pool via NpgsqlDataSource (recommended).
//  - Keep this module independent from Agent State/Config Supabase persistence.
//  - Allow sharing the same NpgsqlDataSource if it is already registered.
// ============================================================
public static class SupabaseMemoryServiceCollectionExtensions
{
    /// <summary>
    /// Register Supabase(Postgres) infrastructure for AI Memory (Options + NpgsqlDataSource).
    /// </summary>
    public static IServiceCollection AddAevatarSupabaseMemory(
        this IServiceCollection services,
        string connectionString,
        Action<SupabaseMemoryOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(connectionString);

        // Register shared NpgsqlDataSource (connection pool) via base infrastructure.
        services.AddAevatarSupabase(connectionString);

        services
            .AddOptions<SupabaseMemoryOptions>()
            .Configure(o =>
            {
                o.ConnectionString = connectionString;
                configure?.Invoke(o);
            });

        return services;
    }

    /// <summary>
    /// Register Supabase(Postgres) infrastructure for AI Memory (Options + NpgsqlDataSource).
    /// Suitable for binding from IConfiguration then fine-tuning.
    /// </summary>
    public static IServiceCollection AddAevatarSupabaseMemory(
        this IServiceCollection services,
        Action<SupabaseMemoryOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        services
            .AddOptions<SupabaseMemoryOptions>()
            .Configure(configure);

        services.TryAddSingleton(sp =>
        {
            var options = sp.GetRequiredService<IOptions<SupabaseMemoryOptions>>().Value;
            if (string.IsNullOrWhiteSpace(options.ConnectionString))
            {
                throw new InvalidOperationException(
                    $"{nameof(SupabaseMemoryOptions)}.{nameof(SupabaseMemoryOptions.ConnectionString)} is required when NpgsqlDataSource is not pre-registered.");
            }

            return NpgsqlDataSource.Create(options.ConnectionString);
        });

        return services;
    }
}


