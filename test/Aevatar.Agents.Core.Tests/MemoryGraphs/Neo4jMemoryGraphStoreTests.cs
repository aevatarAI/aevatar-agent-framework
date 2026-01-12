using Aevatar.Agents.Abstractions.Memory;
using Aevatar.Agents.Persistence.Neo4j;
using Aevatar.Agents.Persistence.Neo4j.MemoryGraph.Stores;
using Google.Protobuf.WellKnownTypes;
using Moq;
using Shouldly;

namespace Aevatar.Agents.Core.Tests.MemoryGraphs;

public class Neo4jMemoryGraphStoreTests
{
    [Fact]
    public async Task SaveAsync_should_write_cypher_with_nodes_and_edges()
    {
        var client = new Mock<INeo4jClient>();
        client.Setup(c => c.WriteAsync(
                It.IsAny<string>(),
                It.IsAny<IReadOnlyDictionary<string, object?>>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask)
            .Verifiable();

        var store = new Neo4jMemoryGraphStore(client.Object);
        var graph = new MemoryGraph
        {
            GraphId = "exec-1",
            Scope = new MemoryScope { Type = MemoryScopeType.Execution, ScopeId = "exec-1" },
            CreatedAt = Timestamp.FromDateTime(DateTime.UtcNow)
        };
        graph.Labels["kind"] = "maker";
        graph.Nodes.Add(new MemoryGraphNode { NodeId = "n1", Type = "trace_node", Name = "root", Content = "hello" });
        graph.Nodes.Add(new MemoryGraphNode { NodeId = "n2", Type = "decision", Name = "d1", Content = "decide" });
        graph.Edges.Add(new MemoryGraphEdge
        {
            EdgeId = "e1",
            FromNodeId = "n1",
            ToNodeId = "n2",
            Type = "has_decision",
            Label = "has_decision"
        });

        await store.SaveAsync(graph);

        client.Verify(c => c.WriteAsync(
            It.Is<string>(s => s.Contains(Neo4jMemoryGraphStore.GraphLabel) &&
                               s.Contains(Neo4jMemoryGraphStore.NodeLabel) &&
                               s.Contains(Neo4jMemoryGraphStore.EdgeRelType)),
            It.Is<IReadOnlyDictionary<string, object?>>(p =>
                (string)p["graphId"]! == "exec-1" &&
                (int)p["scopeType"]! == (int)MemoryScopeType.Execution &&
                ((List<Dictionary<string, object?>>)p["nodes"]!).Count == 2 &&
                ((List<Dictionary<string, object?>>)p["edges"]!).Count == 1),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExistsAsync_returns_false_when_no_match()
    {
        var client = new Mock<INeo4jClient>();
        client.Setup(c => c.ReadAsync(
                It.IsAny<string>(),
                It.IsAny<IReadOnlyDictionary<string, object?>>(),
                It.IsAny<Func<Neo4j.Driver.IRecord, bool>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<bool> { false });

        var store = new Neo4jMemoryGraphStore(client.Object);
        (await store.ExistsAsync("exec-404")).ShouldBeFalse();
    }

    [Fact]
    public async Task DeleteAsync_should_call_write()
    {
        var client = new Mock<INeo4jClient>();
        client.Setup(c => c.WriteAsync(
                It.IsAny<string>(),
                It.IsAny<IReadOnlyDictionary<string, object?>>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask)
            .Verifiable();

        var store = new Neo4jMemoryGraphStore(client.Object);
        await store.DeleteAsync("exec-1");

        client.Verify();
    }
}


