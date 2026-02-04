namespace Aevatar.VibeResearching.Comments.DTOs;

/// <summary>
/// Result DTO containing preview comments and total count for a node.
/// </summary>
public class CommentPreviewResultDto
{
    /// <summary>
    /// Total number of non-deleted comments on this node.
    /// </summary>
    public long TotalCount { get; set; }

    /// <summary>
    /// Latest preview comments (truncated content).
    /// </summary>
    public List<CommentPreviewDto> Comments { get; set; } = [];
}
