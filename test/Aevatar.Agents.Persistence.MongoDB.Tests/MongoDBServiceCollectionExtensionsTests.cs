using Aevatar.Agents.Persistence.MongoDB;
using Aevatar.Agents.Persistence.MongoDB.GAgent;
using Aevatar.Agents.Persistence.MongoDB.GAgent.DependencyInjection;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;
using Moq;
using Xunit;

namespace Aevatar.Agents.Persistence.MongoDB.Tests;

/// <summary>
/// Tests for MongoDBServiceCollectionExtensions
/// 
/// Uses Protobuf-generated test types for byte[] serialization compatibility.
/// </summary>
public class MongoDBServiceCollectionExtensionsTests
{
    [Fact]
    public void AddAevatarMongoDB_ShouldRegisterIMongoClient()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddAevatarMongoDB("mongodb://localhost:27017");
        var provider = services.BuildServiceProvider();

        // Assert
        var client = provider.GetService<IMongoClient>();
        client.Should().NotBeNull();
    }

    [Fact]
    public void AddAevatarMongoDB_ShouldRegisterIMongoDatabase()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddAevatarMongoDB("mongodb://localhost:27017", "test_db");
        var provider = services.BuildServiceProvider();

        // Assert
        var database = provider.GetService<IMongoDatabase>();
        database.Should().NotBeNull();
        database!.DatabaseNamespace.DatabaseName.Should().Be("test_db");
    }

    [Fact]
    public void AddAevatarMongoDB_ShouldRegisterAsSingleton()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddAevatarMongoDB("mongodb://localhost:27017");
        var provider = services.BuildServiceProvider();

        // Assert
        var client1 = provider.GetService<IMongoClient>();
        var client2 = provider.GetService<IMongoClient>();
        client1.Should().BeSameAs(client2);

        var db1 = provider.GetService<IMongoDatabase>();
        var db2 = provider.GetService<IMongoDatabase>();
        db1.Should().BeSameAs(db2);
    }

    [Fact]
    public void AddAevatarMongoDB_WithSettings_ShouldRegisterClient()
    {
        // Arrange
        var services = new ServiceCollection();
        var settings = new MongoClientSettings
        {
            Server = new MongoServerAddress("localhost", 27017),
            MaxConnectionPoolSize = 50
        };

        // Act
        services.AddAevatarMongoDB(settings, "custom_db");
        var provider = services.BuildServiceProvider();

        // Assert
        var client = provider.GetService<IMongoClient>();
        client.Should().NotBeNull();

        var database = provider.GetService<IMongoDatabase>();
        database.Should().NotBeNull();
        database!.DatabaseNamespace.DatabaseName.Should().Be("custom_db");
    }

    [Fact]
    public void AddAevatarMongoDB_ShouldThrowOnNullConnectionString()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act & Assert
        var act = () => services.AddAevatarMongoDB(connectionString: null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void AddAevatarMongoDB_WithSettings_ShouldThrowOnNullSettings()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act & Assert
        var act = () => services.AddAevatarMongoDB((MongoClientSettings)null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void AddAevatarMongoDB_ShouldUseDefaultDatabaseName()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddAevatarMongoDB("mongodb://localhost:27017");
        var provider = services.BuildServiceProvider();

        // Assert
        var database = provider.GetService<IMongoDatabase>();
        database.Should().NotBeNull();
        database!.DatabaseNamespace.DatabaseName.Should().Be("aevatar");
    }

    [Fact]
    public void AddMongoDBEventRouterStore_ShouldRegisterService()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddAevatarMongoDB("mongodb://localhost:27017");

        // Act
        services.AddMongoDBEventRouterStore();

        // Assert - Check that the service is registered (without resolving to avoid MongoDB connection)
        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(MongoDBEventRouterStore));
        descriptor.Should().NotBeNull();
        descriptor!.Lifetime.Should().Be(ServiceLifetime.Singleton);
    }

    [Fact]
    public void AddMongoDBStateStore_ShouldRegisterService()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddAevatarMongoDB("mongodb://localhost:27017");

        // Act
        services.AddMongoDBStateStore<TestState>();

        // Assert - Check that the service is registered
        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(MongoDBStateStore<TestState>));
        descriptor.Should().NotBeNull();
        descriptor!.Lifetime.Should().Be(ServiceLifetime.Singleton);
    }

    [Fact]
    public void AddMongoDBConfigStore_ShouldRegisterService()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddAevatarMongoDB("mongodb://localhost:27017");

        // Act
        services.AddMongoDBConfigStore<TestConfig>();

        // Assert - Check that the service is registered
        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(MongoDbConfigStore<TestConfig>));
        descriptor.Should().NotBeNull();
        descriptor!.Lifetime.Should().Be(ServiceLifetime.Singleton);
    }

    [Fact]
    public void AllExtensionMethods_ShouldSupportChaining()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act & Assert - All methods should support fluent chaining
        var result = services
            .AddAevatarMongoDB("mongodb://localhost:27017", "test_db")
            .AddMongoDBStateStore<TestState>()
            .AddMongoDBConfigStore<TestConfig>()
            .AddMongoDBEventRouterStore();

        result.Should().BeSameAs(services);
    }
}
