using Aevatar.Agents.AI.Abstractions;
using Aevatar.Agents.AI.Tool.Abstractions;
using Google.Protobuf;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.AI.Core;

internal interface IAIGAgentToolsRuntimeHost
{
    string AgentId { get; }
    string AgentType { get; }
    ILogger Logger { get; }

    bool AllowInternalTools { get; }
    bool AllowDangerousTools { get; }

    IAevatarToolManager ToolManager { get; }
    IReadOnlyList<ToolDefinition> RegisteredToolsCache { get; }
    IReadOnlyList<AevatarFunctionDefinition> FunctionDefinitionsCache { get; }

    bool HasEmbeddingGenerator { get; }
    Task<IReadOnlyList<Embedding<float>>> GenerateEmbeddingsAsync(IReadOnlyList<string> inputs, CancellationToken ct);

    IMessage GetState();
    Task PublishAsync(IMessage message, EventDirection direction, CancellationToken ct);
    Task<string> PublishToolEventAsync(IMessage message, EventDirection direction, CancellationToken ct);

    bool IsToolAllowedByPolicy(ToolDefinition tool);

    Task InitializeToolsAsync(CancellationToken ct);
    Task RefreshToolCachesAsync(CancellationToken ct);

    Task<ToolExecutionResult> ExecuteAllowedToolWithHooksAsync(
        string toolName,
        Dictionary<string, object> args,
        ToolExecutionContext executionContext,
        AevatarLLMRequest llmRequest,
        CancellationToken ct);
}

