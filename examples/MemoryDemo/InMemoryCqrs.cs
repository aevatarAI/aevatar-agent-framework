using System.Collections.Concurrent;
using System.Text;
using Aevatar.Agents;
using Aevatar.Agents.Abstractions.CQRS;
using Aevatar.Agents.AI;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;

namespace MemoryDemo;

// ============================================================
//  In-Memory CQRS (Demo Only)
//
//  WHY:
//  - The real CQRS pipeline projects OnStateChangedAsync → Elasticsearch.
//  - MemoryDemo should run out-of-the-box without external dependencies.
//  - We mimic "latest state snapshot" semantics in-process.
//
//  Contract:
//  - Projection input is Protobuf StateWrapper (cross-boundary safe)
//  - Query surface is IStateIndexService / IStateQueryService (framework-level)
// ============================================================

public sealed class InMemoryStateIndexService : IStateIndexService
{
    private readonly ConcurrentDictionary<string, StateIndexDocument> _docs = new(StringComparer.Ordinal);
    private readonly ILogger<InMemoryStateIndexService> _logger;

    public InMemoryStateIndexService(ILogger<InMemoryStateIndexService> logger)
    {
        _logger = logger;
    }

    public Task EnsureIndexExistsAsync(string agentType, System.Type? stateType = null, CancellationToken ct = default)
    {
        // No-op (in-memory)
        return Task.CompletedTask;
    }

    public Task IndexStateAsync(StateIndexDocument document, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (document == null) throw new ArgumentNullException(nameof(document));

        var key = BuildKey(document.AgentType, document.AgentId);

        _docs.AddOrUpdate(
            key,
            _ => document,
            (_, existing) =>
            {
                // Keep latest version only (mimic ES ScriptedUpsert)
                return document.Version > existing.Version ? document : existing;
            });

        return Task.CompletedTask;
    }

    public Task IndexStateBatchAsync(IEnumerable<StateIndexDocument> documents, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (documents == null) throw new ArgumentNullException(nameof(documents));

        foreach (var doc in documents)
        {
            ct.ThrowIfCancellationRequested();
            if (doc == null) continue;
            _ = IndexStateAsync(doc, ct);
        }

        return Task.CompletedTask;
    }

    public Task DeleteStateAsync(string agentType, string agentId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        _docs.TryRemove(BuildKey(agentType, agentId), out _);
        return Task.CompletedTask;
    }

    public Task<StateQueryResult?> GetByIdAsync(string agentType, string agentId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var key = BuildKey(agentType, agentId);
        if (!_docs.TryGetValue(key, out var doc))
            return Task.FromResult<StateQueryResult?>(null);

        var result = new StateQueryResult
        {
            AgentId = doc.AgentId,
            AgentType = doc.AgentType,
            Version = doc.Version,
            IndexedAt = doc.IndexedAt,
            Data = doc.Data.ToDictionary(kv => kv.Key, kv => (object?)kv.Value)
        };

        return Task.FromResult<StateQueryResult?>(result);
    }

    public Task<PagedStateQueryResult> QueryAsync(StateQuery query, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (query == null) throw new ArgumentNullException(nameof(query));

        var list = _docs.Values
            .Where(d => string.Equals(d.AgentType, query.AgentType, StringComparison.Ordinal))
            .Select(d => new StateQueryResult
            {
                AgentId = d.AgentId,
                AgentType = d.AgentType,
                Version = d.Version,
                IndexedAt = d.IndexedAt,
                Data = d.Data.ToDictionary(kv => kv.Key, kv => (object?)kv.Value)
            })
            .ToList();

        if (!string.IsNullOrWhiteSpace(query.QueryString))
        {
            var q = query.QueryString.Trim();
            list = list.Where(r => ContainsInValues(r.Data.Values, q)).ToList();
        }

        // Sort: newest first by default (good UX for demo)
        list = list
            .OrderByDescending(x => x.Version)
            .ThenByDescending(x => x.IndexedAt ?? DateTime.MinValue)
            .ToList();

        var pageSize = query.PageSize <= 0 ? 20 : query.PageSize;
        var pageIndex = Math.Max(0, query.PageIndex);
        var from = pageIndex * pageSize;

        var pageItems = from >= list.Count
            ? new List<StateQueryResult>()
            : list.Skip(from).Take(pageSize).ToList();

        return Task.FromResult(new PagedStateQueryResult
        {
            TotalCount = list.Count,
            Items = pageItems,
            PageIndex = pageIndex,
            PageSize = pageSize
        });
    }

