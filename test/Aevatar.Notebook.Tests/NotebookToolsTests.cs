using System.Text.Json;
using Aevatar.Agents.Abstractions.Memory;
using Aevatar.Agents.AI;
using Aevatar.Agents.AI.Tool.Abstractions;
using Aevatar.Notebook.Tools;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Aevatar.Notebook.Tests;

public sealed class NotebookToolsTests
{
    [Fact]
    public async Task ListSourcesTool_ShouldReturnSources()
    {
        var store = new FakeMemoryStore();
        store.SeedSourceResource("s1");
        store.SeedSourceResource("s2");

        var tool = new ListSourcesTool(store);

        var result = await tool.ExecuteAsync(
            new Dictionary<string, object> { ["limit"] = 50 },
            new ToolContext(),
            NullLogger.Instance,
            CancellationToken.None);

        var json = JsonFormatter.Default.Format(result);
        using var doc = JsonDocument.Parse(json);

        doc.RootElement.GetProperty("count").GetInt32().ShouldBe(2);
        var sources = doc.RootElement.GetProperty("sources");
        sources.GetArrayLength().ShouldBe(2);
    }

    [Fact]
    public async Task RetrieveChunksTool_ShouldUseVector_WhenEmbeddingsProvided()
    {
        var store = new FakeMemoryStore();
        var vector = new FakeVectorIndex();
        vector.AddMatch(
            memoryId: "source::s1",
            sourceId: "s1",
            chunkId: "s1:0",
            entryId: "e1",
            content: "cats are lovely",
            similarity: 0.9);

        var tool = new RetrieveChunksTool(store, vector);

        var ctx = new ToolContext
        {
            GenerateEmbeddingsAsync = DeterministicEmbeddingsAsync
        };

        var result = await tool.ExecuteAsync(
            new Dictionary<string, object>
            {
                ["query"] = "cats",
                ["maxResults"] = 5,
                ["strategy"] = "auto"
            },
            ctx,
            NullLogger.Instance,
            CancellationToken.None);

        var json = JsonFormatter.Default.Format(result);
        using var doc = JsonDocument.Parse(json);

        doc.RootElement.GetProperty("strategyUsed").GetString().ShouldBe("vector");
        doc.RootElement.GetProperty("count").GetInt32().ShouldBe(1);
        doc.RootElement.GetProperty("results")[0].GetProperty("chunkId").GetString().ShouldBe("s1:0");
    }

