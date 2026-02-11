namespace SisyphusDag.Models;

/// <summary>
/// Represents a DEPENDS_ON relationship between two knowledge nodes.
/// Neo4j relationship type: DEPENDS_ON
/// </summary>
public sealed record KnowledgeDependsOnEdge(
    string FromId,
    string ToId,
    string CreatedBy,
    string CreatedAt,
    string UpdatedBy,
    string UpdatedAt
) : IDagEdge;
