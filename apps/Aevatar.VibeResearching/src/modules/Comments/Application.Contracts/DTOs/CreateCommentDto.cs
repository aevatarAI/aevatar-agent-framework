using System.ComponentModel.DataAnnotations;

namespace Aevatar.VibeResearching.Comments.DTOs;

/// <summary>
/// Input DTO for creating a new comment.
/// </summary>
public class CreateCommentDto
{
    /// <summary>
    /// Markdown content of the comment.
    /// </summary>
    [Required]
    [MaxLength(CommentsConsts.MaxContentLength)]
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// Parent comment ID for threaded replies. Null for top-level comments.
    /// </summary>
    public Guid? ParentCommentId { get; set; }

    /// <summary>
    /// List of user IDs mentioned in the comment.
    /// </summary>
    public List<Guid>? MentionedUserIds { get; set; }
}
