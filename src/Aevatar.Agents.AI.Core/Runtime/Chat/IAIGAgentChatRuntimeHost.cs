using System.Runtime.CompilerServices;
using Aevatar.Agents.AI.Abstractions;
using Aevatar.Agents.AI.Core.Hooks;
using Aevatar.Agents.AI.Core.Messages;
using Google.Protobuf;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.AI.Core;

internal interface IAIGAgentChatRuntimeHost
{
    ILogger Logger { get; }
    string AgentId { get; }

    bool EnableChatHistoryInState { get; }
    bool AutoConfirmEvents { get; }
    object? EventStore { get; }

    void EnsureInitialized();

    Task RunSessionStartHooksAsync(ChatRequest request, bool isStreaming, CancellationToken ct);
    Task RunStopHooksAsync(ChatRequest request, bool isStreaming, AevatarAgentHookStopStatus status, TimeSpan duration,
        Exception? exception);
    Task RunSessionEndHooksAsync(ChatRequest request, bool isStreaming, AevatarAgentHookStopStatus status,
        TimeSpan duration, Exception? exception);

    Task CompactChatHistoryIfNeededAsync(CancellationToken ct);
    Task InitializeToolsAsync(CancellationToken ct);
    Task TryReconnectMcpOnChatAsync(CancellationToken ct);

    AevatarLLMRequest BuildLLMRequest(ChatRequest request);
    Task<AevatarLLMResponse> GenerateLLMWithHooksAsync(ChatRequest request, AevatarLLMRequest llmRequest,
        CancellationToken ct);

    IAsyncEnumerable<AevatarLLMToken> GenerateLLMStreamWithHooksAsync(string requestId, AevatarLLMRequest llmRequest,
        CancellationToken ct);

    Task<(AevatarLLMResponse FinalResponse, ToolCallInfo? ToolCall)> ExecuteToolCallLoopAsync(
        ChatRequest request,
        AevatarLLMRequest llmRequest,
        AevatarLLMResponse initialResponse,
        CancellationToken ct);

    void AddMessageToHistory(string content, AevatarChatRole role);
    void AddMessageToHistory(AevatarChatMessage message);

    Task AppendChatMemoryAsync(AevatarChatRole role, string content, ChatRequest request, CancellationToken ct);

    Task PublishAsync(IMessage message, EventDirection direction, CancellationToken ct);

    (string Provider, string Model) GetProviderAndModelForTelemetry();

    void RaiseAIDecision(
        string prompt,
        string response,
        int tokensUsed,
        Dictionary<string, string>? metadata);

    Task ConfirmEventsAsync(CancellationToken ct);

    AIGAgentBase.StreamingToolCallMode GetStreamingToolCallMode();
    void LogChatStreamTokenReadException(Exception exception, ChatRequest request);
}

