using Aevatar.Agents.Knowledge.Graph.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
using Aevatar.VibeResearching.Agents.ReviewAgent;
using Aevatar.VibeResearching.Agents.Tools;

namespace VibeResearching.Api.Tests.ReviewAgent;

/// <summary>
/// Unit tests for ReviewAgentService.
/// Tests T026, T048, T073 from the task list.
/// </summary>
public sealed class ReviewAgentServiceTests
{
    private readonly IOptionsMonitor<ReviewAgentOptions> _optionsMonitor;
    private readonly IVibeGraphAccess _graphAccess;

    public ReviewAgentServiceTests()
    {
        var options = new ReviewAgentOptions
        {
            IterationIntervalMinutes = 60,
            OutOfDateThresholdMinutes = 1440,
            ToDeleteThresholdMinutes = 10080,
            LLMProviderName = "test",
            PerNodeTimeoutSeconds = 60
        };

        _optionsMonitor = Substitute.For<IOptionsMonitor<ReviewAgentOptions>>();
        _optionsMonitor.CurrentValue.Returns(options);

        _graphAccess = Substitute.For<IVibeGraphAccess>();
    }

    // ─────────────────────────────────────────────────────────────
    //  T026: Review Round Logic - Node Selection
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void GetState_ReturnsIdleStatus_WhenInitialized()
    {
        // Arrange
        var service = CreateService();

        // Act
        var state = service.GetState();

        // Assert
        state.Status.ShouldBe(ReviewAgentStatus.Idle);
        state.ErrorMessage.ShouldBeNull();
        state.NodesReviewed.ShouldBe(0);
        state.NodesDeactivated.ShouldBe(0);
    }

    [Fact]
    public void GetSettings_ReturnsCurrentOptions()
    {
        // Arrange
        var service = CreateService();

        // Act
        var settings = service.GetSettings();

        // Assert
        settings.IterationIntervalMinutes.ShouldBe(60);
        settings.OutOfDateThresholdMinutes.ShouldBe(1440);
        settings.ToDeleteThresholdMinutes.ShouldBe(10080);
        settings.LLMProviderName.ShouldBe("test");
    }

    [Fact]
    public async Task UpdateSettingsAsync_StoresRuntimeOverride()
    {
        // Arrange
        var service = CreateService();
        var newSettings = new ReviewAgentOptions
        {
            IterationIntervalMinutes = 30,
            OutOfDateThresholdMinutes = 720,
            ToDeleteThresholdMinutes = 5040,
            LLMProviderName = "updated",
            PerNodeTimeoutSeconds = 120
        };

        // Act
        var result = await service.UpdateSettingsAsync(newSettings);

        // Assert
        result.IterationIntervalMinutes.ShouldBe(30);
        service.GetSettings().IterationIntervalMinutes.ShouldBe(30);
        service.GetSettings().LLMProviderName.ShouldBe("updated");
    }

    [Fact]
    public async Task RunReviewRoundAsync_ReturnsIteration_WhenNoStaleNodes()
    {
        // Arrange
        var service = CreateService();
        _graphAccess.GetStaleKnowledgeNodesAsync(Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<KnowledgeNode>>([]));

        // Act
        var iteration = await service.RunReviewRoundAsync();

        // Assert
        iteration.ShouldNotBeNull();
        iteration.IterationId.ShouldNotBeNullOrEmpty();
        iteration.StartedAt.ShouldNotBe(default);
        iteration.CompletedAt.ShouldNotBeNull();
        iteration.NodesReviewed.ShouldBe(0);
        iteration.NodesDeactivated.ShouldBe(0);
        iteration.Entries.ShouldBeEmpty();
    }

