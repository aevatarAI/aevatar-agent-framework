namespace Aevatar.VibeResearching.Comments.DTOs;

/// <summary>
/// Full comment DTO returned in list responses.
/// </summary>
public class CommentDto
{
    /// <summary>
    /// Unique identifier.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Node this comment belongs to.
    /// </summary>
    public string NodeId { get; set; } = string.Empty;

    /// <summary>
    /// Session this comment belongs to.
    /// </summary>
    public string SessionId { get; set; } = string.Empty;

    /// <summary>
    /// Markdown content.
    /// </summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// Author user ID.
    /// </summary>
    public Guid AuthorId { get; set; }

    /// <summary>
    /// Author display name.
    /// </summary>
    public string AuthorDisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Parent comment ID (null for top-level).
    /// </summary>
    public Guid? ParentCommentId { get; set; }

    /// <summary>
    /// Mentioned user IDs.
    /// </summary>
    public List<Guid> MentionedUserIds { get; set; } = [];

    /// <summary>
    /// Creation timestamp.
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// Last edit timestamp. Null if never edited.
    /// </summary>
    public DateTime? EditedAt { get; set; }

    /// <summary>
    /// Child replies (populated when grouping by parent).
    /// </summary>
    public List<CommentDto> Replies { get; set; } = [];
}
