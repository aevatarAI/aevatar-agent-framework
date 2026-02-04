using Aevatar.Agents.Abstractions.Tracing;
using Aevatar.Agents.AI.Abstractions;
using Aevatar.Agents.AI.Core.Hooks;
using Aevatar.Agents.AI.Core.Hooks.BuiltIn;
using Aevatar.Agents.AI.Tool.Messages;
using Google.Protobuf.WellKnownTypes;
using Shouldly;

namespace Aevatar.Agents.AI.Core.Tests.Hooks;

public class ExecutionTraceProgressHookTests
{
    [Fact(DisplayName = "ExecutionTraceProgressHook publishes session lifecycle trace events")]
    public async Task SessionHooks_ShouldPublish_ExecutionTraceEvents()
    {
        var published = new List<ExecutionTraceEvent>();
        var hook = new ExecutionTraceProgressHook((evt, _) =>
        {
            published.Add(evt);
            return Task.CompletedTask;
        }, emitSessionLifecycle: true);

        var ctx = CreateContext();
        ctx.StopStatus = AevatarAgentHookStopStatus.Completed;
        ctx.Duration = TimeSpan.FromSeconds(2);

        await hook.OnSessionStartAsync(ctx, CancellationToken.None);
        await hook.OnStopAsync(ctx, CancellationToken.None);

        published.Count.ShouldBe(2);

        var start = published.Single(e => e.Phase == ExecutionTraceEventPhase.SessionStart);
        var stop = published.Single(e => e.Phase == ExecutionTraceEventPhase.SessionStop);

        start.Fields.ShouldContainKey(ExecutionTraceEventFields.Progress);
        stop.Fields.ShouldContainKey(ExecutionTraceEventFields.Progress);
        stop.Fields.ShouldContainKey(ExecutionTraceEventFields.DurationMs);

        start.Fields[ExecutionTraceEventFields.SessionId].StringValue.ShouldBe(ctx.RequestId);
        stop.Fields[ExecutionTraceEventFields.SessionId].StringValue.ShouldBe(ctx.RequestId);
        stop.Fields[ExecutionTraceEventFields.Status].StringValue.ShouldBe(ExecutionTraceEventStatus.Completed);
        stop.Fields[ExecutionTraceEventFields.Progress].DoubleValue.ShouldBe(1d);
    }

    [Fact(DisplayName = "ExecutionTraceProgressHook publishes LLM/tool/error trace events")]
    public async Task LlmAndToolHooks_ShouldPublish_ExecutionTraceEvents()
    {
        var published = new List<ExecutionTraceEvent>();
        var hook = new ExecutionTraceProgressHook((evt, _) =>
        {
            published.Add(evt);
            return Task.CompletedTask;
        });

        var ctx = CreateContext();
        ctx.LlmRequest = new AevatarLLMRequest
        {
            Settings = new AevatarLLMSettings { ModelId = "test-model" }
        };
        ctx.LlmResponse = new AevatarLLMResponse
        {
            Usage = new AevatarTokenUsage
            {
                PromptTokens = 3,
                CompletionTokens = 5,
                TotalTokens = 8
            }
        };
        ctx.ToolName = "calculator";
        ctx.ToolCallId = "call-1";
        ctx.ToolResult = new ToolExecutionResult
        {
            ToolName = "calculator",
            ToolCallId = "call-1",
            IsSuccess = false,
            ErrorMessage = "boom",
            Duration = Duration.FromTimeSpan(TimeSpan.FromMilliseconds(120))
        };

        await hook.BeforeLLMRequestAsync(ctx, CancellationToken.None);
        await hook.AfterLLMResponseAsync(ctx, CancellationToken.None);
        await hook.BeforeToolExecuteAsync(ctx, CancellationToken.None);
        await hook.AfterToolExecuteAsync(ctx, CancellationToken.None);
        await hook.OnErrorAsync(ctx, new InvalidOperationException("failed"), CancellationToken.None);

        published.Count.ShouldBe(5);
        published.ShouldContain(e => e.Phase == ExecutionTraceEventPhase.LlmRequest);
        published.ShouldContain(e => e.Phase == ExecutionTraceEventPhase.LlmResponse);
        published.ShouldContain(e => e.Phase == ExecutionTraceEventPhase.ToolStart);
        published.ShouldContain(e => e.Phase == ExecutionTraceEventPhase.ToolEnd);
        published.ShouldContain(e => e.Phase == ExecutionTraceEventPhase.Error);

        var llm = published.Single(e => e.Phase == ExecutionTraceEventPhase.LlmResponse);
        llm.Fields.ShouldContainKey(ExecutionTraceEventFields.PromptTokens);
        llm.Fields[ExecutionTraceEventFields.PromptTokens].IntValue.ShouldBe(3);
        llm.Fields[ExecutionTraceEventFields.CompletionTokens].IntValue.ShouldBe(5);
        llm.Fields[ExecutionTraceEventFields.TokensUsed].IntValue.ShouldBe(8);

        var toolEnd = published.Single(e => e.Phase == ExecutionTraceEventPhase.ToolEnd);
        toolEnd.Fields[ExecutionTraceEventFields.ToolName].StringValue.ShouldBe("calculator");
        toolEnd.Fields[ExecutionTraceEventFields.ToolCallId].StringValue.ShouldBe("call-1");
        toolEnd.Fields[ExecutionTraceEventFields.Error].StringValue.ShouldBe("boom");

        var error = published.Single(e => e.Phase == ExecutionTraceEventPhase.Error);
        error.Fields[ExecutionTraceEventFields.Error].StringValue.ShouldBe("failed");
    }

    private static AevatarAgentHookContext CreateContext()
    {
        return new AevatarAgentHookContext(
            agentId: "agent-1",
            agentType: "test-agent",
            requestId: "req-1",
            policy: new AevatarAgentHookPolicy(
                AllowInternalTools: true,
                AllowDangerousTools: false,
                MaxToolOutputChars: 1024,
                ContextMessageWarn: 10,
                ContextCharsWarn: 1000));
    }
}
