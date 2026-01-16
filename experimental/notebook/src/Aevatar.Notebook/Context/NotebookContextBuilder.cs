using System.Text;
using Aevatar.Agents.Abstractions.Memory;
using Aevatar.Agents.AI.Abstractions.Providers;
using Aevatar.Agents.AI.Core.Embeddings;
using Aevatar.Notebook.Contracts;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Aevatar.Notebook.Context;

// ============================================================
//  NotebookContextBuilder
//
//  目标：
//  - 生成“预算内”的 Notebook context，供 Q&A / Report 注入 LLM
//
//  策略（MVP）：
//  - Coverage-first：每个 source 至少出现一次（preview slice）
//  - Relevance-next：在预算内追加 top‑k chunks（vector 优先，lexical 回退）
//
//  输出：
//  - Protobuf: NotebookContext（用于 trace / replay / tool 输出）
//  - Rendered: string（用于直接注入 system prompt）
// ============================================================
internal sealed class NotebookContextBuilder
{
    private readonly IMemoryStore _store;
    private readonly IMemoryVectorIndex? _vectorIndex;
    private readonly ILogger<NotebookContextBuilder> _logger;
    private readonly ILLMProviderFactory? _llmProviderFactory;
    private readonly IAIAgentEmbeddingFactory? _embeddingFactory;

    private readonly SemaphoreSlim _embeddingInitLock = new(1, 1);
    private IEmbeddingGenerator<string, Embedding<float>>? _embeddingGenerator;
    private EmbeddingGenerationOptions? _embeddingOptions;

    public NotebookContextBuilder(
        IMemoryStore store,
        ILogger<NotebookContextBuilder> logger,
        IMemoryVectorIndex? vectorIndex = null,
        ILLMProviderFactory? llmProviderFactory = null,
        IAIAgentEmbeddingFactory? embeddingFactory = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _vectorIndex = vectorIndex;
        _llmProviderFactory = llmProviderFactory;
        _embeddingFactory = embeddingFactory;
    }

    public async Task<NotebookContextBuildResult> BuildAsync(
        NotebookContextBuildRequest request,
        Func<IReadOnlyList<string>, CancellationToken, Task<IReadOnlyList<Embedding<float>>>>? generateEmbeddingsAsyncOverride = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ct.ThrowIfCancellationRequested();

        var budget = request.Budget ?? new NotebookContextBudget();

        var selectedSourceIds = await ResolveSelectedSourceIdsAsync(request, ct);

        // Deterministic ordering
        selectedSourceIds.Sort(StringComparer.Ordinal);

        var previewPerSource = ComputePreviewCharsPerSource(selectedSourceIds.Count, budget);

        var now = Timestamp.FromDateTime(DateTime.UtcNow);
        var ctx = new NotebookContext
        {
            NotebookId = request.NotebookId ?? string.Empty,
            CreatedAt = now,
            MaxTotalChars = budget.MaxTotalChars,
            MaxPerSourceChars = previewPerSource,
            MaxChunks = budget.MaxChunks
        };

        // ----------------------------
        // Coverage: per-source preview
        // ----------------------------
        foreach (var sourceId in selectedSourceIds)
        {
            ct.ThrowIfCancellationRequested();
            ctx.Slices.Add(await BuildPreviewSliceAsync(sourceId, previewPerSource, ct));
        }

        // ----------------------------
        // Relevance: top‑k chunks
        // ----------------------------
        var query = (request.Query ?? string.Empty).Trim();
        var chunkSlices = new List<NotebookContextSlice>();

        var vectorUsed = false;
        if (query.Length > 0 && budget.MaxChunks > 0)
        {
            var vectorSlices = await TryBuildVectorTopKAsync(
                query,
                selectedSourceIds,
                budget,
                generateEmbeddingsAsyncOverride,
                ct);

            if (vectorSlices.Count > 0)
            {
                vectorUsed = true;
                chunkSlices.AddRange(vectorSlices);
            }
            else
            {
                chunkSlices.AddRange(await BuildLexicalTopKAsync(query, selectedSourceIds, budget, ct));
            }
        }

        // Deduplicate + cap
        var deduped = DeduplicateChunkSlices(chunkSlices);
        var capped = deduped
            .OrderByDescending(s => s.Score)
            .ThenBy(s => s.SourceId, StringComparer.Ordinal)
            .ThenBy(s => s.ChunkId, StringComparer.Ordinal)
            .Take(Math.Max(0, budget.MaxChunks))
            .ToList();

        foreach (var s in capped)
            ctx.Slices.Add(s);

        ctx.Tags["strategy"] = vectorUsed ? "vector" : "lexical_or_none";
        ctx.Tags["source_count"] = selectedSourceIds.Count.ToString();
        ctx.Tags["chunk_slice_count"] = capped.Count.ToString();

        // Render to string with hard budget (coverage-first; never sacrifice source headers first).
        var rendered = Render(ctx, budget.MaxTotalChars);

        return new NotebookContextBuildResult
        {
            SourceIds = selectedSourceIds,
            Context = ctx,
            Rendered = rendered
        };
    }

