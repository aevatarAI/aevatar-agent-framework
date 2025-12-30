using Aevatar.Agents.Abstractions.EventRouting;
using Aevatar.Agents.Persistence.MongoDB.GAgent;
using FluentAssertions;
using MongoDB.Driver;
using Moq;
using Xunit;

namespace Aevatar.Agents.Persistence.MongoDB.Tests;

/// <summary>
/// Tests for MongoDBEventRouterStore
/// </summary>
public class MongoDBEventRouterStoreTests
{
    private readonly Mock<IMongoDatabase> _mockDatabase;
    private readonly Mock<IMongoCollection<EventRouterHierarchyDocument>> _mockCollection;
    private readonly Mock<IMongoIndexManager<EventRouterHierarchyDocument>> _mockIndexManager;
    private readonly MongoDBEventRouterStore _store;
    private readonly string _collectionName;

    public MongoDBEventRouterStoreTests()
    {
        _mockDatabase = new Mock<IMongoDatabase>();
        _mockCollection = new Mock<IMongoCollection<EventRouterHierarchyDocument>>();
        _mockIndexManager = new Mock<IMongoIndexManager<EventRouterHierarchyDocument>>();
        
        // Use unique collection name per test instance to avoid index manager cache issues
        _collectionName = $"agent_event_router_hierarchies_{Guid.NewGuid():N}";

        var dbNamespace = new DatabaseNamespace("test_db");
        _mockDatabase.Setup(d => d.DatabaseNamespace).Returns(dbNamespace);

        var collectionNamespace = new CollectionNamespace("test_db", _collectionName);
        _mockCollection.Setup(c => c.CollectionNamespace).Returns(collectionNamespace);
        _mockCollection.Setup(c => c.Database).Returns(_mockDatabase.Object);
        _mockCollection.Setup(c => c.Indexes).Returns(_mockIndexManager.Object);

        _mockDatabase
            .Setup(d => d.GetCollection<EventRouterHierarchyDocument>(It.IsAny<string>(), null))
            .Returns(_mockCollection.Object);

        _mockIndexManager
            .Setup(i => i.CreateMany(
                It.IsAny<IEnumerable<CreateIndexModel<EventRouterHierarchyDocument>>>(),
                It.IsAny<CancellationToken>()))
            .Returns(new[] { "idx_parent_id", "idx_updated_at" });

        _store = new MongoDBEventRouterStore(_mockDatabase.Object, _collectionName);
    }