    [Fact]
    public async Task RetrieveChunksTool_ShouldFallbackToLexical_WhenNoEmbeddings()
    {
        var store = new FakeMemoryStore();
        store.SeedChunk("s1", "s1:0", "cats are lovely");

        var tool = new RetrieveChunksTool(store, vectorIndex: null);

        var ctx = new ToolContext { GenerateEmbeddingsAsync = null };

        var result = await tool.ExecuteAsync(
            new Dictionary<string, object>
            {
                ["query"] = "cats",
                ["maxResults"] = 5,
                ["strategy"] = "auto"
            },
            ctx,
            NullLogger.Instance,
            CancellationToken.None);

        var json = JsonFormatter.Default.Format(result);
        using var doc = JsonDocument.Parse(json);

        doc.RootElement.GetProperty("strategyUsed").GetString().ShouldBe("lexical");
        doc.RootElement.GetProperty("count").GetInt32().ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task GetExecutionGraphTool_ShouldLoadGraph()
    {
        var graphStore = new FakeGraphStore();
        graphStore.Graph = new MemoryGraph
        {
            GraphId = "exec-1",
            Scope = new MemoryScope { Type = MemoryScopeType.Execution, ScopeId = "exec-1" },
            Nodes =
            {
                new MemoryGraphNode { NodeId = "n1", Type = "t", Name = "name", Content = "c" }
            },
            Edges =
            {
                new MemoryGraphEdge { EdgeId = "e1", FromNodeId = "n1", ToNodeId = "n1", Type = "self", Label = "self" }
            }
        };

        var tool = new GetExecutionGraphTool(graphStore);

        var result = await tool.ExecuteAsync(
            new Dictionary<string, object> { ["executionId"] = "exec-1" },
            new ToolContext(),
            NullLogger.Instance,
            CancellationToken.None);

        var json = JsonFormatter.Default.Format(result);
        using var doc = JsonDocument.Parse(json);

        doc.RootElement.GetProperty("ok").GetBoolean().ShouldBeTrue();
        doc.RootElement.GetProperty("nodeCount").GetInt32().ShouldBe(1);
        doc.RootElement.GetProperty("edgeCount").GetInt32().ShouldBe(1);
    }

    [Fact]
    public async Task GenerateReportTool_ShouldPersistReport()
    {
        var store = new FakeMemoryStore();
        store.SeedSourceResource("s1");
        store.SeedChunk("s1", "s1:0", "cats are lovely");

        var tool = new GenerateReportTool(
            store,
            vectorIndex: null,
            chatAsync: StubReportChatAsync);

        var result = await tool.ExecuteAsync(
            new Dictionary<string, object> { ["topic"] = "Cats" },
            new ToolContext(),
            NullLogger.Instance,
            CancellationToken.None);

        var json = JsonFormatter.Default.Format(result);
        using var doc = JsonDocument.Parse(json);

        doc.RootElement.GetProperty("ok").GetBoolean().ShouldBeTrue();
        var reportId = doc.RootElement.GetProperty("reportId").GetString();
        reportId.ShouldNotBeNullOrWhiteSpace();

        // Report entry should have been appended into MemoryStore.
        store.HasReport(reportId!).ShouldBeTrue();
    }

    [Fact]
    public async Task GetReportTool_ShouldReturnLatestVersion()
    {
        var store = new FakeMemoryStore();
        store.SeedReport("r1", version: 1, topic: "T1", content: "v1");
        store.SeedReport("r1", version: 2, topic: "T1", content: "v2");

        var tool = new GetReportTool(store);

        var result = await tool.ExecuteAsync(
            new Dictionary<string, object>
            {
                ["reportId"] = "r1",
                ["maxChars"] = 10
            },
            new ToolContext(),
            NullLogger.Instance,
            CancellationToken.None);

        var json = JsonFormatter.Default.Format(result);
        using var doc = JsonDocument.Parse(json);

        doc.RootElement.GetProperty("found").GetBoolean().ShouldBeTrue();
        doc.RootElement.GetProperty("version").GetInt32().ShouldBe(2);
        doc.RootElement.GetProperty("content").GetString().ShouldBe("v2");
    }

    private static Task<IReadOnlyList<Embedding<float>>> DeterministicEmbeddingsAsync(
        IReadOnlyList<string> inputs,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var list = new List<Embedding<float>>(inputs.Count);
        foreach (var _ in inputs)
        {
            list.Add(new Embedding<float>(new[] { 1f, 0f }));
        }
        return Task.FromResult<IReadOnlyList<Embedding<float>>>(list);
    }

    private static Task<Aevatar.Agents.AI.ChatResponse> StubReportChatAsync(
        Aevatar.Agents.AI.ChatRequest request,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var content = request.StageHint switch
        {
            "report:outline" => "OUTLINE",
            "report:draft" => "DRAFT",
            "report:refine" => "FINAL",
            _ => "OK"
        };

        return Task.FromResult(new Aevatar.Agents.AI.ChatResponse { Content = content });
    }

    private sealed class FakeMemoryStore : IMemoryStore
    {
        private readonly List<MemoryResourceSummary> _resources = new();
        private readonly Dictionary<string, List<MemoryEntry>> _entries = new(StringComparer.Ordinal);

        public void SeedSourceResource(string sourceId)
        {
            var memoryId = $"source::{sourceId}";
            _resources.Add(new MemoryResourceSummary
            {
                MemoryId = memoryId,
                Scope = new MemoryScope { Type = MemoryScopeType.Graph, ScopeId = sourceId },
                EntryCount = 1,
                LatestAt = Timestamp.FromDateTime(DateTime.UtcNow)
            });
        }

        public void SeedChunk(string sourceId, string chunkId, string content)
        {
            var memoryId = $"source::{sourceId}";
            if (!_entries.TryGetValue(memoryId, out var list))
            {
                list = new List<MemoryEntry>();
                _entries[memoryId] = list;
            }

            list.Add(new MemoryEntry
            {
                EntryId = Guid.NewGuid().ToString("N"),
                MemoryId = memoryId,
                Scope = new MemoryScope { Type = MemoryScopeType.Graph, ScopeId = sourceId },
                Role = "source_chunk",
                Content = content,
                CreatedAt = Timestamp.FromDateTime(DateTime.UtcNow),
                Tags =
                {
                    ["source_id"] = sourceId,
                    ["chunk_id"] = chunkId,
                    ["chunk_index"] = "0"
                }
            });
        }

        public void SeedReport(string reportId, int version, string topic, string content)
        {
            var memoryId = $"report::{reportId}";
            if (!_entries.TryGetValue(memoryId, out var list))
            {
                list = new List<MemoryEntry>();
                _entries[memoryId] = list;
            }

            var entry = new MemoryEntry
            {
                EntryId = Guid.NewGuid().ToString("N"),
                MemoryId = memoryId,
                Scope = new MemoryScope { Type = MemoryScopeType.Graph, ScopeId = reportId },
                Role = "report",
                Content = content,
                CreatedAt = Timestamp.FromDateTime(DateTime.UtcNow)
            };
            entry.Tags["report_id"] = reportId;
            entry.Tags["version"] = version.ToString();
            entry.Tags["topic"] = topic;

            list.Add(entry);
        }

        public bool HasReport(string reportId)
        {
            var memoryId = $"report::{reportId}";
            return _entries.TryGetValue(memoryId, out var list) &&
                   list.Any(e => string.Equals(e.Role, "report", StringComparison.Ordinal));
        }

        public Task AppendAsync(MemoryEntry entry, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            var memoryId = entry.MemoryId ?? string.Empty;
            if (!_entries.TryGetValue(memoryId, out var list))
            {
                list = new List<MemoryEntry>();
                _entries[memoryId] = list;
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
            var list = _resources
                .Where(r => !scopeTypeFilter.HasValue || r.Scope.Type == scopeTypeFilter.Value)
                .Take(limit)
                .ToList();
            return Task.FromResult<IReadOnlyList<MemoryResourceSummary>>(list);
        }

        public Task<IReadOnlyList<MemoryEntry>> ListEntriesAsync(string memoryId, int limit = 200, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            _entries.TryGetValue(memoryId, out var list);
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

            IEnumerable<MemoryEntry> candidates;
            if (!string.IsNullOrWhiteSpace(memoryId) && _entries.TryGetValue(memoryId.Trim(), out var list))
            {
                candidates = list;
            }
            else
            {
                candidates = _entries.Values.SelectMany(v => v);
            }

            if (scopeTypeFilter.HasValue)
                candidates = candidates.Where(e => e.Scope.Type == scopeTypeFilter.Value);

            var hits = candidates
                .Where(e => (e.Content ?? string.Empty).Contains(q, StringComparison.OrdinalIgnoreCase))
                .Take(limit)
                .ToList();

            return Task.FromResult<IReadOnlyList<MemoryEntry>>(hits);
        }
    }

    private sealed class FakeVectorIndex : IMemoryVectorIndex
    {
        private readonly List<MemoryVectorMatch> _matches = new();

        public void AddMatch(string memoryId, string sourceId, string chunkId, string entryId, string content, double similarity)
        {
            var record = new MemoryVectorRecord
            {
                EntryId = entryId,
                MemoryId = memoryId,
                Scope = new MemoryScope { Type = MemoryScopeType.Graph, ScopeId = sourceId },
                Role = "source_chunk",
                Content = content
            };
            record.Tags["source_id"] = sourceId;
            record.Tags["chunk_id"] = chunkId;
            record.Embedding.Add(1f);

            _matches.Add(new MemoryVectorMatch { Record = record, Similarity = similarity });
        }

        public Task UpsertAsync(MemoryVectorRecord record, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<IReadOnlyList<MemoryVectorMatch>> SearchAsync(
            IReadOnlyList<float> queryEmbedding,
            int limit = 20,
            string? memoryId = null,
            MemoryScopeType? scopeTypeFilter = null,
            string? scopeId = null,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();

            var filtered = _matches.AsEnumerable();
            if (!string.IsNullOrWhiteSpace(memoryId))
                filtered = filtered.Where(m => string.Equals(m.Record.MemoryId, memoryId.Trim(), StringComparison.Ordinal));

            if (scopeTypeFilter.HasValue)
                filtered = filtered.Where(m => m.Record.Scope.Type == scopeTypeFilter.Value);

            if (!string.IsNullOrWhiteSpace(scopeId))
                filtered = filtered.Where(m => string.Equals(m.Record.Scope.ScopeId, scopeId.Trim(), StringComparison.Ordinal));

            return Task.FromResult<IReadOnlyList<MemoryVectorMatch>>(filtered.Take(limit).ToList());
        }
    }

    private sealed class FakeGraphStore : IMemoryGraphStore
    {
        public MemoryGraph? Graph { get; set; }

        public Task SaveAsync(MemoryGraph graph, CancellationToken ct = default)
        {
            Graph = graph;
            return Task.CompletedTask;
        }

        public Task<MemoryGraph?> LoadAsync(string graphId, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            if (Graph != null && string.Equals(Graph.GraphId, graphId, StringComparison.Ordinal))
                return Task.FromResult<MemoryGraph?>(Graph);
            return Task.FromResult<MemoryGraph?>(null);
        }

        public Task<bool> ExistsAsync(string graphId, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(Graph != null && string.Equals(Graph.GraphId, graphId, StringComparison.Ordinal));
        }

        public Task DeleteAsync(string graphId, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            if (Graph != null && string.Equals(Graph.GraphId, graphId, StringComparison.Ordinal))
                Graph = null;
            return Task.CompletedTask;
        }
    }
}


