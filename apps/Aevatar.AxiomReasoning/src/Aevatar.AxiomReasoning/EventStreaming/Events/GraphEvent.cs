namespace Aevatar.AxiomReasoning.EventStreaming.Events;

public record GraphEvent : AxiomEvent
{
    public int Iteration { get; init; }
    public List<string> Axioms { get; init; } = [];
    public List<AssumptionNode> Assumptions { get; init; } = [];
    public List<TheoremNode> Theorems { get; init; } = [];
}

public record TheoremNode
{
    public string Id { get; init; } = "";
    public string Statement { get; init; } = "";
    public string Proof { get; init; } = "";
    public List<string> DependsOn { get; init; } = [];
}

public record AssumptionNode
{
    public string Id { get; init; } = "";
    public string Statement { get; init; } = "";
    public string Motivation { get; init; } = "";
}