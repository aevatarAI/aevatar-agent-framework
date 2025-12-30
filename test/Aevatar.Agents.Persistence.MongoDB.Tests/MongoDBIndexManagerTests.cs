using Aevatar.Agents.Persistence.MongoDB.GAgent;
using FluentAssertions;
using MongoDB.Driver;
using Moq;
using Xunit;

namespace Aevatar.Agents.Persistence.MongoDB.Tests;

/// <summary>
/// Tests for MongoDBIndexManager
/// Verifies index creation behavior through mocked MongoDB collections
/// </summary>
public class MongoDBIndexManagerTests
{
    [Fact]
    public void EventRouterStore_ShouldCreateIndexes_WhenConstructed()
    {
        // Arrange
        var (mockDatabase, mockIndexManager) = CreateMockDatabaseWithIndexTracking("test_router_create");

        // Act
        var store = new MongoDBEventRouterStore(mockDatabase.Object, "test_router_create");

        // Assert - Should have called CreateMany with 2 index models
        mockIndexManager.Verify(
            i => i.CreateMany(
                It.Is<IEnumerable<CreateIndexModel<EventRouterHierarchyDocument>>>(
                    models => models.Count() == 2),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public void EventRouterStore_ShouldNotCreateIndexes_WhenCalledTwice()
    {
        // Arrange - Use a unique collection name to avoid cache conflicts
        var collectionName = $"test_router_idempotent_{Guid.NewGuid():N}";
        var callCount = 0;

        var mockDatabase = new Mock<IMongoDatabase>();
        var mockCollection = new Mock<IMongoCollection<EventRouterHierarchyDocument>>();
        var mockIndexManager = new Mock<IMongoIndexManager<EventRouterHierarchyDocument>>();

        SetupMockDatabase(mockDatabase, mockCollection, mockIndexManager, collectionName);

        mockIndexManager
            .Setup(i => i.CreateMany(
                It.IsAny<IEnumerable<CreateIndexModel<EventRouterHierarchyDocument>>>(),
                It.IsAny<CancellationToken>()))
            .Callback(() => callCount++)
            .Returns(new[] { "idx_parent_id", "idx_updated_at" });

        // Act - Create two stores with same collection
        var store1 = new MongoDBEventRouterStore(mockDatabase.Object, collectionName);
        var store2 = new MongoDBEventRouterStore(mockDatabase.Object, collectionName);

        // Assert - Should only be called once due to caching
        callCount.Should().Be(1);
    }

    [Fact]
    public void EventRouterStore_ShouldCreateIndexes_ForDifferentCollections()
    {
        // Arrange
        var collection1 = $"collection_a_{Guid.NewGuid():N}";
        var collection2 = $"collection_b_{Guid.NewGuid():N}";
        var callCounts = new Dictionary<string, int>();

        var mockDatabase = new Mock<IMongoDatabase>();
        var dbNamespace = new DatabaseNamespace("test_db");
        mockDatabase.Setup(d => d.DatabaseNamespace).Returns(dbNamespace);

        mockDatabase
            .Setup(d => d.GetCollection<EventRouterHierarchyDocument>(It.IsAny<string>(), null))
            .Returns((string name, MongoCollectionSettings? _) =>
            {
                var mockCollection = new Mock<IMongoCollection<EventRouterHierarchyDocument>>();
                var mockIndexManager = new Mock<IMongoIndexManager<EventRouterHierarchyDocument>>();

                var collectionNamespace = new CollectionNamespace("test_db", name);
                mockCollection.Setup(c => c.CollectionNamespace).Returns(collectionNamespace);
                mockCollection.Setup(c => c.Database).Returns(mockDatabase.Object);
                mockCollection.Setup(c => c.Indexes).Returns(mockIndexManager.Object);

                mockIndexManager
                    .Setup(i => i.CreateMany(
                        It.IsAny<IEnumerable<CreateIndexModel<EventRouterHierarchyDocument>>>(),
                        It.IsAny<CancellationToken>()))
                    .Callback(() =>
                    {
                        if (!callCounts.ContainsKey(name))
                            callCounts[name] = 0;
                        callCounts[name]++;
                    })
                    .Returns(new[] { "idx_parent_id", "idx_updated_at" });

                return mockCollection.Object;
            });

        // Act - Create stores with different collection names
        var store1 = new MongoDBEventRouterStore(mockDatabase.Object, collection1);
        var store2 = new MongoDBEventRouterStore(mockDatabase.Object, collection2);

        // Assert - Each unique collection should have indexes created once
        callCounts.Should().ContainKey(collection1);
        callCounts.Should().ContainKey(collection2);
        callCounts[collection1].Should().Be(1);
        callCounts[collection2].Should().Be(1);
    }

    private static (Mock<IMongoDatabase>, Mock<IMongoIndexManager<EventRouterHierarchyDocument>>)
        CreateMockDatabaseWithIndexTracking(string collectionName)
    {
        var mockDatabase = new Mock<IMongoDatabase>();
        var mockCollection = new Mock<IMongoCollection<EventRouterHierarchyDocument>>();
        var mockIndexManager = new Mock<IMongoIndexManager<EventRouterHierarchyDocument>>();

        SetupMockDatabase(mockDatabase, mockCollection, mockIndexManager, collectionName);

        mockIndexManager
            .Setup(i => i.CreateMany(
                It.IsAny<IEnumerable<CreateIndexModel<EventRouterHierarchyDocument>>>(),
                It.IsAny<CancellationToken>()))
            .Returns(new[] { "idx_parent_id", "idx_updated_at" });

        return (mockDatabase, mockIndexManager);
    }

    private static void SetupMockDatabase(
        Mock<IMongoDatabase> mockDatabase,
        Mock<IMongoCollection<EventRouterHierarchyDocument>> mockCollection,
        Mock<IMongoIndexManager<EventRouterHierarchyDocument>> mockIndexManager,
        string collectionName)
    {
        var dbNamespace = new DatabaseNamespace("test_db");
        mockDatabase.Setup(d => d.DatabaseNamespace).Returns(dbNamespace);

        var collectionNamespace = new CollectionNamespace("test_db", collectionName);
        mockCollection.Setup(c => c.CollectionNamespace).Returns(collectionNamespace);
        mockCollection.Setup(c => c.Database).Returns(mockDatabase.Object);
        mockCollection.Setup(c => c.Indexes).Returns(mockIndexManager.Object);

        mockDatabase
            .Setup(d => d.GetCollection<EventRouterHierarchyDocument>(collectionName, null))
            .Returns(mockCollection.Object);
    }
}
