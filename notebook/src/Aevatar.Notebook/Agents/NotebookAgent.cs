using System.Runtime.CompilerServices;
using Aevatar.Agents.Abstractions.Memory;
using Aevatar.Agents.Abstractions.Tracing;
using Aevatar.Agents.AI;
using Aevatar.Agents.AI.Abstractions;
using Aevatar.Agents.AI.Core;
using Aevatar.Agents.AI.WithTool.Abstractions;
using Aevatar.Notebook.Streaming;
using Aevatar.Notebook.Tools;

namespace Aevatar.Notebook.Agents;

// ============================================================
//  NotebookAgent (MVP)
//
//  - Uses AIGAgentBase memory stack (Layer 1/2 + tools)
//  - Allows caller to pass "notebook_context" into ChatRequest.Context,
//    and we inject it into system prompt so the LLM sees all provided context.
// ============================================================
public sealed class NotebookAgent : AIGAgentBase
{
    public const string NotebookContextKey = "notebook_context";

    // Injected by AIGAgentFactory (best-effort). Used by tools (Layer 4.2).
    private IMemoryGraphStore? MemoryGraphStore { get; set; }

    // Injected by AIGAgentFactory (best-effort). Reserved for future tooling.
    private IExecutionTraceStore? ExecutionTraceStore { get; set; }

    public NotebookAgent()
    {
        // Default: keep chat stateful + compacted (bounded).
        EnableChatHistoryInState = true;
        EnableChatHistoryCompaction = true;
        ChatHistoryMaxMessages = 24;
        ChatHistorySummaryMaxChars = 6000;

        // Long-term memory writes are best-effort; keep ON for now.
        EnableMemoryStoreAppend = true;
        EnableMemoryVectorIndexAppend = true;

        SystemPrompt =
            """
            You are an assistant for a Notebook-style app.

            Rules:
            - You MUST ground answers in the provided "Notebook context" when present.
            - If the context is insufficient, say what is missing and ask a focused question.
            - Be concise, structured, and avoid hallucination.
            """;
    }

    public override Task<string> GetDescriptionAsync() =>
        Task.FromResult("Aevatar.Notebook Agent (context-grounded Q&A + report)");

    protected override IAevatarToolManager CreateToolManager()
    {
        // Wrap default tool manager to emit tool progress events into current HTTP stream (best-effort).
        return new NotebookToolManager(base.CreateToolManager());
    }

    protected override async Task RegisterToolsAsync(CancellationToken cancellationToken = default)
    {
        await base.RegisterToolsAsync(cancellationToken);

        // Notebook tools depend on MemoryStore; if it's not wired, skip registration gracefully.
        if (MemoryStore == null)
            return;

        await RegisterToolAsync(new ListSourcesTool(MemoryStore), cancellationToken: cancellationToken);
        await RegisterToolAsync(new GetSourceTool(MemoryStore), cancellationToken: cancellationToken);
        await RegisterToolAsync(new RetrieveChunksTool(MemoryStore, MemoryVectorIndex), cancellationToken: cancellationToken);

        // Reports (stored in MemoryStore)
        await RegisterToolAsync(new GetReportTool(MemoryStore), cancellationToken: cancellationToken);
        await RegisterToolAsync(
            new GenerateReportTool(MemoryStore, MemoryVectorIndex, (req, ct) => base.ChatAsync(req, ct)),
            cancellationToken: cancellationToken);

        // Execution graph (best-effort; works when IMemoryGraphStore is wired)
        await RegisterToolAsync(new GetExecutionGraphTool(MemoryGraphStore), cancellationToken: cancellationToken);
    }

    public override async Task<ChatResponse> ChatAsync(ChatRequest request, CancellationToken cancellationToken = default)
    {
        var resp = await base.ChatAsync(request, cancellationToken);

        // Best-effort CQRS projection (Layer 3): project state snapshot after each turn.
        await ProjectStateAsync(GetState(), cancellationToken);

        return resp;
    }

    public override async IAsyncEnumerable<string> ChatStreamAsync(
        ChatRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var chunk in base.ChatStreamAsync(request, cancellationToken))
        {
            yield return chunk;
        }

        // Best-effort CQRS projection (Layer 3): project state snapshot after each turn.
        await ProjectStateAsync(GetState(), cancellationToken);
    }

    protected override AevatarLLMRequest BuildLLMRequest(ChatRequest request)
    {
        var llm = base.BuildLLMRequest(request);

        // Inject notebook context (MVP): append into system prompt.
        // This ensures the LLM sees the full context on every request.
        if (request?.Context != null &&
            request.Context.TryGetValue(NotebookContextKey, out var raw) &&
            raw is string context &&
            !string.IsNullOrWhiteSpace(context))
        {
            llm.SystemPrompt = $"{llm.SystemPrompt}\n\nNotebook context:\n{context.Trim()}\n";
        }

        return llm;
    }
}


