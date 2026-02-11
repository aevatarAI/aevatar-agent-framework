namespace SisyphusDag.Models;

/// <summary>
/// Base contract for all DAG nodes.
/// </summary>
public interface IDagNode
{
    string Id { get; }
    string Title { get; }
    string CreatedBy { get; }
    string CreatedAt { get; }
    string UpdatedBy { get; }
    string UpdatedAt { get; }
}
