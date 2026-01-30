namespace Aevatar.VibeResearching.Comments.DTOs;

/// <summary>
/// Truncated comment DTO for preview display.
/// </summary>
public class CommentPreviewDto
{
    /// <summary>
    /// Comment ID.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Author display name.
    /// </summary>
    public string AuthorDisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Truncated content for preview.
    /// </summary>
    public string ContentPreview { get; set; } = string.Empty;

    /// <summary>
    /// Creation timestamp.
    /// </summary>
    public DateTime CreatedAt { get; set; }
}
