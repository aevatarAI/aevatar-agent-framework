using Aevatar.Agents.Abstractions;
using Aevatar.Agents.AI.Abstractions;
using Aevatar.Agents.AI.Tool.Abstractions;
using Aevatar.Agents.AI.Tool.Messages;
using Google.Protobuf;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.AI.Core;

internal interface IToolingLoopHost
{
    ILogger Logger { get; }
    IAevatarLLMProvider LLMProvider { get; }
    bool EnableChatHistoryInState { get; }

    ToolExecutionContext BuildToolExecutionContext(string sessionId, CancellationToken cancellationToken);

    Task<ToolExecutionResult> ExecuteAllowedToolWithHooksAsync(
        string toolName,
        Dictionary<string, object> args,
        ToolExecutionContext executionContext,
        AevatarLLMRequest llmRequest,
        CancellationToken cancellationToken);

    Task<ToolExecutionResult> ExecuteToolAsync(
        string toolName,
        Dictionary<string, object> parameters,
        ToolExecutionContext executionContext,
        CancellationToken cancellationToken);

    Task PublishAsync(IMessage message, EventDirection direction, CancellationToken ct);
    void AttachToolsToRequest(AevatarLLMRequest llmRequest);

    void TryApplyToolAllowlistFromSkillsLoadResult(
        AevatarLLMRequest llmRequest,
        string toolName,
        string? toolResultJson);

    bool IsToolAllowedByPolicy(ToolDefinition tool);
    string BuildToolPolicyDenyReason(ToolDefinition tool);
    void AddMessageToHistory(AevatarChatMessage message);

    Task<AevatarLLMResponse> GenerateLLMWithHooksAsync(
        ChatRequest request,
        AevatarLLMRequest llmRequest,
        CancellationToken cancellationToken);
}

