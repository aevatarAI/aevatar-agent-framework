using System.Collections.Concurrent;
using AevatarKit;
using Google.Protobuf.WellKnownTypes;

namespace AevatarKit.Runtime.Graphs;

public interface IGraphRegistry
{
    IReadOnlyList<GraphSummary> List(string? query = null);
    GraphDefinition? Get(string graphId);
    GraphDefinition Create(CreateGraphRequest request);
    GraphDefinition Update(string graphId, UpdateGraphRequest request);
}

/// <summary>
/// In-memory Graph registry for MVP.
/// Stores the raw YAML/JSON source (MVP), later SSOT will be Protobuf IR.
/// </summary>
public sealed class InMemoryGraphRegistry : IGraphRegistry
{
    private readonly ConcurrentDictionary<string, GraphDefinition> _graphs = new();

    public InMemoryGraphRegistry()
    {
        // Seed a demo graph for UI.
        var now = Timestamp.FromDateTime(DateTime.UtcNow);
        var g = new GraphDefinition
        {
            GraphId = Guid.NewGuid().ToString("N"),
            Name = "Demo Graph",
            Description = "MVP demo graph (YAML placeholder).",
            Version = 1,
            Source = @"# MVP Graph (placeholder)
steps:
  - id: input
    type: input
  - id: llm_call
    type: llm_call
  - id: transform
    type: transform",
            CreatedAt = now,
            UpdatedAt = now
        };
        _graphs[g.GraphId] = g;
    }

    public IReadOnlyList<GraphSummary> List(string? query = null)
    {
        var q = Normalize(query);
        var graphs = _graphs.Values.ToList();

        if (!string.IsNullOrWhiteSpace(q))
        {
            graphs = graphs
                .Where(g =>
                    (g.Name?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false) ||
                    (g.Description?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false))
                .ToList();
        }

        return graphs
            .OrderByDescending(g => g.UpdatedAt?.ToDateTime() ?? DateTime.MinValue)
            .Select(g => new GraphSummary
            {
                GraphId = g.GraphId,
                Name = g.Name,
                Version = g.Version,
                UpdatedAt = g.UpdatedAt
            })
            .ToList();
    }

    public GraphDefinition? Get(string graphId)
    {
        if (string.IsNullOrWhiteSpace(graphId))
        {
            return null;
        }

        return _graphs.TryGetValue(graphId.Trim(), out var g) ? g.Clone() : null;
    }

    public GraphDefinition Create(CreateGraphRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var name = (request.Name ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("name is required");
        }

        var now = Timestamp.FromDateTime(DateTime.UtcNow);
        var g = new GraphDefinition
        {
            GraphId = Guid.NewGuid().ToString("N"),
            Name = name,
            Description = request.Description ?? string.Empty,
            Version = 1,
            Source = request.Source ?? string.Empty,
            CreatedAt = now,
            UpdatedAt = now
        };

        _graphs[g.GraphId] = g;
        return g.Clone();
    }

    public GraphDefinition Update(string graphId, UpdateGraphRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(graphId))
        {
            throw new ArgumentException("graphId is required");
        }

        var id = graphId.Trim();
        if (!_graphs.TryGetValue(id, out var existing))
        {
            throw new KeyNotFoundException("graph not found");
        }

        var name = (request.Name ?? existing.Name ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("name is required");
        }

        var updated = existing.Clone();
        updated.Name = name;
        updated.Description = request.Description ?? updated.Description ?? string.Empty;
        updated.Source = request.Source ?? updated.Source ?? string.Empty;
        updated.Version = Math.Max(1, updated.Version + 1);
        updated.UpdatedAt = Timestamp.FromDateTime(DateTime.UtcNow);

        _graphs[id] = updated;
        return updated.Clone();
    }

    private static string? Normalize(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}


