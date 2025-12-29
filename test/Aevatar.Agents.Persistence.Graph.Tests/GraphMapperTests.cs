using Aevatar.Agents.Persistence.Graph.Abstractions;
using Aevatar.Agents.Persistence.Graph.Core.Semantic;
using Aevatar.Agents.Persistence.Graph.Providers.Neo4j;
using Moq;
using Neo4j.Driver;
using Shouldly;

namespace Aevatar.Agents.Persistence.Graph.Tests;

public class GraphMapperTests
{
    [Fact]
    public void ToGraphNode_prefers_property_id_over_element()
    {
        var node = new Mock<INode>();
        node.SetupGet(n => n.ElementId).Returns("element-id");
        node.SetupGet(n => n.Labels).Returns(new[] { "Person" });
        node.SetupGet(n => n.Properties).Returns(new Dictionary<string, object>
        {
            ["id"] = "property-id",
            ["name"] = "Alice"
        });

        var mapped = GraphMapper.ToGraphNode(node.Object);

        mapped.Id.ShouldBe(new NodeId("property-id"));
        mapped.Type.ShouldBe("Person");
        mapped.Properties["name"].ShouldBeOfType<StringValue>().Data.ShouldBe("Alice");
    }

    [Fact]
    public void ToGraphEdge_prefers_property_id_over_element()
    {
        var rel = new Mock<IRelationship>();
        rel.SetupGet(r => r.ElementId).Returns("element-rel");
        rel.SetupGet(r => r.Type).Returns("FRIEND_OF");
        rel.SetupGet(r => r.StartNodeElementId).Returns("from-element");
        rel.SetupGet(r => r.EndNodeElementId).Returns("to-element");
        rel.SetupGet(r => r.Properties).Returns(new Dictionary<string, object>
        {
            ["id"] = "property-rel-id",
            ["since"] = 2021
        });

        var mapped = GraphMapper.ToGraphEdge(rel.Object);

        mapped.Id.ShouldBe(new EdgeId("property-rel-id"));
        mapped.From.ShouldBe(new NodeId("from-element"));
        mapped.To.ShouldBe(new NodeId("to-element"));
        mapped.Properties["since"].ShouldBeOfType<IntValue>().Data.ShouldBe(2021);
    }

    [Fact]
    public void ToGraphNode_falls_back_to_element_id_when_no_property_id()
    {
        var node = new Mock<INode>();
        node.SetupGet(n => n.ElementId).Returns("element-id");
        node.SetupGet(n => n.Labels).Returns(new[] { "Person" });
        node.SetupGet(n => n.Properties).Returns(new Dictionary<string, object>());

        var mapped = GraphMapper.ToGraphNode(node.Object);

        mapped.Id.ShouldBe(new NodeId("element-id"));
    }

    [Fact]
    public void ToGraphEdge_falls_back_to_element_id_when_no_property_id()
    {
        var rel = new Mock<IRelationship>();
        rel.SetupGet(r => r.ElementId).Returns("element-rel");
        rel.SetupGet(r => r.Type).Returns("KNOWS");
        rel.SetupGet(r => r.StartNodeElementId).Returns("from-element");
        rel.SetupGet(r => r.EndNodeElementId).Returns("to-element");
        rel.SetupGet(r => r.Properties).Returns(new Dictionary<string, object>());

        var mapped = GraphMapper.ToGraphEdge(rel.Object);

        mapped.Id.ShouldBe(new EdgeId("element-rel"));
    }
}
