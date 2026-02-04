using Aevatar.Agents.Cognitive.Researching.Dag;
using VibeResearching.Contracts.Collab;
using Shouldly;

namespace VibeResearching.Tests;

public sealed class DagExplainTests
{
    [Fact]
    public void Explain_ShouldReturnDepsAndTopo_DependencyFirst()
    {
        var snap = new SraDagSnapshot { SessionId = "test001" };
        snap.Nodes.Add(new SraDagNode { Id = "A", Type = SraDagNodeType.Axiom, Label = "A" });
        snap.Nodes.Add(new SraDagNode { Id = "B", Type = SraDagNodeType.Theorem, Label = "B" });
        snap.Edges.Add(new SraDagEdge { FromId = "A", ToId = "B", Type = "depends_on" });

        var ex = DagExplain.Explain(snap, "B");

        ex.HasCycle.ShouldBeFalse();
        ex.Provable.ShouldBeTrue();
        ex.DirectDeps.ToList().ShouldBe(new List<string> { "A" });
        ex.Topo.ToList().ShouldBe(new List<string> { "A", "B" });
        ex.Missing.Count.ShouldBe(0);
    }

    [Fact]
    public void Explain_ShouldMarkHypothesisDepsAsMissing_AndNotProvable()
    {
        var snap = new SraDagSnapshot { SessionId = "test001" };
        snap.Nodes.Add(new SraDagNode { Id = "H1", Type = SraDagNodeType.Hypothesis, Label = "Hypothesis" });
        snap.Nodes.Add(new SraDagNode { Id = "T1", Type = SraDagNodeType.Theorem, Label = "Theorem" });
        snap.Edges.Add(new SraDagEdge { FromId = "H1", ToId = "T1", Type = "depends_on" });

        var ex = DagExplain.Explain(snap, "T1");

        ex.HasCycle.ShouldBeFalse();
        ex.Provable.ShouldBeFalse();
        ex.Missing.Select(x => x.Id).ToList().ShouldBe(new List<string> { "H1" });
    }

    [Fact]
    public void Explain_ShouldDetectCycle()
    {
        var snap = new SraDagSnapshot { SessionId = "test001" };
        snap.Nodes.Add(new SraDagNode { Id = "A", Type = SraDagNodeType.Axiom, Label = "A" });
        snap.Nodes.Add(new SraDagNode { Id = "B", Type = SraDagNodeType.Theorem, Label = "B" });
        snap.Edges.Add(new SraDagEdge { FromId = "A", ToId = "B", Type = "depends_on" });
        snap.Edges.Add(new SraDagEdge { FromId = "B", ToId = "A", Type = "depends_on" });

        var ex = DagExplain.Explain(snap, "B");
        ex.HasCycle.ShouldBeTrue();
        ex.Provable.ShouldBeFalse();
    }
}


