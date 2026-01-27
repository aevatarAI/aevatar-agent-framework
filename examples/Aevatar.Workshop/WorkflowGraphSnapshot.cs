namespace Aevatar.Workshop;

public sealed record WorkflowGraphSnapshot(
    string WorkflowId,
    IReadOnlyList<WorkflowNodeDto> Nodes,
    IReadOnlyList<WorkflowEdgeDto> Edges,
    DateTimeOffset UpdatedAt);

public sealed record WorkflowNodeDto(
    string Id,
    string Type,
    string Label,
    int Depth,
    IReadOnlyDictionary<string, object?>? Meta = null);

public sealed record WorkflowEdgeDto(
    string From,
    string To,
    string Channel);
