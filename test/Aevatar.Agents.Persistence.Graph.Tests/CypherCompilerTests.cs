using Aevatar.Agents.Persistence.Graph.Abstractions;
using Aevatar.Agents.Persistence.Graph.Core;
using Aevatar.Agents.Persistence.Graph.Core.IR;
using Aevatar.Agents.Persistence.Graph.Core.Semantic;
using Aevatar.Agents.Persistence.Neo4j.Graph;
using Shouldly;

namespace Aevatar.Agents.Persistence.Graph.Tests;

public class CypherCompilerTests
{
    private readonly CypherCompiler _compiler = new();

    [Fact]
    public void Compile_CreateNode_builds_props_and_type()
    {
        var op = new CreateNode("Person", new Dictionary<string, Value>
        {
            ["name"] = new StringValue("Alice")
        });

        var cmd = _compiler.Compile(new GraphPlan { Operation = op });

        cmd.Text.ShouldContain("MERGE (n:__Generic");
        cmd.Text.ShouldContain("SET n:Person");
        cmd.Parameters.ShouldContainKey("props");
        var props = cmd.Parameters["props"] as IReadOnlyDictionary<string, object?>;
        props.ShouldNotBeNull();
        props!["name"].ShouldBe("Alice");
    }

    [Fact]
    public void Compile_ReadNode_sets_id_param()
    {
        var op = new ReadNode(new NodeId("node-1"));

        var cmd = _compiler.Compile(new GraphPlan { Operation = op });

        cmd.Text.ShouldBe("MATCH (n { id: $id }) RETURN n");
        cmd.Parameters["id"].ShouldBe("node-1");
    }

    [Fact]
    public void Compile_QueryNodes_with_condition()
    {
        var op = new QueryNodes(new NodeQuery
        {
            Type = "Person",
            Conditions =
            [
                new Condition("age", Operator.GreaterThan, new IntValue(30))
            ]
        });

        var cmd = _compiler.Compile(new GraphPlan { Operation = op });

        cmd.Text.ShouldContain("MATCH (n:`Person`)");
        cmd.Text.ShouldContain("WHERE n.age > $p0");
        cmd.Parameters["p0"].ShouldBe(30L);
    }

    [Fact]
    public void Compile_UpdateNode_sets_props_and_id()
    {
        var op = new UpdateNode(new NodeId("n1"), new Dictionary<string, Value>
        {
            ["city"] = new StringValue("Shanghai")
        });

        var cmd = _compiler.Compile(new GraphPlan { Operation = op });

        cmd.Text.ShouldBe("MATCH (n { id: $id }) SET n += $props RETURN n");
        cmd.Parameters["id"].ShouldBe("n1");
        var props = cmd.Parameters["props"] as IReadOnlyDictionary<string, object?>;
        props.ShouldNotBeNull();
        props!["city"].ShouldBe("Shanghai");
    }

    [Fact]
    public void Compile_DeleteNode_sets_id()
    {
        var op = new DeleteNode(new NodeId("n1"));

        var cmd = _compiler.Compile(new GraphPlan { Operation = op });

        cmd.Text.ShouldBe("MATCH (n { id: $id }) DETACH DELETE n");
        cmd.Parameters["id"].ShouldBe("n1");
    }

    [Fact]
    public void Compile_DeleteNodes_with_condition()
    {
        var op = new DeleteNodes(new NodeQuery
        {
            Type = "Person",
            Conditions =
            [
                new Condition("age", Operator.GreaterThan, new IntValue(18))
            ]
        });

        var cmd = _compiler.Compile(new GraphPlan { Operation = op });

        cmd.Text.ShouldContain("MATCH (n:`Person`)");
        cmd.Text.ShouldContain("WHERE n.age > $p0");
        cmd.Text.ShouldContain("DETACH DELETE n");
        cmd.Parameters["p0"].ShouldBe(18L);
    }

