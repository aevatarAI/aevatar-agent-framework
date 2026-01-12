using System.Text.Json;
using Aevatar.Agents.Abstractions.Memory;
using Aevatar.Agents.AI;
using Aevatar.Agents.AI.Abstractions;
using Aevatar.Agents.AI.Tool.Abstractions;
using Aevatar.Notebook.Context;
using Aevatar.Notebook.Contracts;
using Aevatar.Notebook.Reports;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;

using static Aevatar.Notebook.Tools.NotebookToolHelpers;

namespace Aevatar.Notebook.Tools;

// ============================================================
//  Notebook Tools (MVP)
//
//  目标：
//  - 为 NotebookAgent 提供一组“可被模型调用”的工具：
//    list_sources / get_source / retrieve_chunks / generate_report / get_report / get_execution_graph
//
//  约束：
//  - 输出必须有界（避免返回超大 JSON）
//  - 必须安全（不输出 secrets；不执行外部命令）
// ============================================================

internal sealed class ListSourcesTool : AevatarToolBase
{
    private readonly IMemoryStore _store;

    public ListSourcesTool(IMemoryStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public override string Name => "list_sources";
    public override string Description => "List notebook sources (memory resources with prefix source::)";
    public override ToolCategory Category => ToolCategory.Custom;

    public override ToolParameters CreateParameters()
    {
        return new ToolParameters
        {
            Required = Array.Empty<string>(),
            Items = new Dictionary<string, ToolParameter>
            {
                ["limit"] = new ToolParameter
                {
                    Type = "integer",
                    Description = "Max number of sources to return (1..200)",
                    Required = false,
                    DefaultValue = 50
                }
            }
        };
    }

    public override async Task<IMessage> ExecuteAsync(
        Dictionary<string, object> parameters,
        ToolContext context,
        ILogger? logger,
        CancellationToken cancellationToken = default)
    {
        var limit = ClampInt(parameters.GetValueOrDefault("limit"), 50, 1, 200);

        var resources = await _store.ListResourcesAsync(
            scopeTypeFilter: MemoryScopeType.Graph,
            limit: limit,
            ct: cancellationToken);

        var sources = resources
            .Where(r => (r.MemoryId ?? string.Empty).StartsWith("source::", StringComparison.Ordinal))
            .Select(r => new
            {
                sourceId = (r.MemoryId ?? string.Empty).Replace("source::", "", StringComparison.Ordinal),
                memoryId = r.MemoryId ?? string.Empty,
                entryCount = r.EntryCount,
                latestAt = r.LatestAt?.ToDateTime().ToString("O") ?? ""
            })
            .OrderBy(x => x.sourceId, StringComparer.Ordinal)
            .ToList();

        return ToStruct(new
        {
            count = sources.Count,
            sources
        });
    }
}

internal sealed class GetSourceTool : AevatarToolBase
{
    private readonly IMemoryStore _store;

