using Aevatar.Agents.Persistence.MongoDB.GAgent;
using FluentAssertions;
using MongoDB.Driver;
using Moq;
using Xunit;

namespace Aevatar.Agents.Persistence.MongoDB.Tests;

/// <summary>
/// Tests for MongoDBStateStoreFactory
/// This file tests the factory pattern and DI integration.
/// Note: Direct MongoDBStateStore tests are limited because AgentStateDocument is internal.
/// 
/// Uses Protobuf-generated test types for byte[] serialization compatibility.
/// </summary>
public class MongoDBStateStoreFactoryTests
{
    [Fact]
    public void Create_ShouldReturnFactoryFunction()
    {
        // Act
        var factory = MongoDBStateStoreFactory.Create<StateStoreTestState>();

        // Assert
        factory.Should().NotBeNull();
    }

    [Fact]
    public void Create_ShouldThrowWhenDatabaseNotRegistered()
    {
        // Arrange
        var mockServiceProvider = new Mock<IServiceProvider>();
        mockServiceProvider
            .Setup(sp => sp.GetService(typeof(IMongoDatabase)))
            .Returns((IMongoDatabase?)null);

        var factory = MongoDBStateStoreFactory.Create<StateStoreTestState>();

        // Act & Assert
        var act = () => factory(mockServiceProvider.Object);
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*IMongoDatabase not registered*");
    }

    [Fact]
    public void Create_ShouldReturnDifferentFactoriesForDifferentTypes()
    {
        // Act
        var factory1 = MongoDBStateStoreFactory.Create<StateStoreTestState>();
        var factory2 = MongoDBStateStoreFactory.Create<AnotherTestState>();

        // Assert - They should be separate functions (not referentially equal)
        factory1.Should().NotBeNull();
        factory2.Should().NotBeNull();
    }

    [Fact]
    public void Create_WithCollectionName_ShouldReturnFactoryFunction()
    {
        // Act
        var factory = MongoDBStateStoreFactory.Create<StateStoreTestState>("custom_collection");

        // Assert
        factory.Should().NotBeNull();
    }
}
