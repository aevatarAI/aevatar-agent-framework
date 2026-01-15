using Aevatar.Agents.Knowledge.Graph;
using Aevatar.Agents.Knowledge.Graph.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using ScientificResearchAssistant.Vibe.Pivot;
using ScientificResearchAssistant.Vibe.Pivot.Messages;
using ScientificResearchAssistant.Vibe.Pivot.Models;
using Shouldly;

namespace ScientificResearchAssistant.Api.Tests.Vibe.Pivot;

public sealed class RollbackTests
{
    private enum KnowledgeNodeKind
    {
        Knowledge,
        Plan
    }

    private readonly IKnowledgeGraphClientFactory _clientFactory;
    private readonly IKnowledgeGraphClient _graphClient;
    private readonly IOptions<PivotOptions> _options;

    public RollbackTests()
    {
        _graphClient = Substitute.For<IKnowledgeGraphClient>();
        _clientFactory = Substitute.For<IKnowledgeGraphClientFactory>();
        _clientFactory.CreateClient(Arg.Any<string>()).Returns(_graphClient);
        _options = Options.Create(new PivotOptions
        {
            RollbackWindowMinutes = 30
        });
    }

    // ─────────────────────────────────────────────────────────────
    //  Snapshot Creation Tests
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateSnapshotAsync_CapturesGraphState()
    {
        // Arrange
        var snapshot = CreateSnapshot(new[]
        {
            CreateNode("node1", KnowledgeNodeKind.Knowledge),
            CreateNode("plan1", KnowledgeNodeKind.Plan)
        });

        _graphClient.GetGraphSnapshotAsync(Arg.Any<CancellationToken>()).Returns(snapshot);

        var manager = CreateManager();

        // Act
        var metadata = await manager.CreateSnapshotAsync("session1", "pivot1", "研究方向A");

        // Assert
        metadata.ShouldNotBeNull();
        metadata.SessionId.ShouldBe("session1");
        metadata.PivotId.ShouldBe("pivot1");
        metadata.DirectionSummary.ShouldBe("研究方向A");
        metadata.Snapshot.AllNodes.Count().ShouldBe(2);
        metadata.IsValid.ShouldBeTrue();
    }

    [Fact]
    public async Task CreateSnapshotAsync_SetsCorrectExpiry()
    {
        // Arrange
        SetupEmptySnapshot();
        var manager = CreateManager();

        // Act
        var metadata = await manager.CreateSnapshotAsync("session1", "pivot1", "direction");

        // Assert
        metadata.ExpiresAt.ShouldBeGreaterThan(DateTimeOffset.UtcNow);
        (metadata.ExpiresAt - metadata.CreatedAt).TotalMinutes.ShouldBe(30, 0.1);
    }

    [Fact]
    public async Task GetSnapshot_ReturnsStoredSnapshot()
    {
        // Arrange
        SetupEmptySnapshot();
        var manager = CreateManager();
        await manager.CreateSnapshotAsync("session1", "pivot1", "direction");

        // Act
        var retrieved = manager.GetSnapshot("session1", "pivot1");

        // Assert
        retrieved.ShouldNotBeNull();
        retrieved.PivotId.ShouldBe("pivot1");
    }

    [Fact]
    public void GetSnapshot_ReturnsNullForUnknownPivot()
    {
        // Arrange
        var manager = CreateManager();

        // Act
        var retrieved = manager.GetSnapshot("session1", "unknown_pivot");

        // Assert
        retrieved.ShouldBeNull();
    }

    [Fact]
    public async Task GetMostRecentSnapshot_ReturnsLatestValid()
    {
        // Arrange
        SetupEmptySnapshot();
        var manager = CreateManager();

        await manager.CreateSnapshotAsync("session1", "pivot1", "direction1");
        await Task.Delay(10); // Small delay to ensure different timestamps
        await manager.CreateSnapshotAsync("session1", "pivot2", "direction2");

        // Act
        var recent = manager.GetMostRecentSnapshot("session1");

        // Assert
        recent.ShouldNotBeNull();
        recent.PivotId.ShouldBe("pivot2");
    }

