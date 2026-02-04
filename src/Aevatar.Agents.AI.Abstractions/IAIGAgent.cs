using Aevatar.Agents.AI.Abstractions.Configuration;
using Aevatar.Agents.AI.Abstractions.Providers;
using Aevatar.Agents.Abstractions;

namespace Aevatar.Agents.AI.Abstractions;

/// <summary>
/// Minimal surface for AI-capable agents.
///
/// 中文 + ASCII:
/// - This interface exists to decouple upper layers (Sessions/Cognitive) from concrete AI agent classes.
/// - Keep it small and stable: only include members that cross layer boundaries.
/// </summary>
public interface IAIGAgent : IGAgent
{
    string SystemPrompt { get; set; }

    bool EnableChatHistoryInState { get; set; }
    bool EnableChatHistoryCompaction { get; set; }
    int ChatHistoryMaxMessages { get; set; }

    bool EnableMemoryStoreAppend { get; set; }
    bool EnableSessionMemoryStoreAppend { get; set; }
    bool EnableMemoryVectorIndexAppend { get; set; }

    bool EnableMcpServers { get; set; }
    bool EnableAgentSkills { get; set; }

    bool AllowDangerousTools { get; set; }
    bool AllowInternalTools { get; set; }

    Task InitializeAsync(
        string providerName,
        Action<AevatarAIAgentConfig>? configAI = null,
        CancellationToken cancellationToken = default);

    Task InitializeAsync(
        LLMProviderConfig providerConfig,
        Action<AevatarAIAgentConfig>? configAI = null,
        CancellationToken cancellationToken = default);
}