    public GetSourceTool(IMemoryStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public override string Name => "get_source";
    public override string Description => "Get a source preview or a specific chunk by sourceId/chunkId";
    public override ToolCategory Category => ToolCategory.Custom;

    public override ToolParameters CreateParameters()
    {
        return new ToolParameters
        {
            Required = new[] { "sourceId" },
            Items = new Dictionary<string, ToolParameter>
            {
                ["sourceId"] = new ToolParameter
                {
                    Type = "string",
                    Description = "Source id (e.g. ab12cd34ef56)",
                    Required = true
                },
                ["chunkId"] = new ToolParameter
                {
                    Type = "string",
                    Description = "Optional chunk id (e.g. ab12cd34ef56:0)",
                    Required = false
                },
                ["maxChars"] = new ToolParameter
                {
                    Type = "integer",
                    Description = "Max chars for returned content preview (1..20000)",
                    Required = false,
                    DefaultValue = 2000
                },
                ["limit"] = new ToolParameter
                {
                    Type = "integer",
                    Description = "Max entries to scan when locating chunk/source (1..2000)",
                    Required = false,
                    DefaultValue = 400
                }
            }
        };
    }

    public override async Task<IMessage> ExecuteAsync(
        Dictionary<string, object> parameters,
        ToolContext context,
        ILogger? logger,
        CancellationToken cancellationToken = default)
    {
        var sourceId = (parameters.GetValueOrDefault("sourceId")?.ToString() ?? string.Empty).Trim();
        if (sourceId.Length == 0)
            throw new ArgumentException("sourceId is required");

        var chunkId = (parameters.GetValueOrDefault("chunkId")?.ToString() ?? string.Empty).Trim();
        var maxChars = ClampInt(parameters.GetValueOrDefault("maxChars"), 2000, 1, 20000);
        var limit = ClampInt(parameters.GetValueOrDefault("limit"), 400, 1, 2000);

        var memoryId = $"source::{sourceId}";
        var entries = await _store.ListEntriesAsync(memoryId, limit: limit, ct: cancellationToken);

        MemoryEntry? hit = null;
        if (chunkId.Length > 0)
        {
            hit = entries.FirstOrDefault(e =>
                e?.Tags != null &&
                e.Tags.TryGetValue("chunk_id", out var v) &&
                string.Equals(v, chunkId, StringComparison.Ordinal));
        }
        else
        {
            // Best-effort: prefer role=source (raw) for preview.
            hit = entries.LastOrDefault(e => string.Equals(e.Role, "source", StringComparison.Ordinal))
                  ?? entries.FirstOrDefault();
        }

        var title = hit?.Tags != null && hit.Tags.TryGetValue("title", out var t) ? t : string.Empty;
        var tags = hit?.Tags == null
            ? new Dictionary<string, string>()
            : hit.Tags.ToDictionary(k => k.Key, v => v.Value);

        return ToStruct(new
        {
            sourceId,
            memoryId,
            title,
            chunkId = chunkId.Length == 0 ? null : chunkId,
            found = hit != null,
            role = hit?.Role ?? "",
            entryId = hit?.EntryId ?? "",
            content = Trunc(hit?.Content, maxChars),
            tags
        });
    }
}

internal sealed class RetrieveChunksTool : AevatarToolBase
{
    private readonly IMemoryStore _store;
    private readonly IMemoryVectorIndex? _vectorIndex;

    public RetrieveChunksTool(IMemoryStore store, IMemoryVectorIndex? vectorIndex)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _vectorIndex = vectorIndex;
    }

    public override string Name => "retrieve_chunks";
    public override string Description => "Retrieve relevant source chunks for a query (vector preferred, lexical fallback)";
    public override ToolCategory Category => ToolCategory.Custom;

    public override ToolParameters CreateParameters()
    {
        return new ToolParameters
        {
            Required = new[] { "query" },
            Items = new Dictionary<string, ToolParameter>
            {
                ["query"] = new ToolParameter
                {
                    Type = "string",
                    Description = "Query to search for relevant chunks",
                    Required = true
                },
                ["sourceIds"] = new ToolParameter
                {
                    Type = "array",
                    Description = "Optional list of sourceIds to restrict retrieval",
                    Required = false,
                    Items = new ToolParameter { Type = "string", Description = "sourceId string" }
                },
                ["maxResults"] = new ToolParameter
                {
                    Type = "integer",
                    Description = "Max chunks to return (1..20)",
                    Required = false,
                    DefaultValue = 8
                },
                ["maxChars"] = new ToolParameter
                {
                    Type = "integer",
                    Description = "Max chars per chunk preview (1..2000)",
                    Required = false,
                    DefaultValue = 400
                },
                ["strategy"] = new ToolParameter
                {
                    Type = "string",
                    Description = "Retrieval strategy (auto|vector|lexical)",
                    Required = false,
                    DefaultValue = "auto",
                    Enum = new[] { "auto", "vector", "lexical" }
                }
            }
        };
    }

    public override async Task<IMessage> ExecuteAsync(
        Dictionary<string, object> parameters,
        ToolContext context,
        ILogger? logger,
        CancellationToken cancellationToken = default)
    {
        var query = (parameters.GetValueOrDefault("query")?.ToString() ?? string.Empty).Trim();
        if (query.Length == 0)
            throw new ArgumentException("query is required");

        var strategy = (parameters.GetValueOrDefault("strategy")?.ToString() ?? "auto").Trim().ToLowerInvariant();
        var maxResults = ClampInt(parameters.GetValueOrDefault("maxResults"), 8, 1, 20);
        var maxChars = ClampInt(parameters.GetValueOrDefault("maxChars"), 400, 1, 2000);

        var sourceIds = ParseStringList(parameters.GetValueOrDefault("sourceIds"));

        var results = new List<object>(capacity: maxResults);
        var used = "lexical";

        // 1) Vector preferred (requires vector index + embeddings callback)
        if (strategy is "auto" or "vector")
        {
            var vectorHits = await TryVectorAsync(query, sourceIds, maxResults, maxChars, context, cancellationToken);
            if (vectorHits.Count > 0)
            {
                used = "vector";
                results.AddRange(vectorHits);
            }
        }

        // 2) Lexical fallback
        if (results.Count == 0 && strategy is "auto" or "lexical")
        {
            var lexical = await LexicalAsync(query, sourceIds, maxResults, maxChars, cancellationToken);
            used = "lexical";
            results.AddRange(lexical);
        }

        return ToStruct(new
        {
            query,
            strategyUsed = used,
            count = results.Count,
            results
        });
    }

