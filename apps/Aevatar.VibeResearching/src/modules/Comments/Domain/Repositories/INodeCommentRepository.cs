using Aevatar.VibeResearching.Comments.Entities;

namespace Aevatar.VibeResearching.Comments.Repositories;

/// <summary>
/// Repository interface for NodeComment persistence operations.
/// </summary>
public interface INodeCommentRepository
{
    /// <summary>
    /// Gets a paged list of comments for a specific node, excluding soft-deleted ones.
    /// </summary>
    Task<List<NodeComment>> GetListByNodeIdAsync(
        string sessionId, string nodeId, int skip, int take, CancellationToken ct = default);

    /// <summary>
    /// Gets the latest preview comments for a node (limited count, non-deleted).
    /// </summary>
    Task<List<NodeComment>> GetPreviewByNodeIdAsync(
        string sessionId, string nodeId, int count, CancellationToken ct = default);

    /// <summary>
    /// Gets the total count of non-deleted comments for a node.
    /// </summary>
    Task<long> GetCountByNodeIdAsync(
        string sessionId, string nodeId, CancellationToken ct = default);

    /// <summary>
    /// Gets a single comment by its ID.
    /// </summary>
    Task<NodeComment?> GetAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Inserts a new comment.
    /// </summary>
    Task InsertAsync(NodeComment comment, CancellationToken ct = default);

    /// <summary>
    /// Updates an existing comment.
    /// </summary>
    Task UpdateAsync(NodeComment comment, CancellationToken ct = default);
}
