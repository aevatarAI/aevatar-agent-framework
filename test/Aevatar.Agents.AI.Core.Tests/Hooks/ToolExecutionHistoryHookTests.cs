using Aevatar.Agents.AI;
using Aevatar.Agents.AI.Core.Hooks;
using Aevatar.Agents.AI.Tool.Evolution;
using Aevatar.Agents.AI.Tool.Messages;
using FluentAssertions;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aevatar.Agents.AI.Core.Tests.Hooks;

public class ToolExecutionHistoryHookTests
{
    [Fact]
    public async Task AfterToolExecuteAsync_ShouldEmitFeedback()
    {
        ToolExecutionFeedback? captured = null;
        var hook = new ToolExecutionHistoryHook(
            new ToolEvolutionOptions
            {
                Enabled = true,
                EnableFeedbackHooks = true,
                EnableFeedbackEvents = true
            },
            publishEvent: (evt, _) =>
            {
                captured = evt;
                return Task.CompletedTask;
            },
            appendMemory: null,
            logger: NullLogger.Instance);

        var ctx = new AevatarAgentHookContext(
            agentId: "agent",
            agentType: "type",
            requestId: "req",
            policy: new AevatarAgentHookPolicy(false, false, 16_000, 64, 200_000))
        {
            ToolName = "echo",
            ToolCallId = "call",
            ToolResult = new ToolExecutionResult
            {
                ToolName = "echo",
                IsSuccess = true,
                Timestamp = Timestamp.FromDateTime(DateTime.UtcNow),
                Duration = Duration.FromTimeSpan(TimeSpan.FromMilliseconds(12))
            }
        };

        await hook.AfterToolExecuteAsync(ctx, CancellationToken.None);

        captured.Should().NotBeNull();
        captured!.ToolName.Should().Be("echo");
        captured.ToolCallId.Should().Be("call");
        captured.Success.Should().BeTrue();
        captured.DurationMs.Should().BeGreaterThan(0);
    }
}
