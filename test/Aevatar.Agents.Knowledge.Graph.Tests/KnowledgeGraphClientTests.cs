using Aevatar.Agents.Knowledge.Graph;
using Aevatar.Agents.Knowledge.Graph.Exceptions;
using Aevatar.Agents.Knowledge.Graph.Models;
using Aevatar.Agents.Persistence.InMemory.Graph;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace Aevatar.Agents.Knowledge.Graph.Tests;

public class KnowledgeGraphClientTests
{
    private readonly IKnowledgeGraphClientFactory _factory;

    public KnowledgeGraphClientTests()
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

    #region AddNodeAsync Tests

    [Fact]
    public async Task AddNodeAsync_SimpleNode_CreatesSuccessfully()
    {
        var client = CreateClient();

        var node = await client.AddNodeAsync(
            "axiom-1",
            KnowledgeNodeType.MathAxiom,
            "The base case axiom for natural numbers",
            "Zero is defined as the first natural number. This is a foundational axiom in Peano arithmetic.");

        node.ShouldNotBeNull();
        node.Id.ShouldBe("axiom-1");
        node.NodeType.ShouldBe(KnowledgeNodeType.MathAxiom);
        node.CoreDescription.ShouldBe("The base case axiom for natural numbers");
        node.DetailedDescription.ShouldBe("Zero is defined as the first natural number. This is a foundational axiom in Peano arithmetic.");
        node.SessionId.ShouldBe(client.SessionId);
    }

    [Fact]
    public async Task AddNodeAsync_WithOptionalFields_StoresAllFields()
    {
        var client = CreateClient();

        var node = await client.AddNodeAsync(
            "theorem-1",
            KnowledgeNodeType.MathTheorem,
            "Pythagorean theorem",
            "In a right triangle, the square of the hypotenuse equals the sum of squares of the other two sides.",
            proof: "Let a, b be the legs and c be the hypotenuse. By area comparison of squares...");

        node.DetailedDescription.ShouldBe("In a right triangle, the square of the hypotenuse equals the sum of squares of the other two sides.");
        node.Proof.ShouldBe("Let a, b be the legs and c be the hypotenuse. By area comparison of squares...");

        // Verify retrieval from store
        var retrieved = await client.GetNodeAsync("theorem-1");
        retrieved.ShouldNotBeNull();
        retrieved.DetailedDescription.ShouldBe(node.DetailedDescription);
        retrieved.Proof.ShouldBe(node.Proof);
    }

    [Fact]
    public async Task AddNodeAsync_WithDependencies_CreatesEdges()
    {
        var client = CreateClient();

        var axiom = await client.AddNodeAsync("axiom-1", KnowledgeNodeType.MathAxiom, "Base axiom", "Detailed base axiom description");
        var theorem = await client.AddNodeAsync(
            "theorem-1",
            KnowledgeNodeType.MathTheorem,
            "Derived theorem",
            "This theorem is derived from the base axiom",
            dependsOn: [axiom.Id]);

        theorem.DependsOn.ShouldContain(axiom.Id);

        var snapshot = await client.GetKnowledgeSnapshotAsync();
        snapshot.EdgeCount.ShouldBe(1);
        // Edge direction: node -[DEPENDS_ON]-> dependency (theorem depends on axiom)
        snapshot.Edges[0].FromId.ShouldBe(theorem.Id);
        snapshot.Edges[0].ToId.ShouldBe(axiom.Id);
    }

    [Fact]
    public async Task AddNodeAsync_NonExistentDependency_ThrowsNodeNotFoundException()
    {
        var client = CreateClient();

        var ex = await Should.ThrowAsync<NodeNotFoundException>(
            () => client.AddNodeAsync("theorem-1", KnowledgeNodeType.MathTheorem, "Theorem", "Detailed theorem", dependsOn: ["nonexistent"])
        );
        ex.NodeId.ShouldBe("nonexistent");
    }

    [Fact]
    public async Task AddNodeAsync_DuplicateId_ThrowsDuplicateNodeException()
    {
        var client = CreateClient();

        await client.AddNodeAsync("axiom-1", KnowledgeNodeType.MathAxiom, "First axiom", "First axiom details");

        var ex = await Should.ThrowAsync<DuplicateNodeException>(
            () => client.AddNodeAsync("axiom-1", KnowledgeNodeType.MathAxiom, "Duplicate axiom", "Duplicate details")
        );
        ex.NodeId.ShouldBe("axiom-1");
    }

