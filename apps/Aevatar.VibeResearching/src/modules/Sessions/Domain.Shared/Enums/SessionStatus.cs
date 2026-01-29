namespace Aevatar.VibeResearching.Sessions.Enums;

/// <summary>
/// Represents the lifecycle status of a research session.
/// </summary>
public enum SessionStatus
{
    /// <summary>
    /// Session is running or idle, accepting input.
    /// </summary>
    Active = 1,

    /// <summary>
    /// Session is frozen. Research progress preserved. No new input accepted.
    /// </summary>
    Paused = 2,

    /// <summary>
    /// Permanently terminated. Hidden from default list. Knowledge nodes unaffected.
    /// </summary>
    Archived = 3
}
