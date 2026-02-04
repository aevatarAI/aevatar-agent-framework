namespace Aevatar.VibeResearching.Agents.Pivot;

/// <summary>
/// Domain interface for pivot operation status tracking.
/// Implementation lives in domain layer.
/// </summary>
public interface IPivotStatusService
{
    /// <summary>
    /// Gets the current pivot status for a session.
    /// </summary>
    Task<object> GetStatusAsync(string sessionId, CancellationToken ct);

    /// <summary>
    /// Updates the pivot status for a session.
    /// </summary>
    Task UpdateStatusAsync(string sessionId, PivotStatus status, string? message, CancellationToken ct);

    /// <summary>
    /// Clears the pivot status for a session.
    /// </summary>
    Task ClearStatusAsync(string sessionId, CancellationToken ct);
}
