namespace Aevatar.AxiomReasoning.Graph.Models;

public sealed record Edge
{
    public string FromId { get; init; } = "";
    public string ToId { get; init; } = "";
    public string Type { get; init; } = "depends_on";
}