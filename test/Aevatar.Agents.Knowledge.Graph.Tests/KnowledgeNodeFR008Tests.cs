using Aevatar.Agents.Knowledge.Graph;
using Aevatar.Agents.Knowledge.Graph.Exceptions;
using Aevatar.Agents.Knowledge.Graph.Models;
using Aevatar.Agents.Persistence.InMemory.Graph;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace Aevatar.Agents.Knowledge.Graph.Tests;

/// <summary>
/// Unit tests for KnowledgeNode operations per FR-008.
/// </summary>
public class KnowledgeNodeFR008Tests
{
    private readonly IKnowledgeGraphClientFactory _factory;

    public KnowledgeNodeFR008Tests()
    {
        var services = new ServiceCollection();
        services.AddAevatarGraphInMemory();
        services.AddKnowledgeGraph();
        _factory = services.BuildServiceProvider().GetRequiredService<IKnowledgeGraphClientFactory>();
    }

    private IKnowledgeGraphClient CreateClient(string? sessionId = null)
    {
        return _factory.CreateClient(sessionId ?? Guid.NewGuid().ToString("N"));
    }

    #region CreateKnowledgeNodeAsync Tests

    [Fact]
    public async Task CreateKnowledgeNodeAsync_SimpleNode_CreatesWithKnowledgeKind()
    {
        var client = CreateClient();

        var node = await client.CreateKnowledgeNodeAsync(
            "theorem-1",
            KnowledgeNodeType.MathTheorem,
            "Pythagorean theorem",
            "In a right triangle, a² + b² = c²");

        node.ShouldNotBeNull();
        node.Id.ShouldBe("theorem-1");
        node.NodeType.ShouldBe(KnowledgeNodeType.MathTheorem);
        node.CoreDescription.ShouldBe("Pythagorean theorem");
    }

    [Fact]
    public async Task CreateKnowledgeNodeAsync_WithDerivation_StoresDerivationProcess()
    {
        var client = CreateClient();

        var node = await client.CreateKnowledgeNodeAsync(
            "theorem-1",
            KnowledgeNodeType.MathTheorem,
            "Derived theorem",
            "A theorem derived from axioms",
            derivationProcess: "Applied axiom A1 with substitution x=2");

        node.DerivationProcess.ShouldBe("Applied axiom A1 with substitution x=2");

        var retrieved = await client.GetNodeAsync("theorem-1");
        retrieved.ShouldNotBeNull();
        var retrievedKnowledge = (KnowledgeNode)retrieved!;
        retrievedKnowledge.DerivationProcess.ShouldBe("Applied axiom A1 with substitution x=2");
    }

    [Fact]
    public async Task CreateKnowledgeNodeAsync_WithReferences_StoresReferences()
    {
        var client = CreateClient();

        var refs = new[] { "https://arxiv.org/abs/1234.5678", "https://doi.org/10.1000/xyz" };
        var node = await client.CreateKnowledgeNodeAsync(
            "result-1",
            KnowledgeNodeType.Generic,
            "Research finding",
            "A significant research finding",
            references: refs);

        node.References.Count.ShouldBe(2);
        node.References.ShouldContain("https://arxiv.org/abs/1234.5678");
        node.References.ShouldContain("https://doi.org/10.1000/xyz");

        var retrieved = await client.GetNodeAsync("result-1");
        retrieved.ShouldNotBeNull();
        var retrievedKnowledge = (KnowledgeNode)retrieved!;
        retrievedKnowledge.References.Count.ShouldBe(2);
    }

    [Fact]
    public async Task CreateKnowledgeNodeAsync_WithProof_StoresProof()
    {
        var client = CreateClient();

        var proof = "## Proof\n\nBy induction on n:\n1. Base case: n=0\n2. Inductive step...";
        var node = await client.CreateKnowledgeNodeAsync(
            "theorem-1",
            KnowledgeNodeType.MathTheorem,
            "Proved theorem",
            "A theorem with formal proof",
            proof: proof);

        node.Proof.ShouldBe(proof);
    }

    [Fact]
    public async Task CreateKnowledgeNodeAsync_WithMotivatedBy_CreatesMotivatedByEdge()
    {
        var client = CreateClient();

        // Create a plan node first
        var plan = await client.CreatePlanNodeAsync("plan-1", "Research step", "Execute research", sequentialOrder: 1);

        // Create knowledge motivated by the plan
        var knowledge = await client.CreateKnowledgeNodeAsync(
            "finding-1",
            KnowledgeNodeType.Generic,
            "Research finding",
            "Finding from plan execution",
            motivatedByPlanNodeId: "plan-1");

        knowledge.ShouldNotBeNull();

        var snapshot = await client.GetKnowledgeSnapshotAsync();
        snapshot.EdgeCount.ShouldBe(1);
    }

