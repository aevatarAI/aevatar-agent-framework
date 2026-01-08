using System.Text.Json;
using Aevatar.Agents.AI;
using Aevatar.Agents.AI.Core;
using Aevatar.Agents.AI.Tool.Abstractions;
using Aevatar.Agents.AI.Tool.Tools.BuiltIn;
using Aevatar.Agents.AI.Tool.Tools.CoreTools;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace MemoryDemo;

/// <summary>
/// Memory demo agent:
/// - State.History replay (EnableChatHistoryInState)
/// - Compaction (sliding window + history_summary)
/// - Tool-based recall via built-in 'search_memory'
/// </summary>
public sealed class MemoryDemoAgent : AIGAgentBase
{
    private const string KnowledgeBaseMemoryIdContextKey = "kb_memory_id";
    private const string KnowledgeBaseTitleContextKey = "kb_title";

    public MemoryDemoAgent()
    {
        // Make the demo deterministic and easy to trigger.
        EnableChatHistoryInState = true;
        EnableChatHistoryCompaction = true;
        ChatHistoryMaxMessages = 8;
        ChatHistorySummaryMaxChars = 1200;

        // Long-term memory (resource + vector) - default ON for demo.
        // (Both are best-effort; failures won't break chat.)
        EnableMemoryStoreAppend = true;
        EnableMemoryVectorIndexAppend = true;

        // Tool safety policy (keep demo stable + predictable):
        // - Allow internal tools like query_state
        // - Disallow dangerous/confirmation tools (HTTP, side effects, etc.)
        AllowInternalTools = true;
        AllowDangerousTools = false;

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
        Task.FromResult("MemoryDemoAgent (History + Compaction + CQRS + search_memory)");

    /// <summary>
    /// Demo: get/set current knowledge-base selection.
    /// The selected memoryId is stored in <c>State.Context</c> so it survives restarts.
    /// </summary>
    public (string? MemoryId, string? Title) GetKnowledgeBase()
    {
        var state = GetState();

        var memoryId = state.Context.TryGetValue(KnowledgeBaseMemoryIdContextKey, out var id) ? id : null;
        var title = state.Context.TryGetValue(KnowledgeBaseTitleContextKey, out var t) ? t : null;

        return (string.IsNullOrWhiteSpace(memoryId) ? null : memoryId.Trim(),
            string.IsNullOrWhiteSpace(title) ? null : title.Trim());
    }

    public void SetKnowledgeBase(string? memoryId, string? title = null)
    {
        var state = GetState();

        if (string.IsNullOrWhiteSpace(memoryId))
        {
            state.Context.Remove(KnowledgeBaseMemoryIdContextKey);
            state.Context.Remove(KnowledgeBaseTitleContextKey);
            return;
        }

        state.Context[KnowledgeBaseMemoryIdContextKey] = memoryId.Trim();

        if (string.IsNullOrWhiteSpace(title))
            state.Context.Remove(KnowledgeBaseTitleContextKey);
        else
            state.Context[KnowledgeBaseTitleContextKey] = title.Trim();
    }

    protected override string? GetEffectiveSystemPrompt()
    {
        var basePrompt = base.GetEffectiveSystemPrompt() ?? string.Empty;
        var (kbMemoryId, kbTitle) = GetKnowledgeBase();

        if (string.IsNullOrWhiteSpace(kbMemoryId))
            return basePrompt;

        var titleLine = string.IsNullOrWhiteSpace(kbTitle) ? "" : $"\n- Book: {kbTitle}";

        // Keep instructions explicit and deterministic: always retrieve before answering.
        return
            $"{basePrompt}\n\nKnowledge base (book) is enabled.\n- memoryId: {kbMemoryId}{titleLine}\n" +
            $"- For EVERY user question, call tool 'search_memory' with memoryType=\"working\" and memoryId=\"{kbMemoryId}\".\n" +
            "- Use retrieved passages to answer. If nothing relevant is found, say you don't know.\n";
    }

    /// <summary>
    /// Demo polish: keep the exposed tool set focused.
    /// - query_state (read-only, internal)
    /// - search_memory (memory recall)
    /// </summary>
    protected override async Task RegisterToolsAsync(CancellationToken cancellationToken = default)
    {
        // Core: state query (read-only, internal access)
        await RegisterToolAsync(new StateQueryTool(), cancellationToken: cancellationToken);

        // Built-in: memory search (CQRS + MemoryStore/VectorIndex + State snapshot)
        await RegisterToolAsync(
            new AevatarMemorySearchTool(
                new TypedLoggerAdapter<AevatarMemorySearchTool>(Logger),
                CqrsStateQueryService,
                MemoryStore,
                MemoryVectorIndex),
            cancellationToken: cancellationToken);
    }

    // ============================================================
    //  Demo-only: force CQRS projection after each chat
    //
    //  WHY:
    //  - In real systems, CQRS projection happens after state persistence (OnStateChangedAsync).
    //  - This demo keeps EventStore optional; we still want to show projected read-model.
    // ============================================================
    public override async Task<ChatResponse> ChatAsync(ChatRequest request, CancellationToken cancellationToken = default)
    {
        var resp = await base.ChatAsync(request, cancellationToken);

        // Best-effort projection: do not break chat if CQRS isn't configured.
        await ProjectStateAsync(GetState(), cancellationToken);

        return resp;
    }

    // ============================================================
    //  Demo-only: seed memory without calling LLM
    //
    //  WHY:
    //  - Helps validate search_memory + CQRS pipeline even without external LLM connectivity.
    // ============================================================
    public async Task SeedAsync(string text, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;

        // Put something into short-term history (Layer 1)
        AddMessageToHistory(text.Trim(), AevatarChatRole.User);
        AddMessageToHistory("(seeded) ok, I remember it.", AevatarChatRole.Assistant);

        // Put something into rolling summary (Layer 2) for easier searching
        var summary = GetHistorySummary() ?? string.Empty;
        var next = string.IsNullOrWhiteSpace(summary)
            ? $"[seed] {text}".Trim()
            : (summary + "\n" + $"[seed] {text}").Trim();

        GetState().Context["history_summary"] = next;

        // Project to CQRS read-model (demo)
        await ProjectStateAsync(GetState(), ct);
    }

    public string? GetHistorySummary()
    {
        var state = GetState();
        return state.Context != null && state.Context.TryGetValue("history_summary", out var s) ? s : null;
    }

    public async Task<ToolExecutionResult> SearchMemoryToolAsync(
        string query,
        int maxResults = 10,
        string memoryType = "all",
        string? memoryId = null,
        CancellationToken ct = default)
    {
        await InitializeToolsAsync(ct);

        var parameters = new Dictionary<string, object>
        {
            ["query"] = query,
            ["maxResults"] = maxResults,
            ["memoryType"] = memoryType
        };

        if (!string.IsNullOrWhiteSpace(memoryId))
            parameters["memoryId"] = memoryId.Trim();

        var execCtx = new ToolExecutionContext
        {
            AgentId = Id.ToString(),
            ToolManager = ToolManager,
            PublishEventCallback = msg => PublishAsync(msg, ct: ct),
            Logger = Logger,
            GetSessionId = () => Id.ToString(),
            AllowInternalTools = AllowInternalTools,
            AllowDangerousTools = AllowDangerousTools
        };

        return await ToolManager.ExecuteToolAsync("search_memory", parameters, execCtx, ct);
    }

    public async Task<IReadOnlyList<float>?> TryGenerateEmbeddingVectorAsync(string text, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        if (!TryGetEmbeddingGenerator(out _))
            return null;

        var emb = await GenerateEmbeddingAsync(text.Trim(), cancellationToken: ct);
        return emb == null ? null : emb.Vector.ToArray();
    }

    // ============================================================
    //  Minimal typed logger adapter for tools
    //
    //  WHY:
    //  - Some tool constructors require ILogger<T>.
    //  - AIGAgentBase exposes ILogger (non-generic).
    //  - Keep demo self-contained; do not rely on internal/private adapters.
    // ============================================================
    private sealed class TypedLoggerAdapter<T> : ILogger<T>
    {
        private readonly ILogger _inner;

        public TypedLoggerAdapter(ILogger inner) => _inner = inner ?? NullLogger.Instance;

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull
            => _inner.BeginScope(state);

        public bool IsEnabled(LogLevel logLevel) => _inner.IsEnabled(logLevel);

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
            => _inner.Log(logLevel, eventId, state, exception, formatter);
    }
}


