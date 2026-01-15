using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Aevatar.CognitiveMesh.Dsl.Models;

/// <summary>
/// Represents the top-level DSL artifact that describes a cognitive mesh topology.
/// </summary>
public sealed record MeshDefinition(
    [property: JsonPropertyName("dsl_version")] string DslVersion,
    GoalSpec Goal,
    StrategyKind Strategy,
    BudgetSpec Budget,
    IReadOnlyList<NodeSpec> Nodes,
    IReadOnlyList<EdgeSpec> Edges,
    IReadOnlyList<ConstraintSpec> Constraints)
{
    public static MeshDefinition Empty => new(
        DslVersion: "0.0",
        Goal: new GoalSpec(string.Empty, null),
        Strategy: StrategyKind.Cot,
        Budget: new BudgetSpec(0, 0),
        Nodes: Array.Empty<NodeSpec>(),
        Edges: Array.Empty<EdgeSpec>(),
        Constraints: Array.Empty<ConstraintSpec>());
}

public sealed record GoalSpec(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("success_metric")] string? SuccessMetric);

public sealed record BudgetSpec(
    [property: JsonPropertyName("max_steps")] int MaxSteps,
    [property: JsonPropertyName("token_limit")] int TokenLimit);

[JsonConverter(typeof(StrategyKindConverter))]
public enum StrategyKind
{
    Cot,
    Tot,
    Got,
    UotComb,
    UotExpl,
    UotTrans
}

public sealed record NodeSpec
{
    public NodeSpec(
        string id,
        string type,
        IReadOnlyDictionary<string, JsonElement>? @params = null)
    {
        Id = id;
        Type = type;
        Params = @params is null
            ? new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, JsonElement>(@params, StringComparer.OrdinalIgnoreCase);
    }

    public string Id { get; }

    public string Type { get; }

    public IReadOnlyDictionary<string, JsonElement> Params { get; }
}

public sealed record EdgeSpec(
    [property: JsonPropertyName("from")] string From,
    [property: JsonPropertyName("to")] string To,
    [property: JsonPropertyName("channel")] string Channel);

public sealed record ConstraintSpec(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("value")] JsonElement Payload);