    private async Task<List<object>> TryVectorAsync(
        string query,
        IReadOnlyList<string> sourceIds,
        int maxResults,
        int maxChars,
        ToolContext context,
        CancellationToken ct)
    {
        if (_vectorIndex == null)
            return new List<object>();

        if (context.GenerateEmbeddingsAsync == null)
            return new List<object>();

        const int maxQueryChars = 2000;
        var q = query.Replace("\r", "").Trim();
        if (q.Length > maxQueryChars)
            q = q[..maxQueryChars];

        var embeddings = await context.GenerateEmbeddingsAsync(new[] { q }, ct);
        if (embeddings.Count == 0)
            return new List<object>();

        var queryEmbedding = embeddings[0].Vector.Span.ToArray();

        var matches = new List<MemoryVectorMatch>();

        // If no source filter, search across all graph-scoped records (memoryId null).
        if (sourceIds.Count == 0)
        {
            var m = await _vectorIndex.SearchAsync(
                queryEmbedding,
                limit: maxResults,
                memoryId: null,
                scopeTypeFilter: MemoryScopeType.Graph,
                scopeId: null,
                ct: ct);

            matches.AddRange(m);
        }
        else
        {
            // Merge per-source results (bounded).
            var perSource = Math.Max(1, Math.Min(3, maxResults));
            foreach (var sid in sourceIds)
            {
                ct.ThrowIfCancellationRequested();
                var memoryId = $"source::{sid}";
                var m = await _vectorIndex.SearchAsync(
                    queryEmbedding,
                    limit: perSource,
                    memoryId: memoryId,
                    scopeTypeFilter: MemoryScopeType.Graph,
                    scopeId: sid,
                    ct: ct);

                matches.AddRange(m);
            }
        }

        // Filter to source::* only (avoid mixing with other graph resources).
        var filtered = matches
            .Where(m => (m.Record.MemoryId ?? string.Empty).StartsWith("source::", StringComparison.Ordinal))
            .OrderByDescending(m => m.Similarity)
            .Take(maxResults)
            .ToList();

        var list = new List<object>(filtered.Count);
        foreach (var m in filtered)
        {
            var record = m.Record;
            var memoryId = record.MemoryId ?? string.Empty;
            var sourceId = record.Tags.TryGetValue("source_id", out var sid)
                ? sid
                : memoryId.Replace("source::", "", StringComparison.Ordinal);

            record.Tags.TryGetValue("chunk_id", out var chunkId);

            list.Add(new
            {
                sourceId,
                chunkId = chunkId ?? "",
                similarity = m.Similarity,
                preview = Trunc(record.Content, maxChars),
                entryId = record.EntryId ?? ""
            });
        }

        return list;
    }

