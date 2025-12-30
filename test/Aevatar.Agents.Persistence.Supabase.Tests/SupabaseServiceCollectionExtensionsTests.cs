using Aevatar.Agents.Persistence.Supabase.DependencyInjection;
using Aevatar.Agents.Persistence.Supabase.GAgent.DependencyInjection;
using Aevatar.Agents.Persistence.Supabase.GAgent.Options;
using Aevatar.Agents.Persistence.Supabase.GAgent.Setup;
using Aevatar.Agents.Persistence.Supabase.GAgent.Stores;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;
using Xunit;

namespace Aevatar.Agents.Persistence.Supabase.Tests;

/// <summary>
/// Tests for SupabaseServiceCollectionExtensions / SchemaScript.
/// 
/// NOTE:
/// - Tests avoid resolving store instances to prevent real DB connections.
/// </summary>
public class SupabaseServiceCollectionExtensionsTests
{
    [Fact]
    public void AddAevatarSupabase_ShouldThrow_OnNullConnectionString()
    {
        var services = new ServiceCollection();
        var act = () => services.AddAevatarSupabase(connectionString: null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void AddAevatarSupabase_ShouldThrow_OnEmptyConnectionString()
    {
        var services = new ServiceCollection();
        var act = () => services.AddAevatarSupabase(connectionString: "  ");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void AddAevatarSupabaseGAgent_ConfigureOnly_ShouldRegisterOptions_ButNotDataSource()
    {
        var services = new ServiceCollection();
        services.AddAevatarSupabaseGAgent(_ => { });

        var provider = services.BuildServiceProvider();

        provider.GetService<IOptions<SupabasePersistenceOptions>>().Should().NotBeNull();
        provider.GetService<NpgsqlDataSource>().Should().BeNull();
    }

    [Fact]
    public void AddAevatarSupabase_And_AddAevatarSupabaseGAgent_ShouldRegisterOptionsAndDataSource()
    {
        var services = new ServiceCollection();

        services.AddAevatarSupabase("Host=localhost;Username=postgres;Password=postgres;Database=postgres");
        services.AddAevatarSupabaseGAgent(_ => { });

        var provider = services.BuildServiceProvider();

        provider.GetService<IOptions<SupabasePersistenceOptions>>().Should().NotBeNull();
        provider.GetService<NpgsqlDataSource>().Should().NotBeNull();
    }

    [Fact]
    public void AddAevatarSupabaseGAgent_ShouldApplyDefaultOptions()
    {
        var services = new ServiceCollection();
        services.AddAevatarSupabaseGAgent(_ => { });

        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<SupabasePersistenceOptions>>().Value;

        options.Schema.Should().Be("aevatar");
        options.AgentStatesTable.Should().Be("agent_states");
        options.LockDownPublicAccess.Should().BeTrue();
    }

    [Fact]
    public void AddSupabaseStores_ShouldRegisterServices()
    {
        var services = new ServiceCollection();

        services.AddAevatarSupabase("Host=localhost;Username=postgres;Password=postgres;Database=postgres");
        services.AddAevatarSupabaseGAgent(o =>
        {
            // Disable init to avoid real DB connections during tests.
            o.AutoCreateSchema = false;
            o.AutoCreateTables = false;
            o.AutoCreateIndexes = false;
            o.LockDownPublicAccess = false;
            o.EnableRowLevelSecurity = false;
        });

        services.AddSupabaseStateStore<TestState>();
        services.AddSupabaseConfigStore<TestConfig>();
        services.AddSupabaseEventRouterStore();

        services.Should().ContainSingle(d => d.ServiceType == typeof(SupabaseStateStore<TestState>)
                                             && d.Lifetime == ServiceLifetime.Singleton);
        services.Should().ContainSingle(d => d.ServiceType == typeof(SupabaseConfigStore<TestConfig>)
                                             && d.Lifetime == ServiceLifetime.Singleton);
        services.Should().ContainSingle(d => d.ServiceType == typeof(SupabaseEventRouterStore)
                                             && d.Lifetime == ServiceLifetime.Singleton);
    }

    [Fact]
    public void AllExtensionMethods_ShouldSupportChaining()
    {
        var services = new ServiceCollection();

        var result = services
            .AddAevatarSupabase("Host=localhost;Username=postgres;Password=postgres;Database=postgres")
            .AddAevatarSupabaseGAgent(_ => { })
            .AddSupabaseStateStore<TestState>()
            .AddSupabaseConfigStore<TestConfig>()
            .AddSupabaseEventRouterStore();

        result.Should().BeSameAs(services);
    }

    [Fact]
    public void SchemaScript_ShouldContainDefaultTables()
    {
        var sql = SupabaseSchemaScript.BuildSql(new SupabasePersistenceOptions());

        sql.Should().Contain("CREATE SCHEMA IF NOT EXISTS aevatar");
        sql.Should().Contain("CREATE TABLE IF NOT EXISTS aevatar.agent_states");
        sql.Should().Contain("CREATE TABLE IF NOT EXISTS aevatar.agent_configs");
        sql.Should().Contain("CREATE TABLE IF NOT EXISTS aevatar.agent_event_router_hierarchies");
    }

    [Fact]
    public void SchemaScript_ShouldThrow_OnInvalidIdentifiers()
    {
        var options = new SupabasePersistenceOptions
        {
            Schema = "public;drop schema public;--"
        };

        var act = () => SupabaseSchemaScript.BuildSql(options);
        act.Should().Throw<ArgumentException>();
    }
}


