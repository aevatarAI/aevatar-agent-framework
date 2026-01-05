using Aevatar.Agents.Persistence.Graph.Abstractions;
using Aevatar.Agents.Persistence.Graph.Core;
using Aevatar.Agents.Persistence.Graph.Core.IR;
using Aevatar.Agents.Persistence.Graph.Core.Semantic;

namespace Aevatar.Agents.Persistence.InMemory.Graph;

/// <summary>
/// InMemory 执行器：执行 <see cref="GraphOperation"/> 并返回语义对象/标识。
/// </summary>
internal sealed class InMemoryGraphExecutor : IGraphExecutor<GraphOperation>
{
    private readonly InMemoryGraphStore _store;

    public InMemoryGraphExecutor(InMemoryGraphStore store)
    {
        _store = store;
    }

    public Task<object?> ExecuteAsync(GraphOperation compiled)
    {
        var result = compiled switch
        {
            ReadNode op => ReadNode(op),
            CreateNode op => CreateNode(op),
            UpdateNode op => UpdateNode(op),
            DeleteNode op => DeleteNode(op),
            DeleteNodes op => DeleteNodes(op),
            QueryNodes op => QueryNodes(op),
            ReadEdge op => ReadEdge(op),
            CreateEdge op => CreateEdge(op),
            UpdateEdge op => UpdateEdge(op),
            DeleteEdge op => DeleteEdge(op),
            QueryEdges op => QueryEdges(op),
            DeleteEdges op => DeleteEdges(op),
            _ => null
        };

        return Task.FromResult(result);
    }

    private object? ReadNode(ReadNode op)
        => _store.TryGetNode(op.Id, out var node) ? node : null;

    private object? ReadEdge(ReadEdge op)
        => _store.TryGetEdge(op.Id, out var edge) ? edge : null;

    private object? CreateNode(CreateNode op)
    {
        var id = ResolveOrGenerateId(op.Props, key: "id");

        // Mirror Neo4j behavior: ensure "id" property exists.
        var props = MergeWithId(op.Props, id);
        var node = new GraphNode(new NodeId(id), op.Type, props);
        _store.UpsertNode(node);
        return node.Id;
    }

    private object? CreateEdge(CreateEdge op)
    {
        // Neo4j MATCH requires nodes to exist; mimic by returning null when missing.
        if (!_store.TryGetNode(op.From, out _) || !_store.TryGetNode(op.To, out _))
        {
            return null;
        }

        var id = ResolveOrGenerateId(op.Props, key: "id");
        var props = MergeWithId(op.Props, id);
        var edge = new GraphEdge(new EdgeId(id), op.Type, op.From, op.To, props);
        _store.UpsertEdge(edge);
        return edge.Id;
    }

    private object? UpdateNode(UpdateNode op)
    {
        if (!_store.TryGetNode(op.Id, out var existing))
        {
            return null;
        }

        // Keep primary key stable: ignore "id" mutation.
        var merged = MergeProperties(existing.Properties, op.Props, ignoreKey: "id");
        _store.UpsertNode(existing with { Properties = merged });
        return null;
    }

    private object? UpdateEdge(UpdateEdge op)
    {
        if (!_store.TryGetEdge(op.Id, out var existing))
        {
            return null;
        }

        // Keep primary key stable: ignore "id" mutation.
        var merged = MergeProperties(existing.Properties, op.Props, ignoreKey: "id");
        _store.UpsertEdge(existing with { Properties = merged });
        return null;
    }

    private object? DeleteNode(DeleteNode op)
    {
        _store.RemoveNode(op.Id);

        // DETACH semantics: remove connected edges.
        foreach (var e in _store.Edges)
        {
            if (e.From.Value == op.Id.Value || e.To.Value == op.Id.Value)
            {
                _store.RemoveEdge(e.Id);
            }
        }

        return null;
    }

    private object? DeleteNodes(DeleteNodes op)
    {
        var q = op.Query;
        var toDelete = _store.Nodes
            .Where(n => string.Equals(n.Type, q.Type, StringComparison.Ordinal))
            .Where(n => MatchesAll(n.Properties, q.Conditions))
            .Select(n => n.Id)
            .ToList();

        // DETACH semantics: same behavior as deleting one-by-one.
        foreach (var id in toDelete)
        {
            DeleteNode(new DeleteNode(id));
        }

        return null;
    }

    private object? DeleteEdge(DeleteEdge op)
    {
        _store.RemoveEdge(op.Id);
        return null;
    }

    private object? QueryNodes(QueryNodes op)
    {
        var q = op.Query;
        var result = _store.Nodes
            .Where(n => string.Equals(n.Type, q.Type, StringComparison.Ordinal))
            .Where(n => MatchesAll(n.Properties, q.Conditions))
            .ToList();

