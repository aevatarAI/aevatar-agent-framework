namespace Aevatar.VibeResearching.Comments.Entities;

/// <summary>
/// Represents a comment on a DAG node within a research session.
/// Stored as a MongoDB document.
/// </summary>
public class NodeComment
{
    /// <summary>
    /// Unique identifier for the comment.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// The DAG node this comment is attached to.
    /// </summary>
    public string NodeId { get; set; } = string.Empty;

    /// <summary>
    /// The research session this comment belongs to.
    /// </summary>
    public string SessionId { get; set; } = string.Empty;

    /// <summary>
    /// Markdown content of the comment.
    /// </summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// The user who authored the comment.
    /// </summary>
    public Guid AuthorId { get; set; }

    /// <summary>
    /// Display name of the author at the time of posting.
    /// </summary>
    public string AuthorDisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Parent comment ID for threaded replies. Null for top-level comments.
    /// </summary>
    public Guid? ParentCommentId { get; set; }

    /// <summary>
    /// List of user IDs mentioned in the comment.
    /// </summary>
    public List<Guid> MentionedUserIds { get; set; } = [];

    /// <summary>
    /// Timestamp when the comment was created.
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// Timestamp when the comment was last edited. Null if never edited.
    /// </summary>
    public DateTime? EditedAt { get; set; }

    /// <summary>
    /// Whether the comment has been soft-deleted.
    /// </summary>
    public bool IsDeleted { get; set; }

    /// <summary>
    /// Timestamp when the comment was soft-deleted.
    /// </summary>
    public DateTime? DeletedAt { get; set; }
}
