using Microsoft.Extensions.Logging;
using Aevatar.VibeResearching.Agents.Contracts.Collab;
using Aevatar.VibeResearching.Agents.Contracts.Sessions;

namespace Aevatar.VibeResearching.Knowledge;

/// <summary>
/// Repository interface for knowledge DAG (Directed Acyclic Graph) operations.
/// Manages the persistent knowledge graph with consensus and staging support.
/// </summary>
public interface IDagRepository
{
    /// <summary>
    /// Gets the file path for the DAG snapshot.
    /// </summary>
    string GetSnapshotPath(string dagId);

    /// <summary>
    /// Loads the current DAG snapshot.
    /// </summary>
    Task<SraDagSnapshot> LoadSnapshotAsync(string dagId, CancellationToken ct);

    /// <summary>
    /// Applies a mutation to the DAG and returns the updated snapshot.
    /// </summary>
    Task<SraDagSnapshot> ApplyMutationAsync(string dagId, SraDagMutation mutation, CancellationToken ct);

    /// <summary>
    /// Writes a staged mutation candidate (not yet promoted to main DAG).
    /// Returns the relative path to the staged file.
    /// </summary>
    Task<string> WriteStagedAsync(string dagId, SraDagMutation candidate, CancellationToken ct);

    /// <summary>
    /// Lists all staged mutation candidates.
    /// </summary>
    Task<List<object>> ListStagedAsync(string dagId, CancellationToken ct);

    /// <summary>
    /// Loads a merged DAG snapshot: global knowledge nodes + current session's plan nodes.
    /// Use this for session-level DAG views that need cross-session knowledge visibility.
    /// </summary>
    Task<SraDagSnapshot> LoadMergedSnapshotAsync(string sessionId, CancellationToken ct);

    /// <summary>
    /// Gets the DAG snapshot formatted for list/UI display.
    /// Includes plan nodes for the current session if specified.
    /// </summary>
    Task<object> GetSnapshotForListAsync(string dagId, CancellationToken ct, string? currentSessionId = null);
}