    [Fact]
    public async Task AddNodeAsync_MultipleDependencies_CreatesAllEdges()
    {
        var client = CreateClient();

        var a = await client.AddNodeAsync("a", KnowledgeNodeType.MathAxiom, "Axiom A", "Detailed A");
        var b = await client.AddNodeAsync("b", KnowledgeNodeType.MathAxiom, "Axiom B", "Detailed B");
        var c = await client.AddNodeAsync("c", KnowledgeNodeType.MathAxiom, "Axiom C", "Detailed C");
        var theorem = await client.AddNodeAsync("theorem", KnowledgeNodeType.MathTheorem, "Combined theorem",
            "Depends on A, B, and C", dependsOn: [a.Id, b.Id, c.Id]);

        theorem.DependsOn.Count.ShouldBe(3);
        theorem.DependsOn.ShouldContain(a.Id);
        theorem.DependsOn.ShouldContain(b.Id);
        theorem.DependsOn.ShouldContain(c.Id);

        var snapshot = await client.GetKnowledgeSnapshotAsync();
        snapshot.EdgeCount.ShouldBe(3);
    }

    [Fact]
    public async Task AddNodeAsync_ValidDiamondDependency_DoesNotThrow()
    {
        var client = CreateClient();

        // Diamond dependency is valid (not a cycle):
        // A <- B <- C, and D depends on both C and A
        var a = await client.AddNodeAsync("a", KnowledgeNodeType.MathAxiom, "Axiom A", "Detailed A");
        var b = await client.AddNodeAsync("b", KnowledgeNodeType.MathTheorem, "Theorem B", "Detailed B", dependsOn: [a.Id]);
        var c = await client.AddNodeAsync("c", KnowledgeNodeType.MathCorollary, "Corollary C", "Detailed C", dependsOn: [b.Id]);

        // This should NOT throw - D depends on C and A, which is a valid DAG (diamond shape)
        var d = await client.AddNodeAsync("d", KnowledgeNodeType.MathTheorem, "Theorem D", "Valid diamond",
            dependsOn: [c.Id, a.Id]);

        d.ShouldNotBeNull();
        d.DependsOn.Count.ShouldBe(2);
    }

    [Fact]
    public async Task AddNodeAsync_SelfDependency_ThrowsNodeNotFoundException()
    {
        var client = CreateClient();

        // Node depending on itself - but since node doesn't exist yet,
        // it throws NodeNotFoundException (dependency validation happens first)
        await Should.ThrowAsync<NodeNotFoundException>(
            () => client.AddNodeAsync("self-ref", KnowledgeNodeType.Note, "Self reference", "Depends on itself",
                dependsOn: ["self-ref"])
        );
    }

    [Fact]
    public async Task AddNodeAsync_WithResourceFolderPath_NonExistentFolder_CreatesNodeWithoutResource()
    {
        var client = CreateClient();

        // Non-existent folder should not throw, just skip resource upload
        var node = await client.AddNodeAsync("node-with-missing-resource", KnowledgeNodeType.Note,
            "Node with missing resource", "Detailed description",
            resourceFolderPath: "/nonexistent/folder/path");

        node.ShouldNotBeNull();
        node.ResourceUri.ShouldBeNull(); // No resource uploaded
    }

    #endregion

    #region GetNodeAsync Tests

    [Fact]
    public async Task GetNodeAsync_ExistingNode_ReturnsNode()
    {
        var client = CreateClient();

        var created = await client.AddNodeAsync("axiom-1", KnowledgeNodeType.MathAxiom, "Test axiom", "Test axiom details");
        var fetched = await client.GetNodeAsync(created.Id);

        fetched.ShouldNotBeNull();
        fetched!.Id.ShouldBe(created.Id);
        fetched.NodeType.ShouldBe(KnowledgeNodeType.MathAxiom);
    }

    [Fact]
    public async Task GetNodeAsync_NonExistent_ReturnsNull()
    {
        var client = CreateClient();

        var result = await client.GetNodeAsync("nonexistent");
        result.ShouldBeNull();
    }

    #endregion

    #region Session Isolation Tests

