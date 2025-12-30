using Aevatar.Agents.Persistence.Graph.Abstractions;
using Aevatar.Agents.Persistence.Graph.Core;
using Aevatar.Agents.Persistence.Graph.Core.IR;
using Aevatar.Agents.Persistence.Graph.Core.Semantic;
using Shouldly;

namespace Aevatar.Agents.Persistence.Graph.Tests;

public class GraphClientTests
{
    private readonly FakeCompiler _compiler = new();
    private readonly FakeExecutor _executor = new();

    [Fact]
    public async Task WriteNode_returns_backend_id()
    {
        _executor.OnExecute = op => op switch
        {
            CreateNode => new NodeId("backend-node-id"),
            _ => null
        };

        var client = new GraphClient<FakeCommand>(_compiler, _executor);
        var id = await client.WriteAsync("Person", new Dictionary<string, Value>());

        id.Value.ShouldBe("backend-node-id");
    }

    [Fact]
    public async Task WriteEdge_returns_backend_id()
    {
        _executor.OnExecute = op => op switch
        {
            CreateEdge => new EdgeId("backend-edge-id"),
            _ => null
        };

        var client = new GraphClient<FakeCommand>(_compiler, _executor);
        var id = await client.WriteAsync(
            "FRIEND_OF",
            new NodeId("from"),
            new NodeId("to"),
            new Dictionary<string, Value>());

        id.Value.ShouldBe("backend-edge-id");
    }

    [Fact]
    public async Task Query_returns_empty_when_executor_null()
    {
        _executor.OnExecute = _ => null;
        var client = new GraphClient<FakeCommand>(_compiler, _executor);

        var result = await client.QueryAsync(new NodeQuery { Type = "Person" });

        result.ShouldNotBeNull();
        result.ShouldBeEmpty();
    }

    [Fact]
    public async Task Update_and_delete_are_forwarded()
    {
        var callCount = 0;
        _executor.OnExecute = op =>
        {
            if (op is UpdateNode or DeleteNode) callCount++;
            return null;
        };

        var client = new GraphClient<FakeCommand>(_compiler, _executor);
        await client.UpdateAsync(new NodeId("n1"), new Dictionary<string, Value>());
        await client.DeleteAsync(new NodeId("n1"));

        callCount.ShouldBe(2);
    }

    [Fact]
    public async Task Read_node_round_trips_from_executor()
    {
        var expected = new GraphNode(new NodeId("n1"), "Person", new Dictionary<string, Value>());
        _executor.OnExecute = op => op is ReadNode ? expected : null;
        var client = new GraphClient<FakeCommand>(_compiler, _executor);

        var node = await client.ReadAsync(new NodeId("n1"));

        node.ShouldNotBeNull();
        node!.Id.Value.ShouldBe("n1");
    }

    private sealed record FakeCommand(GraphOperation Operation);

    private sealed class FakeCompiler : IGraphCompiler<FakeCommand>
    {
        public FakeCommand Compile(GraphPlan plan) => new(plan.Operation);
    }

    private sealed class FakeExecutor : IGraphExecutor<FakeCommand>
    {
        public Func<GraphOperation, object?>? OnExecute { get; set; }

        public Task<object?> ExecuteAsync(FakeCommand compiled)
        {
            var result = OnExecute?.Invoke(compiled.Operation);
            return Task.FromResult(result);
        }
    }
}
