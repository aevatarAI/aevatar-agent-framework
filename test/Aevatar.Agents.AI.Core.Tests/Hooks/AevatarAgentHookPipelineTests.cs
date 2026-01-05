using Aevatar.Agents.AI.Core.Hooks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aevatar.Agents.AI.Core.Tests.Hooks;

public class AevatarAgentHookPipelineTests
{
    private static AevatarAgentHookContext NewContext()
        => new(
            agentId: "agent",
            agentType: "type",
            requestId: "req",
            policy: new AevatarAgentHookPolicy(
                AllowInternalTools: false,
                AllowDangerousTools: false,
                MaxToolOutputChars: 1000,
                ContextMessageWarn: 10,
                ContextCharsWarn: 1000));

    [Fact]
    public async Task Orders_ByPriorityThenName()
    {
        var calls = new List<string>();

        var h1 = new RecordingHook("b", priority: 10, calls);
        var h2 = new RecordingHook("a", priority: 10, calls);
        var h3 = new RecordingHook("z", priority: 0, calls);

        var pipeline = new AevatarAgentHookPipeline(
            hooks: new IAevatarAgentHook[] { h1, h2, h3 },
            options: new AevatarAgentHookOptions(),
            logger: NullLogger<AevatarAgentHookPipeline>.Instance);

        await pipeline.RunBeforeLLMRequestAsync(NewContext(), CancellationToken.None);

        calls.Should().Equal("z", "a", "b");
    }

    [Fact]
    public async Task DisabledHooks_AreSkipped_CaseInsensitive()
    {
        var calls = new List<string>();

        var h1 = new RecordingHook("Keep", priority: 0, calls);
        var h2 = new RecordingHook("SkipMe", priority: 0, calls);

        var options = new AevatarAgentHookOptions
        {
            DisabledHooks = new List<string> { "skipme" }
        };

        var pipeline = new AevatarAgentHookPipeline(
            hooks: new IAevatarAgentHook[] { h1, h2 },
            options: options,
            logger: NullLogger<AevatarAgentHookPipeline>.Instance);

        await pipeline.RunBeforeLLMRequestAsync(NewContext(), CancellationToken.None);

        calls.Should().Equal("Keep");
    }

    [Fact]
    public async Task HookException_IsIsolated_AndDoesNotBlockOthers()
    {
        var calls = new List<string>();

        var bad = new ThrowingHook("Bad", priority: 0);
        var good = new RecordingHook("Good", priority: 1, calls);

        var pipeline = new AevatarAgentHookPipeline(
            hooks: new IAevatarAgentHook[] { bad, good },
            options: new AevatarAgentHookOptions(),
            logger: NullLogger<AevatarAgentHookPipeline>.Instance);

        // Should not throw
        await pipeline.RunBeforeLLMRequestAsync(NewContext(), CancellationToken.None);

        calls.Should().Equal("Good");
    }

    private sealed class RecordingHook(string name, int priority, List<string> calls) : IAevatarAgentHook
    {
        public string Name => name;
        public int Priority => priority;

        public Task BeforeLLMRequestAsync(AevatarAgentHookContext context, CancellationToken cancellationToken)
        {
            calls.Add(name);
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingHook(string name, int priority) : IAevatarAgentHook
    {
        public string Name => name;
        public int Priority => priority;

        public Task BeforeLLMRequestAsync(AevatarAgentHookContext context, CancellationToken cancellationToken)
            => throw new InvalidOperationException("boom");
    }
}


