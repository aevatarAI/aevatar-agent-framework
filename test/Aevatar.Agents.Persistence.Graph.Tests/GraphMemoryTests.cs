using Aevatar.Agents.Persistence.Graph.Abstractions;
using Aevatar.Agents.Persistence.Graph.Core.Semantic;
using Aevatar.Agents.Persistence.InMemory.Graph;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Aevatar.Agents.Persistence.Graph.Tests;

public class GraphMemoryTests
{
    [Fact]
    public void AddAevatarGraphMemory_registers_graph_client()
    {
        var services = new ServiceCollection();
        services.AddAevatarGraphInMemory();

        var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IGraphClient>().ShouldNotBeNull();
    }

    [Fact]
    public async Task InMemory_graph_crud_round_trips()
    {
        await using var provider = new ServiceCollection()
            .AddAevatarGraphInMemory()
            .BuildServiceProvider();

        var graph = provider.GetRequiredService<IGraphClient>();

        var aliceId = await graph.WriteAsync("Person", new Dictionary<string, Value>
        {
            ["name"] = new StringValue("Alice"),
            ["age"] = new IntValue(30)
        });

        var alice = await graph.ReadAsync(aliceId);
        alice.ShouldNotBeNull();
        alice!.Type.ShouldBe("Person");
        alice.Properties["name"].ShouldBeOfType<StringValue>().Data.ShouldBe("Alice");

        var bobId = await graph.WriteAsync("Person", new Dictionary<string, Value>
        {
            ["name"] = new StringValue("Bob"),
            ["age"] = new IntValue(28)
        });

        var edgeId = await graph.WriteAsync("FRIEND_OF", aliceId, bobId, new Dictionary<string, Value>
        {
            ["since"] = new IntValue(2021)
        });

        var edge = await graph.ReadAsync(edgeId);
        edge.ShouldNotBeNull();
        edge!.Type.ShouldBe("FRIEND_OF");

        var edges = await graph.QueryAsync(new EdgeQuery
        {
            Type = "FRIEND_OF",
            Conditions = [new Condition("since", Operator.GreaterThan, new IntValue(2020))]
        });
        edges.Count.ShouldBe(1);

        // Update + query
        await graph.UpdateAsync(aliceId, new Dictionary<string, Value> { ["city"] = new StringValue("Shanghai") });
        var adults = await graph.QueryAsync(new NodeQuery
        {
            Type = "Person",
            Conditions = [new Condition("age", Operator.GreaterThan, new IntValue(18))]
        });
        adults.Count.ShouldBe(2);

        // Delete (node deletion detaches edges)
        await graph.DeleteAsync(aliceId);
        (await graph.ReadAsync(aliceId)).ShouldBeNull();
        (await graph.ReadAsync(edgeId)).ShouldBeNull();
    }

    [Fact]
    public async Task InMemory_delete_nodes_detaches_edges()
    {
        await using var provider = new ServiceCollection()
            .AddAevatarGraphInMemory()
            .BuildServiceProvider();

        var graph = provider.GetRequiredService<IGraphClient>();

        var aliceId = await graph.WriteAsync("Person", new Dictionary<string, Value>
        {
            ["name"] = new StringValue("Alice")
        });

        var bobId = await graph.WriteAsync("Person", new Dictionary<string, Value>
        {
            ["name"] = new StringValue("Bob")
        });

        var edgeId = await graph.WriteAsync("FRIEND_OF", aliceId, bobId, new Dictionary<string, Value>
        {
            ["since"] = new IntValue(2021)
        });

        await graph.DeleteAsync(new NodeQuery
        {
            Type = "Person",
            Conditions =
            [
                new Condition("name", Operator.Equals, new StringValue("Alice"))
            ]
        });

        (await graph.ReadAsync(aliceId)).ShouldBeNull();
        (await graph.ReadAsync(bobId)).ShouldNotBeNull();
        (await graph.ReadAsync(edgeId)).ShouldBeNull();
    }

    [Fact]
    public async Task InMemory_create_edge_throws_when_nodes_missing()
    {
        await using var provider = new ServiceCollection()
            .AddAevatarGraphInMemory()
            .BuildServiceProvider();

        var graph = provider.GetRequiredService<IGraphClient>();

        await Should.ThrowAsync<InvalidOperationException>(async () =>
        {
            await graph.WriteAsync(
                "LIKES",
                new NodeId("missing-from"),
                new NodeId("missing-to"),
                new Dictionary<string, Value>());
        });
    }
}


