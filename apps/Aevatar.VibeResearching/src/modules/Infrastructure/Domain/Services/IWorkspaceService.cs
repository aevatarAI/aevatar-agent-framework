namespace Aevatar.VibeResearching.Infrastructure;

/// <summary>
/// Service interface for workspace path resolution and directory management.
/// Ensures deterministic, safe workspace directory structure for file-based collaboration.
/// </summary>
public interface IWorkspaceService
{
    /// <summary>
    /// Ensures the session workspace exists and returns all relevant paths.
    /// Creates directories if they don't exist (idempotent).
    /// </summary>
    WorkspacePaths EnsureSessionWorkspace(string sessionId);

    /// <summary>
    /// Ensures the DAG workspace exists and returns all relevant paths.
    /// DAG workspaces are shared across sessions for global knowledge graphs.
    /// </summary>
    DagWorkspacePaths EnsureDagWorkspace(string dagId);

    /// <summary>
    /// Scans workspace for facts and artifacts (bounded scan for UI display).
    /// </summary>
    WorkspaceScanResult ScanWorkspace(string sessionId);
}

/// <summary>
/// Container for session workspace paths.
/// </summary>
public record WorkspacePaths
{
    public required string SystemRoot { get; init; }
    public required string WorkspaceRoot { get; init; }
    public required string SessionsRoot { get; init; }
    public required string SessionId { get; init; }
    public required string SessionRoot { get; init; }
    public required string PaperDir { get; init; }
    public required string PaperOutlinePath { get; init; }
    public required string PaperDraftPath { get; init; }
    public required string FactsProposedDir { get; init; }
    public required string DecisionsDir { get; init; }
    public required string MailboxDir { get; init; }
    public required string MailboxDeadDir { get; init; }
    public required string RunsDir { get; init; }
    public required string ArtifactsDir { get; init; }
    public required string DeliverablesDir { get; init; }
    public required string TmpDir { get; init; }
}

/// <summary>
/// Container for DAG workspace paths.
/// </summary>
public record DagWorkspacePaths
{
    public required string SystemRoot { get; init; }
    public required string WorkspaceRoot { get; init; }
    public required string DagsRoot { get; init; }

    public required string DagId { get; init; }
    public required string DagRoot { get; init; }

    public required string ArtifactsDir { get; init; }
    public required string TmpDir { get; init; }
}
