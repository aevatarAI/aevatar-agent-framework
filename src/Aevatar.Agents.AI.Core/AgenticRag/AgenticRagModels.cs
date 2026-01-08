using Aevatar.Agents.Abstractions.Memory;
using Aevatar.Agents.AI.Core.Messages;

namespace Aevatar.Agents.AI.Core.AgenticRag;

// ============================================================
//  Agentic RAG - In-process Models (NOT cross-runtime contracts)
//
//  中文 + ASCII:
//  - 这些类型只用于进程内调用与 DI 边界（AgenticRagGAgent/策略/检索器之间传参）。
//  - 任何跨 runtime/stream 的状态与事件，必须使用 .proto（见 Messages/agentic_rag_messages.proto）。
// ============================================================

public sealed record AgenticRagRequest
{
    /// <summary>
    /// Correlation id from caller (recommended). If empty, caller should generate one.
    /// </summary>
    public required string RequestId { get; init; }

    /// <summary>
    /// User question / query text.
    /// </summary>
    public required string Query { get; init; }

    /// <summary>
    /// Optional small context (strings only) for routing/diagnostics.
    /// </summary>
    public Dictionary<string, string> Context { get; init; } = new();

    /// <summary>
    /// Optional retrieval scope override (for memory-based retrievers).
    /// </summary>
    public MemoryScope? Scope { get; init; }

    /// <summary>
    /// Optional explicit memory id override (for memory-based retrievers).
    /// </summary>
    public string? MemoryId { get; init; }

    /// <summary>
    /// Optional budget overrides (null means: use AgenticRagConfig defaults).
    /// </summary>
    public RagBudget? Budget { get; init; }
}

public sealed record RagBudget
{
    // NOTE: 0 means "unset" (use agent config defaults).
    public int MaxIterations { get; init; }
    public int MaxEvidenceItems { get; init; }
    public int MaxEvidenceChars { get; init; }
    public int MaxContextChars { get; init; }
    public TimeSpan? CallTimeout { get; init; }
}

public enum RagStopReason
{
    Unspecified = 0,
    Succeeded = 1,
    NoEvidence = 2,
    BudgetExceeded = 3,
    Cancelled = 4,
    Failed = 5
}

public sealed record AgenticRagResponse
{
    public required string RequestId { get; init; }
    public required string RunId { get; init; }

    public string Answer { get; init; } = string.Empty;

    /// <summary>
    /// Evidence summaries (snippets + citations). Should be bounded.
    /// </summary>
    public IReadOnlyList<RagEvidenceSummary> Evidence { get; init; } = Array.Empty<RagEvidenceSummary>();

    public RagStopReason StopReason { get; init; } = RagStopReason.Unspecified;
    public int Iterations { get; init; }

    /// <summary>
    /// Small diagnostics for callers (strings only). Never put secrets here.
    /// </summary>
    public Dictionary<string, string> Diagnostics { get; init; } = new();
}

public sealed record RagRetrieveRequest
{
    public required string RequestId { get; init; }
    public required string Query { get; init; }

    public int MaxResults { get; init; } = 20;

    /// <summary>
    /// Optional precomputed query embedding (float32). When present and a vector index is available,
    /// retrievers can do semantic search.
    /// </summary>
    public IReadOnlyList<float>? QueryEmbedding { get; init; }

    /// <summary>
    /// Max snippet characters per evidence item (best-effort; retriever should enforce).
    /// </summary>
    public int MaxSnippetChars { get; init; } = 800;

    public MemoryScope? Scope { get; init; }
    public string? MemoryId { get; init; }

    /// <summary>
    /// Optional small filters (strings only) that a retriever may use (best-effort).
    /// </summary>
    public Dictionary<string, string> Filters { get; init; } = new();
}


