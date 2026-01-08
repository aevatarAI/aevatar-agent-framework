using Aevatar.Agents.AI.Core.Messages;

namespace Aevatar.Agents.AI.Core.AgenticRag;

// ============================================================
//  Agentic RAG - Synthesizer (role strategy)
//
//  中文 + ASCII:
//  - Synthesizer 只基于证据生成草稿（带引用的结构由上层输出组装）。
//  - 默认实现可以是 LLM（不附加 tools），也可以是规则模板。
// ============================================================

public interface IAgenticRagSynthesizer
{
    string Name { get; }

    Task<RagSynthesisResult> SynthesizeAsync(
        AgenticRagRequest request,
        IReadOnlyList<RagEvidenceSummary> evidence,
        CancellationToken cancellationToken);
}

public sealed record RagSynthesisResult
{
    public string Draft { get; init; } = string.Empty;

    /// <summary>
    /// Evidence actually used by the draft (best-effort).
    /// </summary>
    public IReadOnlyList<RagEvidenceSummary> UsedEvidence { get; init; } = Array.Empty<RagEvidenceSummary>();

    public Dictionary<string, string> Diagnostics { get; init; } = new();
}


