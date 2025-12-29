using System.Collections.Immutable;
using Aevatar.Agents.Persistence.Graph.Providers.Neo4j;
using Moq;
using Neo4j.Driver;
using Shouldly;

namespace Aevatar.Agents.Persistence.Graph.Tests;

public class Neo4jClientTests
{
    [Fact]
    public async Task ReadAsync_validates_cypher_and_uses_write_session()
    {
        var sessionFactory = new Mock<INeo4jSessionFactory>();
        var session = new Mock<IAsyncSession>();
        var cursor = new Mock<IResultCursor>();
        cursor.Setup(c => c.FetchAsync()).ReturnsAsync(false);
        session.Setup(s => s.RunAsync(It.IsAny<string>(), It.IsAny<object?>()))
            .ReturnsAsync(cursor.Object);
        sessionFactory.Setup(f => f.ExecuteWriteAsync(It.IsAny<Func<IAsyncSession, Task<IReadOnlyList<int>>>>(), It.IsAny<CancellationToken>()))
            .Returns<Func<IAsyncSession, Task<IReadOnlyList<int>>>, CancellationToken>(async (work, _) => await work(session.Object))
            .Verifiable();

        var client = new Neo4jClient(sessionFactory.Object);
        var result = await client.ReadAsync("MATCH (n) RETURN n", null, _ => 1);

        result.ShouldBeEmpty();
        sessionFactory.Verify();
    }

    [Fact]
    public async Task ReadAsync_throws_on_empty_cypher()
    {
        var client = new Neo4jClient(Mock.Of<INeo4jSessionFactory>());
        await Should.ThrowAsync<ArgumentException>(() => client.ReadAsync("", null, _ => 1));
    }

    [Fact]
    public async Task ReadNodesAsync_throws_on_empty_alias()
    {
        var client = new Neo4jClient(Mock.Of<INeo4jSessionFactory>());
        await Should.ThrowAsync<ArgumentException>(() => client.ReadNodesAsync("MATCH (n) RETURN n", alias: ""));
    }

    [Fact]
    public async Task WriteAsync_validates_and_consumes()
    {
        var sessionFactory = new Mock<INeo4jSessionFactory>();
        var session = new Mock<IAsyncSession>();
        var cursor = new Mock<IResultCursor>();
        cursor.Setup(c => c.ConsumeAsync()).ReturnsAsync(Mock.Of<IResultSummary>());
        session.Setup(s => s.RunAsync(It.IsAny<string>(), It.IsAny<object?>()))
            .ReturnsAsync(cursor.Object);
        sessionFactory.Setup(f => f.ExecuteWriteAsync(It.IsAny<Func<IAsyncSession, Task<bool>>>(), It.IsAny<CancellationToken>()))
            .Returns<Func<IAsyncSession, Task<bool>>, CancellationToken>(async (work, _) => await work(session.Object))
            .Verifiable();

        var client = new Neo4jClient(sessionFactory.Object);
        await client.WriteAsync("CREATE (n)", null);

        sessionFactory.Verify();
    }

    [Fact]
    public async Task NormalizeParameters_returns_immutable_dict()
    {
        var sessionFactory = new Mock<INeo4jSessionFactory>();
        var session = new Mock<IAsyncSession>();
        var cursor = new Mock<IResultCursor>();
        cursor.Setup(c => c.FetchAsync()).ReturnsAsync(false);
        session.Setup(s => s.RunAsync(It.IsAny<string>(), It.IsAny<object?>()))
            .ReturnsAsync(cursor.Object)
            .Verifiable();
        sessionFactory.Setup(f => f.ExecuteWriteAsync(It.IsAny<Func<IAsyncSession, Task<IReadOnlyList<int>>>>(), It.IsAny<CancellationToken>()))
            .Returns<Func<IAsyncSession, Task<IReadOnlyList<int>>>, CancellationToken>(async (work, _) => await work(session.Object));

        var client = new Neo4jClient(sessionFactory.Object);
        await client.ReadAsync("MATCH (n) RETURN n", new Dictionary<string, object?> { ["k"] = 1 }, _ => 1);

        session.Verify();
    }
}
