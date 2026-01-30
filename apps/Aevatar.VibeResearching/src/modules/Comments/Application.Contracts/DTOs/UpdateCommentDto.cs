using System.ComponentModel.DataAnnotations;

namespace Aevatar.VibeResearching.Comments.DTOs;

/// <summary>
/// Input DTO for updating an existing comment.
/// </summary>
public class UpdateCommentDto
{
    /// <summary>
    /// Updated markdown content.
    /// </summary>
    [Required]
    [MaxLength(CommentsConsts.MaxContentLength)]
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// Updated list of mentioned user IDs.
    /// </summary>
    public List<Guid>? MentionedUserIds { get; set; }
}
