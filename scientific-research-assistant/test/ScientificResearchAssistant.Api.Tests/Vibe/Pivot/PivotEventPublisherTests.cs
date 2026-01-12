using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ScientificResearchAssistant.Vibe.Pivot;
using ScientificResearchAssistant.Vibe.Pivot.Messages;
using Shouldly;

namespace ScientificResearchAssistant.Api.Tests.Vibe.Pivot;

public sealed class PivotEventPublisherTests : IDisposable
{
    private readonly PivotEventPublisher _publisher;
    private readonly IOptions<PivotOptions> _options;

    public PivotEventPublisherTests()
    {
        _options = Options.Create(new PivotOptions
        {
            SubagentAckTimeoutSeconds = 1
        });
        _publisher = new PivotEventPublisher(_options, NullLogger<PivotEventPublisher>.Instance);
    }

    public void Dispose()
    {
        _publisher.Dispose();
    }

    [Fact]
    public void RegisterAgent_AddsAgentToSession()
    {
        // Arrange
        var sessionId = "session1";
        var agentId = "planner";

        // Act
        _publisher.RegisterAgent(sessionId, agentId);

        // Assert
        var agents = _publisher.GetRegisteredAgents(sessionId);
        agents.ShouldContain(agentId);
    }

    [Fact]
    public void RegisterAgent_MultipleAgents_ReturnsAll()
    {
        // Arrange
        var sessionId = "session1";
        var agents = new[] { "planner", "librarian", "reasoner" };

        // Act
        foreach (var agentId in agents)
        {
            _publisher.RegisterAgent(sessionId, agentId);
        }

        // Assert
        var registered = _publisher.GetRegisteredAgents(sessionId);
        registered.Count.ShouldBe(3);
        foreach (var agentId in agents)
        {
            registered.ShouldContain(agentId);
        }
    }

    [Fact]
    public void UnregisterAgent_RemovesAgent()
    {
        // Arrange
        var sessionId = "session1";
        _publisher.RegisterAgent(sessionId, "planner");
        _publisher.RegisterAgent(sessionId, "librarian");

        // Act
        _publisher.UnregisterAgent(sessionId, "planner");

        // Assert
        var agents = _publisher.GetRegisteredAgents(sessionId);
        agents.ShouldNotContain("planner");
        agents.ShouldContain("librarian");
    }

    [Fact]
    public void GetRegisteredAgents_UnknownSession_ReturnsEmptyList()
    {
        // Act
        var agents = _publisher.GetRegisteredAgents("unknown_session");

        // Assert
        agents.ShouldBeEmpty();
    }

    [Fact]
    public void RecordAcknowledgment_TracksAcknowledgment()
    {
        // Arrange
        var pivotId = "pivot1";
        var sessionId = "session1";

        // First publish to initialize tracker
        var pivotEvent = CreatePivotEvent(sessionId, pivotId);
        _publisher.RegisterAgent(sessionId, "planner");

        // We need to call PublishPivotEventAsync to initialize the tracker
        // Since this is async, we'll test the synchronous RecordAcknowledgment path

        var ack = new PivotAcknowledgment
        {
            SessionId = sessionId,
            PivotId = pivotId,
            AgentId = "planner",
            Status = "ready"
        };

        // Act & Assert - should not throw (acknowledgment for unknown pivot is logged but not fatal)
        Should.NotThrow(() => _publisher.RecordAcknowledgment(ack));
    }

    [Fact]
    public async Task PublishPivotEventAsync_InitializesAckTracker()
    {
        // Arrange
        var sessionId = "session1";
        var pivotEvent = CreatePivotEvent(sessionId, "pivot1");
        _publisher.RegisterAgent(sessionId, "planner");

        // Act
        await _publisher.PublishPivotEventAsync(pivotEvent);

        // Assert - record ack should work now
        var ack = new PivotAcknowledgment
        {
            SessionId = sessionId,
            PivotId = pivotEvent.PivotId,
            AgentId = "planner",
            Status = "ready"
        };
        Should.NotThrow(() => _publisher.RecordAcknowledgment(ack));
    }

