using Aevatar.Agents.Abstractions.Memory;
using Aevatar.Notebook.Context;
using Aevatar.Notebook.Contracts;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Aevatar.Notebook.Tests;

public sealed class NotebookContextBuilderTests
{
    [Fact]
    public async Task BuildAsync_ShouldIncludeEverySourcePreview_EvenWhenBudgetIsTight()
    {
        var store = new FakeMemoryStore();

        store.Seed(
            sourceId: "s1",
            title: "Source One",
            sourceText: new string('a', 5000),
            chunkId: "s1:0",
            chunkText: "cats cats cats");

        store.Seed(
            sourceId: "s2",
            title: "Source Two",
            sourceText: new string('b', 5000),
            chunkId: "s2:0",
            chunkText: "dogs dogs dogs");

        var builder = new NotebookContextBuilder(
            store,
            NullLogger<NotebookContextBuilder>.Instance,
            vectorIndex: null,
            llmProviderFactory: null,
            embeddingFactory: null);

        var result = await builder.BuildAsync(
            new NotebookContextBuildRequest
            {
                Query = "cats",
                Budget = new NotebookContextBudget
                {
                    MaxTotalChars = 140, // deliberately tight
                    MaxPerSourceChars = 2000,
                    MaxChunks = 4,
                    MaxChunksPerSource = 2
                }
            },
            ct: CancellationToken.None);

        result.SourceIds.ShouldBe(["s1", "s2"]);

        // Coverage: headers for both sources should appear in rendered output.
        result.Rendered.ShouldContain("[source:s1]");
        result.Rendered.ShouldContain("[source:s2]");
    }

    [Fact]
    public async Task BuildAsync_ShouldFallbackToLexicalTopK_WhenVectorIndexUnavailable()
    {
        var store = new FakeMemoryStore();

        store.Seed(
            sourceId: "s1",
            title: "Source One",
            sourceText: "alpha",
            chunkId: "s1:0",
            chunkText: "cats are lovely");

        store.Seed(
            sourceId: "s2",
            title: "Source Two",
            sourceText: "beta",
            chunkId: "s2:0",
            chunkText: "dogs are loyal");

        var builder = new NotebookContextBuilder(
            store,
            NullLogger<NotebookContextBuilder>.Instance,
            vectorIndex: null,
            llmProviderFactory: null,
            embeddingFactory: null);

        var result = await builder.BuildAsync(
            new NotebookContextBuildRequest
            {
                Query = "cats",
                Budget = new NotebookContextBudget
                {
                    MaxTotalChars = 4000,
                    MaxPerSourceChars = 200,
                    MaxChunks = 4,
                    MaxChunksPerSource = 2
                }
            },
            ct: CancellationToken.None);

        // Should include at least one chunk slice via lexical fallback.
        result.Context.Slices.Any(s => s.Kind == NotebookContextSliceKind.SourceChunk).ShouldBeTrue();

        // Rendered output should contain chunk markers.
        result.Rendered.ShouldContain("chunk:");
    }

    private sealed class FakeMemoryStore : IMemoryStore
    {
        private readonly Dictionary<string, List<MemoryEntry>> _byMemoryId = new(StringComparer.Ordinal);

        public void Seed(
            string sourceId,
            string title,
            string sourceText,
            string chunkId,
            string chunkText)
        {
            var memoryId = $"source::{sourceId}";
            var scope = new MemoryScope { Type = MemoryScopeType.Graph, ScopeId = sourceId };
            var now = Timestamp.FromDateTime(DateTime.UtcNow);

            var source = new MemoryEntry
            {
                EntryId = $"{sourceId}-source",
                MemoryId = memoryId,
                Scope = scope,
                Role = "source",
                Content = sourceText,
                CreatedAt = now
            };
            source.Tags["title"] = title;
            source.Tags["source_id"] = sourceId;

            var chunk = new MemoryEntry
            {
                EntryId = $"{sourceId}-chunk-0",
                MemoryId = memoryId,
                Scope = scope,
                Role = "source_chunk",
                Content = chunkText,
                CreatedAt = now
            };
            chunk.Tags["chunk_id"] = chunkId;
            chunk.Tags["chunk_index"] = "0";
            chunk.Tags["source_id"] = sourceId;

            _byMemoryId[memoryId] = [source, chunk];
        }

        public Task AppendAsync(MemoryEntry entry, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            var key = entry.MemoryId ?? string.Empty;
            if (!_byMemoryId.TryGetValue(key, out var list))
            {
                list = new List<MemoryEntry>();
                _byMemoryId[key] = list;
            }
            list.Add(entry);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<MemoryResourceSummary>> ListResourcesAsync(
            MemoryScopeType? scopeTypeFilter = null,
            int limit = 200,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();

            var resources = new List<MemoryResourceSummary>();
            foreach (var (memoryId, entries) in _byMemoryId)
            {
                var first = entries.FirstOrDefault();
                var scope = first?.Scope ?? new MemoryScope { Type = MemoryScopeType.Graph, ScopeId = "" };

                if (scopeTypeFilter.HasValue && scope.Type != scopeTypeFilter.Value)
                    continue;

                var latest = entries.LastOrDefault()?.CreatedAt;
                resources.Add(new MemoryResourceSummary
                {
                    MemoryId = memoryId,
                    Scope = scope,
                    EntryCount = entries.Count,
                    LatestAt = latest
                });
            }

            return Task.FromResult<IReadOnlyList<MemoryResourceSummary>>(resources.Take(limit).ToList());
        }

        public Task<IReadOnlyList<MemoryEntry>> ListEntriesAsync(string memoryId, int limit = 200, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            _byMemoryId.TryGetValue(memoryId, out var list);
            list ??= new List<MemoryEntry>();
            return Task.FromResult<IReadOnlyList<MemoryEntry>>(list.Take(limit).ToList());
        }

        public Task<IReadOnlyList<MemoryEntry>> SearchAsync(
            string query,
            int limit = 50,
            MemoryScopeType? scopeTypeFilter = null,
            string? memoryId = null,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();

            var q = (query ?? string.Empty).Trim();
            if (q.Length == 0)
                return Task.FromResult<IReadOnlyList<MemoryEntry>>(Array.Empty<MemoryEntry>());

            var candidates = new List<MemoryEntry>();
            if (!string.IsNullOrWhiteSpace(memoryId) && _byMemoryId.TryGetValue(memoryId.Trim(), out var list))
            {
                candidates.AddRange(list);
            }
            else
            {
                foreach (var v in _byMemoryId.Values)
                    candidates.AddRange(v);
            }

            if (scopeTypeFilter.HasValue)
                candidates = candidates.Where(e => e.Scope.Type == scopeTypeFilter.Value).ToList();

            var hits = candidates
                .Where(e => (e.Content ?? string.Empty).Contains(q, StringComparison.OrdinalIgnoreCase))
                .Take(limit)
                .ToList();

            return Task.FromResult<IReadOnlyList<MemoryEntry>>(hits);
        }
    }
}


