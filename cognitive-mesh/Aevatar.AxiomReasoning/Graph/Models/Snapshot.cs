namespace Aevatar.AxiomReasoning.Graph.Models;

public sealed record Snapshot
{
    public string SessionId { get; init; } = "";
    public List<Node> Nodes { get; init; } = [];
    public List<Edge> Edges { get; init; } = [];
}