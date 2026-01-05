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
    //  - Concurrency: RepeatedField<T> is NOT thread-safe, protect with _historyLock.
    // ============================================================

    private readonly object _historyLock = new();
    private readonly SemaphoreSlim _historyCompactionSemaphore = new(1, 1);

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

    [field: AllowNull, MaybeNull]
    protected ConversationHistoryManager ConversationHistory =>
        field ??= CreateConversationHistoryManager();

    protected virtual ConversationHistoryManager CreateConversationHistoryManager()
    {
        return new ConversationHistoryManager(State.History);
    }

    protected virtual void AddMessageToHistory(string content, AevatarChatRole role, string? name = null)
    {
        // NOTE:
        // - Cognitive / Vote may run multiple LLM calls concurrently inside the same agent instance.
        // - RepeatedField<T> is NOT thread-safe; protect State.History modifications with a lock.
        lock (_historyLock)
        {
            ConversationHistory.AddMessage(content, role, name);
        }
    }

    protected virtual void AddMessageToHistory(AevatarChatMessage message)
    {
        lock (_historyLock)
        {
            ConversationHistory.AddMessage(message);
        }
    }

    protected virtual async Task CompactChatHistoryIfNeededAsync(CancellationToken cancellationToken = default)
    {
        if (!EnableChatHistoryInState || !EnableChatHistoryCompaction)
            return;

        if (ChatHistoryMaxMessages <= 0)
            return;

        int historyCount;
        lock (_historyLock)
        {
            historyCount = State.History?.Count ?? 0;
        }

        if (historyCount <= ChatHistoryMaxMessages)
            return;

        await _historyCompactionSemaphore.WaitAsync(cancellationToken);
        try
        {
            List<AevatarChatMessage> removed;
            string? existingSummary;

            lock (_historyLock)
            {
                var history = State.History;
                if (history == null)
                {
                    return;
                }

                var count = history.Count;
                if (count <= ChatHistoryMaxMessages)
                {
                    return;
                }

                var toRemove = count - ChatHistoryMaxMessages;
                removed = new List<AevatarChatMessage>(toRemove);
                for (var i = 0; i < toRemove; i++)
                {
                    removed.Add(history[i].Clone());
                }

                var kept = new List<AevatarChatMessage>(ChatHistoryMaxMessages);
                for (var i = toRemove; i < count; i++)
                {
                    kept.Add(history[i].Clone());
                }

                history.Clear();
                history.AddRange(kept);

                existingSummary = State.Context.TryGetValue(AIGAgentKeys.HistorySummary, out var s) ? s : null;
            }

            if (removed.Count == 0)
            {
                return;
            }

            var updatedSummary = await UpdateHistorySummaryAsync(existingSummary, removed, cancellationToken);
            if (string.IsNullOrWhiteSpace(updatedSummary))
            {
                updatedSummary = BuildFallbackHistorySummary(existingSummary, removed);
            }

            if (ChatHistorySummaryMaxChars > 0 && updatedSummary.Length > ChatHistorySummaryMaxChars)
            {
                updatedSummary = updatedSummary[..ChatHistorySummaryMaxChars];
            }

            lock (_historyLock)
            {
                State.Context[AIGAgentKeys.HistorySummary] = updatedSummary;
            }
        }
        finally
        {
            _historyCompactionSemaphore.Release();
        }
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

        if (!EnableChatHistoryInState || !EnableChatHistoryCompaction)
            return mergedPrompt;

        string? summary;
        lock (_historyLock)
        {
            summary = State.Context.TryGetValue(AIGAgentKeys.HistorySummary, out var s) ? s : null;
        }

        if (string.IsNullOrWhiteSpace(summary))
            return mergedPrompt;

        // Keep it explicit and stable; avoid fancy formatting that the model may misinterpret.
        return $"{mergedPrompt}\n\nConversation summary (memory):\n{summary}\n";
    }

    protected virtual async Task<string?> UpdateHistorySummaryAsync(
        string? existingSummary,
        IReadOnlyList<AevatarChatMessage> newlyArchivedMessages,
        CancellationToken cancellationToken)
    {
        try
        {
            var transcript = BuildTranscriptForSummarization(newlyArchivedMessages, maxChars: 12000);

            var prev = string.IsNullOrWhiteSpace(existingSummary)
                ? "(none)"
                : existingSummary.Trim();

            var prompt = $"""
                          You maintain a compact, factual memory summary of a conversation.

                          Existing summary:
                          {prev}

                          New conversation messages to merge:
                          {transcript}

                          Update the summary. Rules:
                          - Be factual. Do NOT invent.
                          - Keep it concise.
                          - Use this structure exactly:

                          Facts:
                          - ...

                          Decisions:
                          - ...

                          Constraints:
                          - ...

                          Open questions:
                          - ...
                          """;

            var modelId = !string.IsNullOrWhiteSpace(Config.Model)
                ? Config.Model
                : AevatarAIDefaults.DefaultModel;

            var summarizeRequest = new AevatarLLMRequest
            {
                SystemPrompt = "You are a precise conversation memory summarizer.",
                UserPrompt = prompt,
                Settings = new AevatarLLMSettings
                {
                    ModelId = modelId,
                    Temperature = 0,
                    MaxTokens = 512
                }
            };

            var response = await GenerateLLMWithHooksAsync(
                requestId: "history_summary::" + Guid.NewGuid().ToString("N"),
                llmRequest: summarizeRequest,
                cancellationToken: cancellationToken);
            return response.Content?.Trim();
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "History summarization failed; falling back to heuristic summary.");
            return null;
        }
    }

    private static string BuildTranscriptForSummarization(
        IReadOnlyList<AevatarChatMessage> messages,
        int maxChars)
    {
        var sb = new StringBuilder();

        foreach (var m in messages)
        {
            var role = m.Role.ToString().ToLowerInvariant();
            var content = (m.Content ?? string.Empty).Replace("\r", "").Trim();
            if (content.Length > 800)
                content = content[..800] + "…";

            sb.Append(role);
            sb.Append(": ");
            sb.AppendLine(content);
        }

        var text = sb.ToString().Trim();
        if (maxChars > 0 && text.Length > maxChars)
        {
            // Keep the tail of archived chunk (usually more relevant than the beginning of the chunk).
            text = text[^maxChars..];
        }

        return text;
    }

    private static string BuildFallbackHistorySummary(
        string? existingSummary,
        IReadOnlyList<AevatarChatMessage> archived)
    {
        var sb = new StringBuilder();

        if (!string.IsNullOrWhiteSpace(existingSummary))
        {
            sb.AppendLine(existingSummary.Trim());
            sb.AppendLine();
        }

        sb.AppendLine("Archived notes:");

        var take = Math.Min(archived.Count, 8);
        var start = Math.Max(0, archived.Count - take);
        for (var i = start; i < archived.Count; i++)
        {
            var m = archived[i];
            var role = m.Role.ToString().ToLowerInvariant();
            var content = (m.Content ?? string.Empty).Replace("\r", "").Trim();
            if (content.Length > 200)
                content = content[..200] + "…";

            sb.Append("- ");
            sb.Append(role);
            sb.Append(": ");
            sb.AppendLine(content);
        }

        if (archived.Count > take)
        {
            sb.AppendLine($"- ... ({archived.Count - take} more)");
        }

        return sb.ToString().Trim();
    }
}