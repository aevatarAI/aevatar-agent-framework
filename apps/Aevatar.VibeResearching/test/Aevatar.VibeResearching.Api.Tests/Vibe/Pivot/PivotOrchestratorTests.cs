using Aevatar.Agents.Knowledge.Graph;
using Aevatar.Agents.Knowledge.Graph.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using VibeResearching.Api.Materials;
using VibeResearching.Api.Sessions;
using VibeResearching.Api.Vibe;
using VibeResearching.Vibe.Pivot;
using VibeResearching.Vibe.Pivot.Models;
using Shouldly;

namespace VibeResearching.Api.Tests.Vibe.Pivot;

public sealed class PivotOrchestratorTests
{
    private enum KnowledgeNodeKind
    {
        Knowledge,
        Plan
    }

    private const string DefaultSessionId = "session1";
    private const string DefaultPlanId = "plan1";
    private const string SecondaryPlanId = "plan2";
    private const string DefaultKnowledgeId = "knowledge1";
    private const string DefaultNodeId = "node1";
    private const string DefaultPivotId = "pivot1";
    private const string NewPlanId = "newplan1";

    private readonly IKnowledgeGraphClientFactory _clientFactory;
    private readonly IKnowledgeGraphClient _graphClient;
    private readonly IPivotSnapshotManager _snapshotManager;
    private readonly IOptions<PivotOptions> _options;

    public PivotOrchestratorTests()
    {
        _graphClient = Substitute.For<IKnowledgeGraphClient>();
        _clientFactory = Substitute.For<IKnowledgeGraphClientFactory>();
        _clientFactory.CreateClient(Arg.Any<string>()).Returns(_graphClient);

        _snapshotManager = Substitute.For<IPivotSnapshotManager>();
        // SnapshotManager mock will be configured per-test via SetupSnapshotManager helper

        _options = Options.Create(new PivotOptions
        {
            ConfidenceThreshold = 0.7,
            ClarificationThreshold = 0.6,
            SemanticSimilarityThreshold = 0.7
        });
    }

