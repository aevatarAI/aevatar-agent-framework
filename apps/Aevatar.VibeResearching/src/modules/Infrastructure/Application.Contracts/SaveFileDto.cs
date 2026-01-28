namespace Aevatar.VibeResearching.Infrastructure;

/// <summary>
/// Input DTO for saving a file to the session workspace.
/// </summary>
public sealed class SaveFileDto
{
    /// <summary>
    /// Relative path under session workspace (required).
    /// </summary>
    public string? Path { get; init; }

    /// <summary>
    /// File content (text, required).
    /// </summary>
    public string? Content { get; init; }
}