    [Fact]
    public async Task RunReviewRoundAsync_UpdatesStateToWorking_DuringExecution()
    {
        // Arrange
        var service = CreateService();
        ReviewAgentStatus? capturedStatus = null;
        var tcs = new TaskCompletionSource();

        _graphAccess.GetStaleKnowledgeNodesAsync(Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(async _ =>
            {
                capturedStatus = service.GetState().Status;
                tcs.SetResult();
                return (IReadOnlyList<KnowledgeNode>)[];
            });

        // Act
        var task = service.RunReviewRoundAsync();
        await tcs.Task;
        await task;

        // Assert
        capturedStatus.ShouldBe(ReviewAgentStatus.WorkingReviewRound);
    }

    // ─────────────────────────────────────────────────────────────
    //  T048: Iteration Persistence
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task RunReviewRoundAsync_CreatesIterationWithCorrectCounts()
    {
        // Arrange
        var service = CreateService();
        var staleNodes = CreateTestNodes(3);

        _graphAccess.GetStaleKnowledgeNodesAsync(Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<KnowledgeNode>>(staleNodes));

        // Since no verifier is provided, nodes will be skipped
        // Act
        var iteration = await service.RunReviewRoundAsync();

        // Assert
        iteration.NodesReviewed.ShouldBe(3);
        iteration.Entries.Count.ShouldBe(3);
    }

    [Fact]
    public async Task RunReviewRoundAsync_CreatesLogEntryForEachNode()
    {
        // Arrange
        var service = CreateService();
        var staleNodes = CreateTestNodes(2);

        _graphAccess.GetStaleKnowledgeNodesAsync(Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<KnowledgeNode>>(staleNodes));

        // Act
        var iteration = await service.RunReviewRoundAsync();

        // Assert
        iteration.Entries.Count.ShouldBe(2);
        iteration.Entries[0].NodeId.ShouldBe("node-0");
        iteration.Entries[1].NodeId.ShouldBe("node-1");
    }

    // ─────────────────────────────────────────────────────────────
    //  T073: Cleanup Logic
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task RunCleanupRoundAsync_ReturnsZero_WhenNoNodesToCleanup()
    {
        // Arrange
        var service = CreateService();
        _graphAccess.GetNodesForCleanupAsync(Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<KnowledgeNode>>([]));

        // Act
        var removed = await service.RunCleanupRoundAsync();

        // Assert
        removed.ShouldBe(0);
    }

    [Fact]
    public async Task RunCleanupRoundAsync_RemovesDeactivatedNodes()
    {
        // Arrange
        var service = CreateService();
        var nodesToCleanup = CreateDeactivatedNodes(3);

        _graphAccess.GetNodesForCleanupAsync(Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<KnowledgeNode>>(nodesToCleanup));
        _graphAccess.RemoveDeactivatedNodesAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(3);

        // Act
        var removed = await service.RunCleanupRoundAsync();

        // Assert
        removed.ShouldBe(3);
        await _graphAccess.Received(1).RemoveDeactivatedNodesAsync(
            Arg.Is<IEnumerable<string>>(ids => ids.Count() == 3),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunCleanupRoundAsync_UpdatesStateToCleanup_DuringExecution()
    {
        // Arrange
        var service = CreateService();
        ReviewAgentStatus? capturedStatus = null;
        var tcs = new TaskCompletionSource();

        _graphAccess.GetNodesForCleanupAsync(Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(async _ =>
            {
                capturedStatus = service.GetState().Status;
                tcs.SetResult();
                return (IReadOnlyList<KnowledgeNode>)[];
            });

        // Act
        var task = service.RunCleanupRoundAsync();
        await tcs.Task;
        await task;

        // Assert
        capturedStatus.ShouldBe(ReviewAgentStatus.WorkingCleanupRound);
    }

    [Fact]
    public async Task RunCleanupRoundAsync_UpdatesNodesRemovedCounter()
    {
        // Arrange
        var service = CreateService();
        var nodesToCleanup = CreateDeactivatedNodes(5);

        _graphAccess.GetNodesForCleanupAsync(Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<KnowledgeNode>>(nodesToCleanup));
        _graphAccess.RemoveDeactivatedNodesAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(5);

        // Act
        await service.RunCleanupRoundAsync();
        var state = service.GetState();

        // Assert
        state.NodesRemoved.ShouldBe(5);
    }

    // ─────────────────────────────────────────────────────────────
    //  Error Handling
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task RunReviewRoundAsync_SetsErrorState_OnException()
    {
        // Arrange
        var service = CreateService();
        _graphAccess.GetStaleKnowledgeNodesAsync(Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns<IReadOnlyList<KnowledgeNode>>(_ => throw new InvalidOperationException("Test error"));

        // Act & Assert
        await Should.ThrowAsync<InvalidOperationException>(() => service.RunReviewRoundAsync());

        var state = service.GetState();
        state.Status.ShouldBe(ReviewAgentStatus.Error);
        state.ErrorMessage.ShouldBe("Test error");
    }

    [Fact]
    public async Task RunCleanupRoundAsync_SetsErrorState_OnException()
    {
        // Arrange
        var service = CreateService();
        _graphAccess.GetNodesForCleanupAsync(Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns<IReadOnlyList<KnowledgeNode>>(_ => throw new InvalidOperationException("Cleanup error"));

        // Act & Assert
        await Should.ThrowAsync<InvalidOperationException>(() => service.RunCleanupRoundAsync());

        var state = service.GetState();
        state.Status.ShouldBe(ReviewAgentStatus.Error);
        state.ErrorMessage.ShouldBe("Cleanup error");
    }

    // ─────────────────────────────────────────────────────────────
    //  T074: Edge Removal Tests
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task RunCleanupRoundAsync_PassesCorrectNodeIdsForRemoval()
    {
        // Arrange
        var service = CreateService();
        var nodesToCleanup = CreateDeactivatedNodes(3);
        var capturedNodeIds = new List<string>();

        _graphAccess.GetNodesForCleanupAsync(Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<KnowledgeNode>>(nodesToCleanup));
        _graphAccess.RemoveDeactivatedNodesAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                capturedNodeIds.AddRange(callInfo.Arg<IEnumerable<string>>());
                return Task.FromResult(3);
            });

        // Act
        await service.RunCleanupRoundAsync();

        // Assert - Verify correct node IDs were passed
        capturedNodeIds.ShouldContain("deactivated-0");
        capturedNodeIds.ShouldContain("deactivated-1");
        capturedNodeIds.ShouldContain("deactivated-2");
        capturedNodeIds.Count.ShouldBe(3);
    }

    [Fact]
    public async Task RunCleanupRoundAsync_DoesNotRemoveActiveNodes()
    {
        // Arrange
        var service = CreateService();
        // Create mixed nodes - some deactivated, some active
        var mixedNodes = new List<KnowledgeNode>
        {
            new()
            {
                Id = "active-node",
                SessionId = "test-session",
                NodeType = KnowledgeNodeType.Generic,
                CoreDescription = "Active node",
                DetailedDescription = "Should not be removed",
                IsActivated = true
            },
            new()
            {
                Id = "deactivated-node",
                SessionId = "test-session",
                NodeType = KnowledgeNodeType.Generic,
                CoreDescription = "Deactivated node",
                DetailedDescription = "Should be removed",
                IsActivated = false,
                DeactivatedTimestamp = DateTimeOffset.UtcNow.AddDays(-30),
                DeactivatedReason = "Test deactivation"
            }
        };

        // GetNodesForCleanup should only return deactivated nodes (based on storage logic)
        // But let's test that only deactivated nodes are processed
        _graphAccess.GetNodesForCleanupAsync(Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<KnowledgeNode>>(
                mixedNodes.Where(n => !n.IsActivated).ToList()));
        _graphAccess.RemoveDeactivatedNodesAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(1);

        // Act
        var removed = await service.RunCleanupRoundAsync();

        // Assert
        removed.ShouldBe(1);
        await _graphAccess.Received(1).RemoveDeactivatedNodesAsync(
            Arg.Is<IEnumerable<string>>(ids => ids.Contains("deactivated-node") && !ids.Contains("active-node")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunCleanupRoundAsync_ReturnsZero_WhenRemovalFails()
    {
        // Arrange
        var service = CreateService();
        var nodesToCleanup = CreateDeactivatedNodes(2);

        _graphAccess.GetNodesForCleanupAsync(Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<KnowledgeNode>>(nodesToCleanup));
        _graphAccess.RemoveDeactivatedNodesAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(0); // Removal failed for all nodes

        // Act
        var removed = await service.RunCleanupRoundAsync();

        // Assert
        removed.ShouldBe(0);
    }

    [Fact]
    public async Task RunCleanupRoundAsync_HandlesPartialRemovalSuccess()
    {
        // Arrange
        var service = CreateService();
        var nodesToCleanup = CreateDeactivatedNodes(5);

        _graphAccess.GetNodesForCleanupAsync(Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<KnowledgeNode>>(nodesToCleanup));
        _graphAccess.RemoveDeactivatedNodesAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(3); // Only 3 of 5 were successfully removed

        // Act
        var removed = await service.RunCleanupRoundAsync();

        // Assert - Should return actual number removed
        removed.ShouldBe(3);
        var state = service.GetState();
        state.NodesRemoved.ShouldBe(3);
    }

    // ─────────────────────────────────────────────────────────────
    //  Helpers
    // ─────────────────────────────────────────────────────────────

    private ReviewAgentService CreateService()
    {
        return new ReviewAgentService(
            _optionsMonitor,
            _graphAccess,
            NullLogger<ReviewAgentService>.Instance);
    }

    private static List<KnowledgeNode> CreateTestNodes(int count)
    {
        var nodes = new List<KnowledgeNode>();
        for (var i = 0; i < count; i++)
        {
            nodes.Add(new KnowledgeNode
            {
                Id = $"node-{i}",
                SessionId = "test-session",
                NodeType = KnowledgeNodeType.Generic,
                CoreDescription = $"Test node {i}",
                DetailedDescription = $"Detailed description for node {i}",
                IsActivated = true,
                LastReviewedAt = DateTimeOffset.UtcNow.AddDays(-30)
            });
        }
        return nodes;
    }

    private static List<KnowledgeNode> CreateDeactivatedNodes(int count)
    {
        var nodes = new List<KnowledgeNode>();
        for (var i = 0; i < count; i++)
        {
            nodes.Add(new KnowledgeNode
            {
                Id = $"deactivated-{i}",
                SessionId = "test-session",
                NodeType = KnowledgeNodeType.Generic,
                CoreDescription = $"Deactivated node {i}",
                DetailedDescription = $"Detailed description for deactivated node {i}",
                IsActivated = false,
                DeactivatedTimestamp = DateTimeOffset.UtcNow.AddDays(-30),
                DeactivatedReason = "Test deactivation"
            });
        }
        return nodes;
    }
}