    [Fact]
    public async Task ExecutePivotAsync_WithValidIntent_ReturnsCompletedOperation()
    {
        // Arrange
        var intent = CreateIntent(isDirectionChange: true, confidence: 0.9, newTopic: "量子计算");
        SetupEmptySnapshot();

        var orchestrator = CreateOrchestrator();

        // Act
        var result = await orchestrator.ExecutePivotAsync(intent, "传统密码学");

        // Assert
        result.ShouldNotBeNull();
        result.Status.ShouldBe(PivotStatus.Completed);
        result.SessionId.ShouldBe(DefaultSessionId);
        result.OldDirection.ShouldBe("传统密码学");
        result.NewDirection.ShouldBe("量子计算");
        result.DurationMs.ShouldBeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public async Task ExecutePivotAsync_WithNoDirectionChange_ThrowsArgumentException()
    {
        // Arrange
        var intent = CreateIntent(isDirectionChange: false, confidence: 0.2);
        var orchestrator = CreateOrchestrator();

        // Act & Assert
        await Should.ThrowAsync<ArgumentException>(async () =>
            await orchestrator.ExecutePivotAsync(intent));
    }

    [Fact]
    public async Task ClassifyNodesForPivotAsync_WithPlanNodes_CancelsOnlyPlanNodes()
    {
        // Arrange
        var snapshot = CreateSnapshot(new[]
        {
            CreateNode(DefaultPlanId, KnowledgeNodeKind.Plan, PivotNodeStatus.Active),
            CreateNode(SecondaryPlanId, KnowledgeNodeKind.Plan, PivotNodeStatus.Active),
            CreateNode(DefaultKnowledgeId, KnowledgeNodeKind.Knowledge, PivotNodeStatus.Active)
        });

        var intent = CreateIntent(isDirectionChange: true, confidence: 0.9);
        var orchestrator = CreateOrchestrator();

        // Act
        var (cancelled, preserved, superseded) = await orchestrator.ClassifyNodesForPivotAsync(
            _graphClient, snapshot, intent, DefaultPivotId);

        // Assert
        cancelled.Count.ShouldBe(2);
        cancelled.ShouldContain(DefaultPlanId);
        cancelled.ShouldContain(SecondaryPlanId);
        superseded.Count.ShouldBe(1);
        superseded.ShouldContain(DefaultKnowledgeId);
    }

    [Fact]
    public async Task ClassifyNodesForPivotAsync_WithAlreadyCancelledNodes_SkipsThem()
    {
        // Arrange
        var snapshot = CreateSnapshot(new[]
        {
            CreateNode(DefaultPlanId, KnowledgeNodeKind.Plan, PivotNodeStatus.Cancelled),
            CreateNode(SecondaryPlanId, KnowledgeNodeKind.Plan, PivotNodeStatus.Active)
        });

        var intent = CreateIntent(isDirectionChange: true, confidence: 0.9);
        var orchestrator = CreateOrchestrator();

        // Act
        var (cancelled, preserved, superseded) = await orchestrator.ClassifyNodesForPivotAsync(
            _graphClient, snapshot, intent, DefaultPivotId);

        // Assert
        cancelled.Count.ShouldBe(1);
        cancelled.ShouldContain(SecondaryPlanId);
        cancelled.ShouldNotContain(DefaultPlanId);
    }

    [Fact]
    public void ShouldPreserveNode_WithMatchingAspect_ReturnsTrue()
    {
        // Arrange
        var node = (KnowledgeNode)CreateNode(
            DefaultNodeId,
            KnowledgeNodeKind.Knowledge,
            PivotNodeStatus.Active,
            coreDescription: "CNN架构分析与优化");
        var preserveAspects = new List<string> { "CNN" };
        var orchestrator = CreateOrchestrator();

        // Act
        var result = orchestrator.ShouldPreserveNode(node, preserveAspects);

        // Assert
        result.ShouldBeTrue();
    }

    [Fact]
    public void ShouldPreserveNode_WithNoMatchingAspect_ReturnsFalse()
    {
        // Arrange
        var node = (KnowledgeNode)CreateNode(
            DefaultNodeId,
            KnowledgeNodeKind.Knowledge,
            PivotNodeStatus.Active,
            coreDescription: "自然语言处理研究");
        var preserveAspects = new List<string> { "CNN", "图像" };
        var orchestrator = CreateOrchestrator();

        // Act
        var result = orchestrator.ShouldPreserveNode(node, preserveAspects);

        // Assert
        result.ShouldBeFalse();
    }

    [Fact]
    public void ShouldPreserveNode_WithDirectionContextMatch_ReturnsTrue()
    {
        // Arrange
        var node = (KnowledgeNode)CreateNode(
            DefaultNodeId,
            KnowledgeNodeKind.Knowledge,
            PivotNodeStatus.Active,
            directionContext: "深度学习基础架构");
        var preserveAspects = new List<string> { "架构" };
        var orchestrator = CreateOrchestrator();

        // Act
        var result = orchestrator.ShouldPreserveNode(node, preserveAspects);

        // Assert
        result.ShouldBeTrue();
    }

    [Fact]
    public void ShouldPreserveNode_WithEmptyPreserveAspects_ReturnsFalse()
    {
        // Arrange
        var node = (KnowledgeNode)CreateNode(DefaultNodeId, KnowledgeNodeKind.Knowledge, PivotNodeStatus.Active);
        var preserveAspects = new List<string>();
        var orchestrator = CreateOrchestrator();

        // Act
        var result = orchestrator.ShouldPreserveNode(node, preserveAspects);

        // Assert
        result.ShouldBeFalse();
    }

    [Fact]
    public async Task ClassifyNodesForPivotAsync_WithPreserveAspects_PreservesMatchingNodes()
    {
        // Arrange
        var snapshot = CreateSnapshot(new[]
        {
            CreateNode(DefaultPlanId, KnowledgeNodeKind.Plan, PivotNodeStatus.Active, coreDescription: "CNN优化"),
            CreateNode(SecondaryPlanId, KnowledgeNodeKind.Plan, PivotNodeStatus.Active, coreDescription: "数据预处理")
        });

        var intent = CreateIntent(isDirectionChange: true, confidence: 0.9, preserveAspects: new[] { "CNN" });
        var orchestrator = CreateOrchestrator();

        // Act
        var (cancelled, preserved, superseded) = await orchestrator.ClassifyNodesForPivotAsync(
            _graphClient, snapshot, intent, DefaultPivotId);

        // Assert
        preserved.Count.ShouldBe(1);
        preserved.ShouldContain(DefaultPlanId);
        cancelled.Count.ShouldBe(1);
        cancelled.ShouldContain(SecondaryPlanId);
    }

    [Fact]
    public async Task ExecutePivotAsync_UpdatesNodeStatusesCorrectly()
    {
        // Arrange
        var intent = CreateIntent(isDirectionChange: true, confidence: 0.9, newTopic: "新方向");
        var snapshot = CreateSnapshot(new[]
        {
            CreateNode(DefaultPlanId, KnowledgeNodeKind.Plan, PivotNodeStatus.Active),
            CreateNode(DefaultKnowledgeId, KnowledgeNodeKind.Knowledge, PivotNodeStatus.Active)
        });

        SetupSnapshot(snapshot);
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
            .Returns(Task.FromResult((KnowledgeNode)CreateNode("updated", KnowledgeNodeKind.Knowledge, PivotNodeStatus.Cancelled)));

        var orchestrator = CreateOrchestrator();

        // Act
        var result = await orchestrator.ExecutePivotAsync(intent);

        // Assert
        result.Status.ShouldBe(PivotStatus.Completed);
        result.CancelledNodeIds.ShouldContain(DefaultPlanId);

        // Verify UpsertNodeAsync was called with correct parameters for cancellation
        await _graphClient.Received().UpsertNodeAsync(
            DefaultPlanId,
            Arg.Any<KnowledgeNodeType>(),
            Arg.Any<string?>(),
            Arg.Any<string?>(),
            Arg.Any<string?>(),
            Arg.Any<string?>(),
            Arg.Any<string?>(),
            PivotNodeStatus.Cancelled,
            Arg.Any<DateTimeOffset?>(),
            Arg.Any<string>(),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreatePlanNodeAsync_CreatesNodeWithPlanKind()
    {
        // Arrange
        var createdPlanNode = (PlanNode)CreateNode(NewPlanId, KnowledgeNodeKind.Plan, PivotNodeStatus.Active);
        _graphClient.CreatePlanNodeAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string?>(),
            Arg.Any<int>(),
            Arg.Any<IEnumerable<string>?>(),
            Arg.Any<IEnumerable<string>?>(),
            Arg.Any<CancellationToken>())
            .Returns(createdPlanNode);

        var orchestrator = CreateOrchestrator();

        // Act
        var result = await orchestrator.CreatePlanNodeAsync(
            DefaultSessionId,
            NewPlanId,
            "新研究计划",
            "详细描述",
            directionContext: "新方向");

        // Assert
        result.ShouldNotBeNull();
        result.ShouldBeOfType<PlanNode>();

        await _graphClient.Received().CreatePlanNodeAsync(
            NewPlanId,
            "新研究计划",
            "详细描述",
            Arg.Any<string?>(),
            Arg.Any<int>(),
            Arg.Any<IEnumerable<string>?>(),
            Arg.Any<IEnumerable<string>?>(),
            Arg.Any<CancellationToken>());
    }

    private PivotOrchestrator CreateOrchestrator()
    {
        return new PivotOrchestrator(
            _clientFactory,
            _snapshotManager,
            _options,
            NullLogger<PivotOrchestrator>.Instance);
    }

    private DirectionChangeIntent CreateIntent(
        bool isDirectionChange,
        double confidence,
        string? newTopic = null,
        string[]? preserveAspects = null)
    {
        return new DirectionChangeIntent
        {
            SessionId = DefaultSessionId,
            MessageId = "msg1",
            IsDirectionChange = isDirectionChange,
            Confidence = confidence,
            NewTopic = newTopic,
            PreserveAspects = preserveAspects?.ToList() ?? [],
            NeedsClarification = false
        };
    }

    private void SetupEmptySnapshot()
    {
        var snapshot = new GraphSnapshot
        {
            SessionId = DefaultSessionId,
            PlanNodes = [],
            KnowledgeNodes = [],
            Edges = []
        };
        SetupSnapshot(snapshot);
    }

    private void SetupSnapshot(GraphSnapshot snapshot)
    {
        _graphClient.GetGraphSnapshotAsync(Arg.Any<CancellationToken>()).Returns(snapshot);
        _snapshotManager.CreateSnapshotAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(ci => PivotSnapshotMetadata.Create(
                ci.ArgAt<string>(0),
                ci.ArgAt<string>(1),
                snapshot,
                ci.ArgAt<string>(2),
                30));
    }

    private GraphSnapshot CreateSnapshot(IGraphNode[] nodes)
    {
        return new GraphSnapshot
        {
            SessionId = DefaultSessionId,
            PlanNodes = nodes.OfType<PlanNode>().ToList(),
            KnowledgeNodes = nodes.OfType<KnowledgeNode>().ToList(),
            Edges = []
        };
    }

    private IGraphNode CreateNode(
        string id,
        KnowledgeNodeKind kind,
        PivotNodeStatus pivotStatus,
        string? coreDescription = null,
        string? directionContext = null)
    {
        coreDescription ??= $"Node {id}";
        if (kind == KnowledgeNodeKind.Plan)
        {
            return new PlanNode
            {
                Id = id,
                SessionId = DefaultSessionId,
                CoreDescription = coreDescription,
                DetailedDescription = $"Detailed {id}",
                PivotStatus = pivotStatus,
                Status = PlanNodeStatus.Pending,
                DirectionContext = directionContext
            };
        }
        return new KnowledgeNode
        {
            Id = id,
            SessionId = DefaultSessionId,
            NodeType = KnowledgeNodeType.Generic,
            CoreDescription = coreDescription,
            DetailedDescription = $"Detailed {id}",
            PivotStatus = pivotStatus,
            DirectionContext = directionContext
        };
    }
}

public sealed class VibeRoundContextTests
{
    private const string DefaultSessionId = "s1";
    private const string DefaultRunId = "run1";

    [Fact]
    public void Constructor_WithNullSession_Throws()
    {
        var input = new SessionInputInDto();
        var materials = CreateMaterialsSnapshot(DefaultSessionId);

        var ex = Should.Throw<ArgumentNullException>(() =>
            new VibeOrchestrator.VibeRoundContext(null!, DefaultRunId, input, "q", materials, _ => { }));

        ex.ParamName.ShouldBe("session");
    }

    [Fact]
    public void Constructor_StoresInputs()
    {
        var session = new ResearchSession(DefaultSessionId);
        var input = new SessionInputInDto { RequestId = "req1" };
        var materials = CreateMaterialsSnapshot(DefaultSessionId);
        var captured = new List<string>();

        var ctx = new VibeOrchestrator.VibeRoundContext(
            session,
            DefaultRunId,
            input,
            "question",
            materials,
            captured.Add);

        ctx.Session.ShouldBe(session);
        ctx.RunId.ShouldBe(DefaultRunId);
        ctx.Input.ShouldBe(input);
        ctx.Question.ShouldBe("question");
        ctx.Materials.ShouldBe(materials);

        ctx.EmitAssistantDelta("hi");
        captured.ShouldBe(["hi"]);
    }

    private static MaterialsSnapshot CreateMaterialsSnapshot(string sessionId)
    {
        return new MaterialsSnapshot
        {
            SessionId = sessionId,
            DagId = "global",
            LoadedAt = DateTimeOffset.UtcNow,
            Facts = [],
            RenderedContext = string.Empty
        };
    }
}