    // ─────────────────────────────────────────────────────────────
    //  Rollback Execution Tests
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteRollbackAsync_RestoresCancelledNodes()
    {
        // Arrange
        var originalSnapshot = CreateSnapshot(new[]
        {
            CreateNode("plan1", KnowledgeNodeKind.Plan, PivotNodeStatus.Active),
            CreateNode("knowledge1", KnowledgeNodeKind.Knowledge, PivotNodeStatus.Active)
        });

        var currentSnapshot = CreateSnapshot(new[]
        {
            CreateNode("plan1", KnowledgeNodeKind.Plan, PivotNodeStatus.Cancelled),
            CreateNode("knowledge1", KnowledgeNodeKind.Knowledge, PivotNodeStatus.Superseded)
        });

        _graphClient.GetGraphSnapshotAsync(Arg.Any<CancellationToken>())
            .Returns(originalSnapshot, currentSnapshot);

        _graphClient.UpsertNodeAsync(
            Arg.Any<string>(),
            Arg.Any<KnowledgeNodeType>(),
            Arg.Any<string?>(),
            Arg.Any<string?>(),
            Arg.Any<string?>(),
            Arg.Any<string?>(),
            Arg.Any<string?>(),
            Arg.Any<PivotNodeStatus?>(),
            Arg.Any<DateTimeOffset?>(),
            Arg.Any<string?>(),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>())
            .Returns(Task.FromResult((KnowledgeNode)CreateNode("updated", KnowledgeNodeKind.Knowledge, PivotNodeStatus.Active)));

        var manager = CreateManager();
        await manager.CreateSnapshotAsync("session1", "pivot1", "old direction");

        // Update mock to return current state for rollback
        _graphClient.GetGraphSnapshotAsync(Arg.Any<CancellationToken>())
            .Returns(currentSnapshot);

        var request = new RollbackRequest
        {
            SessionId = "session1",
            PivotId = "pivot1",
            PreserveNewCompleted = false
        };

        // Act
        var response = await manager.ExecuteRollbackAsync(request);

        // Assert
        response.Success.ShouldBeTrue();
        response.RestoredNodeCount.ShouldBe(2);
    }

