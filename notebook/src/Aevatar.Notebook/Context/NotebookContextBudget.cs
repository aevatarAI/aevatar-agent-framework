namespace Aevatar.Notebook.Context;

// ============================================================
//  NotebookContextBudget
//
//  WHY:
//  - Notebook context 必须“有界”，否则 prompt 会无限膨胀。
//
//  设计品味：
//  - 用“预算”消灭 if/else 特例，而不是在业务路径里堆分支。
// ============================================================
internal sealed record NotebookContextBudget
{
    // Total rendered context size (character-based guardrail).
    public int MaxTotalChars { get; init; } = 18_000;

    // Max chars used for each source preview slice.
    public int MaxPerSourceChars { get; init; } = 2000;

    // Total number of retrieved chunk slices (top‑k).
    public int MaxChunks { get; init; } = 12;

    // Per-source chunk cap (to avoid one source dominating).
    public int MaxChunksPerSource { get; init; } = 2;
}


