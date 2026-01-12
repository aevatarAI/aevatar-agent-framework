namespace Aevatar.Notebook.Sources;

// ============================================================
//  SourceChunker
//
//  目标：
//  - 将一段 source 文本切成有界 chunks（带 offset），用于：
//    - Layer 4: IMemoryStore.AppendAsync 写入（chunk entries）
//    - Layer 4.1: IMemoryVectorIndex.UpsertAsync 写入（chunk embeddings）
//
//  约束：
//  - 必须有界（max chunks / max chars）
//  - 必须确定性（同样输入 -> 同样 chunks）
//  - 避免特殊分支：统一的“窗口 + 回退边界”策略
// ============================================================
internal sealed class SourceChunker
{
    public static readonly SourceChunkingOptions DefaultOptions = new();

    public string Normalize(string? text)
    {
        // NOTE:
        // - 以“规范化后的文本”作为后续 offsets 的基准。
        // - 我们把它当做“Notebook 的 canonical source text”（MVP）。
        var t = (text ?? string.Empty).Replace("\r", "");
        return t.Trim();
    }

    public IReadOnlyList<SourceChunk> Chunk(string? text, SourceChunkingOptions? options = null)
    {
        options ??= DefaultOptions;

        var normalized = Normalize(text);
        if (normalized.Length == 0)
            return Array.Empty<SourceChunk>();

        var maxChunks = Math.Max(1, options.MaxChunks);
        var maxChunkChars = Math.Max(64, options.MaxChunkChars);
        var overlapChars = Math.Clamp(options.OverlapChars, 0, maxChunkChars - 1);

        var list = new List<SourceChunk>(capacity: Math.Min(maxChunks, 64));

        var start = 0;
        var chunkIndex = 0;
        while (start < normalized.Length && chunkIndex < maxChunks)
        {
            var proposedEnd = Math.Min(start + maxChunkChars, normalized.Length);
            var end = proposedEnd;

            if (end < normalized.Length)
            {
                end = TryAdjustToBoundary(normalized, start, proposedEnd, options) ?? proposedEnd;
            }

            if (end <= start)
                end = proposedEnd;

            // 内容可以 Trim（减少噪音），但 offsets 仍指向原范围（用于引用/审计）。
            var raw = normalized.AsSpan(start, end - start).ToString();
            var content = raw.Trim();

            if (!string.IsNullOrWhiteSpace(content))
            {
                list.Add(new SourceChunk(
                    ChunkIndex: chunkIndex,
                    OffsetStart: start,
                    OffsetEnd: end,
                    Content: content));
            }

            chunkIndex++;

            if (end >= normalized.Length)
                break;

            // Overlap 以 end 为锚点回退；确保进度单调递增，避免死循环。
            var nextStart = end - overlapChars;
            if (nextStart <= start)
                nextStart = end;

            start = nextStart;
        }

        return list;
    }

    private static int? TryAdjustToBoundary(
        string text,
        int start,
        int proposedEnd,
        SourceChunkingOptions options)
    {
        var backtrack = Math.Max(0, options.BoundaryBacktrackChars);
        var minChunkChars = Math.Max(0, options.MinChunkChars);

        var minEnd = Math.Min(text.Length, start + minChunkChars);
        var searchStart = Math.Max(minEnd, proposedEnd - backtrack);

        // Prefer newline boundary first.
        for (var i = proposedEnd - 1; i >= searchStart; i--)
        {
            if (text[i] == '\n')
                return i + 1;
        }

        // Fallback to whitespace boundary.
        for (var i = proposedEnd - 1; i >= searchStart; i--)
        {
            if (char.IsWhiteSpace(text[i]))
                return i + 1;
        }

        return null;
    }
}

internal sealed record SourceChunkingOptions
{
    // ------------------------------------------------------------
    //  注意：
    //  - 这些都是“工程侧上限”，防止 prompt / embedding / store 爆炸。
    // ------------------------------------------------------------

    public int MaxChunkChars { get; init; } = 1200;
    public int OverlapChars { get; init; } = 120;
    public int MaxChunks { get; init; } = 256;

    // 尝试在 proposedEnd 附近回退到自然边界（\n / whitespace）
    public int BoundaryBacktrackChars { get; init; } = 200;
    public int MinChunkChars { get; init; } = 200;
}

internal sealed record SourceChunk(
    int ChunkIndex,
    int OffsetStart,
    int OffsetEnd,
    string Content);


