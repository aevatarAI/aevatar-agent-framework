using System.Collections.Concurrent;
using Aevatar.Agents;
using Aevatar.Agents.Abstractions.CQRS;
using Aevatar.Agents.AI;
using Google.Protobuf;

namespace Aevatar.Notebook.Cqrs;

// ============================================================
//  InMemory CQRS (Demo-grade)
//
//  Purpose:
//  - Provide a lightweight CQRS read-model so `search_memory` can prefer Layer 3.
//
//  Notes:
//  - This is intentionally minimal and process-local.
// ============================================================

internal sealed class InMemoryStateIndexService : IStateIndexService
{
    private readonly ConcurrentDictionary<string, StateIndexDocument> _byKey = new(StringComparer.Ordinal);

    public Task EnsureIndexExistsAsync(string agentType, Type? stateType = null, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    public Task IndexStateAsync(StateIndexDocument document, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ct.ThrowIfCancellationRequested();

        var key = BuildKey(document.AgentType, document.AgentId);
        _byKey[key] = document;
        return Task.CompletedTask;
    }

    public Task IndexStateBatchAsync(IEnumerable<StateIndexDocument> documents, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(documents);
        ct.ThrowIfCancellationRequested();

        foreach (var d in documents)
        {
            ct.ThrowIfCancellationRequested();
            if (d == null) continue;
            var key = BuildKey(d.AgentType, d.AgentId);
            _byKey[key] = d;
        }
        return Task.CompletedTask;
    }

    public Task DeleteStateAsync(string agentType, string agentId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var key = BuildKey(agentType, agentId);
        _byKey.TryRemove(key, out _);
        return Task.CompletedTask;
    }

    public Task<StateQueryResult?> GetByIdAsync(string agentType, string agentId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var key = BuildKey(agentType, agentId);
        if (!_byKey.TryGetValue(key, out var doc))
            return Task.FromResult<StateQueryResult?>(null);

        return Task.FromResult<StateQueryResult?>(new StateQueryResult
        {
            AgentType = doc.AgentType,
            AgentId = doc.AgentId,
            Version = doc.Version,
            IndexedAt = doc.IndexedAt,
            Data = doc.Data.ToDictionary(k => k.Key, v => (object?)v.Value)
        });
    }

    public Task<PagedStateQueryResult> QueryAsync(StateQuery query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ct.ThrowIfCancellationRequested();

        // Demo-grade: no Lucene parser. Return empty to force GetByIdAsync fallback.
        return Task.FromResult(new PagedStateQueryResult
        {
            Items = new List<StateQueryResult>(),
            TotalCount = 0,
            PageIndex = query.PageIndex,
            PageSize = query.PageSize
        });
    }

    public Task<long> CountAsync(string agentType, string? queryString = null, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var count = _byKey.Keys.Count(k => k.StartsWith($"{agentType}::", StringComparison.Ordinal));
        return Task.FromResult((long)count);
    }

    private static string BuildKey(string agentType, string agentId)
        => $"{agentType}::{agentId}";
}

internal sealed class InMemoryStateProjector : IStateProjector
{
    private readonly IStateIndexService _index;

    public InMemoryStateProjector(IStateIndexService index)
    {
        _index = index ?? throw new ArgumentNullException(nameof(index));
    }

    public async Task ProjectAsync(StateWrapper wrapper, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(wrapper);

        var agentType = wrapper.AgentType ?? string.Empty;
        var agentId = wrapper.AgentId ?? string.Empty;
        if (string.IsNullOrWhiteSpace(agentType) || string.IsNullOrWhiteSpace(agentId))
            return;

        await _index.EnsureIndexExistsAsync(agentType, stateType: null, ct);

        var data = new Dictionary<string, object>(StringComparer.Ordinal);

        // Best-effort unpack for AI agent state and flatten key fields.
        try
        {
            if (wrapper.StateData != null && wrapper.StateData.Is(AevatarAIAgentState.Descriptor))
            {
                var state = wrapper.StateData.Unpack<AevatarAIAgentState>();
                state.Context.TryGetValue("history_summary", out var summary);
                data["historySummary"] = summary ?? string.Empty;

                // Keep bounded history text (for lexical fallback).
                var lines = new List<string>(state.History.Count);
                foreach (var m in state.History)
                {
                    var content = (m.Content ?? string.Empty).Replace("\r", "").Trim();
                    if (content.Length == 0) continue;
                    lines.Add($"{m.Role}: {content}");
                }

                var historyText = string.Join("\n", lines);
                if (historyText.Length > 12000)
                    historyText = historyText[..12000];
                data["historyText"] = historyText;
            }
            else
            {
                data["stateTypeUrl"] = wrapper.StateData?.TypeUrl ?? string.Empty;
            }
        }
        catch
        {
            data["stateTypeUrl"] = wrapper.StateData?.TypeUrl ?? string.Empty;
        }

        // Include wrapper metadata (if any)
        if (wrapper.Metadata != null && wrapper.Metadata.Count > 0)
        {
            foreach (var kv in wrapper.Metadata)
            {
                if (string.IsNullOrWhiteSpace(kv.Key)) continue;
                data[$"meta.{kv.Key}"] = kv.Value ?? string.Empty;
            }
        }

        var doc = new StateIndexDocument
        {
            AgentType = agentType,
            AgentId = agentId,
            Version = wrapper.Version,
            IndexedAt = wrapper.PublishedAt?.ToDateTime() ?? DateTime.UtcNow,
            Data = data
        };

        await _index.IndexStateAsync(doc, ct);
    }
}


