using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Aevatar.VibeResearching.Agents.Pivot;
using Aevatar.VibeResearching.Agents.Pivot.Messages;
using Aevatar.VibeResearching.Agents;
using Shouldly;

namespace VibeResearching.Api.Tests.Vibe.Pivot;

public sealed class PivotAwareAgentTests
{
    [Fact]
    public async Task HandlePivotAsync_ReturnsAcknowledgment()
    {
        // Arrange
        var agent = new TestPivotAgent("test_agent");
        var pivotEvent = CreatePivotEvent("session1", "pivot1");

        // Act
        var ack = await agent.HandlePivotAsync(pivotEvent);

        // Assert
        ack.ShouldNotBeNull();
        ack.SessionId.ShouldBe("session1");
        ack.PivotId.ShouldBe("pivot1");
        ack.AgentId.ShouldBe("test_agent");
        ack.Acknowledged.ShouldBeTrue();
        ack.Status.ShouldBe("ready");
    }

    [Fact]
    public async Task HandlePivotAsync_CallsOnPivot()
    {
        // Arrange
        var agent = new TestPivotAgent("test_agent");
        var pivotEvent = CreatePivotEvent("session1", "pivot1");

        // Act
        await agent.HandlePivotAsync(pivotEvent);

        // Assert
        agent.OnPivotCalled.ShouldBeTrue();
        agent.LastPivotEvent.ShouldBe(pivotEvent);
    }

    [Fact]
    public async Task HandlePivotAsync_SetsStateToReady()
    {
        // Arrange
        var agent = new TestPivotAgent("test_agent");
        var pivotEvent = CreatePivotEvent("session1", "pivot1");

        // Act
        await agent.HandlePivotAsync(pivotEvent);

        // Assert
        agent.CurrentState.ShouldBe(PivotAgentState.Ready);
    }

    [Fact]
    public async Task RequestCancellationAsync_WhenIdle_ReturnsTrue()
    {
        // Arrange
        var agent = new TestPivotAgent("test_agent");

        // Act
        var result = await agent.RequestCancellationAsync("test");

        // Assert
        result.ShouldBeTrue();
        agent.CurrentState.ShouldBe(PivotAgentState.Idle);
    }

    [Fact]
    public async Task RequestCancellationAsync_WhenWorking_SetsCancellingState()
    {
        // Arrange
        var agent = new TestPivotAgent("test_agent");
        agent.SimulateWorking();

        // Act
        var result = await agent.RequestCancellationAsync("pivot requested");

        // Assert
        result.ShouldBeTrue();
        agent.CurrentState.ShouldBe(PivotAgentState.Cancelling);
    }

    [Fact]
    public void BeginOperation_SetsWorkingState()
    {
        // Arrange
        var agent = new TestPivotAgent("test_agent");

        // Act
        agent.TestBeginOperation(CancellationToken.None);

        // Assert
        agent.CurrentState.ShouldBe(PivotAgentState.Working);
    }

    [Fact]
    public void EndOperation_SetsIdleState()
    {
        // Arrange
        var agent = new TestPivotAgent("test_agent");
        agent.TestBeginOperation(CancellationToken.None);

        // Act
        agent.TestEndOperation();

        // Assert
        agent.CurrentState.ShouldBe(PivotAgentState.Idle);
    }

    [Fact]
    public async Task IsPivotCancellationRequested_WhenCancelling_ReturnsTrue()
    {
        // Arrange
        var agent = new TestPivotAgent("test_agent");
        agent.SimulateWorking();
        await agent.RequestCancellationAsync("test");

        // Act
        var result = agent.TestIsPivotCancellationRequested();

        // Assert
        result.ShouldBeTrue();
    }

    [Fact]
    public void IsPivotCancellationRequested_WhenNotCancelling_ReturnsFalse()
    {
        // Arrange
        var agent = new TestPivotAgent("test_agent");

        // Act
        var result = agent.TestIsPivotCancellationRequested();

        // Assert
        result.ShouldBeFalse();
    }

    [Fact]
    public async Task HandlePivotAsync_WhenOnPivotThrows_ReturnsErrorStatus()
    {
        // Arrange
        var agent = new FailingPivotAgent("failing_agent");
        var pivotEvent = CreatePivotEvent("session1", "pivot1");

        // Act
        var ack = await agent.HandlePivotAsync(pivotEvent);

        // Assert
        ack.Acknowledged.ShouldBeFalse();
        ack.Status.ShouldBe("error");
        ack.Message.ShouldNotBeNull();
        ack.Message!.ShouldContain("Simulated failure");
    }

    private static PivotEvent CreatePivotEvent(string sessionId, string pivotId)
    {
        return new PivotEvent
        {
            SessionId = sessionId,
            PivotId = pivotId,
            OldDirectionSummary = "旧方向",
            NewDirection = "新方向"
        };
    }

    /// <summary>
    /// Test implementation of PivotAwareAgentBase.
    /// </summary>
    private sealed class TestPivotAgent : PivotAwareAgentBase
    {
        public override string AgentId { get; }
        public bool OnPivotCalled { get; private set; }
        public PivotEvent? LastPivotEvent { get; private set; }

        public TestPivotAgent(string agentId) : base(NullLogger<TestPivotAgent>.Instance)
        {
            AgentId = agentId;
        }

        protected override Task OnPivotAsync(PivotEvent pivotEvent, CancellationToken cancellationToken)
        {
            OnPivotCalled = true;
            LastPivotEvent = pivotEvent;
            return Task.CompletedTask;
        }

        public void SimulateWorking()
        {
            TestBeginOperation(CancellationToken.None);
        }

        public CancellationToken TestBeginOperation(CancellationToken parentToken)
        {
            return BeginOperation(parentToken);
        }

        public void TestEndOperation()
        {
            EndOperation();
        }

        public bool TestIsPivotCancellationRequested()
        {
            return IsPivotCancellationRequested();
        }
    }

    /// <summary>
    /// Test agent that fails during pivot handling.
    /// </summary>
    private sealed class FailingPivotAgent : PivotAwareAgentBase
    {
        public override string AgentId { get; }

        public FailingPivotAgent(string agentId) : base(NullLogger<FailingPivotAgent>.Instance)
        {
            AgentId = agentId;
        }

        protected override Task OnPivotAsync(PivotEvent pivotEvent, CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("Simulated failure");
        }
    }
}