    public Task<long> CountAsync(string agentType, string? queryString = null, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var list = _docs.Values.Where(d => string.Equals(d.AgentType, agentType, StringComparison.Ordinal)).ToList();
        if (!string.IsNullOrWhiteSpace(queryString))
        {
            var q = queryString.Trim();
            list = list.Where(d => ContainsInValues(d.Data.Values.Cast<object?>(), q)).ToList();
        }

        return Task.FromResult((long)list.Count);
    }

    private static string BuildKey(string agentType, string agentId) => $"{agentType}::{agentId}";

    private static bool ContainsInValues(IEnumerable<object?> values, string q)
    {
        foreach (var v in values)
        {
            if (v is string s && s.Contains(q, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }
}

public sealed class InMemoryStateProjector : IStateProjector
{
    private readonly IStateIndexService _index;
    private readonly ILogger<InMemoryStateProjector> _logger;

    public InMemoryStateProjector(IStateIndexService index, ILogger<InMemoryStateProjector> logger)
    {
        _index = index;
        _logger = logger;
    }

    public async Task ProjectAsync(StateWrapper wrapper, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        if (wrapper == null)
            throw new ArgumentNullException(nameof(wrapper));

        // NOTE:
        // - This is demo-grade conversion logic.
        // - Real system uses Plugins.CQRS.StateDocumentConverter to unpack any Protobuf state.
        var doc = new StateIndexDocument
        {
            AgentId = wrapper.AgentId,
            AgentType = wrapper.AgentType,
            Version = wrapper.Version,
            IndexedAt = wrapper.PublishedAt?.ToDateTime() ?? DateTime.UtcNow,
            Data = ConvertStateToData(wrapper.StateData)
        };

        await _index.EnsureIndexExistsAsync(wrapper.AgentType, stateType: null, ct);
        await _index.IndexStateAsync(doc, ct);
    }

    private Dictionary<string, object> ConvertStateToData(Any? stateAny)
    {
        var data = new Dictionary<string, object>(StringComparer.Ordinal);
        if (stateAny == null)
            return data;

        try
        {
            // Fast path: MemoryDemo uses AevatarAIAgentState
            if (stateAny.Is(AevatarAIAgentState.Descriptor))
            {
                var state = stateAny.Unpack<AevatarAIAgentState>();

                // 1) context map (key=value)
                foreach (var (k, v) in state.Context)
                {
                    if (string.IsNullOrWhiteSpace(k)) continue;
                    if (string.IsNullOrWhiteSpace(v)) continue;
                    data[$"ctx_{k}"] = v;
                }

                // 2) flattened history text (best-effort, good for search_memory)
                var sb = new StringBuilder();
                foreach (var m in state.History)
                {
                    var content = m.Content ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(content)) continue;
                    sb.AppendLine($"{m.Role}: {content}".Trim());
                }

                var historyText = sb.ToString().Trim();
                if (!string.IsNullOrWhiteSpace(historyText))
                    data["history_text"] = historyText;

                // 3) last activity
                if (state.LastActivity != null)
                    data["last_activity"] = state.LastActivity.ToDateTime().ToString("O");

                // 4) token usage
                if (state.TotalTokenUsed > 0)
                    data["total_token_used"] = state.TotalTokenUsed.ToString();

                return data;
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "[MemoryDemo] Failed to unpack state Any (best-effort)");
        }

        // Fallback: store typeUrl so you can see something in CQRS panel
        data["state_type_url"] = stateAny.TypeUrl ?? string.Empty;
        return data;
    }
}
