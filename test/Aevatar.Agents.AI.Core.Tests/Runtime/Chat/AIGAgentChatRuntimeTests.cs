using Aevatar.Agents.Abstractions;
using Aevatar.Agents.AI.Abstractions;
using Aevatar.Agents.AI.Core.Hooks;
using Aevatar.Agents.AI.Core.Messages;
using FluentAssertions;
using Google.Protobuf;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aevatar.Agents.AI.Core.Tests.Runtime.Chat;

public sealed class AIGAgentChatRuntimeTests
{
    [Fact(DisplayName = "ChatAsync publishes ChatResponseEvent and raises decision")]
    public async Task ChatAsync_ShouldPublishChatResponseEvent_AndRaiseDecision()
    {
        // Arrange
        var host = new Host
        {
            NextResponse = new AevatarLLMResponse
            {
                Content = "hello",
                Usage = new AevatarTokenUsage { PromptTokens = 1, CompletionTokens = 2, TotalTokens = 3 },
                AevatarStopReason = AevatarStopReason.Complete
            }
        };
        var rt = new AIGAgentChatRuntime(host);

        // Act
        var resp = await rt.ChatAsync(new ChatRequest { RequestId = "r1", Message = "hi" }, CancellationToken.None);

        // Assert
        resp.Content.Should().Be("hello");
        host.Published.OfType<ChatResponseEvent>().Should().ContainSingle(e => e.RequestId == "r1" && e.Content == "hello");
        host.Decisions.Should().ContainSingle(d => d.Prompt == "hi" && d.Response == "hello" && d.TokensUsed == 3);
    }

    [Fact(DisplayName = "ChatAsync executes tool loop when function call is present")]
    public async Task ChatAsync_ShouldExecuteToolLoop_WhenFunctionCallPresent()
    {
        // Arrange
        var host = new Host
        {
            NextResponse = new AevatarLLMResponse
            {
                AevatarFunctionCall = new AevatarFunctionCall { Name = "t1", Arguments = "{}", CallId = "c1" }
            }
        };
        host.ToolLoopFinal = new AevatarLLMResponse { Content = "final", AevatarStopReason = AevatarStopReason.Complete };

        var rt = new AIGAgentChatRuntime(host);

        // Act
        var resp = await rt.ChatAsync(new ChatRequest { RequestId = "r1", Message = "hi" }, CancellationToken.None);

        // Assert
        resp.Content.Should().Be("final");
        host.ExecuteToolLoopCalls.Should().Be(1);
    }

    [Fact(DisplayName = "ChatAsync appends user/assistant messages to state history when enabled")]
    public async Task ChatAsync_ShouldAppendHistory_WhenEnabled()
    {
        // Arrange
        var host = new Host
        {
            EnableChatHistoryInState = true,
            NextResponse = new AevatarLLMResponse { Content = "a", AevatarStopReason = AevatarStopReason.Complete }
        };
        var rt = new AIGAgentChatRuntime(host);

        // Act
        await rt.ChatAsync(new ChatRequest { RequestId = "r1", Message = "u" }, CancellationToken.None);

        // Assert
        host.History.Should().ContainInOrder(
            new AevatarChatMessage { Role = AevatarChatRole.User, Content = "u" },
            new AevatarChatMessage { Role = AevatarChatRole.Assistant, Content = "a" });
    }

    private sealed class Host : IAIGAgentChatRuntimeHost
    {
        public Microsoft.Extensions.Logging.ILogger Logger { get; } = NullLogger.Instance;
        public string AgentId { get; } = "agent";

        public bool EnableChatHistoryInState { get; set; }
        public bool AutoConfirmEvents { get; set; }
        public object? EventStore { get; set; }

        public int ExecuteToolLoopCalls { get; private set; }
        public List<IMessage> Published { get; } = new();
        public List<(string Prompt, string Response, int TokensUsed)> Decisions { get; } = new();
        public List<AevatarChatMessage> History { get; } = new();

        public AevatarLLMResponse NextResponse { get; set; } =
            new() { Content = "ok", AevatarStopReason = AevatarStopReason.Complete };

        public AevatarLLMResponse ToolLoopFinal { get; set; } =
            new() { Content = "tool-final", AevatarStopReason = AevatarStopReason.Complete };

        public void EnsureInitialized() { }

        public Task RunSessionStartHooksAsync(ChatRequest request, bool isStreaming, CancellationToken ct) => Task.CompletedTask;

        public Task RunStopHooksAsync(ChatRequest request, bool isStreaming, AevatarAgentHookStopStatus status,
            TimeSpan duration, Exception? exception) => Task.CompletedTask;

        public Task RunSessionEndHooksAsync(ChatRequest request, bool isStreaming, AevatarAgentHookStopStatus status,
            TimeSpan duration, Exception? exception) => Task.CompletedTask;

        public Task CompactChatHistoryIfNeededAsync(CancellationToken ct) => Task.CompletedTask;
        public Task InitializeToolsAsync(CancellationToken ct) => Task.CompletedTask;
        public Task TryReconnectMcpOnChatAsync(CancellationToken ct) => Task.CompletedTask;

        public AevatarLLMRequest BuildLLMRequest(ChatRequest request)
            => new() { Messages = new List<AevatarChatMessage> { new() { Role = AevatarChatRole.User, Content = request.Message } } };

        public Task<AevatarLLMResponse> GenerateLLMWithHooksAsync(ChatRequest request, AevatarLLMRequest llmRequest,
            CancellationToken ct) => Task.FromResult(NextResponse);

        public IAsyncEnumerable<AevatarLLMToken> GenerateLLMStreamWithHooksAsync(string requestId,
            AevatarLLMRequest llmRequest, CancellationToken ct) => AsyncEnumerable.Empty<AevatarLLMToken>();

        public Task<(AevatarLLMResponse FinalResponse, ToolCallInfo? ToolCall)> ExecuteToolCallLoopAsync(
            ChatRequest request, AevatarLLMRequest llmRequest, AevatarLLMResponse initialResponse, CancellationToken ct)
        {
            ExecuteToolLoopCalls++;
            return Task.FromResult((ToolLoopFinal, (ToolCallInfo?)null));
        }

        public void AddMessageToHistory(string content, AevatarChatRole role)
            => History.Add(new AevatarChatMessage { Role = role, Content = content });

        public void AddMessageToHistory(AevatarChatMessage message) => History.Add(message);

        public Task AppendChatMemoryAsync(AevatarChatRole role, string content, ChatRequest request, CancellationToken ct)
            => Task.CompletedTask;

        public Task PublishAsync(IMessage message, EventDirection direction, CancellationToken ct)
        {
            Published.Add(message);
            return Task.CompletedTask;
        }

        public (string Provider, string Model) GetProviderAndModelForTelemetry() => ("p", "m");

        public void RaiseAIDecision(string prompt, string response, int tokensUsed, Dictionary<string, string>? metadata)
            => Decisions.Add((prompt, response, tokensUsed));

        public Task ConfirmEventsAsync(CancellationToken ct) => Task.CompletedTask;

        public AIGAgentBase.StreamingToolCallMode GetStreamingToolCallMode()
            => AIGAgentBase.StreamingToolCallMode.EmitFinalAnswerAsSingleChunk;

        public void LogChatStreamTokenReadException(Exception exception, ChatRequest request) { }
    }
}

