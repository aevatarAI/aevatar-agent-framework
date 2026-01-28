namespace Aevatar.VibeResearching.Agents.Mesh.ValueObjects;

/// <summary>
/// Result of executing a mesh execution plan.
/// </summary>
public sealed record MeshRunResult(
    bool Ok,
    IReadOnlyDictionary<string, string> OutputsByNodeId,
    IReadOnlyList<string> Errors);
