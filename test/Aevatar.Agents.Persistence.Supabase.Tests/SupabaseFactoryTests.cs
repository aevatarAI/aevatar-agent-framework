using Aevatar.Agents.Persistence.Supabase.GAgent.Options;
using Aevatar.Agents.Persistence.Supabase.GAgent.Stores;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;
using Npgsql;
using Xunit;

namespace Aevatar.Agents.Persistence.Supabase.Tests;

public class SupabaseFactoryTests
{
    [Fact]
    public void StateStoreFactory_Create_ShouldReturnFactoryFunction()
    {
        SupabaseStateStoreFactory.Create<TestState>().Should().NotBeNull();
    }

    [Fact]
    public void ConfigStoreFactory_Create_ShouldReturnFactoryFunction()
    {
        SupabaseConfigurationStoreFactory.Create<TestConfig>().Should().NotBeNull();
    }

    [Fact]
    public void EventRouterStoreFactory_Create_ShouldReturnFactoryFunction()
    {
        SupabaseEventRouterStoreFactory.Create().Should().NotBeNull();
    }

    [Fact]
    public void StateStoreFactory_ShouldThrow_WhenDataSourceNotRegistered()
    {
        var sp = new Mock<IServiceProvider>();
        sp.Setup(x => x.GetService(typeof(NpgsqlDataSource))).Returns((object?)null);

        var factory = SupabaseStateStoreFactory.Create<TestState>();
        var act = () => factory(sp.Object);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*NpgsqlDataSource not registered*");
    }

    [Fact]
    public void ConfigStoreFactory_ShouldThrow_WhenOptionsNotRegistered()
    {
        var sp = new Mock<IServiceProvider>();
        var ds = NpgsqlDataSource.Create("Host=localhost;Username=postgres;Password=postgres;Database=postgres");

        sp.Setup(x => x.GetService(typeof(NpgsqlDataSource))).Returns(ds);
        sp.Setup(x => x.GetService(typeof(IOptions<SupabasePersistenceOptions>))).Returns((object?)null);

        var factory = SupabaseConfigurationStoreFactory.Create<TestConfig>();
        var act = () => factory(sp.Object);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*SupabasePersistenceOptions not registered*");
    }

    [Fact]
    public void EventRouterStoreFactory_ShouldCreateStore_WhenDependenciesPresent_AndInitDisabled()
    {
        // Arrange: disable all auto-init toggles to avoid opening DB during construction
        var options = Microsoft.Extensions.Options.Options.Create(new SupabasePersistenceOptions
        {
            ConnectionString = "Host=localhost;Username=postgres;Password=postgres;Database=postgres",
            AutoCreateSchema = false,
            AutoCreateTables = false,
            AutoCreateIndexes = false,
            LockDownPublicAccess = false,
            EnableRowLevelSecurity = false
        });

        var ds = NpgsqlDataSource.Create(options.Value.ConnectionString);

        var sp = new Mock<IServiceProvider>();
        sp.Setup(x => x.GetService(typeof(NpgsqlDataSource))).Returns(ds);
        sp.Setup(x => x.GetService(typeof(IOptions<SupabasePersistenceOptions>))).Returns(options);

        // Act
        var factory = SupabaseEventRouterStoreFactory.Create();
        var store = factory(sp.Object);

        // Assert
        store.Should().NotBeNull();
    }
}


