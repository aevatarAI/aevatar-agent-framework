namespace Aevatar.AxiomReasoning.Graph.Models;

using System.Text.Json.Serialization;

public sealed record Node
{
    public string Id { get; init; } = "";
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public NodeType Type { get; init; } = NodeType.Unknown;
    public string Label { get; init; } = "";
    public string Proof { get; init; } = "";
    public DateTimeOffset UpdatedAt { get; init; } = DateTimeOffset.UtcNow;
}