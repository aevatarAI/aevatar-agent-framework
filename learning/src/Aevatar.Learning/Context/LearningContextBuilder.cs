using System.Text;
using Aevatar.Learning.Notebooks;
using Aevatar.Learning.Sources;

namespace Aevatar.Learning.Context;

// ============================================================
//  LearningContextBuilder (MVP)
//
//  目标：把 notebook 目录里的 sources 组装成“有界、可追溯”的上下文文本，
//       供 Q&A / 报告 / 百科 / 测验复用。
//
//  约束：
//  - 有界：maxTotalChars / maxPerSourceChars / maxSources
//  - 可追溯：每个片段必须携带 sourceId（与 UI/引用对齐）
//  - 稳定：同一输入与相同 sources 状态下，输出顺序应可预测
// ============================================================
public sealed class LearningContextBuilder
{
    private readonly SourceStore _sources;

    public LearningContextBuilder(SourceStore sources)
    {
        _sources = sources ?? throw new ArgumentNullException(nameof(sources));
    }

    public async Task<LearningContextResult> BuildAsync(
        NotebookWorkspace workspace,
        string? query,
        IReadOnlyList<string>? selectedSourceIds = null,
        LearningContextBudget? budget = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);

        var b = budget ?? LearningContextBudget.Default;
        b = b.Clamp();

        query = (query ?? string.Empty).Trim();

        // 1) Resolve sources
        var sourceIds = await ResolveSourceIdsAsync(workspace, selectedSourceIds, b.MaxSources, ct);
        if (sourceIds.Count == 0)
        {
            return new LearningContextResult(
                Text: string.Empty,
                Slices: Array.Empty<LearningContextSlice>(),
                Budget: b);
        }

        // 2) Assemble bounded slices
        var slices = new List<LearningContextSlice>(capacity: Math.Min(sourceIds.Count, b.MaxSources));
        var remaining = b.MaxTotalChars;

        // Header budget (keep small and deterministic).
        var header = BuildHeader(query);
        if (header.Length > 0)
        {
            var take = Math.Min(header.Length, remaining);
            header = header[..take];
            remaining -= take;
        }

        foreach (var sourceId in sourceIds)
        {
            ct.ThrowIfCancellationRequested();
            if (remaining <= 0)
                break;

            var per = Math.Min(b.MaxPerSourceChars, remaining);
            if (per <= 0)
                break;

            SourceMeta meta;
            string content;
            try
            {
                (meta, content) = await _sources.GetAsync(workspace, sourceId, ct);
            }
            catch
            {
                // Best-effort: skip missing/broken sources.
                continue;
            }

            content = (content ?? string.Empty).Trim();
            if (content.Length == 0)
                continue;

            var snippet = content.Length <= per ? content : content[..per].TrimEnd();
            if (content.Length > per)
                snippet += "\n...(truncated)";

            var block = BuildSourceBlock(meta, snippet);
            if (block.Length > remaining)
            {
                // Last-resort trim (keep marker lines).
                block = block[..Math.Max(0, remaining)].TrimEnd();
            }

            if (block.Length == 0)
                continue;

            remaining -= block.Length;
            slices.Add(new LearningContextSlice(
                SourceId: meta.SourceId,
                Title: meta.Title,
                MimeType: meta.MimeType,
                Content: snippet,
                Reason: selectedSourceIds != null && selectedSourceIds.Count > 0 ? "selected" : "auto",
                OriginalChars: meta.SizeChars));
        }

        // 3) Final text
        var sb = new StringBuilder(capacity: b.MaxTotalChars);
        if (header.Length > 0)
            sb.Append(header);

        foreach (var s in slices)
        {
            if (sb.Length >= b.MaxTotalChars)
                break;

            var remaining2 = b.MaxTotalChars - sb.Length;
            var block = BuildSourceBlock(
                new SourceMeta
                {
                    SourceId = s.SourceId,
                    Title = s.Title,
                    MimeType = s.MimeType,
                    SizeChars = s.OriginalChars,
                    Sha256Hex = "",
                    CreatedAt = default,
                    UpdatedAt = default
                },
                s.Content);

            if (block.Length > remaining2)
                block = block[..remaining2];

            sb.Append(block);
        }

        return new LearningContextResult(
            Text: sb.ToString(),
            Slices: slices,
            Budget: b);
    }

    private static async Task<List<string>> ResolveSourceIdsAsync(
        NotebookWorkspace workspace,
        IReadOnlyList<string>? selectedSourceIds,
        int maxSources,
        CancellationToken ct)
    {
        // If user explicitly selected sources, keep their ordering (deterministic).
        if (selectedSourceIds != null && selectedSourceIds.Count > 0)
        {
            var list = new List<string>(capacity: Math.Min(selectedSourceIds.Count, maxSources));
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var raw in selectedSourceIds)
            {
                ct.ThrowIfCancellationRequested();
                if (list.Count >= maxSources) break;

                var id = (raw ?? string.Empty).Trim();
                if (id.Length == 0) continue;
                if (!seen.Add(id)) continue;
                list.Add(id);
            }
            return list;
        }

        // No selection => include up to maxSources (sorted by id for stability).
        // NOTE: we keep this method independent from SourceStore to avoid coupling;
        // the API/service layer can decide better heuristics later.
        var dir = workspace.SourcesDir;
        if (!Directory.Exists(dir))
            return new List<string>();

        var ids = Directory.EnumerateDirectories(dir)
            .Select(Path.GetFileName)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x!.Trim())
            .Where(x => x.Length > 0)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .Take(maxSources)
            .ToList();

        await Task.CompletedTask;
        return ids;
    }

    private static string BuildHeader(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return string.Empty;

        return
            $"""
            [LEARNING_NOTEBOOK_CONTEXT]
            QUERY:
            {query.Trim()}

            """;
    }

    private static string BuildSourceBlock(SourceMeta meta, string snippet)
    {
        var title = (meta.Title ?? string.Empty).Trim();
        if (title.Length == 0) title = "(untitled)";

        var mime = (meta.MimeType ?? "text/plain").Trim();

        return
            $"""
            --- SOURCE {meta.SourceId} ---
            TITLE: {title}
            MIME: {mime}
            CONTENT:
            {snippet.TrimEnd()}

            """;
    }
}

public sealed record LearningContextBudget(int MaxTotalChars, int MaxPerSourceChars, int MaxSources)
{
    public static LearningContextBudget Default { get; } = new(MaxTotalChars: 12_000, MaxPerSourceChars: 2_000, MaxSources: 8);

    public LearningContextBudget Clamp()
    {
        var total = Math.Clamp(MaxTotalChars, 1_000, 100_000);
        var per = Math.Clamp(MaxPerSourceChars, 200, 50_000);
        var sources = Math.Clamp(MaxSources, 1, 50);
        return new LearningContextBudget(total, per, sources);
    }
}

public sealed record LearningContextSlice(
    string SourceId,
    string Title,
    string MimeType,
    string Content,
    string Reason,
    int OriginalChars);

public sealed record LearningContextResult(
    string Text,
    IReadOnlyList<LearningContextSlice> Slices,
    LearningContextBudget Budget);


