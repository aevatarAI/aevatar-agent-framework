using Aevatar.Agents.Knowledge.Graph;
using Aevatar.Agents.Knowledge.Graph.Exceptions;
using Aevatar.Agents.Knowledge.Graph.Models;
using Aevatar.Agents.Persistence.InMemory.Graph;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace Aevatar.Agents.Knowledge.Graph.Tests;

/// <summary>
/// Unit tests for PlanNode operations per FR-007.
/// </summary>
public class PlanNodeTests
{
    private readonly IKnowledgeGraphClientFactory _factory;

    public PlanNodeTests()
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

    #region CreatePlanNodeAsync Tests

    [Fact]
    public async Task CreatePlanNodeAsync_SimplePlan_CreatesWithPendingStatus()
    {
        var client = CreateClient();

        var node = await client.CreatePlanNodeAsync(
            "plan-1",
            "Research foundation",
            "Establish the foundational axioms for the research",
            methodology: "Literature review and axiom formulation",
            sequentialOrder: 1);

        node.ShouldNotBeNull();
        node.Id.ShouldBe("plan-1");
        node.Status.ShouldBe(PlanNodeStatus.Pending);
        node.CoreDescription.ShouldBe("Research foundation");
        node.Methodology.ShouldBe("Literature review and axiom formulation");
        node.SequentialOrder.ShouldBe(1);
    }

