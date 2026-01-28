namespace Aevatar.VibeResearching.Sessions.Constants;

/// <summary>
/// Constants for session management.
/// </summary>
public static class SessionConsts
{
    /// <summary>
    /// Global DAG ID shared by all sessions for cross-session knowledge sharing.
    /// All sessions share a single global knowledge graph by default.
    /// </summary>
    public const string GlobalDagId = "global";

    /// <summary>
    /// Maximum length for session IDs.
    /// </summary>
    public const int MaxSessionIdLength = 64;

    /// <summary>
    /// Maximum number of messages to keep in session message log for reconnect.
    /// </summary>
    public const int MaxMessagesSnapshot = 200;

    /// <summary>
    /// Index key for session index storage.
    /// </summary>
    public const string SessionIndexKey = "vibe_researching_sessions_index";
}
