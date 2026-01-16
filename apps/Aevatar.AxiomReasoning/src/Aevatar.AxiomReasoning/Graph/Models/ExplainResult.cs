namespace Aevatar.AxiomReasoning.Graph.Models;

public sealed record ExplainResult
{
    public string SessionId { get; init; } = "";
    public Node? Node { get; init; }

    public List<string> DirectDependencies { get; init; } = [];
    public List<string> TopologicalOrder { get; init; } = [];

    public bool HasCycle { get; init; }
    public bool ProvableFromAxioms { get; init; }

    public List<Node> MissingDependencies { get; init; } = [];
}