    [Fact]
    public async Task CreatePlanNodeAsync_WithDependencies_CreatesEdges()
    {
        var client = CreateClient();

        var plan1 = await client.CreatePlanNodeAsync("plan-1", "Step 1", "First step", sequentialOrder: 1);
        var plan2 = await client.CreatePlanNodeAsync(
            "plan-2", "Step 2", "Second step depends on first",
            sequentialOrder: 2,
            dependsOnNodeIds: ["plan-1"]);

        plan2.DependsOn.ShouldContain("plan-1");

        var snapshot = await client.GetKnowledgeSnapshotAsync();
        snapshot.EdgeCount.ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task CreatePlanNodeAsync_WithPromotes_CreatesPromotesEdges()
    {
        var client = CreateClient();

        // Create a goal node first
        var goal = await client.AddNodeAsync("goal-1", KnowledgeNodeType.Generic, "Research goal", "Main research objective");

        // Create plan that promotes the goal
        var plan = await client.CreatePlanNodeAsync(
            "plan-1", "Achieve goal", "Steps to achieve the goal",
            sequentialOrder: 1,
            promotesNodeIds: ["goal-1"]);

        plan.ShouldNotBeNull();
        // Verify through snapshot
        var snapshot = await client.GetKnowledgeSnapshotAsync();
        snapshot.EdgeCount.ShouldBe(1);
    }

    [Fact]
    public async Task CreatePlanNodeAsync_DuplicateId_ThrowsDuplicateNodeException()
    {
        var client = CreateClient();

        await client.CreatePlanNodeAsync("plan-1", "First", "First plan", sequentialOrder: 1);

        await Should.ThrowAsync<DuplicateNodeException>(
            () => client.CreatePlanNodeAsync("plan-1", "Duplicate", "Duplicate plan", sequentialOrder: 2));
    }

    [Fact]
    public async Task CreatePlanNodeAsync_NonExistentDependency_ThrowsNodeNotFoundException()
    {
        var client = CreateClient();

        await Should.ThrowAsync<NodeNotFoundException>(
            () => client.CreatePlanNodeAsync("plan-1", "Plan", "Plan details",
                sequentialOrder: 1, dependsOnNodeIds: ["nonexistent"]));
    }

    [Fact]
    public async Task CreatePlanNodeAsync_CycleDetected_ThrowsCycleDetectedException()
    {
        var client = CreateClient();

        // Create plan-1 that depends on plan-2
        // But plan-2 doesn't exist yet, so first create both without deps
        var plan1 = await client.CreatePlanNodeAsync("plan-1", "Step 1", "First", sequentialOrder: 1);
        var plan2 = await client.CreatePlanNodeAsync("plan-2", "Step 2", "Second",
            sequentialOrder: 2, dependsOnNodeIds: ["plan-1"]);

        // Now try to add a dependency from plan-1 to plan-2, which would create a cycle
        // plan-1 -> plan-2 -> plan-1
        await Should.ThrowAsync<CycleDetectedException>(
            () => client.AddDependenciesAsync("plan-1", ["plan-2"]));
    }

    #endregion

    #region UpdatePlanNodeStatusAsync Tests

    [Fact]
    public async Task UpdatePlanNodeStatusAsync_PendingToActive_Succeeds()
    {
        var client = CreateClient();
        await client.CreatePlanNodeAsync("plan-1", "Plan", "Plan details", sequentialOrder: 1);

        var updated = await client.UpdatePlanNodeStatusAsync("plan-1", PlanNodeStatus.Active);

        updated.Status.ShouldBe(PlanNodeStatus.Active);
    }

    [Fact]
    public async Task UpdatePlanNodeStatusAsync_ActiveToCompleted_Succeeds()
    {
        var client = CreateClient();
        await client.CreatePlanNodeAsync("plan-1", "Plan", "Plan details", sequentialOrder: 1);
        await client.UpdatePlanNodeStatusAsync("plan-1", PlanNodeStatus.Active);

        var updated = await client.UpdatePlanNodeStatusAsync("plan-1", PlanNodeStatus.Completed, "Done!");

        updated.Status.ShouldBe(PlanNodeStatus.Completed);
        updated.ProgressText.ShouldBe("Done!");
    }

    [Fact]
    public async Task UpdatePlanNodeStatusAsync_ActiveToPending_AllowedForReplanning()
    {
        var client = CreateClient();
        await client.CreatePlanNodeAsync("plan-1", "Plan", "Plan details", sequentialOrder: 1);
        await client.UpdatePlanNodeStatusAsync("plan-1", PlanNodeStatus.Active);

        var updated = await client.UpdatePlanNodeStatusAsync("plan-1", PlanNodeStatus.Pending, "Need to revisit");

        updated.Status.ShouldBe(PlanNodeStatus.Pending);
        updated.ProgressText.ShouldBe("Need to revisit");
    }

    [Fact]
    public async Task UpdatePlanNodeStatusAsync_CompletedToActive_ThrowsInvalidStateTransition()
    {
        var client = CreateClient();
        await client.CreatePlanNodeAsync("plan-1", "Plan", "Plan details", sequentialOrder: 1);
        await client.UpdatePlanNodeStatusAsync("plan-1", PlanNodeStatus.Active);
        await client.UpdatePlanNodeStatusAsync("plan-1", PlanNodeStatus.Completed);

        var ex = await Should.ThrowAsync<GraphOperationException>(
            () => client.UpdatePlanNodeStatusAsync("plan-1", PlanNodeStatus.Active));

        ex.ErrorCode.ShouldBe(GraphErrorCode.InvalidStateTransition);
    }

    [Fact]
    public async Task UpdatePlanNodeStatusAsync_PendingToCompleted_ThrowsInvalidStateTransition()
    {
        var client = CreateClient();
        await client.CreatePlanNodeAsync("plan-1", "Plan", "Plan details", sequentialOrder: 1);

        var ex = await Should.ThrowAsync<GraphOperationException>(
            () => client.UpdatePlanNodeStatusAsync("plan-1", PlanNodeStatus.Completed));

        ex.ErrorCode.ShouldBe(GraphErrorCode.InvalidStateTransition);
    }

    [Fact]
    public async Task UpdatePlanNodeStatusAsync_NonExistentNode_ThrowsNodeNotFoundException()
    {
        var client = CreateClient();

        await Should.ThrowAsync<NodeNotFoundException>(
            () => client.UpdatePlanNodeStatusAsync("nonexistent", PlanNodeStatus.Active));
    }

    [Fact]
    public async Task UpdatePlanNodeStatusAsync_OnKnowledgeNode_ThrowsInvalidStateTransition()
    {
        var client = CreateClient();
        await client.AddNodeAsync("knowledge-1", KnowledgeNodeType.MathTheorem, "Theorem", "A theorem");

        var ex = await Should.ThrowAsync<GraphOperationException>(
            () => client.UpdatePlanNodeStatusAsync("knowledge-1", PlanNodeStatus.Active));

        ex.ErrorCode.ShouldBe(GraphErrorCode.InvalidStateTransition);
    }

    #endregion

    #region GetPlanNodesAsync Tests

    [Fact]
    public async Task GetPlanNodesAsync_EmptySession_ReturnsEmpty()
    {
        var client = CreateClient();

        var nodes = await client.GetPlanNodesAsync();

        nodes.ShouldBeEmpty();
    }

    [Fact]
    public async Task GetPlanNodesAsync_WithPlanNodes_ReturnsSorted()
    {
        var client = CreateClient();

        await client.CreatePlanNodeAsync("plan-3", "Third", "Third step", sequentialOrder: 3);
        await client.CreatePlanNodeAsync("plan-1", "First", "First step", sequentialOrder: 1);
        await client.CreatePlanNodeAsync("plan-2", "Second", "Second step", sequentialOrder: 2);

        var nodes = await client.GetPlanNodesAsync();

        nodes.Count.ShouldBe(3);
        nodes[0].Id.ShouldBe("plan-1");
        nodes[1].Id.ShouldBe("plan-2");
        nodes[2].Id.ShouldBe("plan-3");
    }

    [Fact]
    public async Task GetPlanNodesAsync_MixedNodeTypes_ReturnsOnlyPlanNodes()
    {
        var client = CreateClient();

        await client.CreatePlanNodeAsync("plan-1", "Plan", "A plan step", sequentialOrder: 1);
        await client.AddNodeAsync("knowledge-1", KnowledgeNodeType.MathTheorem, "Theorem", "A theorem");
        await client.CreatePlanNodeAsync("plan-2", "Plan 2", "Another plan step", sequentialOrder: 2);

        var nodes = await client.GetPlanNodesAsync();

        nodes.Count.ShouldBe(2);
        // All returned nodes should be PlanNode instances
        nodes.ShouldAllBe(n => n is PlanNode);
    }

    #endregion
}
