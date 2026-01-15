using Aevatar.Agents.AI.Abstractions;
using Aevatar.Agents.AI.Core.Utils;

namespace Aevatar.Agents.AI.Core;

public abstract partial class AIGAgentBase
{
    private LlmRequestRuntimeContext? _llmRequestRuntimeContext;
    private LlmRequestRuntimeContext LlmRequestContext => _llmRequestRuntimeContext ??= new LlmRequestRuntimeContext(this);

    private sealed class LlmRequestRuntimeContext : ILlmRequestHost
    {
        private readonly AIGAgentBase _owner;

        internal LlmRequestRuntimeContext(AIGAgentBase owner)
        {
            _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        }

        bool ILlmRequestHost.EnableChatHistoryInState => _owner.EnableChatHistoryInState;

        IReadOnlyCollection<string>? ILlmRequestHost.FixedToolAllowlist => _owner._fixedToolAllowlist;

        AevatarLLMSettings ILlmRequestHost.GetLLMSettings(ChatRequest request)
            => _owner.GetLLMSettings(request);

        string ILlmRequestHost.BuildEffectiveSystemPromptWithSummary()
            => _owner.BuildEffectiveSystemPromptWithSummary();

        IReadOnlyList<AevatarChatMessage> ILlmRequestHost.SnapshotChatHistoryMessages()
            => _owner.SnapshotChatHistoryMessages();

        void ILlmRequestHost.AttachToolsToRequest(AevatarLLMRequest llmRequest)
            => _owner.AttachToolsToRequest(llmRequest);
    }
}

