namespace SisyphusDag.Models;

/// <summary>
/// Represents a knowledge node in the DAG. Immutable record.
/// Neo4j label: KnowledgeNode
/// </summary>
public sealed record KnowledgeNode(
    string Id,
    string Title,
    string SessionId,
    string Description,
    string DeriveDetails,
    string ResourceUri,
    List<string> References,
    string LastReviewedAt,
    bool IsActive,
    string DeactivateReason,
    string DeactivateAt,
    string CreatedBy,
    string CreatedAt,
    string UpdatedBy,
    string UpdatedAt
) : IDagNode;