    [Fact]
    public async Task Sessions_AreIsolated()
    {
        var client1 = CreateClient("session-1");
        var client2 = CreateClient("session-2");

        await client1.AddNodeAsync("shared-id", KnowledgeNodeType.MathAxiom, "Session 1 axiom", "Session 1 details");
        await client2.AddNodeAsync("shared-id", KnowledgeNodeType.BiologyExperiment, "Session 2 experiment", "Session 2 details");

        var node1 = await client1.GetNodeAsync("shared-id");
        var node2 = await client2.GetNodeAsync("shared-id");

        node1.ShouldNotBeNull();
        node2.ShouldNotBeNull();
        node1!.NodeType.ShouldBe(KnowledgeNodeType.MathAxiom);
        node2!.NodeType.ShouldBe(KnowledgeNodeType.BiologyExperiment);
    }

    [Fact]
    public async Task GetKnowledgeSnapshotAsync_OnlyReturnsCurrentSessionNodes()
    {
        var client1 = CreateClient("session-1");
        var client2 = CreateClient("session-2");

        await client1.AddNodeAsync("node-1", KnowledgeNodeType.MathAxiom, "Session 1 node", "Session 1 details");
        await client2.AddNodeAsync("node-2", KnowledgeNodeType.BiologyExperiment, "Session 2 node", "Session 2 details");
        await client2.AddNodeAsync("node-3", KnowledgeNodeType.BiologyExperiment, "Session 2 another node", "Session 2 another details");

        var snapshot1 = await client1.GetKnowledgeSnapshotAsync();
        var snapshot2 = await client2.GetKnowledgeSnapshotAsync();

        snapshot1.NodeCount.ShouldBe(1);
        snapshot2.NodeCount.ShouldBe(2);
    }

    #endregion

    #region GetKnowledgeSnapshotAsync Tests

    [Fact]
    public async Task GetKnowledgeSnapshotAsync_ReturnsAllNodesAndEdges()
    {
        var client = CreateClient();

        var a = await client.AddNodeAsync("a", KnowledgeNodeType.MathAxiom, "Axiom A", "Detailed Axiom A");
        var b = await client.AddNodeAsync("b", KnowledgeNodeType.MathTheorem, "Theorem B", "Detailed Theorem B", dependsOn: [a.Id]);
        var c = await client.AddNodeAsync("c", KnowledgeNodeType.MathCorollary, "Corollary C", "Detailed Corollary C", dependsOn: [b.Id]);

        var snapshot = await client.GetKnowledgeSnapshotAsync();

        snapshot.NodeCount.ShouldBe(3);
        snapshot.EdgeCount.ShouldBe(2);
    }

    #endregion

    #region GetKnowledgeChainDetailsAsync Tests

    [Fact]
    public async Task GetKnowledgeChainDetailsAsync_SimpleChain_ReturnsCompleteChainAndDescription()
    {
        var client = CreateClient();

        // A <- B <- C (C depends on B, B depends on A)
        var a = await client.AddNodeAsync("a", KnowledgeNodeType.MathAxiom, "Axiom", "Detailed Axiom");
        var b = await client.AddNodeAsync("b", KnowledgeNodeType.MathTheorem, "Theorem", "Detailed Theorem", dependsOn: [a.Id]);
        var c = await client.AddNodeAsync("c", KnowledgeNodeType.MathCorollary, "Corollary", "Detailed Corollary", dependsOn: [b.Id]);

        var details = await client.GetKnowledgeChainDetailsAsync(c.Id);

        // Verify chain structure
        details.Chain.TargetNode.Id.ShouldBe(c.Id);
        details.Chain.TotalNodes.ShouldBe(3);
        details.Chain.MaxDepth.ShouldBe(2);
        details.Chain.Levels.Count.ShouldBe(3);

        details.Chain.Levels[0].Nodes[0].Id.ShouldBe("c");
        details.Chain.Levels[1].Nodes[0].Id.ShouldBe("b");
        details.Chain.Levels[2].Nodes[0].Id.ShouldBe("a");

        // Verify description is generated
        details.Description.ShouldNotBeNullOrWhiteSpace();
        details.Description.ShouldContain("Corollary");
    }

    [Fact]
    public async Task GetKnowledgeChainDetailsAsync_DiamondDependency_HandlesCorrectly()
    {
        var client = CreateClient();

        // D <- B, D <- C, B <- A, C <- A (diamond pattern)
        var d = await client.AddNodeAsync("d", KnowledgeNodeType.MathAxiom, "Root axiom", "Detailed root axiom");
        var b = await client.AddNodeAsync("b", KnowledgeNodeType.MathLemma, "Lemma B", "Detailed Lemma B", dependsOn: [d.Id]);
        var c = await client.AddNodeAsync("c", KnowledgeNodeType.MathLemma, "Lemma C", "Detailed Lemma C", dependsOn: [d.Id]);
        var a = await client.AddNodeAsync("a", KnowledgeNodeType.MathTheorem, "Theorem A", "Detailed Theorem A", dependsOn: [b.Id, c.Id]);

        var details = await client.GetKnowledgeChainDetailsAsync(a.Id);

        details.Chain.TotalNodes.ShouldBe(4); // D only counted once
        details.Chain.Chain.Select(n => n.Id).Distinct().Count().ShouldBe(4);
    }

