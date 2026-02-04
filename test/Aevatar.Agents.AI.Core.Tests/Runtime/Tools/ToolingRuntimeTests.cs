using Aevatar.Agents.Abstractions;
using Aevatar.Agents.AI.Abstractions;
using Aevatar.Agents.AI.Core.Utils;
using Aevatar.Agents.AI.Core.Tests.TestKit;
using Aevatar.Agents.AI.Tool.Abstractions;
using Aevatar.Agents.AI.Tool.Messages;
using FluentAssertions;
using Google.Protobuf;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aevatar.Agents.AI.Core.Tests.Runtime.Tools;

public sealed class ToolingRuntimeTests
{
    [Fact(DisplayName = "InitializeToolsAsync is idempotent and concurrency-safe")]
    public async Task InitializeToolsAsync_ShouldBeIdempotent_AndConcurrencySafe()
    {
        // Arrange
        var init = new InitHost();
        var loop = new LoopHost();
        var rt = new ToolingRuntime(init, loop);

        // Act
        var tasks = Enumerable.Range(0, 16).Select(_ => rt.InitializeToolsAsync()).ToArray();
        await Task.WhenAll(tasks);

        // Assert
        init.RegisterToolsCalls.Should().Be(1);
        init.CreateToolManagerCalls.Should().Be(1);
    }

    [Fact(DisplayName = "ExecuteToolCallLoopAsync executes tool and then calls LLM again")]
    public async Task ExecuteToolCallLoopAsync_ShouldExecuteTool_AndThenCallLlmAgain()
    {
        // Arrange
        var init = new InitHost();
        var loop = new LoopHost
        {
            NextLlmResponse = new AevatarLLMResponse { Content = "final", AevatarStopReason = AevatarStopReason.Complete }
        };
        var rt = new ToolingRuntime(init, loop);

        var req = new ChatRequest { RequestId = "r1", Message = "hi" };
        var llmReq = new AevatarLLMRequest { Messages = new List<AevatarChatMessage>() };
        var initial = new AevatarLLMResponse
        {
            AevatarFunctionCall = new AevatarFunctionCall
            {
                Name = "tool1",
                Arguments = """{"x":1}""",
                CallId = "c1"
            }
        };

        // Act
        var (final, toolCall) = await rt.ExecuteToolCallLoopAsync(req, llmReq, initial, CancellationToken.None);

        // Assert
        final.Content.Should().Be("final");
        toolCall.Should().NotBeNull();
        loop.ExecuteAllowedToolWithHooksCalls.Should().Be(1);
        loop.GenerateLlmCalls.Should().Be(1);
        llmReq.Messages.Should().HaveCount(2); // tool call + tool result
    }

    [Fact(DisplayName = "ExecuteAllowedToolAsync denies execution when allowlist is active")]
    public async Task ExecuteAllowedToolAsync_ShouldDeny_WhenAllowlistActive()
    {
        // Arrange
        var init = new InitHost();
        var loop = new LoopHost();
        var rt = new ToolingRuntime(init, loop);

        await rt.InitializeToolsAsync();
        await rt.RefreshToolCachesAsync();

        var llmReq = new AevatarLLMRequest { Context = new Dictionary<string, object>() };
        llmReq.Context[AIGAgentKeys.ToolAllowlist] = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "other" };

        // Act
        var result = await rt.ExecuteAllowedToolAsync(
            "tool1",
            new Dictionary<string, object> { ["x"] = 1 },
            new ToolExecutionContext { AgentId = "a", ToolManager = init.ToolManager },
            llmReq,
            CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("allowlist");
    }

    private sealed class InitHost : IToolingInitHost
    {
        public int CreateToolManagerCalls { get; private set; }
        public int RegisterToolsCalls { get; private set; }

        public RecordingToolManager ToolManager { get; } =
            new(new ToolDefinition { Name = "tool1", Description = "d", Category = ToolCategory.Core });

        public IAevatarToolManager CreateToolManager()
        {
            CreateToolManagerCalls++;
            return ToolManager;
        }

        public Task RegisterToolsAsync(CancellationToken cancellationToken)
        {
            RegisterToolsCalls++;
            return Task.CompletedTask;
        }
    }

    private sealed class LoopHost : IToolingLoopHost
    {
        public int ExecuteAllowedToolWithHooksCalls { get; private set; }
        public int GenerateLlmCalls { get; private set; }

        public AevatarLLMResponse NextLlmResponse { get; set; } =
            new() { Content = "ok", AevatarStopReason = AevatarStopReason.Complete };

        public Microsoft.Extensions.Logging.ILogger Logger { get; } = NullLogger.Instance;
        public IAevatarLLMProvider LLMProvider { get; } = new FakeProvider();
        public bool EnableChatHistoryInState => false;

        public ToolExecutionContext BuildToolExecutionContext(string sessionId, CancellationToken cancellationToken)
            => new() { AgentId = "a", ToolManager = new RecordingToolManager() };

        public Task<ToolExecutionResult> ExecuteAllowedToolWithHooksAsync(
            string toolName,
            Dictionary<string, object> args,
            ToolExecutionContext executionContext,
            AevatarLLMRequest llmRequest,
            CancellationToken cancellationToken)
        {
            ExecuteAllowedToolWithHooksCalls++;
            return Task.FromResult(new ToolExecutionResult
            {
                ToolName = toolName,
                IsSuccess = true,
                Content = """{"success":true}"""
            });
        }

        public Task<ToolExecutionResult> ExecuteToolAsync(string toolName, Dictionary<string, object> parameters,
            ToolExecutionContext executionContext, CancellationToken cancellationToken)
            => Task.FromResult(new ToolExecutionResult { ToolName = toolName, IsSuccess = true, Content = "{}" });

        public Task PublishAsync(IMessage message, EventDirection direction, CancellationToken ct) => Task.CompletedTask;
        public void AttachToolsToRequest(AevatarLLMRequest llmRequest) { }

        public void TryApplyToolAllowlistFromSkillsLoadResult(AevatarLLMRequest llmRequest, string toolName,
            string? toolResultJson) { }

        public bool IsToolAllowedByPolicy(ToolDefinition tool) => true;
        public string BuildToolPolicyDenyReason(ToolDefinition tool) => "denied";
        public void AddMessageToHistory(AevatarChatMessage message) { }

        public Task<AevatarLLMResponse> GenerateLLMWithHooksAsync(ChatRequest request, AevatarLLMRequest llmRequest,
            CancellationToken cancellationToken)
        {
            GenerateLlmCalls++;
            return Task.FromResult(NextLlmResponse);
        }
    }

    private sealed class FakeProvider : IAevatarLLMProvider
    {
        public Task<AevatarLLMResponse> GenerateAsync(AevatarLLMRequest request, CancellationToken cancellationToken)
            => Task.FromResult(new AevatarLLMResponse { Content = "forced", AevatarStopReason = AevatarStopReason.Complete });

        public IAsyncEnumerable<AevatarLLMToken> GenerateStreamAsync(AevatarLLMRequest request,
            CancellationToken cancellationToken)
            => AsyncEnumerable.Empty<AevatarLLMToken>();

        public Task<AevatarModelInfo> GetModelInfoAsync(CancellationToken cancellationToken)
            => Task.FromResult(new AevatarModelInfo { SupportsStreaming = true });
    }

}

