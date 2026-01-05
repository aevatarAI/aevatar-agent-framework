using Aevatar.Agents.Abstractions.Memory;
using Aevatar.Agents.AI.Abstractions.Providers;
using Aevatar.Agents.AI.Core.Embeddings;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Aevatar.Notebook.Sources;

// ============================================================
//  SourceIndexer
//
//  目标：
//  - 将 source（文本）写入 MemoryStore（Layer 4）
//  - 将 source chunks 写入 MemoryStore（Layer 4）
//  - 可选：将 chunks 的 embeddings 写入 VectorIndex（Layer 4.1）
//
//  关键原则：
//  - Best-effort：向量/embedding 失败不影响主流程（仍写入 store）
//  - 有界：每次索引的文本、chunks 数、embedding 输入都必须限制
//  - 可测试：允许 override embedding 生成回调（单测不依赖真实 LLM）
// ============================================================
internal sealed class SourceIndexer
{
    private readonly SourceChunker _chunker;
    private readonly IMemoryStore _store;
    private readonly IMemoryVectorIndex? _vectorIndex;
    private readonly ILogger<SourceIndexer> _logger;
    private readonly ILLMProviderFactory? _llmProviderFactory;
    private readonly IAIAgentEmbeddingFactory? _embeddingFactory;

    private readonly SemaphoreSlim _embeddingInitLock = new(1, 1);
    private IEmbeddingGenerator<string, Embedding<float>>? _embeddingGenerator;
    private EmbeddingGenerationOptions? _embeddingOptions;

    public SourceIndexer(
        SourceChunker chunker,
        IMemoryStore store,
        ILogger<SourceIndexer> logger,
        IMemoryVectorIndex? vectorIndex = null,
        ILLMProviderFactory? llmProviderFactory = null,
        IAIAgentEmbeddingFactory? embeddingFactory = null)
    {
        _chunker = chunker ?? throw new ArgumentNullException(nameof(chunker));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _vectorIndex = vectorIndex;
        _llmProviderFactory = llmProviderFactory;
        _embeddingFactory = embeddingFactory;
    }

    public async Task<SourceIndexResult> IndexTextSourceAsync(
        SourceIndexRequest request,
        Func<IReadOnlyList<string>, CancellationToken, Task<IReadOnlyList<Embedding<float>>>>? generateEmbeddingsAsyncOverride = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ct.ThrowIfCancellationRequested();

        var sourceId = (request.SourceId ?? string.Empty).Trim();
        if (sourceId.Length == 0)
            throw new ArgumentException("SourceId is required.", nameof(request));

        var title = (request.Title ?? string.Empty).Trim();

        // ------------------------------------------------------------
        //  Bound inputs (avoid using MemoryStore as a blob store).
        // ------------------------------------------------------------
        const int maxSourceChars = 200_000;
        var normalized = _chunker.Normalize(request.Text);
        if (normalized.Length == 0)
            throw new ArgumentException("Text is required.", nameof(request));
        if (normalized.Length > maxSourceChars)
            throw new InvalidOperationException($"Source text too large: {normalized.Length} chars (max {maxSourceChars}).");

        var now = Timestamp.FromDateTime(DateTime.UtcNow);
        var memoryId = $"source::{sourceId}";
        var scope = new MemoryScope { Type = MemoryScopeType.Graph, ScopeId = sourceId };

        // ------------------------------------------------------------
        //  1) Append source entry (raw text)
        // ------------------------------------------------------------
        var sourceEntry = new MemoryEntry
        {
            EntryId = Guid.NewGuid().ToString("N"),
            MemoryId = memoryId,
            Scope = scope,
            Role = "source",
            Content = normalized,
            CreatedAt = now
        };
        sourceEntry.Tags["source_id"] = sourceId;
        sourceEntry.Tags["kind"] = "source";
        sourceEntry.Tags["mime_type"] = request.MimeType ?? "text/plain";
        if (title.Length > 0)
            sourceEntry.Tags["title"] = title;

        await _store.AppendAsync(sourceEntry, ct);

        // ------------------------------------------------------------
        //  2) Chunk + append chunk entries
        // ------------------------------------------------------------
        var chunks = _chunker.Chunk(normalized, request.ChunkingOptions);
        var chunkEntries = new List<MemoryEntry>(capacity: chunks.Count);

        foreach (var chunk in chunks)
        {
            ct.ThrowIfCancellationRequested();

            var chunkId = $"{sourceId}:{chunk.ChunkIndex}";

            var chunkEntry = new MemoryEntry
            {
                EntryId = Guid.NewGuid().ToString("N"),
                MemoryId = memoryId,
                Scope = scope,
                Role = "source_chunk",
                Content = chunk.Content,
                CreatedAt = now
            };

            chunkEntry.Tags["source_id"] = sourceId;
            chunkEntry.Tags["chunk_id"] = chunkId;
            chunkEntry.Tags["chunk_index"] = chunk.ChunkIndex.ToString();
            chunkEntry.Tags["offset_start"] = chunk.OffsetStart.ToString();
            chunkEntry.Tags["offset_end"] = chunk.OffsetEnd.ToString();
            if (title.Length > 0)
                chunkEntry.Tags["title"] = title;

            await _store.AppendAsync(chunkEntry, ct);
            chunkEntries.Add(chunkEntry);
        }

        // ------------------------------------------------------------
        //  3) Optional: upsert vectors (best-effort)
        // ------------------------------------------------------------
        var vectorUpserts = 0;
        if (_vectorIndex != null && chunkEntries.Count > 0)
        {
            try
            {
                vectorUpserts = await UpsertVectorsAsync(
                    chunkEntries,
                    generateEmbeddingsAsyncOverride,
                    ct);
            }
            catch (Exception ex)
            {
                // Best-effort: never fail indexing due to vectors.
                _logger.LogDebug(ex, "[Notebook] Vector indexing failed (best-effort).");
            }
        }

        return new SourceIndexResult
        {
            SourceId = sourceId,
            MemoryId = memoryId,
            SourceEntryId = sourceEntry.EntryId,
            ChunkCount = chunkEntries.Count,
            VectorUpsertCount = vectorUpserts
        };
    }

