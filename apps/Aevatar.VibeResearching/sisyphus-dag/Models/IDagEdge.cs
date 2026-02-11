namespace SisyphusDag.Models;

/// <summary>
/// Base contract for all DAG edges.
/// </summary>
public interface IDagEdge
{
    string FromId { get; }
    string ToId { get; }
    string CreatedBy { get; }
    string CreatedAt { get; }
    string UpdatedBy { get; }
    string UpdatedAt { get; }
}
