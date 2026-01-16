using Aevatar.Agents.Abstractions.Attributes;
using Aevatar.Agents.AI.Abstractions;
using Aevatar.Agents.AI.Tool.Messages;
using Google.Protobuf.WellKnownTypes;

namespace Aevatar.Agents.AI.Core;

public abstract partial class AIGAgentBase
{
    private async Task<(AevatarLLMResponse FinalResponse, ToolCallInfo? ToolCall)> ExecuteToolCallLoopAsync(
        ChatRequest request,
        AevatarLLMRequest llmRequest,
        AevatarLLMResponse initialResponse,
        CancellationToken cancellationToken)
    {
        return await Tooling.ExecuteToolCallLoopAsync(request, llmRequest, initialResponse, cancellationToken);
    }

    [EventHandler]
    protected virtual async Task HandleToolExecutionRequestEvent(ToolExecutionRequestEvent evt)
    {
        await InitializeToolsAsync();

        var parameters = Tooling.ParseToolArguments(evt.Arguments);
        var executionContext = BuildToolExecutionContext(evt.RequestId, CancellationToken.None);
        var result = await ExecuteToolAsync(evt.ToolName, parameters, executionContext, CancellationToken.None);

        await PublishAsync(new ToolExecutionResponseEvent
        {
            RequestId = evt.RequestId,
            ToolName = evt.ToolName,
            Success = result.IsSuccess,
            Result = result.Content ?? string.Empty,
            Error = result.ErrorMessage ?? string.Empty,
            Timestamp = Timestamp.FromDateTime(DateTime.UtcNow)
        });
    }
}