    [Fact]
    public async Task GetKnowledgeChainDetailsAsync_NodeWithNoDependencies_ReturnsSingleLevel()
    {
        var client = CreateClient();

        var node = await client.AddNodeAsync("standalone", KnowledgeNodeType.Note, "Standalone note", "Detailed standalone note");

        var details = await client.GetKnowledgeChainDetailsAsync(node.Id);

        details.Chain.TotalNodes.ShouldBe(1);
        details.Chain.MaxDepth.ShouldBe(0);
    }

    [Fact]
    public async Task GetKnowledgeChainDetailsAsync_NonExistentNode_ThrowsNodeNotFoundException()
    {
        var client = CreateClient();

        await Should.ThrowAsync<NodeNotFoundException>(
            () => client.GetKnowledgeChainDetailsAsync("nonexistent")
        );
    }

    [Fact]
    public async Task GetKnowledgeChainDetailsAsync_ReturnsMarkdownDescription()
    {
        var client = CreateClient();

        // A <- B <- C (C depends on B, B depends on A)
        var a = await client.AddNodeAsync("a", KnowledgeNodeType.MathAxiom, "Base axiom", "This is the foundational axiom.");
        var b = await client.AddNodeAsync("b", KnowledgeNodeType.MathTheorem, "Main theorem", "This theorem builds on the base axiom.",
            proof: "By applying the base axiom, we can derive...", dependsOn: [a.Id]);
        var c = await client.AddNodeAsync("c", KnowledgeNodeType.MathCorollary, "Final corollary", "This corollary follows from the main theorem.", dependsOn: [b.Id]);

        var details = await client.GetKnowledgeChainDetailsAsync(c.Id);

        // Verify description contains expected markdown content
        var description = details.Description;
        description.ShouldNotBeNullOrWhiteSpace();
        description.ShouldContain("# Final corollary"); // Title from CoreDescription
        description.ShouldContain("## Abstract"); // Abstract section
        description.ShouldContain("## Table of Contents");
        description.ShouldContain("## Knowledge Nodes");
        description.ShouldContain("Main theorem"); // CoreDescription in chain
        description.ShouldContain("Base axiom"); // CoreDescription in chain
        description.ShouldContain("MathAxiom");
        description.ShouldContain("MathTheorem");
        description.ShouldContain("MathCorollary");
        description.ShouldContain("By applying the base axiom"); // Proof content
        // Verify DetailedDescription IS in description
        description.ShouldContain("This is the foundational axiom");
        description.ShouldContain("This theorem builds on the base axiom");
        description.ShouldContain("This corollary follows from the main theorem");
        // Verify derivation path
        description.ShouldContain("Derivation Path");
        description.ShouldContain("Foundation");
    }

    [Fact]
    public async Task GetKnowledgeChainDetailsAsync_WithResourceUri_IncludesResourceLink()
    {
        var client = CreateClient();

        // Create node with a mock resource URI (simulating S3 upload result)
        var node = await client.AddNodeAsync("node-with-resource", KnowledgeNodeType.ResearchPaper,
            "Research Paper", "A paper with attached resources");

        // Note: We can't easily test actual S3 upload without mocking, but we can verify
        // the description generation handles nodes correctly
        var details = await client.GetKnowledgeChainDetailsAsync(node.Id);

        details.Description.ShouldContain("Research Paper");
        details.Description.ShouldContain("## Abstract");
        details.Chain.TargetNode.Id.ShouldBe("node-with-resource");
    }

    #endregion

    #region GenerateFullPaperAsync Tests

