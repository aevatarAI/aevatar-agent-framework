using Aevatar.Agents.AI.Core.Messages;

namespace Aevatar.Agents.AI.Core.AgenticRag;

// ============================================================
//  Agentic RAG - Planner (role strategy)
//
//  中文 + ASCII:
//  - Planner 决定“下一步检索什么”：改写 query、选择 scope/filter、是否停止。
//  - 这是可替换策略：可以是 LLM，也可以是规则/模板/领域策略。
// ============================================================

public interface IAgenticRagPlanner
{
    string Name { get; }

    Task<RagPlan> PlanAsync(
        AgenticRagRequest request,
        int iteration,
        IReadOnlyList<RagEvidenceSummary> evidenceSoFar,
        CancellationToken cancellationToken);
}

public sealed record RagPlan
{
    /// <summary>
    /// Retrieval requests for the next iteration (often 1, but kept as list for extensibility).
    /// </summary>
    public IReadOnlyList<RagRetrieveRequest> Retrievals { get; init; } = Array.Empty<RagRetrieveRequest>();

    /// <summary>
    /// Optional rationale (keep it short; may be logged).
    /// </summary>
    public string? Rationale { get; init; }

    /// <summary>
    /// If true, loop should stop (planner says "enough").
    /// </summary>
    public bool ShouldStop { get; init; }

    /// <summary>
    /// Optional stop reason (diagnostics only).
    /// </summary>
    public string? StopReason { get; init; }
}