    private async Task<int> UpsertVectorsAsync(
        IReadOnlyList<MemoryEntry> chunkEntries,
        Func<IReadOnlyList<string>, CancellationToken, Task<IReadOnlyList<Embedding<float>>>>? generateEmbeddingsAsyncOverride,
        CancellationToken ct)
    {
        if (_vectorIndex == null)
            return 0;

        // Prepare generator (override > factory-configured).
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
            return 0;

        // Keep per-input bounded (avoid huge embedding calls).
        const int maxEmbeddingChars = 2000;
        const int batchSize = 16;

        var upserts = 0;
        for (var i = 0; i < chunkEntries.Count; i += batchSize)
        {
            ct.ThrowIfCancellationRequested();

            var batch = chunkEntries
                .Skip(i)
                .Take(batchSize)
                .ToList();

            var inputs = new List<string>(batch.Count);
            foreach (var e in batch)
            {
                var t = (e.Content ?? string.Empty).Replace("\r", "").Trim();
                if (t.Length > maxEmbeddingChars)
                    t = t[..maxEmbeddingChars];
                inputs.Add(t);
            }

            var embeddings = await generate(inputs, ct);
            if (embeddings.Count != batch.Count)
                throw new InvalidOperationException("Embedding generator returned mismatched count.");

            for (var j = 0; j < batch.Count; j++)
            {
                ct.ThrowIfCancellationRequested();

                var entry = batch[j];
                var embedding = embeddings[j];

                var record = new MemoryVectorRecord
                {
                    EntryId = entry.EntryId ?? string.Empty,
                    MemoryId = entry.MemoryId ?? string.Empty,
                    Scope = entry.Scope,
                    RunId = entry.RunId ?? string.Empty,
                    AgentId = entry.AgentId ?? string.Empty,
                    Role = entry.Role ?? string.Empty,
                    CreatedAt = entry.CreatedAt,
                    Content = inputs[j]
                };

                foreach (var v in embedding.Vector.Span)
                    record.Embedding.Add(v);

                foreach (var kv in entry.Tags)
                    record.Tags[kv.Key] = kv.Value;

                await _vectorIndex.UpsertAsync(record, ct);
                upserts++;
            }
        }

        return upserts;
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

            // Best-effort: use default provider config.
            var cfg = _llmProviderFactory.GetDefaultProviderConfig();
            if (cfg is not { Embeddings.Enabled: true })
                return null;

            _embeddingOptions = BuildEmbeddingOptions(cfg);

            var generator = await _embeddingFactory.CreateAsync(cfg, ct);
            _embeddingGenerator = generator;
            return generator;
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
}

internal sealed record SourceIndexRequest
{
    public required string SourceId { get; init; }
    public required string Text { get; init; }

    public string? Title { get; init; }
    public string? MimeType { get; init; }

    public SourceChunkingOptions? ChunkingOptions { get; init; }
}

internal sealed record SourceIndexResult
{
    public required string SourceId { get; init; }
    public required string MemoryId { get; init; }
    public required string SourceEntryId { get; init; }
    public required int ChunkCount { get; init; }
    public required int VectorUpsertCount { get; init; }
}


