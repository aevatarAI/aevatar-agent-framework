using Aevatar.Agents.AI.Abstractions;
using Aevatar.Agents.AI.Core.Utils;
using FluentAssertions;

namespace Aevatar.Agents.AI.Core.Tests.Runtime.Llm;

public sealed class LlmRequestRuntimeTests
{
    [Fact(DisplayName = "BuildRequest includes history messages when history is enabled")]
    public void BuildRequest_ShouldIncludeHistory_WhenEnabled()
    {
        // Arrange
        var host = new Host
        {
            EnableChatHistoryInState = true,
            History =
            [
                new AevatarChatMessage { Role = AevatarChatRole.User, Content = "u1" },
                new AevatarChatMessage { Role = AevatarChatRole.Assistant, Content = "a1" }
            ],
            SystemPrompt = "sys"
        };

        // Act
        var rt = new LlmRequestRuntime(host);
        var req = rt.BuildRequest(new ChatRequest { RequestId = "r1", Message = "u2" });

        // Assert
        req.SystemPrompt.Should().Be("sys");
        req.Messages.Should().HaveCount(3);
        req.Messages[0].Content.Should().Be("u1");
        req.Messages[1].Content.Should().Be("a1");
        req.Messages[2].Content.Should().Be("u2");
    }

    [Fact(DisplayName = "BuildRequest applies fixed tool allowlist to request context")]
    public void BuildRequest_ShouldApplyFixedToolAllowlist_ToContext()
    {
        // Arrange
        var host = new Host
        {
            EnableChatHistoryInState = false,
            FixedToolAllowlist = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "t1", "t2" },
            SystemPrompt = "sys"
        };

        // Act
        var rt = new LlmRequestRuntime(host);
        var req = rt.BuildRequest(new ChatRequest { RequestId = "r1", Message = "hello" });

        // Assert
        req.Context.Should().NotBeNull();
        var ctx = req.Context!;
        ctx.Should().ContainKey(AIGAgentKeys.ToolAllowlist);
        ctx[AIGAgentKeys.ToolAllowlist].Should().BeAssignableTo<IReadOnlyCollection<string>>();
    }

    [Fact(DisplayName = "BuildRequest uses stop sequences override from chat request")]
    public void BuildRequest_ShouldAllowStopSequencesOverride()
    {
        // Arrange
        var host = new Host { SystemPrompt = "sys" };
        var rt = new LlmRequestRuntime(host);

        // Act
        var req = rt.BuildRequest(new ChatRequest
        {
            RequestId = "r1",
            Message = "hello",
            StopSequences = { "###", "END" }
        });

        // Assert
        req.Settings.StopSequences.Should().BeEquivalentTo(new[] { "###", "END" });
    }

    private sealed class Host : ILlmRequestHost
    {
        public bool EnableChatHistoryInState { get; set; }
        public IReadOnlyCollection<string>? FixedToolAllowlist { get; set; }
        public string SystemPrompt { get; set; } = string.Empty;
        public List<AevatarChatMessage> History { get; set; } = new();
        public bool AttachToolsCalled { get; private set; }

        public AevatarLLMSettings GetLLMSettings(ChatRequest request) => new();

        public string BuildEffectiveSystemPromptWithSummary() => SystemPrompt;

        public IReadOnlyList<AevatarChatMessage> SnapshotChatHistoryMessages() => History;

        public void AttachToolsToRequest(AevatarLLMRequest llmRequest) => AttachToolsCalled = true;
    }
}