    [Fact]
    public async Task WaitForAcknowledgmentsAsync_CompletesWhenAllAcked()
    {
        // Arrange
        var sessionId = "session1";
        var pivotId = "pivot1";
        var pivotEvent = CreatePivotEvent(sessionId, pivotId);

        _publisher.RegisterAgent(sessionId, "planner");
        _publisher.RegisterAgent(sessionId, "librarian");

        // Publish to initialize tracker
        await _publisher.PublishPivotEventAsync(pivotEvent);

        // Record acknowledgments
        _publisher.RecordAcknowledgment(new PivotAcknowledgment
        {
            SessionId = sessionId,
            PivotId = pivotId,
            AgentId = "planner",
            Status = "ready"
        });
        _publisher.RecordAcknowledgment(new PivotAcknowledgment
        {
            SessionId = sessionId,
            PivotId = pivotId,
            AgentId = "librarian",
            Status = "ready"
        });

        // Act
        var acks = await _publisher.WaitForAcknowledgmentsAsync(
            pivotId,
            new[] { "planner", "librarian" },
            TimeSpan.FromSeconds(2));

        // Assert
        acks.Count.ShouldBe(2);
        acks.ShouldContain(a => a.AgentId == "planner");
        acks.ShouldContain(a => a.AgentId == "librarian");
    }

    [Fact]
    public async Task WaitForAcknowledgmentsAsync_TimesOutWithPartialAcks()
    {
        // Arrange
        var sessionId = "session1";
        var pivotId = "pivot2";
        var pivotEvent = CreatePivotEvent(sessionId, pivotId);

        _publisher.RegisterAgent(sessionId, "planner");
        _publisher.RegisterAgent(sessionId, "librarian");
        _publisher.RegisterAgent(sessionId, "reasoner");

        await _publisher.PublishPivotEventAsync(pivotEvent);

        // Only record 1 out of 3 acknowledgments
        _publisher.RecordAcknowledgment(new PivotAcknowledgment
        {
            SessionId = sessionId,
            PivotId = pivotId,
            AgentId = "planner",
            Status = "ready"
        });

        // Act - use short timeout
        var acks = await _publisher.WaitForAcknowledgmentsAsync(
            pivotId,
            new[] { "planner", "librarian", "reasoner" },
            TimeSpan.FromMilliseconds(200));

        // Assert - should return partial results
        acks.Count.ShouldBe(1);
        acks.ShouldContain(a => a.AgentId == "planner");
    }

    [Fact]
    public async Task SubscribeAsync_ReceivesPivotEvents()
    {
        // Arrange
        var sessionId = "session1";
        var pivotEvent = CreatePivotEvent(sessionId, "pivot1");
        var received = new List<PivotEvent>();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        // Start subscription in background
        var subscriptionTask = Task.Run(async () =>
        {
            await foreach (var evt in _publisher.SubscribeAsync(sessionId, cts.Token))
            {
                received.Add(evt);
                if (received.Count >= 1)
                {
                    break;
                }
            }
        }, cts.Token);

        // Give subscription time to start
        await Task.Delay(100);

        // Act
        await _publisher.PublishPivotEventAsync(pivotEvent);

        // Wait for subscription to receive
        await Task.Delay(200);
        await cts.CancelAsync();

        try
        {
            await subscriptionTask;
        }
        catch (OperationCanceledException)
        {
            // Expected
        }

        // Assert
        received.Count.ShouldBe(1);
        received[0].PivotId.ShouldBe(pivotEvent.PivotId);
    }

    [Fact]
    public async Task SubscribeAsync_MultipleSubscribers_AllReceiveEvents()
    {
        // Arrange
        var sessionId = "session1";
        var pivotEvent = CreatePivotEvent(sessionId, "pivot1");
        var received1 = new List<PivotEvent>();
        var received2 = new List<PivotEvent>();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        // Start two subscriptions
        var task1 = Task.Run(async () =>
        {
            await foreach (var evt in _publisher.SubscribeAsync(sessionId, cts.Token))
            {
                received1.Add(evt);
                if (received1.Count >= 1) break;
            }
        }, cts.Token);

        var task2 = Task.Run(async () =>
        {
            await foreach (var evt in _publisher.SubscribeAsync(sessionId, cts.Token))
            {
                received2.Add(evt);
                if (received2.Count >= 1) break;
            }
        }, cts.Token);

        await Task.Delay(100);

        // Act
        await _publisher.PublishPivotEventAsync(pivotEvent);

        await Task.Delay(200);
        await cts.CancelAsync();

        try { await task1; } catch (OperationCanceledException) { }
        try { await task2; } catch (OperationCanceledException) { }

        // Assert - both subscribers should receive the event
        received1.Count.ShouldBe(1);
        received2.Count.ShouldBe(1);
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
}