    private async Task<List<string>> ResolveSelectedSourceIdsAsync(
        NotebookContextBuildRequest request,
        CancellationToken ct)
    {
        // Explicit selection wins.
        if (request.SelectedSourceIds is { Count: > 0 })
        {
            return request.SelectedSourceIds
                .Select(s => (s ?? string.Empty).Trim())
                .Where(s => s.Length > 0)
                .Distinct(StringComparer.Ordinal)
                .ToList();
        }

        // Default: all sources (within limit).
        var resources = await _store.ListResourcesAsync(
            scopeTypeFilter: MemoryScopeType.Graph,
            limit: 200,
            ct: ct);

        return resources
            .Select(r => (r.MemoryId ?? string.Empty).Trim())
            .Where(id => id.StartsWith("source::", StringComparison.Ordinal))
            .Select(id => id.Replace("source::", "", StringComparison.Ordinal))
            .Where(id => id.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    private async Task<NotebookContextSlice> BuildPreviewSliceAsync(
        string sourceId,
        int previewChars,
        CancellationToken ct)
    {
        var slice = new NotebookContextSlice
        {
            Kind = NotebookContextSliceKind.SourcePreview,
            SourceId = sourceId,
            ChunkId = string.Empty,
            Content = string.Empty,
            Score = 0,
            Reason = "coverage:source_preview"
        };

        try
        {
            var memoryId = $"source::{sourceId}";
            var entries = await _store.ListEntriesAsync(memoryId, limit: 200, ct: ct);

            // Best-effort pick the source entry.
            var sourceEntry = entries.LastOrDefault(e => string.Equals(e.Role, "source", StringComparison.Ordinal))
                           ?? entries.FirstOrDefault();

            if (sourceEntry == null)
                return slice;

            if (sourceEntry.Tags.TryGetValue("title", out var title) && !string.IsNullOrWhiteSpace(title))
                slice.Tags["title"] = title.Trim();

            var text = (sourceEntry.Content ?? string.Empty).Replace("\r", "").Trim();
            if (text.Length > previewChars)
                text = text[..previewChars];

            slice.Content = text;
            slice.Tags["memory_id"] = memoryId;
        }
        catch (Exception ex)
        {
            // Best-effort: keep a placeholder so "every source appears once".
            _logger.LogDebug(ex, "[Notebook] Failed to build preview slice for {SourceId}", sourceId);
            slice.Content = string.Empty;
            slice.Tags["error"] = "preview_load_failed";
        }

        return slice;
    }

    private async Task<IReadOnlyList<NotebookContextSlice>> TryBuildVectorTopKAsync(
        string query,
        IReadOnlyList<string> sourceIds,
        NotebookContextBudget budget,
        Func<IReadOnlyList<string>, CancellationToken, Task<IReadOnlyList<Embedding<float>>>>? generateEmbeddingsAsyncOverride,
        CancellationToken ct)
    {
        if (_vectorIndex == null)
            return Array.Empty<NotebookContextSlice>();

        // Prepare generator
        var generator = generateEmbeddingsAsyncOverride != null
            ? null
            : await TryGetEmbeddingGeneratorAsync(ct);

        Func<IReadOnlyList<string>, CancellationToken, Task<IReadOnlyList<Embedding<float>>>>? generate =
            generateEmbeddingsAsyncOverride;

        if (generate == null && generator != null)
        {
            var options = _embeddingOptions;
            generate = async (inputs, cancellationToken) =>
            {
                var embeddings = await generator.GenerateAsync(inputs, options, cancellationToken);
                return embeddings;
            };
        }

        if (generate == null)
            return Array.Empty<NotebookContextSlice>();

        // Build query embedding once
        const int maxQueryChars = 2000;
        var q = query.Replace("\r", "").Trim();
        if (q.Length > maxQueryChars)
            q = q[..maxQueryChars];

        var qEmbeddings = await generate(new[] { q }, ct);
        if (qEmbeddings.Count == 0)
            return Array.Empty<NotebookContextSlice>();

        var qEmbedding = qEmbeddings[0].Vector.Span.ToArray();

        var perSource = Math.Max(0, budget.MaxChunksPerSource);
        if (perSource == 0)
            return Array.Empty<NotebookContextSlice>();

        var slices = new List<NotebookContextSlice>(capacity: Math.Min(sourceIds.Count * perSource, 64));

        foreach (var sourceId in sourceIds)
        {
            ct.ThrowIfCancellationRequested();

            var memoryId = $"source::{sourceId}";

            IReadOnlyList<MemoryVectorMatch> matches;
            try
            {
                matches = await _vectorIndex.SearchAsync(
                    queryEmbedding: qEmbedding,
                    limit: perSource,
                    memoryId: memoryId,
                    scopeTypeFilter: MemoryScopeType.Graph,
                    scopeId: sourceId,
                    ct: ct);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "[Notebook] Vector search failed for {MemoryId} (best-effort).", memoryId);
                continue;
            }

            foreach (var m in matches)
            {
                ct.ThrowIfCancellationRequested();

                var record = m.Record;
                record.Tags.TryGetValue("chunk_id", out var chunkId);

                var slice = new NotebookContextSlice
                {
                    Kind = NotebookContextSliceKind.SourceChunk,
                    SourceId = sourceId,
                    ChunkId = chunkId ?? string.Empty,
                    Content = (record.Content ?? string.Empty).Replace("\r", "").Trim(),
                    Score = (float)m.Similarity,
                    Reason = "relevance:vector_topk"
                };
                slice.Tags["memory_id"] = memoryId;
                slice.Tags["entry_id"] = record.EntryId ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(chunkId))
                    slice.Tags["chunk_id"] = chunkId!;

                slices.Add(slice);
            }
        }

        return slices;
    }

