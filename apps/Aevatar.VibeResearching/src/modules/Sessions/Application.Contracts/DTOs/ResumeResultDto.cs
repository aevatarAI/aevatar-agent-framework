namespace Aevatar.VibeResearching.Sessions.DTOs;

/// <summary>
/// Result of resuming a paused session.
/// </summary>
public class ResumeResultDto
{
    /// <summary>
    /// Whether the resume operation succeeded.
    /// </summary>
    public bool Ok { get; set; } = true;

    /// <summary>
    /// Whether a research run was automatically re-triggered using the last user message.
    /// False when there is no previous user message and the user must submit new input.
    /// </summary>
    public bool AutoResumed { get; set; }

    /// <summary>
    /// The run ID of the auto-resumed run, if any.
    /// </summary>
    public string? RunId { get; set; }
}