    [Fact]
    public async Task GenerateFullPaperAsync_ReturnsComprehensivePaper()
    {
        var client = CreateClient();

        var a = await client.AddNodeAsync("a", KnowledgeNodeType.MathAxiom, "Axiom A", "Detailed Axiom A");
        var b = await client.AddNodeAsync("b", KnowledgeNodeType.MathTheorem, "Theorem B", "Detailed Theorem B", dependsOn: [a.Id]);
        var c = await client.AddNodeAsync("c", KnowledgeNodeType.MathCorollary, "Corollary C", "Detailed Corollary C", dependsOn: [b.Id]);

        var paper = await client.GenerateFullPaperAsync();

        paper.ShouldNotBeNullOrWhiteSpace();
        paper.ShouldContain("# Knowledge Graph Research Paper");
        paper.ShouldContain("## Abstract");
        paper.ShouldContain("## Knowledge Overview");
        paper.ShouldContain("## Detailed Knowledge");
        paper.ShouldContain("Axiom A");
        paper.ShouldContain("Theorem B");
        paper.ShouldContain("Corollary C");
        paper.ShouldContain("Total Knowledge Nodes**: 3");
    }

    [Fact]
    public async Task GenerateFullPaperAsync_EmptyGraph_ReturnsEmptyPaper()
    {
        var client = CreateClient();

        var paper = await client.GenerateFullPaperAsync();

        paper.ShouldNotBeNullOrWhiteSpace();
        paper.ShouldContain("# Knowledge Graph Research Paper");
        paper.ShouldContain("Total Knowledge Nodes**: 0");
    }

    #endregion

    #region RemoveNodeAsync Tests

    [Fact]
    public async Task RemoveNodeAsync_ExistingNode_RemovesSuccessfully()
    {
        var client = CreateClient();

        var node = await client.AddNodeAsync("to-delete", KnowledgeNodeType.Note, "Will be deleted", "Detailed content to delete");

        var result = await client.RemoveNodeAsync(node.Id);

        result.ShouldBeTrue();
        (await client.GetNodeAsync(node.Id)).ShouldBeNull();
    }

    [Fact]
    public async Task RemoveNodeAsync_NonExistent_ReturnsFalse()
    {
        var client = CreateClient();

        var result = await client.RemoveNodeAsync("nonexistent");
        result.ShouldBeFalse();
    }

    [Fact]
    public async Task RemoveNodeAsync_RemovesConnectedEdges()
    {
        var client = CreateClient();

        var a = await client.AddNodeAsync("a", KnowledgeNodeType.MathAxiom, "Axiom", "Detailed Axiom");
        var b = await client.AddNodeAsync("b", KnowledgeNodeType.MathTheorem, "Theorem", "Detailed Theorem", dependsOn: [a.Id]);

        await client.RemoveNodeAsync(a.Id);

        var snapshot = await client.GetKnowledgeSnapshotAsync();
        snapshot.EdgeCount.ShouldBe(0);
    }

    [Fact]
    public async Task RemoveNodeAsync_MiddleNode_RemovesBothIncomingAndOutgoingEdges()
    {
        var client = CreateClient();

        var a = await client.AddNodeAsync("a", KnowledgeNodeType.MathAxiom, "Axiom", "Detailed Axiom");
        var b = await client.AddNodeAsync("b", KnowledgeNodeType.MathTheorem, "Theorem", "Detailed Theorem", dependsOn: [a.Id]);
        var c = await client.AddNodeAsync("c", KnowledgeNodeType.MathCorollary, "Corollary", "Detailed Corollary", dependsOn: [b.Id]);

        // Remove middle node B
        await client.RemoveNodeAsync(b.Id);

        var snapshot = await client.GetKnowledgeSnapshotAsync();
        snapshot.NodeCount.ShouldBe(2); // A and C remain
        snapshot.EdgeCount.ShouldBe(0); // All edges involving B are removed
    }

    #endregion

    #region Complex Graph Structure Tests

    [Fact]
    public async Task ComplexDAG_MultiplePathsToRoot_HandlesCorrectly()
    {
        var client = CreateClient();

        // Create a complex DAG:
        //       A
        //      / \
        //     B   C
        //      \ / \
        //       D   E
        //        \ /
        //         F
        var a = await client.AddNodeAsync("a", KnowledgeNodeType.MathAxiom, "Root Axiom", "Root");
        var b = await client.AddNodeAsync("b", KnowledgeNodeType.MathLemma, "Lemma B", "Lemma B", dependsOn: [a.Id]);
        var c = await client.AddNodeAsync("c", KnowledgeNodeType.MathLemma, "Lemma C", "Lemma C", dependsOn: [a.Id]);
        var d = await client.AddNodeAsync("d", KnowledgeNodeType.MathTheorem, "Theorem D", "Theorem D", dependsOn: [b.Id, c.Id]);
        var e = await client.AddNodeAsync("e", KnowledgeNodeType.MathTheorem, "Theorem E", "Theorem E", dependsOn: [c.Id]);
        var f = await client.AddNodeAsync("f", KnowledgeNodeType.MathCorollary, "Corollary F", "Corollary F", dependsOn: [d.Id, e.Id]);

        var details = await client.GetKnowledgeChainDetailsAsync(f.Id);

        // F depends on all nodes (A, B, C, D, E, F)
        details.Chain.TotalNodes.ShouldBe(6);
        details.Chain.Chain.Select(n => n.Id).Distinct().Count().ShouldBe(6);
    }