    private async Task<IReadOnlyList<NotebookContextSlice>> BuildLexicalTopKAsync(
        string query,
        IReadOnlyList<string> sourceIds,
        NotebookContextBudget budget,
        CancellationToken ct)
    {
        var perSource = Math.Max(0, budget.MaxChunksPerSource);
        if (perSource == 0)
            return Array.Empty<NotebookContextSlice>();

        var slices = new List<NotebookContextSlice>(capacity: Math.Min(sourceIds.Count * perSource, 64));

        foreach (var sourceId in sourceIds)
        {
            ct.ThrowIfCancellationRequested();

            var memoryId = $"source::{sourceId}";
            IReadOnlyList<MemoryEntry> hits;
            try
            {
                hits = await _store.SearchAsync(
                    query,
                    limit: Math.Max(perSource * 4, perSource),
                    scopeTypeFilter: MemoryScopeType.Graph,
                    memoryId: memoryId,
                    ct: ct);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "[Notebook] Store lexical search failed for {MemoryId} (best-effort).", memoryId);
                continue;
            }

            foreach (var e in hits.Where(e => string.Equals(e.Role, "source_chunk", StringComparison.Ordinal)).Take(perSource))
            {
                ct.ThrowIfCancellationRequested();

                e.Tags.TryGetValue("chunk_id", out var chunkId);

                var slice = new NotebookContextSlice
                {
                    Kind = NotebookContextSliceKind.SourceChunk,
                    SourceId = sourceId,
                    ChunkId = chunkId ?? string.Empty,
                    Content = (e.Content ?? string.Empty).Replace("\r", "").Trim(),
                    Score = 0,
                    Reason = "relevance:lexical_fallback"
                };
                slice.Tags["memory_id"] = memoryId;
                slice.Tags["entry_id"] = e.EntryId ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(chunkId))
                    slice.Tags["chunk_id"] = chunkId!;

                slices.Add(slice);
            }
        }

        return slices;
    }

    private static List<NotebookContextSlice> DeduplicateChunkSlices(IEnumerable<NotebookContextSlice> slices)
    {
        var byKey = new Dictionary<string, NotebookContextSlice>(StringComparer.Ordinal);

        foreach (var s in slices)
        {
            var sourceId = s.SourceId ?? string.Empty;
            var chunkId = s.ChunkId ?? string.Empty;
            var key = $"{sourceId}::{chunkId}";

            // Keep the best score when duplicates occur.
            if (!byKey.TryGetValue(key, out var existing) || s.Score > existing.Score)
            {
                byKey[key] = s;
            }
        }

        return byKey.Values.ToList();
    }

    private static int ComputePreviewCharsPerSource(int sourceCount, NotebookContextBudget budget)
    {
        // Reserve roughly half budget for previews; the rest for top‑k chunks + headers.
        // Deterministic formula; avoids special-casing.
        var count = Math.Max(1, sourceCount);
        var per = budget.MaxTotalChars / count / 2;
        per = Math.Max(80, per);
        per = Math.Min(per, budget.MaxPerSourceChars);
        return Math.Max(0, per);
    }

    private async Task<IEmbeddingGenerator<string, Embedding<float>>?> TryGetEmbeddingGeneratorAsync(CancellationToken ct)
    {
        if (_embeddingGenerator != null)
            return _embeddingGenerator;

        if (_embeddingFactory == null || _llmProviderFactory == null)
            return null;

        await _embeddingInitLock.WaitAsync(ct);
        try
        {
            if (_embeddingGenerator != null)
                return _embeddingGenerator;

            var cfg = _llmProviderFactory.GetDefaultProviderConfig();
            if (cfg.Embeddings == null)
                return null;

            _embeddingOptions = BuildEmbeddingOptions(cfg);
            _embeddingGenerator = await _embeddingFactory.CreateAsync(cfg, ct);
            return _embeddingGenerator;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "[Notebook] Embedding generator init failed (best-effort).");
            return null;
        }
        finally
        {
            _embeddingInitLock.Release();
        }
    }

    private static EmbeddingGenerationOptions BuildEmbeddingOptions(Aevatar.Agents.AI.Abstractions.Configuration.LLMProviderConfig cfg)
    {
        var options = new EmbeddingGenerationOptions();

        if (cfg.Embeddings != null)
        {
            if (!string.IsNullOrWhiteSpace(cfg.Embeddings.Model))
            {
                options.ModelId = cfg.Embeddings.Model;
            }
            else if (!string.IsNullOrWhiteSpace(cfg.Model))
            {
                options.ModelId = cfg.Model;
            }

            if (cfg.Embeddings.Dimensions.HasValue)
            {
                options.Dimensions = cfg.Embeddings.Dimensions;
            }
        }
        else if (!string.IsNullOrWhiteSpace(cfg.Model))
        {
            options.ModelId = cfg.Model;
        }

        return options;
    }

    private static string Render(NotebookContext context, int maxTotalChars)
    {
        var sources = context.Slices.Count(s => s.Kind == NotebookContextSliceKind.SourcePreview);
        var chunks = context.Slices.Count(s => s.Kind == NotebookContextSliceKind.SourceChunk);

        maxTotalChars = Math.Max(0, maxTotalChars);

        var previewSlices = context.Slices
            .Where(s => s.Kind == NotebookContextSliceKind.SourcePreview)
            .ToList();

        var chunkSlices = context.Slices
            .Where(s => s.Kind == NotebookContextSliceKind.SourceChunk)
            .ToList();

        // ------------------------------------------------------------
        //  Coverage-first budget math:
        //  - Precompute minimal representation for all sources, then
        //    distribute remaining chars to per-source preview content.
        // ------------------------------------------------------------
        var previewMinLens = new int[previewSlices.Count];
        for (var i = 0; i < previewSlices.Count; i++)
        {
            var id = (previewSlices[i].SourceId ?? string.Empty).Trim();
            var header = $"[source:{id}]";

            // Minimal:
            // header + '\n' + '\n'
            previewMinLens[i] = header.Length + 2;
        }

        var totalPreviewMin = previewMinLens.Sum();

        context.Tags.TryGetValue("strategy", out var strategy);
        strategy ??= string.Empty;

        var headerLine = $"Notebook context (sources={sources}, chunks={chunks}, strategy={strategy})";
        var headerLineLen = headerLine.Length + 2;

        var sb = new StringBuilder(capacity: Math.Min(maxTotalChars + 64, 4096));

        // Only print header if it won't steal space from the minimal source coverage.
        if (headerLineLen + totalPreviewMin <= maxTotalChars)
        {
            sb.AppendLine(headerLine);
            sb.AppendLine();
        }

        // 1) Previews (guarantee coverage whenever possible)
        for (var i = 0; i < previewSlices.Count; i++)
        {
            if (sb.Length >= maxTotalChars)
                break;

            var s = previewSlices[i];
            var sourceId = (s.SourceId ?? string.Empty).Trim();
            var title = s.Tags.TryGetValue("title", out var t) ? t : string.Empty;

            // Reserve minimal chars for remaining sources.
            var remainingMin = 0;
            for (var j = i + 1; j < previewMinLens.Length; j++)
                remainingMin += previewMinLens[j];

            var minimalHeader = $"[source:{sourceId}]";
            var fullHeader = string.IsNullOrWhiteSpace(title) ? minimalHeader : $"{minimalHeader} {title.Trim()}";

            // Ensure header fits; fall back to minimal if needed.
            var headerToUse = fullHeader;
            var headerFootprint = headerToUse.Length + 2; // '\n' + '\n'
            if (sb.Length + headerFootprint + remainingMin > maxTotalChars)
            {
                headerToUse = minimalHeader;
                headerFootprint = headerToUse.Length + 2;
            }

            // If even minimal can't fit, stop.
            if (sb.Length + headerFootprint + remainingMin > maxTotalChars)
                break;

            sb.AppendLine(headerToUse);

            // Content budget for this source (after reserving remaining minimum coverage).
            // We will append:
            // - content line: content + '\n'
            // - trailing blank line: '\n'
            // So reserve 2 chars here.
            var contentBudget = maxTotalChars - remainingMin - sb.Length - 2;
            if (contentBudget > 0 && !string.IsNullOrWhiteSpace(s.Content))
            {
                var content = s.Content.Replace("\r", "").Trim();
                if (content.Length > contentBudget)
                    content = content[..contentBudget];
                if (content.Length > 0)
                    sb.AppendLine(content);
            }

            sb.AppendLine();
        }

        // 2) Chunks (best-effort; fill remaining budget)
        foreach (var s in chunkSlices)
        {
            if (sb.Length >= maxTotalChars)
                break;

            var sourceId = (s.SourceId ?? string.Empty).Trim();
            var chunkId = (s.ChunkId ?? string.Empty).Trim();

            var score = s.Score;
            var scoreSuffix = score > 0 ? $" score:{score:0.###}" : string.Empty;
            var header = $"[source:{sourceId} chunk:{chunkId}{scoreSuffix}]";

            // Minimal chunk footprint: header + '\n' + '\n'
            var minFootprint = header.Length + 2;
            if (sb.Length + minFootprint > maxTotalChars)
                break;

            sb.AppendLine(header);

            // Same as preview: reserve 2 chars for "\n" (content line) + "\n" (blank line).
            var contentBudget = maxTotalChars - sb.Length - 2;
            if (contentBudget > 0 && !string.IsNullOrWhiteSpace(s.Content))
            {
                var content = s.Content.Replace("\r", "").Trim();
                if (content.Length > contentBudget)
                    content = content[..contentBudget];
                if (content.Length > 0)
                    sb.AppendLine(content);
            }

            sb.AppendLine();
        }

        var result = sb.ToString().Trim();
        if (result.Length > maxTotalChars)
            result = result[..maxTotalChars];

        return result;
    }
}

internal sealed record NotebookContextBuildRequest
{
    public string? NotebookId { get; init; }
    public string? Query { get; init; }
    public List<string>? SelectedSourceIds { get; init; }
    public NotebookContextBudget? Budget { get; init; }
}

internal sealed record NotebookContextBuildResult
{
    public required List<string> SourceIds { get; init; }
    public required NotebookContext Context { get; init; }
    public required string Rendered { get; init; }
}


