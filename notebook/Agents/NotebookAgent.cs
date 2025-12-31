using Aevatar.Agents.AI;
using Aevatar.Agents.AI.Abstractions;
using Aevatar.Agents.AI.Core;

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

    public override async Task<ChatResponse> ChatAsync(ChatRequest request, CancellationToken cancellationToken = default)
    {
        var resp = await base.ChatAsync(request, cancellationToken);

        // Best-effort CQRS projection (Layer 3): project state snapshot after each turn.
        await ProjectStateAsync(GetState(), cancellationToken);

        return resp;
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


