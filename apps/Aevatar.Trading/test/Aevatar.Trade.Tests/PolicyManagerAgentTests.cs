using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Runtime.Local;
using Aevatar.Trade;
using Aevatar.Trade.Agents.Policy;
using Google.Protobuf;
using Shouldly;

namespace Aevatar.Trade.Tests;

public class PolicyManagerAgentTests
{
    [Fact]
    public async Task UpdatePolicy_PublishesEvent_AndUpdatesState()
    {
        // Arrange
        var registry = new LocalMessageStreamRegistry();
        var agent = new PolicyManagerAgent();
        agent.Configure(new TradingPolicyConfig
        {
            Trading = new TradingConfig { MinConfidenceToTrade = 60 }
        });

        var actor = new LocalGAgentActor(agent, registry);
        await actor.ActivateAsync();

        TradingPolicyUpdatedEvent? published = null;
        var stream = registry.GetOrCreateStream(agent.Id);
        await stream.SubscribeAsync<EventEnvelope>(
            envelope =>
            {
                if (envelope.Payload.Is(TradingPolicyUpdatedEvent.Descriptor))
                {
                    published = envelope.Payload.Unpack<TradingPolicyUpdatedEvent>();
                }
                return Task.CompletedTask;
            },
            null,
            CancellationToken.None);

        var next = new TradingPolicyConfig
        {
            Trading = new TradingConfig { MinConfidenceToTrade = 75 }
        };

        // Act
        var evt = await agent.UpdatePolicyAsync(next, "tester", "unit-test");

        // Assert
        evt.Policy.Trading.MinConfidenceToTrade.ShouldBe(75);
        published.ShouldNotBeNull();
        published!.UpdatedBy.ShouldBe("tester");
        agent.GetPolicySnapshot().Trading.MinConfidenceToTrade.ShouldBe(75);
    }

    [Fact]
    public async Task UpdatePolicy_WithInvalidValues_ShouldThrow()
    {
        // Arrange
        var registry = new LocalMessageStreamRegistry();
        var agent = new PolicyManagerAgent();
        agent.Configure(new TradingPolicyConfig());

        var actor = new LocalGAgentActor(agent, registry);
        await actor.ActivateAsync();

        var invalid = new TradingPolicyConfig
        {
            Trading = new TradingConfig { MinConfidenceToTrade = 200 }
        };

        // Act / Assert
        await Should.ThrowAsync<ArgumentException>(() => agent.UpdatePolicyAsync(invalid, "tester", "invalid"));
    }
}
