using Aevatar.Agents.Abstractions.Memory;
using Aevatar.Agents.Persistence.Supabase.Memory.DependencyInjection;
using Aevatar.Agents.Persistence.Supabase.Memory.Options;
using Aevatar.Agents.Persistence.Supabase.Memory.Setup;
using Aevatar.Agents.Persistence.Supabase.Memory.Stores;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;
using Xunit;

namespace Aevatar.Agents.Persistence.Supabase.Tests;

public class SupabaseMemoryTests
{
    [Fact]
    public void SupabaseMemorySchemaScript_ShouldContainDefaultTables()
    {
        var stmts = SupabaseMemorySchemaScript.BuildStatements(new SupabaseMemoryOptions
        {
            VectorDimensions = 2
        });

        var sql = string.Join("\n", stmts);
        sql.Should().Contain("CREATE SCHEMA IF NOT EXISTS aevatar_memory");
        sql.Should().Contain("CREATE TABLE IF NOT EXISTS aevatar_memory.memory_entries");
        sql.Should().Contain("CREATE TABLE IF NOT EXISTS aevatar_memory.memory_vectors");
        sql.Should().Contain("embedding vector(2)");
    }

    [Fact]
    public void SupabaseMemorySchemaScript_ShouldThrow_WhenVectorDimensionsMissing()
    {
        var act = () => SupabaseMemorySchemaScript.BuildStatements(new SupabaseMemoryOptions
        {
            VectorDimensions = 0
        });

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void AddAevatarSupabaseMemory_ShouldRegisterOptions_And_DataSource()
    {
        var services = new ServiceCollection();
        services.AddAevatarSupabaseMemory(
            connectionString: "Host=localhost;Username=postgres;Password=postgres;Database=postgres",
            configure: o =>
            {
                // Disable init to avoid real DB connections during tests.
                o.AutoCreateSchema = false;
                o.AutoCreateTables = false;
                o.AutoCreateIndexes = false;
                o.LockDownPublicAccess = false;
                o.EnableRowLevelSecurity = false;

                o.VectorDimensions = 2;
            });

        using var provider = services.BuildServiceProvider();
        provider.GetService<IOptions<SupabaseMemoryOptions>>().Should().NotBeNull();
        provider.GetService<NpgsqlDataSource>().Should().NotBeNull();
    }

    [Fact]
    public void SupabaseMemoryStore_And_VectorIndex_ShouldBeConstructible_WhenAutoInitDisabled()
    {
        var services = new ServiceCollection();
        services.AddAevatarSupabaseMemory(
            connectionString: "Host=localhost;Username=postgres;Password=postgres;Database=postgres",
            configure: o =>
            {
                // All init switches off => constructors won't open connections.
                o.AutoCreateVectorExtension = false;
                o.AutoCreateSchema = false;
                o.AutoCreateTables = false;
                o.AutoCreateIndexes = false;
                o.LockDownPublicAccess = false;
                o.EnableRowLevelSecurity = false;
                o.ForceRowLevelSecurity = false;
                o.CreateServiceRolePolicies = false;

                // Keep sane identifiers.
                o.Schema = "aevatar_memory";
                o.MemoryEntriesTable = "memory_entries";
                o.MemoryVectorsTable = "memory_vectors";

                o.VectorDimensions = 2;
            });

        services.AddSingleton<IMemoryStore, SupabaseMemoryStore>();
        services.AddSingleton<IMemoryVectorIndex, SupabaseMemoryVectorIndex>();

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IMemoryStore>().Should().BeOfType<SupabaseMemoryStore>();
        provider.GetRequiredService<IMemoryVectorIndex>().Should().BeOfType<SupabaseMemoryVectorIndex>();
    }
}


