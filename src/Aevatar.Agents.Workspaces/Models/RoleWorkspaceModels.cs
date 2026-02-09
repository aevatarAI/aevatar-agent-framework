namespace Aevatar.Agents.Workspaces.Models;

public sealed record RoleInstanceSnapshot(
    string Role,
    string ActorId,
    DateTimeOffset CreatedAt,
    bool IsRoot);

public sealed record RoleGraphSnapshot(
    string RootRole,
    IReadOnlyList<RoleGraphNode> Nodes,
    IReadOnlyList<RoleGraphEdge> Edges);

public sealed record RoleGraphNode(
    string Role,
    string ActorId,
    bool IsRoot);

public sealed record RoleGraphEdge(
    string ParentRole,
    string ChildRole);
