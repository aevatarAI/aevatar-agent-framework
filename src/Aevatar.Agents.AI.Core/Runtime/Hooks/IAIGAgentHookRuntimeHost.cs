using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.Attributes;
using Aevatar.Agents.AI.Abstractions;
using Aevatar.Agents.AI.Core.Hooks;
using Aevatar.Agents.AI.Tool.Abstractions;
using Google.Protobuf;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.AI.Core;

internal interface IAIGAgentHookRuntimeHost
{
    ILogger Logger { get; }
    string AgentId { get; }
    string AgentType { get; }

    bool AllowInternalTools { get; }
    bool AllowDangerousTools { get; }

    IAevatarLLMProvider LLMProvider { get; }

    IEnumerable<IAevatarAgentHook> CreateBuiltInHooks();
    IEnumerable<IAevatarAgentHook> AdditionalHooks { get; }
    AevatarAgentHookOptions HookOptions { get; }

    void AttachToolsToRequest(AevatarLLMRequest llmRequest);

    Task<ToolExecutionResult> ExecuteAllowedToolAsync(
        string toolName,
        Dictionary<string, object> args,
        ToolExecutionContext executionContext,
        AevatarLLMRequest llmRequest,
        CancellationToken ct);

    Task PublishAsync(IMessage message, EventDirection direction, CancellationToken ct);

    string? ResolveEventType(EventEnvelope envelope, object? payload);
    string ResolveEventRequestId(EventEnvelope envelope);
}

