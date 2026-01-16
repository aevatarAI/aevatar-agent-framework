using System.Diagnostics.CodeAnalysis;
using System.Text;
using Aevatar.Agents.AI.Abstractions;
using Aevatar.Agents.AI.Core.Utils;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.AI.Core;

// ReSharper disable InconsistentNaming
public abstract partial class AIGAgentBase
{
    /// <summary>
    /// History runtime extracted from <see cref="AIGAgentBase.History"/>.
    ///
    /// 中文 + ASCII:
    /// - Layer 1: State.History（可选，滑动窗口）
    /// - Layer 2: State.Context["history_summary"]（可选，滚动摘要）
    /// - Concurrency: RepeatedField<T> 非线程安全；统一在 runtime 内持有锁
    /// - 失败语义：摘要生成 best-effort（失败回退 heuristic），绝不影响 Chat 主路径
    /// </summary>
    private sealed class HistoryRuntime(AIGAgentBase owner)
    {
        private readonly AIGAgentBase _owner = owner ?? throw new ArgumentNullException(nameof(owner));

        private readonly object _historyLock = new();
        private readonly SemaphoreSlim _compactionSemaphore = new(1, 1);

        [field: AllowNull, MaybeNull]
        private ConversationHistoryManager ConversationHistory =>
            field ??= new ConversationHistoryManager(_owner.State.History);

        internal void AddMessage(string content, AevatarChatRole role, string? name = null)
        {
            lock (_historyLock)
            {
                ConversationHistory.AddMessage(content, role, name);
            }
        }

        internal void AddMessage(AevatarChatMessage message)
        {
            lock (_historyLock)
            {
                ConversationHistory.AddMessage(message);
            }
        }

        internal IReadOnlyList<AevatarChatMessage> SnapshotHistoryMessages()
        {
            if (!_owner.EnableChatHistoryInState)
                return Array.Empty<AevatarChatMessage>();

            lock (_historyLock)
            {
                if (_owner.State.History == null || _owner.State.History.Count == 0)
                    return Array.Empty<AevatarChatMessage>();

                // Clone to avoid concurrent mutation issues.
                var list = new List<AevatarChatMessage>(_owner.State.History.Count);
                foreach (var msg in _owner.State.History)
                {
                    list.Add(msg.Clone());
                }
                return list;
            }
        }

        internal string AppendSummaryToSystemPrompt(string mergedPrompt)
        {
            if (!_owner.EnableChatHistoryInState || !_owner.EnableChatHistoryCompaction)
                return mergedPrompt;

            string? summary;
            lock (_historyLock)
            {
                summary = _owner.State.Context.TryGetValue(AIGAgentKeys.HistorySummary, out var s) ? s : null;
            }

            if (string.IsNullOrWhiteSpace(summary))
                return mergedPrompt;

            // Keep it explicit and stable; avoid fancy formatting that the model may misinterpret.
            return $"{mergedPrompt}\n\nConversation summary (memory):\n{summary}\n";
        }

        internal async Task CompactIfNeededAsync(CancellationToken cancellationToken)
        {
            if (!_owner.EnableChatHistoryInState || !_owner.EnableChatHistoryCompaction)
                return;

            if (_owner.ChatHistoryMaxMessages <= 0)
                return;

            int historyCount;
            lock (_historyLock)
            {
                historyCount = _owner.State.History?.Count ?? 0;
            }

            if (historyCount <= _owner.ChatHistoryMaxMessages)
                return;

            await _compactionSemaphore.WaitAsync(cancellationToken);
            try
            {
                List<AevatarChatMessage> removed;
                string? existingSummary;

                lock (_historyLock)
                {
                    var history = _owner.State.History;
                    if (history == null)
                        return;

                    var count = history.Count;
                    if (count <= _owner.ChatHistoryMaxMessages)
                        return;

                    var toRemove = count - _owner.ChatHistoryMaxMessages;
                    removed = new List<AevatarChatMessage>(toRemove);
                    for (var i = 0; i < toRemove; i++)
                    {
                        removed.Add(history[i].Clone());
                    }

                    var kept = new List<AevatarChatMessage>(_owner.ChatHistoryMaxMessages);
                    for (var i = toRemove; i < count; i++)
                    {
                        kept.Add(history[i].Clone());
                    }

                    history.Clear();
                    history.AddRange(kept);

                    existingSummary = _owner.State.Context.TryGetValue(AIGAgentKeys.HistorySummary, out var s) ? s : null;
                }

                if (removed.Count == 0)
                    return;

                var updatedSummary = await UpdateHistorySummaryAsync(existingSummary, removed, cancellationToken);
                if (string.IsNullOrWhiteSpace(updatedSummary))
                {
                    updatedSummary = BuildFallbackHistorySummary(existingSummary, removed);
                }

                if (_owner.ChatHistorySummaryMaxChars > 0 && updatedSummary.Length > _owner.ChatHistorySummaryMaxChars)
                {
                    updatedSummary = updatedSummary[.._owner.ChatHistorySummaryMaxChars];
                }

                lock (_historyLock)
                {
                    _owner.State.Context[AIGAgentKeys.HistorySummary] = updatedSummary;
                }
            }
            finally
            {
                _compactionSemaphore.Release();
            }
        }

        private async Task<string?> UpdateHistorySummaryAsync(
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

                var modelId = !string.IsNullOrWhiteSpace(_owner.Config.Model)
                    ? _owner.Config.Model
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

                var response = await _owner.GenerateLLMWithHooksAsync(
                    requestId: "history_summary::" + Guid.NewGuid().ToString("N"),
                    llmRequest: summarizeRequest,
                    cancellationToken: cancellationToken);
                return response.Content?.Trim();
            }
            catch (Exception ex)
            {
                _owner.Logger.LogWarning(ex, "History summarization failed; falling back to heuristic summary.");
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
                if (content.Length > 240)
                    content = content[..240] + "…";
                sb.Append("- ");
                sb.Append(role);
                sb.Append(": ");
                sb.AppendLine(content);
            }

            return sb.ToString().Trim();
        }
    }
}