    [Fact]
    public void Compile_CreateEdge_builds_from_to_and_type()
    {
        var op = new CreateEdge(
            "FRIEND_OF",
            new NodeId("from-id"),
            new NodeId("to-id"),
            new Dictionary<string, Value>
            {
                ["since"] = new IntValue(2021)
            });

        var cmd = _compiler.Compile(new GraphPlan { Operation = op });

        cmd.Text.ShouldContain("MATCH (from { id: $from }), (to { id: $to })");
        cmd.Text.ShouldContain("[r:FRIEND_OF");
        cmd.Parameters["from"].ShouldBe("from-id");
        cmd.Parameters["to"].ShouldBe("to-id");
        var props = cmd.Parameters["props"] as IReadOnlyDictionary<string, object?>;
        props.ShouldNotBeNull();
        props!["since"].ShouldBe(2021L);
    }

    [Fact]
    public void Compile_CreateEdge_uses_props_id_as_merge_key()
    {
        var op = new CreateEdge(
            "DEPENDS_ON",
            new NodeId("from-id"),
            new NodeId("to-id"),
            new Dictionary<string, Value>
            {
                ["id"] = new StringValue("edge-123"),
                ["since"] = new IntValue(2021)
            });

        var cmd = _compiler.Compile(new GraphPlan { Operation = op });

        cmd.Parameters["id"].ShouldBe("edge-123");
        var props = cmd.Parameters["props"] as IReadOnlyDictionary<string, object?>;
        props.ShouldNotBeNull();
        props!["id"].ShouldBe("edge-123");
    }

    [Fact]
    public void Compile_ReadEdge_sets_id_param()
    {
        var op = new ReadEdge(new EdgeId("e1"));

        var cmd = _compiler.Compile(new GraphPlan { Operation = op });

        cmd.Text.ShouldBe("MATCH ()-[r { id: $id }]-() RETURN r");
        cmd.Parameters["id"].ShouldBe("e1");
    }

    [Fact]
    public void Compile_UpdateEdge_sets_props_and_id()
    {
        var op = new UpdateEdge(new EdgeId("e1"), new Dictionary<string, Value>
        {
            ["weight"] = new FloatValue(0.5)
        });

        var cmd = _compiler.Compile(new GraphPlan { Operation = op });

        cmd.Text.ShouldBe("MATCH ()-[r { id: $id }]-() SET r += $props RETURN r");
        cmd.Parameters["id"].ShouldBe("e1");
        var props = cmd.Parameters["props"] as IReadOnlyDictionary<string, object?>;
        props.ShouldNotBeNull();
        props!["weight"].ShouldBe(0.5);
    }

    [Fact]
    public void Compile_DeleteEdge_sets_id()
    {
        var op = new DeleteEdge(new EdgeId("e1"));

        var cmd = _compiler.Compile(new GraphPlan { Operation = op });

        cmd.Text.ShouldBe("MATCH ()-[r { id: $id }]-() DELETE r");
        cmd.Parameters["id"].ShouldBe("e1");
    }

    [Fact]
    public void Compile_QueryEdges_with_type_and_condition()
    {
        var op = new QueryEdges(new EdgeQuery
        {
            Type = "LIKES",
            Conditions =
            [
                new Condition("score", Operator.GreaterThan, new FloatValue(0.8))
            ]
        });

        var cmd = _compiler.Compile(new GraphPlan { Operation = op });

        cmd.Text.ShouldContain("MATCH ()-[r:`LIKES`]->()");
        cmd.Text.ShouldContain("WHERE r.score > $p0");
        cmd.Text.ShouldContain("RETURN r");
        cmd.Parameters["p0"].ShouldBe(0.8);
    }

    [Fact]
    public void Compile_DeleteEdges_with_type_and_condition()
    {
        var op = new DeleteEdges(new EdgeQuery
        {
            Type = "DEPENDS_ON",
            Conditions =
            [
                new Condition("session_id", Operator.Equals, new StringValue("s1"))
            ]
        });

        var cmd = _compiler.Compile(new GraphPlan { Operation = op });

        cmd.Text.ShouldContain("MATCH ()-[r:`DEPENDS_ON`]->()");
        cmd.Text.ShouldContain("WHERE r.session_id = $p0");
        cmd.Text.ShouldContain("DELETE r");
        cmd.Parameters["p0"].ShouldBe("s1");
    }
}
