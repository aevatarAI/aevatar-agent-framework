using Aevatar.Agents.AI.Core.Hooks;
using Aevatar.Agents.AI.Core.Hooks.BuiltIn;
using Aevatar.Agents.AI.Tool.Messages;
using FluentAssertions;

namespace Aevatar.Agents.AI.Core.Tests.Hooks;

public class ToolOutputTruncationHookTests
{
    [Fact]
    public async Task Truncates_WhenContentExceedsPolicyMax()
    {
        var hook = new ToolOutputTruncationHook();

        var ctx = new AevatarAgentHookContext(
            agentId: "agent",
            agentType: "type",
            requestId: "req",
            policy: new AevatarAgentHookPolicy(
                AllowInternalTools: false,
                AllowDangerousTools: false,
                MaxToolOutputChars: 200,
                ContextMessageWarn: 10,
                ContextCharsWarn: 1000))
        {
            ToolName = "dummy_tool",
            ToolResult = new ToolExecutionResult
            {
                ToolName = "dummy_tool",
                IsSuccess = true,
                Content = new string('a', 1000)
            }
        };

        await hook.AfterToolExecuteAsync(ctx, CancellationToken.None);

        ctx.ToolResult!.Content.Should().NotBeNullOrEmpty();
        ctx.ToolResult!.Content.Length.Should().BeLessThanOrEqualTo(200);
        ctx.Metadata.Should().ContainKey("tool_output_truncated");
        ctx.Metadata["tool_output_truncated"].Should().Be(true);
    }

    [Fact]
    public async Task NoOp_WhenContentWithinLimit()
    {
        var hook = new ToolOutputTruncationHook();

        var original = "hello";
        var ctx = new AevatarAgentHookContext(
            agentId: "agent",
            agentType: "type",
            requestId: "req",
            policy: new AevatarAgentHookPolicy(
                AllowInternalTools: false,
                AllowDangerousTools: false,
                MaxToolOutputChars: 200,
                ContextMessageWarn: 10,
                ContextCharsWarn: 1000))
        {
            ToolResult = new ToolExecutionResult
            {
                ToolName = "dummy_tool",
                IsSuccess = true,
                Content = original
            }
        };

        await hook.AfterToolExecuteAsync(ctx, CancellationToken.None);

        ctx.ToolResult!.Content.Should().Be(original);
        ctx.Metadata.Should().NotContainKey("tool_output_truncated");
    }
}