    private async Task<List<object>> LexicalAsync(
        string query,
        IReadOnlyList<string> sourceIds,
        int maxResults,
        int maxChars,
        CancellationToken ct)
    {
        var hits = new List<MemoryEntry>();

        if (sourceIds.Count == 0)
        {
            var all = await _store.SearchAsync(
                query,
                limit: Math.Max(20, maxResults * 4),
                scopeTypeFilter: MemoryScopeType.Graph,
                memoryId: null,
                ct: ct);
            hits.AddRange(all);
        }
        else
        {
            foreach (var sid in sourceIds)
            {
                ct.ThrowIfCancellationRequested();
                var memoryId = $"source::{sid}";
                var per = await _store.SearchAsync(
                    query,
                    limit: Math.Max(20, maxResults * 4),
                    scopeTypeFilter: MemoryScopeType.Graph,
                    memoryId: memoryId,
                    ct: ct);
                hits.AddRange(per);
            }
        }

        var chunks = hits
            .Where(e => string.Equals(e.Role, "source_chunk", StringComparison.Ordinal))
            .Take(maxResults)
            .ToList();

        var list = new List<object>(chunks.Count);
        foreach (var e in chunks)
        {
            e.Tags.TryGetValue("source_id", out var sourceId);
            e.Tags.TryGetValue("chunk_id", out var chunkId);
            list.Add(new
            {
                sourceId = sourceId ?? "",
                chunkId = chunkId ?? "",
                similarity = 0,
                preview = Trunc(e.Content, maxChars),
                entryId = e.EntryId ?? ""
            });
        }

        return list;
    }
}

internal sealed class GenerateReportTool : AevatarToolBase
{
    private readonly IMemoryStore _store;
    private readonly IMemoryVectorIndex? _vectorIndex;
    private readonly Func<ChatRequest, CancellationToken, Task<ChatResponse>> _chatAsync;

    public GenerateReportTool(
        IMemoryStore store,
        IMemoryVectorIndex? vectorIndex,
        Func<ChatRequest, CancellationToken, Task<ChatResponse>> chatAsync)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _vectorIndex = vectorIndex;
        _chatAsync = chatAsync ?? throw new ArgumentNullException(nameof(chatAsync));
    }

    public override string Name => "generate_report";
    public override string Description => "Generate a structured report based on notebook sources (persists to MemoryStore)";
    public override ToolCategory Category => ToolCategory.Custom;

    public override ToolParameters CreateParameters()
    {
        return new ToolParameters
        {
            Required = new[] { "topic" },
            Items = new Dictionary<string, ToolParameter>
            {
                ["topic"] = new ToolParameter
                {
                    Type = "string",
                    Description = "Report topic",
                    Required = true
                },
                ["reportId"] = new ToolParameter
                {
                    Type = "string",
                    Description = "Optional report id (to append new version)",
                    Required = false
                },
                ["sourceIds"] = new ToolParameter
                {
                    Type = "array",
                    Description = "Optional sourceIds to restrict context building (empty = all)",
                    Required = false,
                    Items = new ToolParameter { Type = "string", Description = "sourceId string" }
                }
            }
        };
    }

    public override async Task<IMessage> ExecuteAsync(
        Dictionary<string, object> parameters,
        ToolContext context,
        ILogger? logger,
        CancellationToken cancellationToken = default)
    {
        var topic = (parameters.GetValueOrDefault("topic")?.ToString() ?? string.Empty).Trim();
        if (topic.Length == 0)
            throw new ArgumentException("topic is required");

        var reportId = (parameters.GetValueOrDefault("reportId")?.ToString() ?? string.Empty).Trim();
        var sourceIds = ParseStringList(parameters.GetValueOrDefault("sourceIds"));

        // Build notebook context (vector preferred, fallback lexical)
        var ctxBuilder = new NotebookContextBuilder(
            _store,
            logger: Microsoft.Extensions.Logging.Abstractions.NullLogger<NotebookContextBuilder>.Instance,
            vectorIndex: _vectorIndex,
            llmProviderFactory: null,
            embeddingFactory: null);

        var ctx = await ctxBuilder.BuildAsync(
            new NotebookContextBuildRequest
            {
                Query = topic,
                SelectedSourceIds = sourceIds.Count == 0 ? null : sourceIds.ToList(),
                Budget = new NotebookContextBudget { MaxTotalChars = 18_000, MaxChunks = 12, MaxChunksPerSource = 2 }
            },
            generateEmbeddingsAsyncOverride: context.GenerateEmbeddingsAsync,
            ct: cancellationToken);

        var chunkIds = ctx.Context.Slices
            .Where(s => s.Kind == NotebookContextSliceKind.SourceChunk)
            .Select(s => (s.ChunkId ?? string.Empty).Trim())
            .Where(s => s.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .Take(120)
            .ToList();

        var pipeline = new ReportPipeline(
            _store,
            logger: Microsoft.Extensions.Logging.Abstractions.NullLogger<ReportPipeline>.Instance);

        var executionId = $"notebook-report-tool-{Guid.NewGuid():N}";
        var result = await pipeline.GenerateAsync(
            new ReportPipelineRequest
            {
                ChatAsync = _chatAsync,
                NotebookContext = ctx.Rendered,
                SourceIds = ctx.SourceIds,
                CitationChunkIds = chunkIds,
                Topic = topic,
                ReportId = reportId.Length == 0 ? null : reportId,
                ExecutionId = executionId
            },
            cancellationToken);

        return ToStruct(new
        {
            ok = true,
            executionId,
            reportId = result.ReportId,
            version = result.Version,
            topic = result.Topic,
            memoryId = $"report::{result.ReportId}",
            sourceIds = ctx.SourceIds,
            citations = chunkIds,
            contentPreview = Trunc(result.Content, 2000)
        });
    }
}

internal sealed class GetReportTool : AevatarToolBase
{
    private readonly IMemoryStore _store;

