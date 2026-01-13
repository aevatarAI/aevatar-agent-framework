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

public sealed class SubagentCoordinationTests
{
    private readonly IPivotEventPublisher _eventPublisher;
    private readonly IPivotOrchestrator _pivotOrchestrator;
    private readonly IOptions<PivotOptions> _options;

    public SubagentCoordinationTests()
    {
        _eventPublisher = Substitute.For<IPivotEventPublisher>();
        _pivotOrchestrator = Substitute.For<IPivotOrchestrator>();
        _options = Options.Create(new PivotOptions
        {
            SubagentAckTimeoutSeconds = 1
        });
    }

    [Fact]
    public async Task CoordinatePivotAsync_PublishesEventToAllAgents()
    {
        // Arrange
        var intent = CreateIntent();
        var dagOperation = CreateDagOperation(intent);

        _pivotOrchestrator.ExecutePivotAsync(intent, Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(dagOperation);

        _eventPublisher.GetRegisteredAgents(intent.SessionId)
            .Returns(AgentPivotCoordinator.StandardAgents);

        _eventPublisher.WaitForAcknowledgmentsAsync(
            Arg.Any<string>(),
            Arg.Any<IEnumerable<string>>(),
            Arg.Any<TimeSpan>(),
            Arg.Any<CancellationToken>())
            .Returns(CreateAcknowledgments(dagOperation.PivotId, intent.SessionId));

        var coordinator = CreateCoordinator();

        // Act
        var result = await coordinator.CoordinatePivotAsync(intent, "旧方向");

        // Assert
        result.ShouldNotBeNull();
        result.SessionId.ShouldBe(intent.SessionId);
        result.DagOperation.ShouldNotBeNull();
        result.DagOperation.PivotId.ShouldBe(dagOperation.PivotId);

        await _eventPublisher.Received(1).PublishPivotEventAsync(
            Arg.Is<PivotEvent>(e => e.PivotId == dagOperation.PivotId),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CoordinatePivotAsync_WaitsForAcknowledgments()
    {
        // Arrange
        var intent = CreateIntent();
        var dagOperation = CreateDagOperation(intent);

        _pivotOrchestrator.ExecutePivotAsync(intent, Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(dagOperation);

        var expectedAgents = new[] { "planner", "librarian", "reasoner" };
        _eventPublisher.GetRegisteredAgents(intent.SessionId).Returns(expectedAgents);

        var acks = CreateAcknowledgments(dagOperation.PivotId, intent.SessionId, expectedAgents);
        _eventPublisher.WaitForAcknowledgmentsAsync(
            Arg.Any<string>(),
            Arg.Any<IEnumerable<string>>(),
            Arg.Any<TimeSpan>(),
            Arg.Any<CancellationToken>())
            .Returns(acks);

        var coordinator = CreateCoordinator();

        // Act
        var result = await coordinator.CoordinatePivotAsync(intent);

        // Assert
        result.Acknowledgments.ShouldNotBeNull();
        result.Acknowledgments.Count.ShouldBe(3);
        result.AllAgentsAcknowledged.ShouldBeTrue();

        await _eventPublisher.Received(1).WaitForAcknowledgmentsAsync(
            dagOperation.PivotId,
            Arg.Any<IEnumerable<string>>(),
            Arg.Any<TimeSpan>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CoordinatePivotAsync_ReportsPartialAcknowledgments()
    {
        // Arrange
        var intent = CreateIntent();
        var dagOperation = CreateDagOperation(intent);

        _pivotOrchestrator.ExecutePivotAsync(intent, Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(dagOperation);

        var expectedAgents = new[] { "planner", "librarian", "reasoner" };
        _eventPublisher.GetRegisteredAgents(intent.SessionId).Returns(expectedAgents);

        // Only 2 out of 3 agents acknowledged
        var acks = CreateAcknowledgments(dagOperation.PivotId, intent.SessionId, new[] { "planner", "librarian" });
        _eventPublisher.WaitForAcknowledgmentsAsync(
            Arg.Any<string>(),
            Arg.Any<IEnumerable<string>>(),
            Arg.Any<TimeSpan>(),
            Arg.Any<CancellationToken>())
            .Returns(acks);

        var coordinator = CreateCoordinator();

        // Act
        var result = await coordinator.CoordinatePivotAsync(intent);

        // Assert
        result.Acknowledgments.Count.ShouldBe(2);
        result.AllAgentsAcknowledged.ShouldBeFalse();
    }

    [Fact]
    public void RegisterSessionAgents_RegistersAllStandardAgents()
    {
        // Arrange
        var sessionId = "session123";
        var coordinator = CreateCoordinator();

        // Act
        coordinator.RegisterSessionAgents(sessionId);

        // Assert
        foreach (var agentId in AgentPivotCoordinator.StandardAgents)
        {
            _eventPublisher.Received(1).RegisterAgent(sessionId, agentId);
        }
    }

    [Fact]
    public void UnregisterSessionAgents_UnregistersAllAgents()
    {
        // Arrange
        var sessionId = "session123";
        var coordinator = CreateCoordinator();

        // Act
        coordinator.UnregisterSessionAgents(sessionId);

        // Assert
        foreach (var agentId in AgentPivotCoordinator.StandardAgents)
        {
            _eventPublisher.Received(1).UnregisterAgent(sessionId, agentId);
        }
    }

    [Fact]
    public async Task NotifyAgentAsync_WithPivotAwareAgent_CallsHandlePivot()
    {
        // Arrange
        var sessionId = "session123";
        var pivotEvent = CreatePivotEvent(sessionId);
        var agent = Substitute.For<IPivotAwareAgent>();
        agent.HandlePivotAsync(Arg.Any<PivotEvent>(), Arg.Any<CancellationToken>())
            .Returns(new PivotAcknowledgment
            {
                SessionId = sessionId,
                PivotId = pivotEvent.PivotId,
                AgentId = "planner",
                Status = "ready"
            });

        var coordinator = CreateCoordinator();

        // Act
        var result = await coordinator.NotifyAgentAsync(sessionId, "planner", pivotEvent, agent);

        // Assert
        result.ShouldBeTrue();
        await agent.Received(1).HandlePivotAsync(pivotEvent, Arg.Any<CancellationToken>());
        _eventPublisher.Received(1).RecordAcknowledgment(Arg.Any<PivotAcknowledgment>());
    }

    [Fact]
    public async Task NotifyAgentAsync_WithNullAgent_RecordsSyntheticAck()
    {
        // Arrange
        var sessionId = "session123";
        var pivotEvent = CreatePivotEvent(sessionId);
        var coordinator = CreateCoordinator();

        // Act
        var result = await coordinator.NotifyAgentAsync(sessionId, "legacy_agent", pivotEvent, null);

        // Assert
        result.ShouldBeTrue();
        _eventPublisher.Received(1).RecordAcknowledgment(
            Arg.Is<PivotAcknowledgment>(a =>
                a.AgentId == "legacy_agent" &&
                a.Status == "not_pivot_aware" &&
                a.Acknowledged));
    }

    [Fact]
    public async Task NotifyAgentAsync_WhenAgentThrows_RecordsErrorAck()
    {
        // Arrange
        var sessionId = "session123";
        var pivotEvent = CreatePivotEvent(sessionId);
        var agent = Substitute.For<IPivotAwareAgent>();
        agent.HandlePivotAsync(Arg.Any<PivotEvent>(), Arg.Any<CancellationToken>())
            .Returns<PivotAcknowledgment>(x => throw new InvalidOperationException("Agent error"));

        var coordinator = CreateCoordinator();

        // Act
        var result = await coordinator.NotifyAgentAsync(sessionId, "failing_agent", pivotEvent, agent);

        // Assert
        result.ShouldBeFalse();
        _eventPublisher.Received(1).RecordAcknowledgment(
            Arg.Is<PivotAcknowledgment>(a =>
                a.AgentId == "failing_agent" &&
                a.Status == "error" &&
                !a.Acknowledged));
    }

    private AgentPivotCoordinator CreateCoordinator()
    {
        return new AgentPivotCoordinator(
            _eventPublisher,
            _pivotOrchestrator,
            _options,
            NullLogger<AgentPivotCoordinator>.Instance);
    }

    private static DirectionChangeIntent CreateIntent()
    {
        return new DirectionChangeIntent
        {
            SessionId = "session1",
            MessageId = "msg1",
            IsDirectionChange = true,
            Confidence = 0.9,
            NewTopic = "新研究方向",
            PreserveAspects = new List<string> { "基础架构" },
            NeedsClarification = false
        };
    }

    private static PivotOperation CreateDagOperation(DirectionChangeIntent intent)
    {
        var op = PivotOperation.Create(intent);
        op.Complete(PivotStatus.Completed);
        return op;
    }

    private static IReadOnlyList<PivotAcknowledgment> CreateAcknowledgments(
        string pivotId,
        string sessionId,
        IEnumerable<string>? agentIds = null)
    {
        var agents = agentIds ?? AgentPivotCoordinator.StandardAgents;
        return agents.Select(a => new PivotAcknowledgment
        {
            SessionId = sessionId,
            PivotId = pivotId,
            AgentId = a,
            Status = "ready"
        }).ToList();
    }

    private static PivotEvent CreatePivotEvent(string sessionId)
    {
        return new PivotEvent
        {
            SessionId = sessionId,
            PivotId = $"pivot_{Guid.NewGuid():N}",
            OldDirectionSummary = "旧方向",
            NewDirection = "新方向"
        };
    }
}
