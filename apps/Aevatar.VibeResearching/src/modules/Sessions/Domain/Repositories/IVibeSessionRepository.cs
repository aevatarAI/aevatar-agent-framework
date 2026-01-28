using Aevatar.VibeResearching.Agents.Contracts.Sessions;

namespace Aevatar.VibeResearching.Sessions.Repositories;

/// <summary>
/// Repository interface for managing vibe research session persistence.
/// </summary>
public interface IVibeSessionRepository
{
    /// <summary>
    /// Gets a session record by session ID.
    /// </summary>
    Task<VibeSessionRecord?> GetAsync(string sessionId, CancellationToken ct = default);

    /// <summary>
    /// Saves or updates a session record.
    /// </summary>
    Task SaveAsync(VibeSessionRecord record, CancellationToken ct = default);

    /// <summary>
    /// Checks if a session exists.
    /// </summary>
    Task<bool> ExistsAsync(string sessionId, CancellationToken ct = default);

    /// <summary>
    /// Lists all session records.
    /// </summary>
    Task<IReadOnlyList<VibeSessionRecord>> ListAsync(CancellationToken ct = default);
}
