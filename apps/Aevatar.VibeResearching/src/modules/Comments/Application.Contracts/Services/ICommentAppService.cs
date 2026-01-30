using Aevatar.VibeResearching.Comments.DTOs;
using Volo.Abp.Application.Services;

namespace Aevatar.VibeResearching.Comments.Services;

/// <summary>
/// Application service for managing node comments.
/// </summary>
public interface ICommentAppService : IApplicationService
{
    /// <summary>
    /// Gets a paged list of comments for a node, with replies grouped under parents.
    /// </summary>
    Task<List<CommentDto>> GetListAsync(
        string sessionId, string nodeId, int skip = 0, int take = 50, CancellationToken ct = default);

    /// <summary>
    /// Gets the total count of comments for a node.
    /// </summary>
    Task<long> GetCountAsync(
        string sessionId, string nodeId, CancellationToken ct = default);

    /// <summary>
    /// Gets a preview of the latest comments and total count for a node.
    /// </summary>
    Task<CommentPreviewResultDto> GetPreviewAsync(
        string sessionId, string nodeId, CancellationToken ct = default);

    /// <summary>
    /// Creates a new comment on a node.
    /// </summary>
    Task<CommentDto> CreateAsync(
        string sessionId, string nodeId, CreateCommentDto input, CancellationToken ct = default);

    /// <summary>
    /// Updates an existing comment. Only the author or an admin may update.
    /// </summary>
    Task<CommentDto> UpdateAsync(
        Guid commentId, UpdateCommentDto input, CancellationToken ct = default);

    /// <summary>
    /// Soft-deletes a comment. Only the author or an admin may delete.
    /// </summary>
    Task DeleteAsync(Guid commentId, CancellationToken ct = default);
}
