using Aevatar.Agents.Abstractions;
using Aevatar.Agents.AI.Abstractions;
using Aevatar.Agents.AI.Tool.Abstractions;
using Aevatar.Agents.AI.Tool.Messages;
using Google.Protobuf;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.AI.Core;

public abstract partial class AIGAgentBase
{
    private ToolingInitContext? _toolingInitContext;
    private ToolingInitContext ToolingInitHost => _toolingInitContext ??= new ToolingInitContext(this);

    private ToolingLoopContext? _toolingLoopContext;
    private ToolingLoopContext ToolingLoopHost => _toolingLoopContext ??= new ToolingLoopContext(this);

    private sealed class ToolingInitContext : IToolingInitHost
    {
        private readonly AIGAgentBase _owner;

        internal ToolingInitContext(AIGAgentBase owner)
        {
            _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        }

        IAevatarToolManager IToolingInitHost.CreateToolManager()
            => _owner.CreateToolManager();

        Task IToolingInitHost.RegisterToolsAsync(CancellationToken cancellationToken)
            => _owner.RegisterToolsAsync(cancellationToken);
    }

    private sealed class ToolingLoopContext : IToolingLoopHost
    {
        private readonly AIGAgentBase _owner;

        internal ToolingLoopContext(AIGAgentBase owner)
        {
            _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        }

        ILogger IToolingLoopHost.Logger => _owner.Logger;
        IAevatarLLMProvider IToolingLoopHost.LLMProvider => _owner.LLMProvider;
        bool IToolingLoopHost.EnableChatHistoryInState => _owner.EnableChatHistoryInState;

        ToolExecutionContext IToolingLoopHost.BuildToolExecutionContext(
            string sessionId,
            CancellationToken cancellationToken)
            => _owner.BuildToolExecutionContext(sessionId, cancellationToken);

        Task<ToolExecutionResult> IToolingLoopHost.ExecuteAllowedToolWithHooksAsync(
            string toolName,
            Dictionary<string, object> args,
            ToolExecutionContext executionContext,
            AevatarLLMRequest llmRequest,
            CancellationToken cancellationToken)
            => _owner.ExecuteAllowedToolWithHooksAsync(toolName, args, executionContext, llmRequest, cancellationToken);

        Task<ToolExecutionResult> IToolingLoopHost.ExecuteToolAsync(
            string toolName,
            Dictionary<string, object> parameters,
            ToolExecutionContext executionContext,
            CancellationToken cancellationToken)
            => _owner.ExecuteToolAsync(toolName, parameters, executionContext, cancellationToken);

        Task IToolingLoopHost.PublishAsync(IMessage message, EventDirection direction, CancellationToken ct)
            => _owner.PublishAsync((dynamic)message, direction, ct);

        void IToolingLoopHost.AttachToolsToRequest(AevatarLLMRequest llmRequest)
            => _owner.AttachToolsToRequest(llmRequest);

        void IToolingLoopHost.TryApplyToolAllowlistFromSkillsLoadResult(
            AevatarLLMRequest llmRequest,
            string toolName,
            string? toolResultJson)
            => _owner.TryApplyToolAllowlistFromSkillsLoadResult(llmRequest, toolName, toolResultJson);

        bool IToolingLoopHost.IsToolAllowedByPolicy(ToolDefinition tool)
            => _owner.IsToolAllowedByPolicy(tool);

        string IToolingLoopHost.BuildToolPolicyDenyReason(ToolDefinition tool)
            => _owner.BuildToolPolicyDenyReason(tool);

        void IToolingLoopHost.AddMessageToHistory(AevatarChatMessage message)
            => _owner.AddMessageToHistory(message);

        Task<AevatarLLMResponse> IToolingLoopHost.GenerateLLMWithHooksAsync(
            ChatRequest request,
            AevatarLLMRequest llmRequest,
            CancellationToken cancellationToken)
            => _owner.GenerateLLMWithHooksAsync(request, llmRequest, cancellationToken);
    }
}

