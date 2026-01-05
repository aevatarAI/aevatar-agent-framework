using Aevatar.Agents.Abstractions.Helpers;
using Shouldly;
using Xunit;

namespace Aevatar.Agents.Abstractions.Tests;

public class AgentIdTests
{
    private sealed class FooAgent : IGAgent
    {
        public string Id => "FooAgent:any";
        public string GetAgentCategory() => nameof(FooAgent);
        public Task<string> GetDescriptionAsync() => Task.FromResult("foo");
        public Task<List<Type>> GetAllSubscribedEventsAsync(bool includeAllEventHandler = false) => Task.FromResult(new List<Type>());
        public Task ActivateAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task DeactivateAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task HandleEventAsync(EventEnvelope envelope, CancellationToken ct = default) => Task.CompletedTask;
    }

    [Fact]
    public void Normalize_ShouldPrependTypePrefix_WhenRawIdProvided()
    {
        var id = AgentId.Normalize(typeof(FooAgent), "raw-1");
        id.ShouldBe("FooAgent:raw-1");
    }

    [Fact]
    public void Normalize_ShouldReturnSame_WhenAlreadyNormalizedAndPrefixMatches()
    {
        var id = AgentId.Normalize(typeof(FooAgent), "FooAgent:raw-1");
        id.ShouldBe("FooAgent:raw-1");
    }

    [Fact]
    public void Normalize_ShouldThrow_WhenPrefixDoesNotMatch()
    {
        var ex = Should.Throw<ArgumentException>(() => AgentId.Normalize(typeof(FooAgent), "BarAgent:raw-1"));
        ex.Message.ShouldContain("does not match");
    }

    [Fact]
    public void TrySplit_AndExtractRawId_ShouldBehave()
    {
        AgentId.TrySplit("Foo:abc", out var t, out var raw).ShouldBeTrue();
        t.ShouldBe("Foo");
        raw.ShouldBe("abc");

        AgentId.TrySplit("invalid", out _, out _).ShouldBeFalse();

        AgentId.ExtractRawId("Foo:abc").ShouldBe("abc");
        AgentId.ExtractRawId("abc").ShouldBe("abc");
    }
}


