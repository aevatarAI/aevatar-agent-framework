using Aevatar.Agents.AI.Abstractions;
using Aevatar.Agents.AI.Core.Hooks;
using Aevatar.Agents.AI.Core.Hooks.BuiltIn;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aevatar.Agents.AI.Core.Tests.Hooks;

public class ContextBudgetMonitorHookTests
{
    [Fact]
    public async Task SetsWarningMetadata_WhenThresholdExceeded()
    {
        var hook = new ContextBudgetMonitorHook(NullLogger.Instance);

        var llmRequest = new AevatarLLMRequest
        {
            SystemPrompt = new string('s', 200),
            Messages =
            {
                new AevatarChatMessage { Role = AevatarChatRole.User, Content = new string('u', 200) }
            }
        };

        var ctx = new AevatarAgentHookContext(
            agentId: "agent",
            agentType: "type",
            requestId: "req",
            policy: new AevatarAgentHookPolicy(
                AllowInternalTools: false,
                AllowDangerousTools: false,
                MaxToolOutputChars: 1000,
                ContextMessageWarn: 1,
                ContextCharsWarn: 100))
        {
            LlmRequest = llmRequest
        };

        await hook.BeforeLLMRequestAsync(ctx, CancellationToken.None);

        ctx.Metadata.Should().ContainKey("context_budget_warning");
        ctx.Metadata["context_budget_warning"].Should().Be(true);
        ctx.Metadata.Should().ContainKey("context_budget_total_chars");
    }

    [Fact]
    public async Task NoOp_WhenBelowThreshold()
    {
        var hook = new ContextBudgetMonitorHook(NullLogger.Instance);

        var llmRequest = new AevatarLLMRequest
        {
            SystemPrompt = "ok",
            Messages =
            {
                new AevatarChatMessage { Role = AevatarChatRole.User, Content = "hi" }
            }
        };

        var ctx = new AevatarAgentHookContext(
            agentId: "agent",
            agentType: "type",
            requestId: "req",
            policy: new AevatarAgentHookPolicy(
                AllowInternalTools: false,
                AllowDangerousTools: false,
                MaxToolOutputChars: 1000,
                ContextMessageWarn: 10,
                ContextCharsWarn: 10_000))
        {
            LlmRequest = llmRequest
        };

        await hook.BeforeLLMRequestAsync(ctx, CancellationToken.None);

        ctx.Metadata.Should().NotContainKey("context_budget_warning");
    }
}


