using Aevatar.Agents.AI.Core.Hooks;
using Aevatar.Agents.AI.Core.Hooks.Policy;
using FluentAssertions;

namespace Aevatar.Agents.AI.Core.Tests.Hooks;

public class ToolDenyListHookTests
{
    [Fact(DisplayName = "ToolDenyListHook denies tool when tool name is in deny list")]
    public async Task BeforeToolExecuteAsync_ShouldDeny_WhenToolInList()
    {
        var hook = new ToolDenyListHook(new ToolDenyListHookOptions
        {
            DeniedTools = new List<string> { "bash", "file_write" },
            DenyReason = "blocked"
        });

        var ctx = new AevatarAgentHookContext(
            agentId: "agent",
            agentType: "type",
            requestId: "req",
            policy: new AevatarAgentHookPolicy(
                AllowInternalTools: false,
                AllowDangerousTools: false,
                MaxToolOutputChars: 16_000,
                ContextMessageWarn: 64,
                ContextCharsWarn: 200_000))
        {
            ToolName = "bash"
        };

        await hook.BeforeToolExecuteAsync(ctx, CancellationToken.None);

        ctx.TryGetToolDenyReason(out var reason).Should().BeTrue();
        reason.Should().Be("blocked");
    }

    [Fact(DisplayName = "ToolDenyListHook does not deny tool when tool name is not in deny list")]
    public async Task BeforeToolExecuteAsync_ShouldNotDeny_WhenToolNotInList()
    {
        var hook = new ToolDenyListHook(new ToolDenyListHookOptions
        {
            DeniedTools = new List<string> { "bash" }
        });

        var ctx = new AevatarAgentHookContext(
            agentId: "agent",
            agentType: "type",
            requestId: "req",
            policy: new AevatarAgentHookPolicy(
                AllowInternalTools: false,
                AllowDangerousTools: false,
                MaxToolOutputChars: 16_000,
                ContextMessageWarn: 64,
                ContextCharsWarn: 200_000))
        {
            ToolName = "file_read"
        };

        await hook.BeforeToolExecuteAsync(ctx, CancellationToken.None);

        ctx.TryGetToolDenyReason(out _).Should().BeFalse();
    }
}
