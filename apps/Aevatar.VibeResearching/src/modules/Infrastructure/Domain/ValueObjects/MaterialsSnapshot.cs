namespace Aevatar.VibeResearching.Infrastructure;

/// <summary>
/// Snapshot of materials (facts) loaded for a research session.
/// </summary>
public sealed record MaterialsSnapshot
{
    public required string SessionId { get; init; }
    public required string DagId { get; init; }
    public required DateTimeOffset LoadedAt { get; init; }

    public required List<MaterialFile> Facts { get; init; }

    /// <summary>
    /// Bounded string for LLM injection.
    /// </summary>
    public required string RenderedContext { get; init; }
}

/// <summary>
/// Represents a single material file (fact) in the workspace.
/// </summary>
public sealed record MaterialFile
{
    public required string Kind { get; init; }            // fact
    public required string Id { get; init; }              // stable id: dag:{nodeId}
    public required string Title { get; init; }           // label or fallback id
    public required string RelativePath { get; init; }    // dag/{nodeId}
    public required string FullPath { get; init; }        // dag:{nodeId}
    public required string Content { get; init; }         // bounded content
}
