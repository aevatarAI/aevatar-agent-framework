using Aevatar.Agents.Abstractions.Memory;
using Aevatar.Agents.AI.Core.Messages;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.AI.Core.AgenticRag;

// ============================================================
//  Agentic RAG - Default Retriever (MemoryStore + VectorIndex)
//
//  Strategy (MVP):
//  - Semantic-first (IMemoryVectorIndex) when query embedding is provided.
//  - Fallback to lexical (IMemoryStore) when semantic is unavailable/empty.
//
//  Safety:
//  - Best-effort: retrieval failures must NOT crash the Agentic RAG loop.
//  - Bounded outputs: snippets are truncated; tags are strings only.
// ============================================================

public sealed class MemoryStoreAgenticRagRetriever : IAgenticRagRetriever
{
    private readonly IMemoryStore? _memoryStore;
    private readonly IMemoryVectorIndex? _memoryVectorIndex;
    private readonly ILogger<MemoryStoreAgenticRagRetriever>? _logger;

    public string Name => "memory";

    public MemoryStoreAgenticRagRetriever(
        IMemoryStore? memoryStore = null,
        IMemoryVectorIndex? memoryVectorIndex = null,
        ILogger<MemoryStoreAgenticRagRetriever>? logger = null)
    {
        _memoryStore = memoryStore;
        _memoryVectorIndex = memoryVectorIndex;
        _logger = logger;
    }

    public async Task<IReadOnlyList<RagEvidenceSummary>> RetrieveAsync(
        RagRetrieveRequest request,
        CancellationToken cancellationToken)
    {
        if (request == null) throw new ArgumentNullException(nameof(request));
        if (string.IsNullOrWhiteSpace(request.Query))
            return Array.Empty<RagEvidenceSummary>();

        var query = request.Query.Trim();
        var maxResults = Math.Clamp(request.MaxResults, 1, 200);
        var maxChars = Math.Clamp(request.MaxSnippetChars, 0, 4000);

        var scope = request.Scope;
        var memoryId = NormalizeMemoryId(request.MemoryId, scope);

        // 1) Semantic-first: VectorIndex
        if (_memoryVectorIndex != null && request.QueryEmbedding is { Count: > 0 })
        {
            try
            {
                var matches = await _memoryVectorIndex.SearchAsync(
                    request.QueryEmbedding,
                    limit: maxResults,
                    memoryId: memoryId,
                    scopeTypeFilter: scope?.Type,
                    scopeId: scope?.ScopeId,
                    ct: cancellationToken);

                if (matches.Count > 0)
                {
                    var list = new List<RagEvidenceSummary>(matches.Count);
                    foreach (var m in matches)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        list.Add(ToEvidenceFromVectorMatch(m, query, maxChars));
                    }

                    return list;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger?.LogDebug(ex, "Memory vector retrieval failed (best-effort).");
            }
        }

        // 2) Fallback: MemoryStore lexical search
        if (_memoryStore == null)
            return Array.Empty<RagEvidenceSummary>();

        try
        {
            var entries = await _memoryStore.SearchAsync(
                query,
                limit: maxResults,
                scopeTypeFilter: scope?.Type,
                memoryId: memoryId,
                ct: cancellationToken);

            if (entries.Count == 0)
                return Array.Empty<RagEvidenceSummary>();

            var list = new List<RagEvidenceSummary>(entries.Count);
            foreach (var e in entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                list.Add(ToEvidenceFromMemoryEntry(e, query, maxChars));
            }

            return list;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Memory store lexical retrieval failed (best-effort).");
            return Array.Empty<RagEvidenceSummary>();
        }
    }

    private static string? NormalizeMemoryId(string? memoryId, MemoryScope? scope)
    {
        if (!string.IsNullOrWhiteSpace(memoryId))
            return memoryId.Trim();

        if (scope == null)
            return null;

        var id = (scope.ScopeId ?? string.Empty).Trim();
        if (id.Length == 0)
            return null;

        // Keep consistent with AIGAgentBase.BuildMemoryId(): "<type>::<id>"
        var type = scope.Type.ToString().ToLowerInvariant();
        return $"{type}::{id}";
    }

    private static RagEvidenceSummary ToEvidenceFromVectorMatch(MemoryVectorMatch match, string query, int maxChars)
    {
        var r = match.Record;
        var snippet = BuildSnippet(r.Content ?? string.Empty, query, maxChars);

        var ev = new RagEvidenceSummary
        {
            EvidenceId = !string.IsNullOrWhiteSpace(r.EntryId) ? r.EntryId : Guid.NewGuid().ToString("N"),
            Snippet = snippet,
            Citation = new RagCitation
            {
                MemoryEntry = new MemoryEntryCitation
                {
                    MemoryId = r.MemoryId ?? string.Empty,
                    EntryId = r.EntryId ?? string.Empty,
                    Scope = r.Scope
                }
            },
            Score = match.Similarity
        };

        ev.Tags["source"] = "memory.vector_index";
        ev.Tags["ranking"] = "vector";
        if (!string.IsNullOrWhiteSpace(r.Role)) ev.Tags["role"] = r.Role;
        if (!string.IsNullOrWhiteSpace(r.AgentId)) ev.Tags["agent_id"] = r.AgentId;
        if (!string.IsNullOrWhiteSpace(r.RunId)) ev.Tags["run_id"] = r.RunId;

        return ev;
    }

    private static RagEvidenceSummary ToEvidenceFromMemoryEntry(MemoryEntry entry, string query, int maxChars)
    {
        var snippet = BuildSnippet(entry.Content ?? string.Empty, query, maxChars);

        var ev = new RagEvidenceSummary
        {
            EvidenceId = !string.IsNullOrWhiteSpace(entry.EntryId) ? entry.EntryId : Guid.NewGuid().ToString("N"),
            Snippet = snippet,
            Citation = new RagCitation
            {
                MemoryEntry = new MemoryEntryCitation
                {
                    MemoryId = entry.MemoryId ?? string.Empty,
                    EntryId = entry.EntryId ?? string.Empty,
                    Scope = entry.Scope
                }
            },
            Score = 0
        };

        ev.Tags["source"] = "memory.store";
        ev.Tags["ranking"] = "lexical";
        if (!string.IsNullOrWhiteSpace(entry.Role)) ev.Tags["role"] = entry.Role;
        if (!string.IsNullOrWhiteSpace(entry.AgentId)) ev.Tags["agent_id"] = entry.AgentId;
        if (!string.IsNullOrWhiteSpace(entry.RunId)) ev.Tags["run_id"] = entry.RunId;

        return ev;
    }

    private static string BuildSnippet(string content, string query, int maxChars)
    {
        if (string.IsNullOrEmpty(content) || maxChars <= 0)
            return string.Empty;

        if (content.Length <= maxChars)
            return content;

        var q = (query ?? string.Empty).Trim();
        if (q.Length > 0)
        {
            var idx = content.IndexOf(q, StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
            {
                var half = maxChars / 2;
                var start = Math.Max(0, idx - half);
                var len = Math.Min(maxChars, content.Length - start);
                var snippet = content.Substring(start, len);

                if (start > 0) snippet = "..." + snippet;
                if (start + len < content.Length) snippet += "...";

                return snippet;
            }
        }

        return content[..maxChars] + "...";
    }
}


