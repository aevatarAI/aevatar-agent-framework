using Aevatar.Agents.Abstractions.Memory;
using Aevatar.Agents.Persistence.MongoDB.Memory.DependencyInjection;
using Aevatar.Agents.Persistence.MongoDB.Memory.Documents;
using Aevatar.Agents.Persistence.MongoDB.Memory.Stores;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;
using Moq;
using Xunit;

namespace Aevatar.Agents.Persistence.MongoDB.Tests;

public class MongoDbMemoryServiceCollectionExtensionsTests
{
    [Fact]
    public void AddAevatarMongoDBMemory_ShouldResolveStoreAndVectorIndex_WhenMongoDatabaseIsProvided()
    {
        var services = new ServiceCollection();

        // ------------------------------------------------------------
        //  Mock MongoDB surface to avoid external DB dependency.
        //  - Store constructors create indexes eagerly.
        // ------------------------------------------------------------
        var entryIndexManager = new Mock<IMongoIndexManager<MemoryEntryDocument>>(MockBehavior.Strict);
        entryIndexManager
            .Setup(m => m.CreateOne(
                It.IsAny<CreateIndexModel<MemoryEntryDocument>>(),
                It.IsAny<CreateOneIndexOptions>(),
                It.IsAny<CancellationToken>()))
            .Returns("idx_entries");

        var entries = new Mock<IMongoCollection<MemoryEntryDocument>>(MockBehavior.Strict);
        entries.SetupGet(c => c.Indexes).Returns(entryIndexManager.Object);

        var vectorIndexManager = new Mock<IMongoIndexManager<MemoryVectorDocument>>(MockBehavior.Strict);
        vectorIndexManager
            .Setup(m => m.CreateOne(
                It.IsAny<CreateIndexModel<MemoryVectorDocument>>(),
                It.IsAny<CreateOneIndexOptions>(),
                It.IsAny<CancellationToken>()))
            .Returns("idx_vectors");

        var vectors = new Mock<IMongoCollection<MemoryVectorDocument>>(MockBehavior.Strict);
        vectors.SetupGet(c => c.Indexes).Returns(vectorIndexManager.Object);

        var db = new Mock<IMongoDatabase>(MockBehavior.Strict);
        db.Setup(d => d.GetCollection<MemoryEntryDocument>(
                It.IsAny<string>(),
                It.IsAny<MongoCollectionSettings>()))
            .Returns(entries.Object);
        db.Setup(d => d.GetCollection<MemoryVectorDocument>(
                It.IsAny<string>(),
                It.IsAny<MongoCollectionSettings>()))
            .Returns(vectors.Object);

        services.AddSingleton(db.Object);

        // Act
        services.AddAevatarMongoDBMemory();
        using var provider = services.BuildServiceProvider();

        // Assert
        provider.GetRequiredService<IMemoryStore>().Should().BeOfType<MongoDbMemoryStore>();
        provider.GetRequiredService<IMemoryVectorIndex>().Should().BeOfType<MongoDbMemoryVectorIndex>();
    }
}