    [Fact]
    public async Task CreateKnowledgeNodeAsync_WithDependencies_CreatesEdges()
    {
        var client = CreateClient();

        var axiom = await client.CreateKnowledgeNodeAsync("axiom-1", KnowledgeNodeType.MathAxiom,
            "Base axiom", "Foundational axiom");
        var theorem = await client.CreateKnowledgeNodeAsync("theorem-1", KnowledgeNodeType.MathTheorem,
            "Derived theorem", "Theorem derived from axiom",
            dependsOnNodeIds: ["axiom-1"]);

        theorem.DependsOn.ShouldContain("axiom-1");
    }

    [Fact]
    public async Task CreateKnowledgeNodeAsync_NonExistentMotivatedBy_ThrowsNodeNotFoundException()
    {
        var client = CreateClient();

        await Should.ThrowAsync<NodeNotFoundException>(
            () => client.CreateKnowledgeNodeAsync("finding-1", KnowledgeNodeType.Generic,
                "Finding", "Details", motivatedByPlanNodeId: "nonexistent"));
    }

    #endregion

    #region GetKnowledgeNodesAsync Tests

    [Fact]
    public async Task GetKnowledgeNodesAsync_EmptySession_ReturnsEmpty()
    {
        var client = CreateClient();

        var nodes = await client.GetKnowledgeNodesAsync();

        nodes.ShouldBeEmpty();
    }

    [Fact]
    public async Task GetKnowledgeNodesAsync_MixedNodeTypes_ReturnsOnlyKnowledgeNodes()
    {
        var client = CreateClient();

        await client.CreatePlanNodeAsync("plan-1", "Plan", "A plan step", sequentialOrder: 1);
        await client.CreateKnowledgeNodeAsync("knowledge-1", KnowledgeNodeType.MathAxiom, "Axiom", "An axiom");
        await client.CreateKnowledgeNodeAsync("knowledge-2", KnowledgeNodeType.MathTheorem, "Theorem", "A theorem");

        var nodes = await client.GetKnowledgeNodesAsync();

        nodes.Count.ShouldBe(2);
        // All returned nodes should be KnowledgeNode instances
        nodes.ShouldAllBe(n => n is KnowledgeNode);
    }

    #endregion

    #region LinkKnowledgeToPlanAsync Tests

    [Fact]
    public async Task LinkKnowledgeToPlanAsync_ValidNodes_CreatesLink()
    {
        var client = CreateClient();

        var plan = await client.CreatePlanNodeAsync("plan-1", "Research", "Research step", sequentialOrder: 1);
        var knowledge = await client.CreateKnowledgeNodeAsync("knowledge-1", KnowledgeNodeType.Generic,
            "Finding", "Research finding");

        await client.LinkKnowledgeToPlanAsync("knowledge-1", "plan-1");

        var snapshot = await client.GetKnowledgeSnapshotAsync();
        snapshot.EdgeCount.ShouldBe(1);
    }

    [Fact]
    public async Task LinkKnowledgeToPlanAsync_NonExistentKnowledge_ThrowsNodeNotFoundException()
    {
        var client = CreateClient();
        await client.CreatePlanNodeAsync("plan-1", "Plan", "Plan details", sequentialOrder: 1);

        await Should.ThrowAsync<NodeNotFoundException>(
            () => client.LinkKnowledgeToPlanAsync("nonexistent", "plan-1"));
    }

    [Fact]
    public async Task LinkKnowledgeToPlanAsync_NonExistentPlan_ThrowsNodeNotFoundException()
    {
        var client = CreateClient();
        await client.CreateKnowledgeNodeAsync("knowledge-1", KnowledgeNodeType.Generic, "Finding", "Details");

        await Should.ThrowAsync<NodeNotFoundException>(
            () => client.LinkKnowledgeToPlanAsync("knowledge-1", "nonexistent"));
    }

    [Fact]
    public async Task LinkKnowledgeToPlanAsync_KnowledgeIsPlanNode_ThrowsInvalidOperation()
    {
        var client = CreateClient();
        var plan1 = await client.CreatePlanNodeAsync("plan-1", "Plan 1", "First plan", sequentialOrder: 1);
        var plan2 = await client.CreatePlanNodeAsync("plan-2", "Plan 2", "Second plan", sequentialOrder: 2);

        await Should.ThrowAsync<InvalidOperationException>(
            () => client.LinkKnowledgeToPlanAsync("plan-1", "plan-2"));
    }

    [Fact]
    public async Task LinkKnowledgeToPlanAsync_PlanIsKnowledgeNode_ThrowsInvalidOperation()
    {
        var client = CreateClient();
        var knowledge1 = await client.CreateKnowledgeNodeAsync("k1", KnowledgeNodeType.Generic, "K1", "Details 1");
        var knowledge2 = await client.CreateKnowledgeNodeAsync("k2", KnowledgeNodeType.Generic, "K2", "Details 2");

        await Should.ThrowAsync<InvalidOperationException>(
            () => client.LinkKnowledgeToPlanAsync("k1", "k2"));
    }

    #endregion
}
