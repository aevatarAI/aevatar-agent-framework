using Aevatar.Agents.Persistence.Graph.Abstractions;
using Aevatar.Agents.Persistence.Graph.Core;
using Aevatar.Agents.Persistence.Graph.Core.IR;
using Aevatar.Agents.Persistence.Graph.Core.Semantic;
using Aevatar.Agents.Persistence.Graph.Providers.Neo4j;
using Moq;
using Neo4j.Driver;
using Shouldly;

namespace Aevatar.Agents.Persistence.Graph.Tests;

public class Neo4jExecutorTests
{
    [Fact]
    public async Task CreateNode_returns_mapped_id()
    {
        var client = new Mock<INeo4jClient>();
        var node = new Mock<INode>();
        node.SetupGet(n => n.ElementId).Returns("elem-node");
        node.SetupGet(n => n.Labels).Returns(new[] { "__Generic" });
        node.SetupGet(n => n.Properties).Returns(new Dictionary<string, object>
        {
            ["id"] = "node-prop-id",
            ["name"] = "Alice"
        });

        client.Setup(c => c.ReadAsync(
                It.IsAny<string>(),
                It.IsAny<IReadOnlyDictionary<string, object?>>(),
                It.IsAny<Func<IRecord, INode>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<INode> { node.Object });

        var executor = new Neo4jExecutor(client.Object);
        var cmd = new CypherCommand("create", new Dictionary<string, object?>(), new CreateNode("Person", new Dictionary<string, Value>()));

        var result = await executor.ExecuteAsync(cmd);

        result.ShouldBeOfType<NodeId>().Value.ShouldBe("node-prop-id");
    }

    [Fact]
    public async Task CreateEdge_returns_mapped_id()
    {
        var client = new Mock<INeo4jClient>();
        var rel = new Mock<IRelationship>();
        rel.SetupGet(r => r.ElementId).Returns("elem-rel");
        rel.SetupGet(r => r.StartNodeElementId).Returns("from-element");
        rel.SetupGet(r => r.EndNodeElementId).Returns("to-element");
        rel.SetupGet(r => r.Type).Returns("FRIEND_OF");
        rel.SetupGet(r => r.Properties).Returns(new Dictionary<string, object>
        {
            ["id"] = "edge-prop-id",
            ["since"] = 2021
        });

        client.Setup(c => c.ReadAsync(
                It.IsAny<string>(),
                It.IsAny<IReadOnlyDictionary<string, object?>>(),
                It.IsAny<Func<IRecord, IRelationship>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<IRelationship> { rel.Object });

        var executor = new Neo4jExecutor(client.Object);
        var cmd = new CypherCommand("create edge", new Dictionary<string, object?>(), new CreateEdge("FRIEND_OF", new NodeId("from"), new NodeId("to"), new Dictionary<string, Value>()));

        var result = await executor.ExecuteAsync(cmd);

        result.ShouldBeOfType<EdgeId>().Value.ShouldBe("edge-prop-id");
    }

    [Fact]
    public async Task UpdateEdge_calls_write()
    {
        var client = new Mock<INeo4jClient>();
        client.Setup(c => c.WriteAsync(
                It.IsAny<string>(),
                It.IsAny<IReadOnlyDictionary<string, object?>>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask)
            .Verifiable();

        var executor = new Neo4jExecutor(client.Object);
        var cmd = new CypherCommand("update edge", new Dictionary<string, object?>(), new UpdateEdge(new EdgeId("e1"), new Dictionary<string, Value>()));

        await executor.ExecuteAsync(cmd);

        client.Verify(c => c.WriteAsync(
            It.IsAny<string>(),
            It.IsAny<IReadOnlyDictionary<string, object?>>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateNode_calls_write()
    {
        var client = new Mock<INeo4jClient>();
        client.Setup(c => c.WriteAsync(
                It.IsAny<string>(),
                It.IsAny<IReadOnlyDictionary<string, object?>>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask)
            .Verifiable();

        var executor = new Neo4jExecutor(client.Object);
        var cmd = new CypherCommand("update node", new Dictionary<string, object?>(), new UpdateNode(new NodeId("n1"), new Dictionary<string, Value>()));

        await executor.ExecuteAsync(cmd);

        client.Verify(c => c.WriteAsync(
            It.IsAny<string>(),
            It.IsAny<IReadOnlyDictionary<string, object?>>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteNode_calls_write()
    {
        var client = new Mock<INeo4jClient>();
        client.Setup(c => c.WriteAsync(
                It.IsAny<string>(),
                It.IsAny<IReadOnlyDictionary<string, object?>>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask)
            .Verifiable();

        var executor = new Neo4jExecutor(client.Object);
        var cmd = new CypherCommand("delete node", new Dictionary<string, object?>(), new DeleteNode(new NodeId("n1")));

        await executor.ExecuteAsync(cmd);

        client.Verify(c => c.WriteAsync(
            It.IsAny<string>(),
            It.IsAny<IReadOnlyDictionary<string, object?>>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteEdge_calls_write()
    {
        var client = new Mock<INeo4jClient>();
        client.Setup(c => c.WriteAsync(
                It.IsAny<string>(),
                It.IsAny<IReadOnlyDictionary<string, object?>>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask)
            .Verifiable();

        var executor = new Neo4jExecutor(client.Object);
        var cmd = new CypherCommand("delete edge", new Dictionary<string, object?>(), new DeleteEdge(new EdgeId("e1")));

        await executor.ExecuteAsync(cmd);

        client.Verify(c => c.WriteAsync(
            It.IsAny<string>(),
            It.IsAny<IReadOnlyDictionary<string, object?>>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ReadNode_returns_mapped_node()
    {
        var client = new Mock<INeo4jClient>();
        var node = new Mock<INode>();
        node.SetupGet(n => n.ElementId).Returns("elem-node");
        node.SetupGet(n => n.Labels).Returns(new[] { "Person" });
        node.SetupGet(n => n.Properties).Returns(new Dictionary<string, object>());
        client.Setup(c => c.ReadAsync(
                It.IsAny<string>(),
                It.IsAny<IReadOnlyDictionary<string, object?>>(),
                It.IsAny<Func<IRecord, INode>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<INode> { node.Object });

        var executor = new Neo4jExecutor(client.Object);
        var cmd = new CypherCommand("read node", new Dictionary<string, object?>(), new ReadNode(new NodeId("n1")));

        var result = await executor.ExecuteAsync(cmd);

        result.ShouldBeOfType<GraphNode>().Id.Value.ShouldBe("elem-node");
    }

    [Fact]
    public async Task ReadEdge_returns_mapped_edge()
    {
        var client = new Mock<INeo4jClient>();
        var rel = new Mock<IRelationship>();
        rel.SetupGet(r => r.ElementId).Returns("elem-rel");
        rel.SetupGet(r => r.StartNodeElementId).Returns("from-element");
        rel.SetupGet(r => r.EndNodeElementId).Returns("to-element");
        rel.SetupGet(r => r.Type).Returns("LIKES");
        rel.SetupGet(r => r.Properties).Returns(new Dictionary<string, object>());
        client.Setup(c => c.ReadAsync(
                It.IsAny<string>(),
                It.IsAny<IReadOnlyDictionary<string, object?>>(),
                It.IsAny<Func<IRecord, IRelationship>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<IRelationship> { rel.Object });

        var executor = new Neo4jExecutor(client.Object);
        var cmd = new CypherCommand("read edge", new Dictionary<string, object?>(), new ReadEdge(new EdgeId("e1")));

        var result = await executor.ExecuteAsync(cmd);

        result.ShouldBeOfType<GraphEdge>().Type.ShouldBe("LIKES");
    }

    [Fact]
    public async Task QueryNodes_returns_list()
    {
        var client = new Mock<INeo4jClient>();
        var node = new Mock<INode>();
        node.SetupGet(n => n.ElementId).Returns("elem-node");
        node.SetupGet(n => n.Labels).Returns(new[] { "Person" });
        node.SetupGet(n => n.Properties).Returns(new Dictionary<string, object>());
        client.Setup(c => c.ReadAsync(
                It.IsAny<string>(),
                It.IsAny<IReadOnlyDictionary<string, object?>>(),
                It.IsAny<Func<IRecord, INode>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<INode> { node.Object, node.Object });

        var executor = new Neo4jExecutor(client.Object);
        var cmd = new CypherCommand("query nodes", new Dictionary<string, object?>(), new QueryNodes(new NodeQuery { Type = "Person" }));

        var result = await executor.ExecuteAsync(cmd);

        var list = result.ShouldBeOfType<List<GraphNode>>();
        list.Count.ShouldBe(2);
    }

    [Fact]
    public async Task ReadEdgesBetween_returns_list()
    {
        var client = new Mock<INeo4jClient>();
        var rel = new Mock<IRelationship>();
        rel.SetupGet(r => r.ElementId).Returns("elem-rel");
        rel.SetupGet(r => r.StartNodeElementId).Returns("from-element");
        rel.SetupGet(r => r.EndNodeElementId).Returns("to-element");
        rel.SetupGet(r => r.Type).Returns("LIKES");
        rel.SetupGet(r => r.Properties).Returns(new Dictionary<string, object>());
        client.Setup(c => c.ReadAsync(
                It.IsAny<string>(),
                It.IsAny<IReadOnlyDictionary<string, object?>>(),
                It.IsAny<Func<IRecord, IRelationship>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<IRelationship> { rel.Object, rel.Object });

        var executor = new Neo4jExecutor(client.Object);
        var cmd = new CypherCommand("read edges between", new Dictionary<string, object?>(), new ReadEdgesBetween(new NodeId("from"), new NodeId("to"), null));

        var result = await executor.ExecuteAsync(cmd);

        var list = result.ShouldBeOfType<List<GraphEdge>>();
        list.Count.ShouldBe(2);
    }
}
