using Volo.Abp.Application.Services;

namespace Aevatar.VibeResearching.Agents.Application.Contracts.Services;

/// <summary>
/// Application service for Review Agent operations.
/// Provides automated knowledge graph review and cleanup functionality.
/// </summary>
public interface IReviewAgentAppService : IApplicationService
{
    /// <summary>
    /// Gets the current status of the Review Agent.
    /// </summary>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Current status including counters and timestamps</returns>
    Task<object> GetStatusAsync(CancellationToken ct = default);

    /// <summary>
    /// Gets the current Review Agent settings.
    /// </summary>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Current configuration values</returns>
    Task<object> GetSettingsAsync(CancellationToken ct = default);

    /// <summary>
    /// Updates Review Agent settings.
    /// </summary>
    /// <param name="settings">Updated settings</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Updated configuration</returns>
    Task<object> UpdateSettingsAsync(object settings, CancellationToken ct = default);

    /// <summary>
    /// Gets review iteration history.
    /// </summary>
    /// <param name="limit">Maximum number of iterations to return</param>
    /// <param name="offset">Offset for pagination</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Paginated list of review iterations</returns>
    Task<object> GetIterationsAsync(int limit = 10, int offset = 0, CancellationToken ct = default);

    /// <summary>
    /// Gets details of a specific review iteration.
    /// </summary>
    /// <param name="iterationId">Iteration identifier</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Iteration details including all log entries</returns>
    Task<object?> GetIterationAsync(string iterationId, CancellationToken ct = default);

    /// <summary>
    /// Gets review log entries for the current iteration in progress.
    /// </summary>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Current iteration entries, empty if no iteration is in progress</returns>
    Task<object> GetCurrentIterationEntriesAsync(CancellationToken ct = default);

    /// <summary>
    /// Gets the knowledge graph with review status for visualization.
    /// </summary>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Graph nodes and edges with review status</returns>
    Task<object> GetReviewGraphAsync(CancellationToken ct = default);

    /// <summary>
    /// Manually triggers a review round.
    /// </summary>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Trigger result</returns>
    Task<object> TriggerReviewAsync(CancellationToken ct = default);
}
