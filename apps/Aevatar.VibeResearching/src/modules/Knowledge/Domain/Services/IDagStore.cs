using Aevatar.VibeResearching.Agents.Contracts.Collab;

namespace Aevatar.VibeResearching.Knowledge;

/// <summary>
/// Domain interface for DAG (Directed Acyclic Graph) storage service.
/// Provides file-based single source of truth for research knowledge graphs.
/// Implementation lives in infrastructure layer.
/// </summary>
public interface IDagStore
{
    /// <summary>
    /// Gets the file path for the DAG snapshot.
    /// </summary>
    string GetSnapshotPath(string dagId);

    /// <summary>
    /// Loads the DAG snapshot for the specified DAG ID.
    /// </summary>
    Task<SraDagSnapshot> LoadSnapshotAsync(string dagId, CancellationToken ct);

    /// <summary>
    /// Applies a mutation to the DAG and returns the updated snapshot.
    /// </summary>
    Task<SraDagSnapshot> ApplyMutationAsync(string dagId, SraDagMutation mutation, CancellationToken ct);

    /// <summary>
    /// Writes a staged (no-consensus) mutation candidate.
    /// </summary>
    Task<string> WriteStagedAsync(string dagId, SraDagMutation candidate, CancellationToken ct);

    /// <summary>
    /// Lists staged mutation candidates.
    /// </summary>
    Task<List<object>> ListStagedAsync(string dagId, CancellationToken ct);

    /// <summary>
    /// Gets a snapshot suitable for listing/display purposes.
    /// </summary>
    Task<object> GetSnapshotForListAsync(string dagId, CancellationToken ct, string? currentSessionId = null);
}
