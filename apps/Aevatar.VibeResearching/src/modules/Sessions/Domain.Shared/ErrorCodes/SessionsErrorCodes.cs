namespace Aevatar.VibeResearching.Sessions.ErrorCodes;

/// <summary>
/// Error codes for Sessions module.
/// Used for standardized error handling and localization.
/// </summary>
public static class SessionsErrorCodes
{
    /// <summary>Prefix for all Sessions error codes.</summary>
    private const string Prefix = "VibeResearching.Sessions:";

    // Session errors
    public const string SessionNotFound = Prefix + "SessionNotFound";
    public const string SessionAlreadyExists = Prefix + "SessionAlreadyExists";
    public const string SessionAlreadyActive = Prefix + "SessionAlreadyActive";
    public const string InvalidSessionId = Prefix + "InvalidSessionId";
    public const string SessionCreationFailed = Prefix + "SessionCreationFailed";

    // Workspace errors
    public const string WorkspaceNotFound = Prefix + "WorkspaceNotFound";
    public const string WorkspaceAccessDenied = Prefix + "WorkspaceAccessDenied";
    public const string FileNotFound = Prefix + "FileNotFound";
    public const string FileUploadFailed = Prefix + "FileUploadFailed";
    public const string InvalidFilePath = Prefix + "InvalidFilePath";

    // General errors
    public const string OperationCancelled = Prefix + "OperationCancelled";
    public const string OperationTimeout = Prefix + "OperationTimeout";
    public const string InvalidInput = Prefix + "InvalidInput";
    public const string UnexpectedError = Prefix + "UnexpectedError";
}
