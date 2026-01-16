using Aevatar.Agents.AI.Abstractions;
using Aevatar.Agents.AI.Core.Utils;

namespace Aevatar.Agents.AI.Core;

internal interface ILlmRequestHost
{
    bool EnableChatHistoryInState { get; }
    IReadOnlyCollection<string>? FixedToolAllowlist { get; }

    AevatarLLMSettings GetLLMSettings(ChatRequest request);
    string BuildEffectiveSystemPromptWithSummary();
    IReadOnlyList<AevatarChatMessage> SnapshotChatHistoryMessages();
    void AttachToolsToRequest(AevatarLLMRequest llmRequest);
}

