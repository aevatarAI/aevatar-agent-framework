using System.Text.Json;
using Aevatar.Agents.AI;
using Aevatar.Agents.AI.Abstractions;
using Aevatar.Agents.AI.Core;
using Aevatar.Agents.AI.WithTool.Abstractions;

namespace MemoryDemo;

/// <summary>
/// Memory demo agent:
/// - State.History replay (EnableChatHistoryInState)
/// - Compaction (sliding window + history_summary)
/// - Optional long-term memory via IAevatarAIMemory (DI-injected)
/// - Tool-based recall via built-in 'search_memory'
/// </summary>
public sealed class MemoryDemoAgent : AIGAgentBase
{
    public MemoryDemoAgent()
    {
        // Make the demo deterministic and easy to trigger.
        EnableChatHistoryInState = true;
        EnableChatHistoryCompaction = true;
        ChatHistoryMaxMessages = 8;
        ChatHistorySummaryMaxChars = 1200;
        ArchiveCompactedHistoryToAIMemory = true;

        SystemPrompt =
            """
            You are a helpful assistant in a memory demo.

            Rules:
            - Treat "Conversation summary (memory)" as authoritative compressed memory.
            - If the user asks about earlier details, call tool 'search_memory' before answering.
            - Be concise and factual. Do not invent.
            """;
    }

    public override Task<string> GetDescriptionAsync() =>
        Task.FromResult("MemoryDemoAgent (History + Compaction + Long-term Memory + search_memory)");

    public bool HasLongTermMemory => AIMemory != null;

    public string LongTermMemoryType => AIMemory?.GetType().Name ?? "none";

    public string? GetHistorySummary()
    {
        var state = GetState();
        return state.Context != null && state.Context.TryGetValue("history_summary", out var s) ? s : null;
    }

    public async Task<ToolExecutionResult> SearchMemoryToolAsync(
        string query,
        int maxResults = 10,
        string memoryType = "all",
        CancellationToken ct = default)
    {
        await InitializeToolsAsync(ct);

        var parameters = new Dictionary<string, object>
        {
            ["query"] = query,
            ["maxResults"] = maxResults,
            ["memoryType"] = memoryType
        };

        var execCtx = new ToolExecutionContext
        {
            AgentId = Id.ToString(),
            ToolManager = ToolManager,
            Memory = AIMemory,
            PublishEventCallback = msg => PublishAsync(msg, ct: ct),
            Logger = Logger,
            GetSessionId = () => Id.ToString()
        };

        return await ToolManager.ExecuteToolAsync("search_memory", parameters, execCtx, ct);
    }

    public async Task<IReadOnlyList<AevatarConversationEntry>> GetLongTermHistoryAsync(
        int limit = 200,
        CancellationToken ct = default)
    {
        if (AIMemory == null)
            return Array.Empty<AevatarConversationEntry>();

        return await AIMemory.GetHistoryAsync(limit: limit, cancellationToken: ct);
    }

    public async Task<IReadOnlyList<string>> SearchLongTermAsync(
        string query,
        int topK = 5,
        CancellationToken ct = default)
    {
        if (AIMemory == null)
            return Array.Empty<string>();

        return await AIMemory.SearchAsync(query, topK: topK, cancellationToken: ct);
    }

    public async Task<string?> TryPeekLongTermTailAsync(int maxChars = 1200, CancellationToken ct = default)
    {
        var history = await GetLongTermHistoryAsync(limit: 20, ct);
        if (history.Count == 0) return null;

        var items = history
            .Select(x => $"[{x.Role}] {(x.Content ?? string.Empty).Replace("\r", "").Trim()}")
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToList();

        var joined = string.Join("\n", items);
        if (maxChars > 0 && joined.Length > maxChars)
        {
            joined = joined[^maxChars..];
        }

        // Keep output stable for UI rendering.
        return JsonSerializer.Serialize(new
        {
            count = history.Count,
            tail = joined
        });
    }
}


