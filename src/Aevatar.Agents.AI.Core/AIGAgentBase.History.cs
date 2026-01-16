using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Aevatar.Agents.AI.Abstractions;
using Aevatar.Agents.AI.Core.Utils;
using Microsoft.Extensions.Logging;

// ReSharper disable InconsistentNaming
namespace Aevatar.Agents.AI.Core;

public abstract partial class AIGAgentBase
{
    // ============================================================
    //  History (Layer 1 + Layer 2)
    //
    //  中文 + ASCII:
    //  - Layer 1: State.History (sliding window, optional)
    //  - Layer 2: State.Context["history_summary"] (rolling summary, optional)
    //  - Concurrency: RepeatedField<T> is NOT thread-safe; lock is owned by HistoryRuntime.
    // ============================================================

    private HistoryRuntime? _historyRuntime;
    private HistoryRuntime History => _historyRuntime ??= new HistoryRuntime(this);

    /// <summary>
    /// Switch (default: false):
    /// - When enabled, <see cref="ChatAsync"/> / <see cref="ChatStreamAsync"/> will append
    ///   user/assistant messages into <see cref="AevatarAIAgentState.History"/>.
    /// - When enabled, <see cref="BuildLLMRequest"/> will also replay <see cref="AevatarAIAgentState.History"/>
    ///   into <see cref="AevatarLLMRequest.Messages"/> (so the LLM becomes "stateful" across calls).
    ///
    /// NOTE:
    /// - This is intentionally OFF by default to avoid unbounded token growth and large state payloads.
    /// - Some agents (e.g. tool-aware agents) manage history on their own.
    /// </summary>
    public bool EnableChatHistoryInState { get; set; }

    /// <summary>
    /// Layer 1 + 2 (default: false):
    /// - Layer 1: Keep a sliding window of recent messages in <see cref="AevatarAIAgentState.History"/>.
    /// - Layer 2: Archive removed messages into a rolling summary stored in <see cref="AevatarAIAgentState.Context"/>
    ///   under key <c>history_summary</c>, and inject it into the next LLM requests via system prompt.
    ///
    /// NOTE:
    /// - This may trigger extra LLM calls (for summarization) when compaction happens.
    /// - If you want "LLM decides when to recall", prefer tool-based retrieval (AIGAgentBase + search_memory tool).
    /// </summary>
    public bool EnableChatHistoryCompaction { get; set; }

    /// <summary>
    /// Sliding window size for <see cref="AevatarAIAgentState.History"/> when compaction is enabled.
    /// Default: 40 messages (~20 turns).
    /// </summary>
    public int ChatHistoryMaxMessages { get; set; } = 40;

    /// <summary>
    /// Hard cap for the rolling summary stored in <c>State.Context["history_summary"]</c>.
    /// Default: 4000 characters.
    /// </summary>
    public int ChatHistorySummaryMaxChars { get; set; } = 4000;

    protected virtual void AddMessageToHistory(string content, AevatarChatRole role, string? name = null)
    {
        History.AddMessage(content, role, name);
    }

    protected virtual void AddMessageToHistory(AevatarChatMessage message)
    {
        History.AddMessage(message);
    }

    protected virtual async Task CompactChatHistoryIfNeededAsync(CancellationToken cancellationToken = default)
    {
        await History.CompactIfNeededAsync(cancellationToken);
    }

    private string BuildEffectiveSystemPromptWithSummary()
    {
        var basePrompt = GetEffectiveSystemPrompt() ?? string.Empty;

        // Merge tool instructions into the system prompt (tools are always available now).
        var toolBlock = BuildToolInstructionBlock();
        var mergedPrompt = string.IsNullOrWhiteSpace(toolBlock)
            ? basePrompt
            : string.IsNullOrWhiteSpace(basePrompt)
                ? toolBlock
                : $"{basePrompt}\n\n{toolBlock}";

        return History.AppendSummaryToSystemPrompt(mergedPrompt);
    }

    protected virtual IReadOnlyList<AevatarChatMessage> SnapshotChatHistoryMessages()
        => History.SnapshotHistoryMessages();
}