using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Runtime.Local;
using Aevatar.Trade;
using Aevatar.Trade.Agents.Triggers;
using Google.Protobuf;
using Shouldly;

namespace Aevatar.Trade.Tests;

public class DecisionTriggerAgentTests
{
    [Fact]
    public async Task EmitsDecisionTrigger_WhenThresholdReached_AndRespectsCooldown()
    {
        // Arrange
        var registry = new LocalMessageStreamRegistry();
        var agent = new DecisionTriggerAgent();
        agent.Configure(new DecisionTriggerConfig
        {
            PriceChangePct = 1,
            WindowSeconds = 60,
            CooldownSeconds = 1
        });

        var actor = new LocalGAgentActor(agent, registry);
        await actor.ActivateAsync();

        var received = new List<DecisionTriggerEvent>();
        var stream = registry.GetOrCreateStream(agent.Id);
        await stream.SubscribeAsync<EventEnvelope>(
            envelope =>
            {
                if (envelope.Payload.Is(DecisionTriggerEvent.Descriptor))
                {
                    received.Add(envelope.Payload.Unpack<DecisionTriggerEvent>());
                }
                return Task.CompletedTask;
            },
            null,
            CancellationToken.None);

        // Act: base price
        await actor.PublishEventAsync(new MarketTickEvent { Symbol = "BTCUSDT", Price = 100 }, EventDirection.Down);
        await Task.Delay(50);

        // Trigger
        await actor.PublishEventAsync(new MarketTickEvent { Symbol = "BTCUSDT", Price = 102 }, EventDirection.Down);
        await Task.Delay(100);

        // Cooldown should block
        await actor.PublishEventAsync(new MarketTickEvent { Symbol = "BTCUSDT", Price = 104 }, EventDirection.Down);
        await Task.Delay(100);

        // Wait cooldown then trigger again
        await Task.Delay(1100);
        await actor.PublishEventAsync(new MarketTickEvent { Symbol = "BTCUSDT", Price = 106 }, EventDirection.Down);
        await Task.Delay(100);

        // Assert
        received.Count.ShouldBe(2);
        received[0].Symbol.ShouldBe("BTCUSDT");
        received[1].Symbol.ShouldBe("BTCUSDT");
    }

    [Fact]
    public async Task UpdatesThreshold_FromPolicyUpdate()
    {
        // Arrange
        var registry = new LocalMessageStreamRegistry();
        var agent = new DecisionTriggerAgent();
        agent.Configure(new DecisionTriggerConfig
        {
            PriceChangePct = 10,
            WindowSeconds = 60,
            CooldownSeconds = 0
        });

        var actor = new LocalGAgentActor(agent, registry);
        await actor.ActivateAsync();

        var received = new List<DecisionTriggerEvent>();
        var stream = registry.GetOrCreateStream(agent.Id);
        await stream.SubscribeAsync<EventEnvelope>(
            envelope =>
            {
                if (envelope.Payload.Is(DecisionTriggerEvent.Descriptor))
                {
                    received.Add(envelope.Payload.Unpack<DecisionTriggerEvent>());
                }
                return Task.CompletedTask;
            },
            null,
            CancellationToken.None);

        // Base price
        await actor.PublishEventAsync(new MarketTickEvent { Symbol = "BTCUSDT", Price = 100 }, EventDirection.Down);
        await Task.Delay(50);

        // Not enough to trigger (10%)
        await actor.PublishEventAsync(new MarketTickEvent { Symbol = "BTCUSDT", Price = 102 }, EventDirection.Down);
        await Task.Delay(100);
        received.Count.ShouldBe(0);

        // Apply policy update (1%)
        await actor.PublishEventAsync(new TradingPolicyUpdatedEvent
        {
            UpdatedBy = "test",
            Policy = new TradingPolicyConfig
            {
                Trigger = new DecisionTriggerConfig
                {
                    PriceChangePct = 1,
                    WindowSeconds = 60,
                    CooldownSeconds = 0
                }
            }
        }, EventDirection.Down);

        await Task.Delay(50);

        // Should trigger now
        await actor.PublishEventAsync(new MarketTickEvent { Symbol = "BTCUSDT", Price = 102 }, EventDirection.Down);
        await Task.Delay(100);

        received.Count.ShouldBe(1);
        received[0].DeltaPct.ShouldBeGreaterThan(0);
    }
}