        return result;
    }

    private object? QueryEdges(QueryEdges op)
    {
        var q = op.Query;
        var type = q.Type;
        var conditions = q.Conditions;

        var result = _store.Edges
            .Where(e => string.IsNullOrWhiteSpace(type) || string.Equals(e.Type, type, StringComparison.Ordinal))
            .Where(e => MatchesAll(e.Properties, conditions))
            .ToList();

        return result;
    }

    private object? DeleteEdges(DeleteEdges op)
    {
        var q = op.Query;
        var type = q.Type;
        var conditions = q.Conditions;

        var toDelete = _store.Edges
            .Where(e => string.IsNullOrWhiteSpace(type) || string.Equals(e.Type, type, StringComparison.Ordinal))
            .Where(e => MatchesAll(e.Properties, conditions))
            .Select(e => e.Id)
            .ToList();

        foreach (var id in toDelete)
        {
            _store.RemoveEdge(id);
        }

        return null;
    }

    private static bool MatchesAll(IReadOnlyDictionary<string, Value> props, IReadOnlyList<Condition> conditions)
    {
        for (var i = 0; i < conditions.Count; i++)
        {
            var c = conditions[i];
            if (!props.TryGetValue(c.Property, out var current))
            {
                return false;
            }

            if (!Matches(current, c.Operator, c.Value))
            {
                return false;
            }
        }

        return true;
    }

    private static bool Matches(Value current, Operator op, Value expected)
    {
        return op switch
        {
            Operator.Equals => EqualsValue(current, expected),
            Operator.Contains => ContainsValue(current, expected),
            Operator.GreaterThan => CompareNumber(current, expected, greaterThan: true),
            Operator.LessThan => CompareNumber(current, expected, greaterThan: false),
            _ => false
        };
    }

    private static bool EqualsValue(Value a, Value b) => (a, b) switch
    {
        (StringValue x, StringValue y) => string.Equals(x.Data, y.Data, StringComparison.Ordinal),
        (BoolValue x, BoolValue y) => x.Data == y.Data,
        (IntValue x, IntValue y) => x.Data == y.Data,
        (FloatValue x, FloatValue y) => x.Data.Equals(y.Data),
        // Allow numeric cross-compare
        (IntValue x, FloatValue y) => ((double)x.Data).Equals(y.Data),
        (FloatValue x, IntValue y) => x.Data.Equals((double)y.Data),
        _ => false
    };

    private static bool ContainsValue(Value current, Value expected) => (current, expected) switch
    {
        (StringValue x, StringValue y) => x.Data.Contains(y.Data, StringComparison.Ordinal),
        _ => false
    };

    private static bool CompareNumber(Value current, Value expected, bool greaterThan)
    {
        if (!TryAsDouble(current, out var left) || !TryAsDouble(expected, out var right))
        {
            return false;
        }

        return greaterThan ? left > right : left < right;
    }

    private static bool TryAsDouble(Value v, out double value)
    {
        switch (v)
        {
            case IntValue i:
                value = i.Data;
                return true;
            case FloatValue f:
                value = f.Data;
                return true;
            default:
                value = 0;
                return false;
        }
    }

    private static string ResolveOrGenerateId(IReadOnlyDictionary<string, Value> props, string key)
    {
        if (props.TryGetValue(key, out var v) && v is StringValue s && !string.IsNullOrWhiteSpace(s.Data))
        {
            return s.Data.Trim();
        }

        return Guid.NewGuid().ToString("N");
    }

    private static IReadOnlyDictionary<string, Value> MergeWithId(IReadOnlyDictionary<string, Value> props, string id)
    {
        if (props.TryGetValue("id", out var existing) && existing is StringValue s && !string.IsNullOrWhiteSpace(s.Data))
        {
            // Keep original dictionary if it already has a valid string id.
            return props;
        }

        var merged = new Dictionary<string, Value>(props, StringComparer.Ordinal)
        {
            ["id"] = new StringValue(id)
        };
        return merged;
    }

    private static IReadOnlyDictionary<string, Value> MergeProperties(
        IReadOnlyDictionary<string, Value> existing,
        IReadOnlyDictionary<string, Value> delta,
        string? ignoreKey)
    {
        if (delta.Count == 0)
        {
            return existing;
        }

        var merged = new Dictionary<string, Value>(existing, StringComparer.Ordinal);
        foreach (var kv in delta)
        {
            if (!string.IsNullOrWhiteSpace(ignoreKey) && string.Equals(kv.Key, ignoreKey, StringComparison.Ordinal))
            {
                continue;
            }

            merged[kv.Key] = kv.Value;
        }

        return merged;
    }
}