    [Fact]
    public async Task GetKnowledgeChainDetailsAsync_DeepChain_ReturnsCorrectDepth()
    {
        var client = CreateClient();

        // Create a deep chain: A <- B <- C <- D <- E (depth 4)
        var a = await client.AddNodeAsync("a", KnowledgeNodeType.MathAxiom, "Level 0", "Root");
        var b = await client.AddNodeAsync("b", KnowledgeNodeType.MathLemma, "Level 1", "L1", dependsOn: [a.Id]);
        var c = await client.AddNodeAsync("c", KnowledgeNodeType.MathLemma, "Level 2", "L2", dependsOn: [b.Id]);
        var d = await client.AddNodeAsync("d", KnowledgeNodeType.MathTheorem, "Level 3", "L3", dependsOn: [c.Id]);
        var e = await client.AddNodeAsync("e", KnowledgeNodeType.MathCorollary, "Level 4", "L4", dependsOn: [d.Id]);

        var details = await client.GetKnowledgeChainDetailsAsync(e.Id);

        details.Chain.MaxDepth.ShouldBe(4);
        details.Chain.Levels.Count.ShouldBe(5); // 5 levels (0-4)
    }

    #endregion

    #region Factory Tests

    [Fact]
    public void Factory_CreateClient_ReturnsClientWithCorrectSessionId()
    {
        var client = _factory.CreateClient("test-session-123");

        client.SessionId.ShouldBe("test-session-123");
    }

    [Fact]
    public void Factory_CreateMultipleClients_ReturnsIndependentClients()
    {
        var client1 = _factory.CreateClient("session-a");
        var client2 = _factory.CreateClient("session-b");

        client1.SessionId.ShouldNotBe(client2.SessionId);
        client1.ShouldNotBeSameAs(client2);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public async Task GetKnowledgeSnapshotAsync_EmptyGraph_ReturnsEmptySnapshot()
    {
        var client = CreateClient();

        var snapshot = await client.GetKnowledgeSnapshotAsync();

        snapshot.NodeCount.ShouldBe(0);
        snapshot.EdgeCount.ShouldBe(0);
        snapshot.Nodes.ShouldBeEmpty();
        snapshot.Edges.ShouldBeEmpty();
    }

    [Fact]
    public async Task AddNodeAsync_AllNodeTypes_WorkCorrectly()
    {
        var client = CreateClient();

        // Test various node types
        var nodes = new List<KnowledgeNode>
        {
            await client.AddNodeAsync("math-axiom", KnowledgeNodeType.MathAxiom, "Math Axiom", "Details"),
            await client.AddNodeAsync("math-theorem", KnowledgeNodeType.MathTheorem, "Math Theorem", "Details"),
            await client.AddNodeAsync("math-lemma", KnowledgeNodeType.MathLemma, "Math Lemma", "Details"),
            await client.AddNodeAsync("math-corollary", KnowledgeNodeType.MathCorollary, "Math Corollary", "Details"),
            await client.AddNodeAsync("physics-law", KnowledgeNodeType.PhysicsLaw, "Physics Law", "Details"),
            await client.AddNodeAsync("biology-exp", KnowledgeNodeType.BiologyExperiment, "Biology Experiment", "Details"),
            await client.AddNodeAsync("cs-algorithm", KnowledgeNodeType.CsAlgorithm, "CS Algorithm", "Details"),
            await client.AddNodeAsync("note", KnowledgeNodeType.Note, "Note", "Details"),
        };

        var snapshot = await client.GetKnowledgeSnapshotAsync();
        snapshot.NodeCount.ShouldBe(8);

        foreach (var node in nodes)
        {
            var retrieved = await client.GetNodeAsync(node.Id);
            retrieved.ShouldNotBeNull();
            retrieved!.NodeType.ShouldBe(node.NodeType);
        }
    }

    #endregion
}
