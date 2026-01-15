using Aevatar.Agents.Abstractions.Memory;
using Aevatar.Notebook.Sources;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Aevatar.Notebook.Tests;

public sealed class SourceIndexerTests
{
    [Fact]
    public async Task IndexTextSourceAsync_ShouldAppendSourceAndChunkEntries()
    {
        var store = new FakeMemoryStore();
        var vector = new FakeVectorIndex();
        var chunker = new SourceChunker();

        var indexer = new SourceIndexer(
            chunker,
            store,
            NullLogger<SourceIndexer>.Instance,
            vectorIndex: vector,
            llmProviderFactory: null,
            embeddingFactory: null);

        var result = await indexer.IndexTextSourceAsync(
            new SourceIndexRequest
            {
                SourceId = "s1",
                Title = "t",
                Text = "cats are lovely.\n" + new string('x', 3000),
                ChunkingOptions = new SourceChunkingOptions { MaxChunkChars = 500, OverlapChars = 50, MaxChunks = 10 }
            },
            generateEmbeddingsAsyncOverride: DeterministicEmbeddingsAsync,
            ct: CancellationToken.None);

        result.SourceId.ShouldBe("s1");
        result.MemoryId.ShouldBe("source::s1");
        result.ChunkCount.ShouldBeGreaterThan(0);

        // 1 source entry + N chunk entries
        store.Appended.Count.ShouldBe(1 + result.ChunkCount);

        var source = store.Appended[0];
        source.Role.ShouldBe("source");
        source.MemoryId.ShouldBe("source::s1");
        source.Scope.Type.ShouldBe(MemoryScopeType.Graph);
        source.Scope.ScopeId.ShouldBe("s1");
        source.Tags["title"].ShouldBe("t");

        var chunks = store.Appended.Skip(1).ToList();
        chunks.Count.ShouldBe(result.ChunkCount);
        chunks.All(e => e.Role == "source_chunk").ShouldBeTrue();

        // Vector records should be upserted for each chunk.
        vector.Upserted.Count.ShouldBe(result.ChunkCount);
        foreach (var r in vector.Upserted)
        {
            r.MemoryId.ShouldBe("source::s1");
            r.Scope.Type.ShouldBe(MemoryScopeType.Graph);
            r.Scope.ScopeId.ShouldBe("s1");
            r.Tags.ShouldContainKey("chunk_id");
            r.Tags.ShouldContainKey("chunk_index");
            r.Content.Length.ShouldBeLessThanOrEqualTo(2000);
            r.Embedding.Count.ShouldBeGreaterThan(0);
        }
    }

    [Fact]
    public async Task IndexTextSourceAsync_ShouldSkipVectorUpsert_WhenNoEmbeddingGenerator()
    {
        var store = new FakeMemoryStore();
        var vector = new FakeVectorIndex();
        var chunker = new SourceChunker();

        var indexer = new SourceIndexer(
            chunker,
            store,
            NullLogger<SourceIndexer>.Instance,
            vectorIndex: vector,
            llmProviderFactory: null,
            embeddingFactory: null);

        var result = await indexer.IndexTextSourceAsync(
            new SourceIndexRequest
            {
                SourceId = "s2",
                Text = "hello world",
                ChunkingOptions = new SourceChunkingOptions { MaxChunkChars = 5, OverlapChars = 0, MaxChunks = 10 }
            },
            generateEmbeddingsAsyncOverride: null,
            ct: CancellationToken.None);

        result.VectorUpsertCount.ShouldBe(0);
        vector.Upserted.Count.ShouldBe(0);
    }

    private static Task<IReadOnlyList<Embedding<float>>> DeterministicEmbeddingsAsync(
        IReadOnlyList<string> inputs,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var list = new List<Embedding<float>>(inputs.Count);
        foreach (var s in inputs)
        {
            list.Add(Embed(s));
        }

        return Task.FromResult<IReadOnlyList<Embedding<float>>>(list);
    }

    private static Embedding<float> Embed(string? text)
    {
        var t = (text ?? string.Empty).ToLowerInvariant();
        var vec = t.Contains("cat") ? new[] { 1f, 0f } : new[] { 0f, 1f };
        return new Embedding<float>(vec);
    }

    private sealed class FakeMemoryStore : IMemoryStore
    {
        public List<MemoryEntry> Appended { get; } = new();

        public Task AppendAsync(MemoryEntry entry, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            Appended.Add(entry);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<MemoryResourceSummary>> ListResourcesAsync(
            MemoryScopeType? scopeTypeFilter = null,
            int limit = 200,
            CancellationToken ct = default) =>
            throw new NotSupportedException("Not needed for this unit test.");

        public Task<IReadOnlyList<MemoryEntry>> ListEntriesAsync(string memoryId, int limit = 200, CancellationToken ct = default) =>
            throw new NotSupportedException("Not needed for this unit test.");

        public Task<IReadOnlyList<MemoryEntry>> SearchAsync(
            string query,
            int limit = 50,
            MemoryScopeType? scopeTypeFilter = null,
            string? memoryId = null,
            CancellationToken ct = default) =>
            throw new NotSupportedException("Not needed for this unit test.");
    }

    private sealed class FakeVectorIndex : IMemoryVectorIndex
    {
        public List<MemoryVectorRecord> Upserted { get; } = new();

        public Task UpsertAsync(MemoryVectorRecord record, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            Upserted.Add(record);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<MemoryVectorMatch>> SearchAsync(
            IReadOnlyList<float> queryEmbedding,
            int limit = 20,
            string? memoryId = null,
            MemoryScopeType? scopeTypeFilter = null,
            string? scopeId = null,
            CancellationToken ct = default) =>
            throw new NotSupportedException("Not needed for this unit test.");
    }
}