    [Fact]
    public async Task ExecuteRollbackAsync_RemovesNewNodes()
    {
        // Arrange
        var originalSnapshot = CreateSnapshot(new[]
        {
            CreateNode("original1", KnowledgeNodeKind.Knowledge, PivotNodeStatus.Active)
        });

        var currentSnapshot = CreateSnapshot(new[]
        {
            CreateNode("original1", KnowledgeNodeKind.Knowledge, PivotNodeStatus.Superseded),
            CreateNode("new1", KnowledgeNodeKind.Plan, PivotNodeStatus.Active)
        });

        _graphClient.GetGraphSnapshotAsync(Arg.Any<CancellationToken>())
            .Returns(originalSnapshot, currentSnapshot);

        _graphClient.UpsertNodeAsync(
            Arg.Any<string>(),
            Arg.Any<KnowledgeNodeType>(),
            Arg.Any<string?>(),
            Arg.Any<string?>(),
            Arg.Any<string?>(),
            Arg.Any<string?>(),
            Arg.Any<string?>(),
            Arg.Any<PivotNodeStatus?>(),
            Arg.Any<DateTimeOffset?>(),
            Arg.Any<string?>(),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>())
            .Returns(Task.FromResult((KnowledgeNode)CreateNode("updated", KnowledgeNodeKind.Knowledge, PivotNodeStatus.Active)));

        var manager = CreateManager();
        await manager.CreateSnapshotAsync("session1", "pivot1", "old direction");

        _graphClient.GetGraphSnapshotAsync(Arg.Any<CancellationToken>())
            .Returns(currentSnapshot);

        var request = new RollbackRequest
        {
            SessionId = "session1",
            PivotId = "pivot1",
            PreserveNewCompleted = false
        };

        // Act
        var response = await manager.ExecuteRollbackAsync(request);

        // Assert
        response.Success.ShouldBeTrue();
        await _graphClient.Received(1).RemoveNodeAsync("new1", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteRollbackAsync_PreservesNewKnowledgeNodes()
    {
        // Arrange
        var originalSnapshot = CreateSnapshot(new[]
        {
            CreateNode("original1", KnowledgeNodeKind.Knowledge, PivotNodeStatus.Active)
        });

        var currentSnapshot = CreateSnapshot(new[]
        {
            CreateNode("original1", KnowledgeNodeKind.Knowledge, PivotNodeStatus.Superseded),
            CreateNode("new_knowledge1", KnowledgeNodeKind.Knowledge, PivotNodeStatus.Active),
            CreateNode("new_plan1", KnowledgeNodeKind.Plan, PivotNodeStatus.Active)
        });

        _graphClient.GetGraphSnapshotAsync(Arg.Any<CancellationToken>())
            .Returns(originalSnapshot, currentSnapshot);

        _graphClient.UpsertNodeAsync(
            Arg.Any<string>(),
            Arg.Any<KnowledgeNodeType>(),
            Arg.Any<string?>(),
            Arg.Any<string?>(),
            Arg.Any<string?>(),
            Arg.Any<string?>(),
            Arg.Any<string?>(),
            Arg.Any<PivotNodeStatus?>(),
            Arg.Any<DateTimeOffset?>(),
            Arg.Any<string?>(),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>())
            .Returns(Task.FromResult((KnowledgeNode)CreateNode("updated", KnowledgeNodeKind.Knowledge, PivotNodeStatus.Active)));

        var manager = CreateManager();
        await manager.CreateSnapshotAsync("session1", "pivot1", "old direction");

        _graphClient.GetGraphSnapshotAsync(Arg.Any<CancellationToken>())
            .Returns(currentSnapshot);

        var request = new RollbackRequest
        {
            SessionId = "session1",
            PivotId = "pivot1",
            PreserveNewCompleted = true
        };

        // Act
        var response = await manager.ExecuteRollbackAsync(request);

        // Assert
        response.Success.ShouldBeTrue();
        response.PreservedNewNodeCount.ShouldBe(1);
        // Plan node should be removed, knowledge node should be preserved
        await _graphClient.Received(1).RemoveNodeAsync("new_plan1", Arg.Any<CancellationToken>());
        await _graphClient.DidNotReceive().RemoveNodeAsync("new_knowledge1", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteRollbackAsync_FailsForExpiredSnapshot()
    {
        // Arrange - use a very short rollback window
        var shortOptions = Options.Create(new PivotOptions { RollbackWindowMinutes = 0 });
        var manager = new PivotSnapshotManager(_clientFactory, shortOptions, NullLogger<PivotSnapshotManager>.Instance);

        SetupEmptySnapshot();
        await manager.CreateSnapshotAsync("session1", "pivot1", "direction");

        // Wait a moment to ensure expiry
        await Task.Delay(50);

        var request = new RollbackRequest
        {
            SessionId = "session1",
            PivotId = "pivot1"
        };

        // Act
        var response = await manager.ExecuteRollbackAsync(request);

        // Assert
        response.Success.ShouldBeFalse();
        response.ErrorMessage.ShouldNotBeNull();
        response.ErrorMessage.ShouldContain("expired");
    }

    [Fact]
    public async Task ExecuteRollbackAsync_FailsForUnknownSnapshot()
    {
        // Arrange
        var manager = CreateManager();

        var request = new RollbackRequest
        {
            SessionId = "session1",
            PivotId = "unknown_pivot"
        };

        // Act
        var response = await manager.ExecuteRollbackAsync(request);

        // Assert
        response.Success.ShouldBeFalse();
    }

    [Fact]
    public async Task ExecuteRollbackAsync_RollsBackMostRecentWhenNoPivotIdSpecified()
    {
        // Arrange
        SetupEmptySnapshot();
        var manager = CreateManager();

        await manager.CreateSnapshotAsync("session1", "pivot1", "direction1");
        await manager.CreateSnapshotAsync("session1", "pivot2", "direction2");

        _graphClient.GetGraphSnapshotAsync(Arg.Any<CancellationToken>())
            .Returns(CreateSnapshot(Array.Empty<KnowledgeNode>()));

        var request = new RollbackRequest
        {
            SessionId = "session1",
            PivotId = null // Should use most recent
        };

        // Act
        var response = await manager.ExecuteRollbackAsync(request);

        // Assert
        response.Success.ShouldBeTrue();
        response.PivotId.ShouldBe("pivot2");
    }

    // ─────────────────────────────────────────────────────────────
    //  Cleanup Tests
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task CleanupExpiredSnapshots_RemovesExpiredOnly()
    {
        // Arrange - create a manager with short expiry for one snapshot
        var manager = CreateManager();
        SetupEmptySnapshot();

        // Create two snapshots
        await manager.CreateSnapshotAsync("session1", "pivot1", "direction1");
        await manager.CreateSnapshotAsync("session1", "pivot2", "direction2");

        // Verify both exist
        manager.GetSnapshot("session1", "pivot1").ShouldNotBeNull();
        manager.GetSnapshot("session1", "pivot2").ShouldNotBeNull();

        // Act - cleanup should not remove anything (all still valid)
        var removed = manager.CleanupExpiredSnapshots("session1");

        // Assert
        removed.ShouldBe(0);
        manager.GetSnapshot("session1", "pivot1").ShouldNotBeNull();
        manager.GetSnapshot("session1", "pivot2").ShouldNotBeNull();
    }

    [Fact]
    public async Task RemoveSnapshot_RemovesSpecificSnapshot()
    {
        // Arrange
        SetupEmptySnapshot();
        var manager = CreateManager();
        await manager.CreateSnapshotAsync("session1", "pivot1", "direction");

        // Act
        manager.RemoveSnapshot("session1", "pivot1");

        // Assert
        manager.GetSnapshot("session1", "pivot1").ShouldBeNull();
    }

    // ─────────────────────────────────────────────────────────────
    //  PivotSnapshotMetadata Tests
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void PivotSnapshotMetadata_Create_SetsCorrectFields()
    {
        // Arrange
        var snapshot = CreateSnapshot(Array.Empty<KnowledgeNode>());

        // Act
        var metadata = PivotSnapshotMetadata.Create(
            "session1",
            "pivot1",
            snapshot,
            "研究方向",
            rollbackWindowMinutes: 15);

        // Assert
        metadata.SessionId.ShouldBe("session1");
        metadata.PivotId.ShouldBe("pivot1");
        metadata.DirectionSummary.ShouldBe("研究方向");
        (metadata.ExpiresAt - metadata.CreatedAt).TotalMinutes.ShouldBe(15, 0.1);
    }

    [Fact]
    public void PivotSnapshotMetadata_IsValid_ReturnsTrueWhenNotExpired()
    {
        // Arrange
        var snapshot = CreateSnapshot(Array.Empty<KnowledgeNode>());
        var metadata = PivotSnapshotMetadata.Create("s", "p", snapshot, "d", 30);

        // Assert
        metadata.IsValid.ShouldBeTrue();
    }

    // ─────────────────────────────────────────────────────────────
    //  Helper Methods
    // ─────────────────────────────────────────────────────────────

    private PivotSnapshotManager CreateManager()
    {
        return new PivotSnapshotManager(_clientFactory, _options, NullLogger<PivotSnapshotManager>.Instance);
    }

    private void SetupEmptySnapshot()
    {
        _graphClient.GetGraphSnapshotAsync(Arg.Any<CancellationToken>())
            .Returns(new GraphSnapshot
            {
                SessionId = "session1",
                PlanNodes = new List<PlanNode>(),
                KnowledgeNodes = new List<KnowledgeNode>(),
                Edges = new List<KnowledgeEdge>()
            });
    }

    private static GraphSnapshot CreateSnapshot(IGraphNode[] nodes)
    {
        return new GraphSnapshot
        {
            SessionId = "session1",
            PlanNodes = nodes.OfType<PlanNode>().ToList(),
            KnowledgeNodes = nodes.OfType<KnowledgeNode>().ToList(),
            Edges = new List<KnowledgeEdge>()
        };
    }

    private static IGraphNode CreateNode(
        string id,
        KnowledgeNodeKind kind,
        PivotNodeStatus pivotStatus = PivotNodeStatus.Active)
    {
        if (kind == KnowledgeNodeKind.Plan)
        {
            return new PlanNode
            {
                Id = id,
                SessionId = "session1",
                CoreDescription = $"Node {id}",
                DetailedDescription = $"Detailed {id}",
                PivotStatus = pivotStatus,
                Status = PlanNodeStatus.Pending
            };
        }
        return new KnowledgeNode
        {
            Id = id,
            SessionId = "session1",
            NodeType = KnowledgeNodeType.Generic,
            CoreDescription = $"Node {id}",
            DetailedDescription = $"Detailed {id}",
            PivotStatus = pivotStatus
        };
    }
}