    public GetReportTool(IMemoryStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public override string Name => "get_report";
    public override string Description => "Get a report (latest or a specific version) from MemoryStore";
    public override ToolCategory Category => ToolCategory.Custom;

    public override ToolParameters CreateParameters()
    {
        return new ToolParameters
        {
            Required = new[] { "reportId" },
            Items = new Dictionary<string, ToolParameter>
            {
                ["reportId"] = new ToolParameter
                {
                    Type = "string",
                    Description = "Report id",
                    Required = true
                },
                ["version"] = new ToolParameter
                {
                    Type = "integer",
                    Description = "Optional version (if omitted, returns latest)",
                    Required = false
                },
                ["maxChars"] = new ToolParameter
                {
                    Type = "integer",
                    Description = "Max chars of report content returned (1..20000)",
                    Required = false,
                    DefaultValue = 4000
                }
            }
        };
    }

    public override async Task<IMessage> ExecuteAsync(
        Dictionary<string, object> parameters,
        ToolContext context,
        ILogger? logger,
        CancellationToken cancellationToken = default)
    {
        var reportId = (parameters.GetValueOrDefault("reportId")?.ToString() ?? string.Empty).Trim();
        if (reportId.Length == 0)
            throw new ArgumentException("reportId is required");

        var maxChars = ClampInt(parameters.GetValueOrDefault("maxChars"), 4000, 1, 20000);
        var versionFilter = parameters.GetValueOrDefault("version");
        var wantVersion = TryParseInt(versionFilter, out var v) ? v : (int?)null;

        var memoryId = $"report::{reportId}";
        var entries = await _store.ListEntriesAsync(memoryId, limit: 2000, ct: cancellationToken);

        var reports = entries
            .Where(e => string.Equals(e.Role, "report", StringComparison.Ordinal))
            .Select(e =>
            {
                var ver = e.Tags.TryGetValue("version", out var s) && int.TryParse(s, out var i) ? i : 0;
                return new { entry = e, version = ver };
            })
            .ToList();

        var chosen = wantVersion.HasValue
            ? reports.FirstOrDefault(x => x.version == wantVersion.Value)
            : reports.OrderByDescending(x => x.version).FirstOrDefault();

        var entry = chosen?.entry;
        var tags = entry?.Tags == null
            ? new Dictionary<string, string>()
            : entry.Tags.ToDictionary(k => k.Key, v => v.Value);

        return ToStruct(new
        {
            reportId,
            memoryId,
            found = entry != null,
            version = chosen?.version ?? 0,
            topic = entry?.Tags != null && entry.Tags.TryGetValue("topic", out var t) ? t : "",
            content = Trunc(entry?.Content, maxChars),
            tags
        });
    }
}

internal sealed class GetExecutionGraphTool : AevatarToolBase
{
    private readonly IMemoryGraphStore? _graphStore;

    public GetExecutionGraphTool(IMemoryGraphStore? graphStore)
    {
        _graphStore = graphStore;
    }

    public override string Name => "get_execution_graph";
    public override string Description => "Load a projected MemoryGraph for an executionId (Layer 4.2)";
    public override ToolCategory Category => ToolCategory.Custom;

    public override ToolParameters CreateParameters()
    {
        return new ToolParameters
        {
            Required = new[] { "executionId" },
            Items = new Dictionary<string, ToolParameter>
            {
                ["executionId"] = new ToolParameter
                {
                    Type = "string",
                    Description = "Execution id (trace bundle id / graph id)",
                    Required = true
                },
                ["maxNodes"] = new ToolParameter
                {
                    Type = "integer",
                    Description = "Max nodes to return (1..200)",
                    Required = false,
                    DefaultValue = 80
                },
                ["maxEdges"] = new ToolParameter
                {
                    Type = "integer",
                    Description = "Max edges to return (1..400)",
                    Required = false,
                    DefaultValue = 120
                }
            }
        };
    }