    [Fact]
    public async Task LoadAsync_ShouldReturnNull_WhenNoDocument()
    {
        // Arrange
        var agentId = Guid.NewGuid().ToString();
        var mockCursor = CreateEmptyCursor();

        _mockCollection
            .Setup(c => c.FindAsync(
                It.IsAny<FilterDefinition<EventRouterHierarchyDocument>>(),
                It.IsAny<FindOptions<EventRouterHierarchyDocument, EventRouterHierarchyDocument>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockCursor);

        // Act
        var result = await _store.LoadAsync(agentId);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task LoadAsync_ShouldReturnHierarchy_WhenDocumentExists()
    {
        // Arrange
        var agentId = Guid.NewGuid().ToString();
        var parentId = Guid.NewGuid().ToString();
        var childId = Guid.NewGuid().ToString();

        var doc = new EventRouterHierarchyDocument
        {
            AgentId = agentId,
            ParentId = parentId,
            ChildrenIds = new List<string> { childId },
            UpdatedAt = DateTime.UtcNow
        };

        var mockCursor = CreateCursor(doc);

        _mockCollection
            .Setup(c => c.FindAsync(
                It.IsAny<FilterDefinition<EventRouterHierarchyDocument>>(),
                It.IsAny<FindOptions<EventRouterHierarchyDocument, EventRouterHierarchyDocument>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockCursor);

        // Act
        var result = await _store.LoadAsync(agentId);

        // Assert
        result.Should().NotBeNull();
        result!.ParentId.Should().Be(parentId);
        result.ChildrenIds.Should().Contain(childId);
    }

    [Fact]
    public async Task SaveAsync_ShouldCallReplaceOne()
    {
        // Arrange
        var agentId = Guid.NewGuid().ToString();
        var hierarchy = new EventRouterHierarchy
        {
            ParentId = Guid.NewGuid().ToString(),
            ChildrenIds = new HashSet<string> { Guid.NewGuid().ToString() }
        };

        _mockCollection
            .Setup(c => c.ReplaceOneAsync(
                It.IsAny<FilterDefinition<EventRouterHierarchyDocument>>(),
                It.IsAny<EventRouterHierarchyDocument>(),
                It.IsAny<ReplaceOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReplaceOneResult.Acknowledged(1, 1, null));

        // Act
        await _store.SaveAsync(agentId, hierarchy);

        // Assert
        _mockCollection.Verify(c => c.ReplaceOneAsync(
            It.IsAny<FilterDefinition<EventRouterHierarchyDocument>>(),
            It.Is<EventRouterHierarchyDocument>(d =>
                d.AgentId == agentId &&
                d.ParentId == hierarchy.ParentId &&
                d.ChildrenIds.Count == 1),
            It.Is<ReplaceOptions>(o => o.IsUpsert),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteAsync_ShouldCallDeleteOne()
    {
        // Arrange
        var agentId = Guid.NewGuid().ToString();

        _mockCollection
            .Setup(c => c.DeleteOneAsync(
                It.IsAny<FilterDefinition<EventRouterHierarchyDocument>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DeleteResult.Acknowledged(1));

        // Act
        await _store.DeleteAsync(agentId);

        // Assert
        _mockCollection.Verify(c => c.DeleteOneAsync(
            It.IsAny<FilterDefinition<EventRouterHierarchyDocument>>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExistsAsync_ShouldReturnTrue_WhenDocumentExists()
    {
        // Arrange
        var agentId = Guid.NewGuid().ToString();

        _mockCollection
            .Setup(c => c.CountDocumentsAsync(
                It.IsAny<FilterDefinition<EventRouterHierarchyDocument>>(),
                It.IsAny<CountOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        // Act
        var result = await _store.ExistsAsync(agentId);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task ExistsAsync_ShouldReturnFalse_WhenDocumentNotExists()
    {
        // Arrange
        var agentId = Guid.NewGuid().ToString();

        _mockCollection
            .Setup(c => c.CountDocumentsAsync(
                It.IsAny<FilterDefinition<EventRouterHierarchyDocument>>(),
                It.IsAny<CountOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        // Act
        var result = await _store.ExistsAsync(agentId);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void Constructor_ShouldUseDefaultCollectionName()
    {
        // Arrange
        var mockDatabase = new Mock<IMongoDatabase>();
        var mockCollection = new Mock<IMongoCollection<EventRouterHierarchyDocument>>();
        var mockIndexManager = new Mock<IMongoIndexManager<EventRouterHierarchyDocument>>();
        var uniqueCollectionName = $"default_test_{Guid.NewGuid():N}";

        var dbNamespace = new DatabaseNamespace("test_db");
        mockDatabase.Setup(d => d.DatabaseNamespace).Returns(dbNamespace);

        var collectionNamespace = new CollectionNamespace("test_db", "agent_event_router_hierarchies");
        mockCollection.Setup(c => c.CollectionNamespace).Returns(collectionNamespace);
        mockCollection.Setup(c => c.Database).Returns(mockDatabase.Object);
        mockCollection.Setup(c => c.Indexes).Returns(mockIndexManager.Object);

        mockDatabase
            .Setup(d => d.GetCollection<EventRouterHierarchyDocument>("agent_event_router_hierarchies", null))
            .Returns(mockCollection.Object);

        mockIndexManager
            .Setup(i => i.CreateMany(
                It.IsAny<IEnumerable<CreateIndexModel<EventRouterHierarchyDocument>>>(),
                It.IsAny<CancellationToken>()))
            .Returns([]);

        // Act
        var store = new MongoDBEventRouterStore(mockDatabase.Object);

        // Assert - Just verify it doesn't throw
        store.Should().NotBeNull();
    }

    private static IAsyncCursor<EventRouterHierarchyDocument> CreateEmptyCursor()
    {
        var mockCursor = new Mock<IAsyncCursor<EventRouterHierarchyDocument>>();
        mockCursor.Setup(c => c.MoveNextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        mockCursor.Setup(c => c.Current)
            .Returns(Enumerable.Empty<EventRouterHierarchyDocument>());
        return mockCursor.Object;
    }

    private static IAsyncCursor<EventRouterHierarchyDocument> CreateCursor(
        EventRouterHierarchyDocument document)
    {
        var mockCursor = new Mock<IAsyncCursor<EventRouterHierarchyDocument>>();
        mockCursor
            .SetupSequence(c => c.MoveNextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(true)
            .ReturnsAsync(false);
        mockCursor.Setup(c => c.Current)
            .Returns(new[] { document });
        return mockCursor.Object;
    }
}

/// <summary>
/// Tests for MongoDBEventRouterStoreFactory
/// </summary>
public class MongoDBEventRouterStoreFactoryTests
{
    [Fact]
    public void Create_ShouldReturnFactoryFunction()
    {
        // Act
        var factory = MongoDBEventRouterStoreFactory.Create();

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

        var factory = MongoDBEventRouterStoreFactory.Create();

        // Act & Assert
        var act = () => factory(mockServiceProvider.Object);
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*IMongoDatabase not registered*");
    }
}
