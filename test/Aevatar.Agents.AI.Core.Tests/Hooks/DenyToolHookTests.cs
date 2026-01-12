using Aevatar.Agents.AI.Core.Hooks;
using FluentAssertions;

namespace Aevatar.Agents.AI.Core.Tests.Hooks;

public class DenyToolHookTests
{
    [Fact]
    public void DenyTool_SetsTypedState_AndMetadataKeys()
    {
        var ctx = new AevatarAgentHookContext(
            agentId: "agent",
            agentType: "type",
            requestId: "req",
            policy: new AevatarAgentHookPolicy(
                AllowInternalTools: false,
                AllowDangerousTools: false,
                MaxToolOutputChars: 16_000,
                ContextMessageWarn: 64,
                ContextCharsWarn: 200_000));

        ctx.DenyTool("nope");

        ctx.TryGetToolDenyReason(out var reason).Should().BeTrue();
        reason.Should().Be("nope");
        ctx.Metadata.Should().ContainKey("deny_tool");
        ctx.Metadata["deny_tool"].Should().Be(true);
        ctx.Metadata.Should().ContainKey("deny_reason");
        ctx.Metadata["deny_reason"].Should().Be("nope");
    }

    [Fact]
    public void TryGetToolDenyReason_FallsBackToMetadata_ForBackwardCompatibility()
    {
        var ctx = new AevatarAgentHookContext(
            agentId: "agent",
            agentType: "type",
            requestId: "req",
            policy: new AevatarAgentHookPolicy(
                AllowInternalTools: false,
                AllowDangerousTools: false,
                MaxToolOutputChars: 16_000,
                ContextMessageWarn: 64,
                ContextCharsWarn: 200_000));

        // Old behavior: hooks may set metadata keys directly.
        ctx.Metadata["deny_tool"] = true;
        ctx.Metadata["deny_reason"] = "legacy";

        ctx.TryGetToolDenyReason(out var reason).Should().BeTrue();
        reason.Should().Be("legacy");
    }
}