    public override async Task<IMessage> ExecuteAsync(
        Dictionary<string, object> parameters,
        ToolContext context,
        ILogger? logger,
        CancellationToken cancellationToken = default)
    {
        var executionId = (parameters.GetValueOrDefault("executionId")?.ToString() ?? string.Empty).Trim();
        if (executionId.Length == 0)
            throw new ArgumentException("executionId is required");

        if (_graphStore == null)
        {
            return ToStruct(new
            {
                ok = false,
                error = "IMemoryGraphStore is not available (MemoryGraphStore not wired).",
                executionId
            });
        }

        var maxNodes = ClampInt(parameters.GetValueOrDefault("maxNodes"), 80, 1, 200);
        var maxEdges = ClampInt(parameters.GetValueOrDefault("maxEdges"), 120, 1, 400);

        var graph = await _graphStore.LoadAsync(executionId, cancellationToken);
        if (graph == null)
        {
            return ToStruct(new
            {
                ok = false,
                error = "graph not found",
                executionId
            });
        }

        var nodes = graph.Nodes
            .Take(maxNodes)
            .Select(n => new
            {
                nodeId = n.NodeId,
                type = n.Type,
                name = n.Name,
                content = Trunc(n.Content, 240),
                labels = n.Labels
            })
            .ToList();

        var edges = graph.Edges
            .Take(maxEdges)
            .Select(e => new
            {
                edgeId = e.EdgeId,
                from = e.FromNodeId,
                to = e.ToNodeId,
                type = e.Type,
                label = e.Label,
                labels = e.Labels
            })
            .ToList();

        return ToStruct(new
        {
            ok = true,
            executionId,
            graphId = graph.GraphId,
            scope = new { type = graph.Scope.Type.ToString(), scopeId = graph.Scope.ScopeId },
            nodeCount = graph.Nodes.Count,
            edgeCount = graph.Edges.Count,
            nodes,
            edges
        });
    }
}

// ============================================================
//  Helpers
// ============================================================

internal static class NotebookToolHelpers
{
    public static Struct ToStruct(object obj)
    {
        var json = JsonSerializer.Serialize(obj);
        return JsonParser.Default.Parse<Struct>(json);
    }

    public static int ClampInt(object? v, int fallback, int min, int max)
    {
        if (!TryParseInt(v, out var i))
            i = fallback;
        return Math.Clamp(i, min, max);
    }

    public static bool TryParseInt(object? v, out int i)
    {
        i = 0;
        if (v == null) return false;
        if (v is int ii) { i = ii; return true; }
        if (v is long ll) { i = (int)Math.Clamp(ll, int.MinValue, int.MaxValue); return true; }
        return int.TryParse(v.ToString(), out i);
    }

    public static IReadOnlyList<string> ParseStringList(object? value)
    {
        if (value == null)
            return Array.Empty<string>();

        if (value is string s)
        {
            s = s.Trim();
            return s.Length == 0 ? Array.Empty<string>() : new[] { s };
        }

        if (value is string[] arr)
        {
            return arr
                .Select(x => (x ?? string.Empty).Trim())
                .Where(x => x.Length > 0)
                .Distinct(StringComparer.Ordinal)
                .ToList();
        }

        if (value is List<string> list)
        {
            return list
                .Select(x => (x ?? string.Empty).Trim())
                .Where(x => x.Length > 0)
                .Distinct(StringComparer.Ordinal)
                .ToList();
        }

        if (value is JsonElement el && el.ValueKind == JsonValueKind.Array)
        {
            var outList = new List<string>();
            foreach (var item in el.EnumerateArray())
            {
                var str = item.ValueKind == JsonValueKind.String ? item.GetString() : item.ToString();
                str = (str ?? string.Empty).Trim();
                if (str.Length > 0)
                    outList.Add(str);
            }

            return outList
                .Distinct(StringComparer.Ordinal)
                .ToList();
        }

        return Array.Empty<string>();
    }

    public static string Trunc(string? text, int maxChars)
    {
        var s = (text ?? string.Empty).Replace("\r", "").Trim();
        if (s.Length <= maxChars)
            return s;
        return s[..maxChars];
    }
}

