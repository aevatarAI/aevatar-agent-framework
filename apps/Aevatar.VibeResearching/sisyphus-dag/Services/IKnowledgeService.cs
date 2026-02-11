namespace SisyphusDag.Services;

using SisyphusDag.Dtos;

/// <summary>
/// Business logic for knowledge DAG operations.
/// </summary>
public interface IKnowledgeService
{
    /// <summary>
    /// Creates knowledge nodes and their dependency edges.
    /// Returns the list of generated node IDs in the same order as input.
    /// </summary>
    Task<List<string>> CreateAsync(KnowledgesDto request, CancellationToken ct = default);

    /// <summary>
    /// Reads the complete knowledge DAG snapshot.
    /// </summary>
    Task<KnowledgeSnapshotDto> GetSnapshotAsync(CancellationToken ct = default);

    /// <summary>
    /// Updates knowledge nodes by ID.
    /// Returns the list of node IDs that were requested for update.
    /// </summary>
    Task<List<string>> UpdateAsync(
        Dictionary<string, KnowledgeDto> updates,
        CancellationToken ct = default);

    /// <summary>
    /// Deletes knowledge nodes (DETACH DELETE) by ID.
    /// Returns the input list of node IDs.
    /// </summary>
    Task<List<string>> DeleteAsync(List<string> ids, CancellationToken ct = default);

    /// <summary>
    /// Generates a comprehensive markdown explanation for a knowledge node,
    /// including metadata, full content, and derivation chain.
    /// </summary>
    /// <param name="id">The knowledge node ID.</param>
    /// <param name="level">Max BFS traversal depth for upstream/downstream chains. Default 10.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Markdown string.</returns>
    /// <exception cref="KeyNotFoundException">Thrown when the node ID does not exist.</exception>
    Task<string> ExplainAsync(string id, int level = 10, CancellationToken ct = default);
}
