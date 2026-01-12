using Aevatar.Agents.AI.Core.Messages;

namespace Aevatar.Agents.AI.Core.AgenticRag;

// ============================================================
//  Agentic RAG - Critic (role strategy)
//
//  中文 + ASCII:
//  - Critic 检查“草稿中的关键断言是否有证据支撑 / 是否存在缺口”。
//  - 若失败，应返回 gaps（缺口）以驱动下一轮检索（bounce-back）。
// ============================================================

public interface IAgenticRagCritic
{
    string Name { get; }

    Task<RagCritiqueResult> CritiqueAsync(
        AgenticRagRequest request,
        RagSynthesisResult draft,
        IReadOnlyList<RagEvidenceSummary> evidence,
        CancellationToken cancellationToken);
}

public sealed record RagCritiqueResult
{
    public bool Passed { get; init; }

    /// <summary>
    /// Missing evidence / open questions (best-effort).
    /// </summary>
    public IReadOnlyList<RagGap> Gaps { get; init; } = Array.Empty<RagGap>();

    public string? Feedback { get; init; }

    public Dictionary<string, string> Diagnostics { get; init; } = new();
}

public sealed record RagGap
{
    public required string Description { get; init; }

    /// <summary>
    /// Optional suggested query to fill the gap (best-effort).
    /// </summary>
    public string? SuggestedQuery { get; init; }
}


