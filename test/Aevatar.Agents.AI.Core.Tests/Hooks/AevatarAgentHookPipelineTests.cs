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

    [Fact]
    public async Task DuplicateHookName_LastWins()
    {
        var calls = new List<string>();

        var first = new RecordingHook("Dup", priority: 0, calls, marker: "first");
        var last = new RecordingHook("Dup", priority: 0, calls, marker: "last");

        var pipeline = new AevatarAgentHookPipeline(
            hooks: new IAevatarAgentHook[] { first, last },
            options: new AevatarAgentHookOptions(),
            logger: NullLogger<AevatarAgentHookPipeline>.Instance);

        await pipeline.RunBeforeLLMRequestAsync(NewContext(), CancellationToken.None);

        calls.Should().Equal("last");
    }

    [Fact]
    public async Task DisabledHooks_ByTypeName_AreSkipped()
    {
        var calls = new List<string>();

        var hook = new AliasNameHook(calls); // Name != type name
        var options = new AevatarAgentHookOptions
        {
            DisabledHooks = new List<string> { nameof(AliasNameHook) } // disable by type name
        };

        var pipeline = new AevatarAgentHookPipeline(
            hooks: new IAevatarAgentHook[] { hook },
            options: options,
            logger: NullLogger<AevatarAgentHookPipeline>.Instance);

        await pipeline.RunBeforeLLMRequestAsync(NewContext(), CancellationToken.None);

        calls.Should().BeEmpty();
    }

    [Fact]
    public void CreatePolicySnapshot_ShouldClampBudgets()
    {
        var pipeline = new AevatarAgentHookPipeline(
            hooks: Array.Empty<IAevatarAgentHook>(),
            options: new AevatarAgentHookOptions
            {
                MaxToolOutputChars = -1,
                ContextMessageWarn = -1,
                ContextCharsWarn = -1
            },
            logger: NullLogger<AevatarAgentHookPipeline>.Instance);

        var policy = pipeline.CreatePolicySnapshot(allowInternalTools: true, allowDangerousTools: true);
        policy.AllowInternalTools.Should().BeTrue();
        policy.AllowDangerousTools.Should().BeTrue();

        // clamp ranges in pipeline:
        // MaxToolOutputChars: [1000, 512000], ContextMessageWarn: [1, 10000], ContextCharsWarn: [1000, 5000000]
        policy.MaxToolOutputChars.Should().Be(1000);
        policy.ContextMessageWarn.Should().Be(1);
        policy.ContextCharsWarn.Should().Be(1000);
    }

    [Fact]
    public async Task Cancellation_ShouldStopPipeline_AndBubbleOperationCanceled()
    {
        var calls = new List<string>();
        var hook = new RecordingHook("h", priority: 0, calls);
        var pipeline = new AevatarAgentHookPipeline(
            hooks: new IAevatarAgentHook[] { hook },
            options: new AevatarAgentHookOptions(),
            logger: NullLogger<AevatarAgentHookPipeline>.Instance);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await pipeline.RunBeforeLLMRequestAsync(NewContext(), cts.Token));
    }

    private sealed class RecordingHook(string name, int priority, List<string> calls) : IAevatarAgentHook
    {
        private readonly string _marker = name;

        public RecordingHook(string name, int priority, List<string> calls, string marker) : this(name, priority, calls)
        {
            _marker = marker;
        }

        public string Name => name;
        public int Priority => priority;

        public Task BeforeLLMRequestAsync(AevatarAgentHookContext context, CancellationToken cancellationToken)
        {
            calls.Add(_marker);
            return Task.CompletedTask;
        }
    }

    private sealed class AliasNameHook(List<string> calls) : IAevatarAgentHook
    {
        public string Name => "alias";

        public Task BeforeLLMRequestAsync(AevatarAgentHookContext context, CancellationToken cancellationToken)
        {
            calls.Add("ran");
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


