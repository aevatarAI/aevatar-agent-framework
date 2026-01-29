using VibeResearching.Vibe.ReviewAgent;

namespace Aevatar.VibeResearching.Api.ReviewAgent.Storage;

/// <summary>
/// Storage interface for Review Agent state, configuration, and iteration history.
/// Implementations persist to workspace/review-agent/ directory as JSON files.
/// </summary>
public interface IReviewAgentStorage
{
    /// <summary>
    /// Save the current agent state for restart recovery.
    /// </summary>
    /// <param name="state">State to persist.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task SaveStateAsync(ReviewAgentState state, CancellationToken cancellationToken = default);

    /// <summary>
    /// Load the agent state from storage.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Persisted state or null if not found.</returns>
    Task<ReviewAgentState?> LoadStateAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Save a completed review iteration.
    /// </summary>
    /// <param name="iteration">Iteration to persist.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task SaveIterationAsync(ReviewIteration iteration, CancellationToken cancellationToken = default);

    /// <summary>
    /// Load iterations with pagination.
    /// </summary>
    /// <param name="limit">Maximum iterations to return.</param>
    /// <param name="offset">Number to skip.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Paginated list of iterations.</returns>
    Task<IterationListResponse> LoadIterationsAsync(int limit = 50, int offset = 0, CancellationToken cancellationToken = default);

    /// <summary>
    /// Load a specific iteration by ID.
    /// </summary>
    /// <param name="iterationId">Iteration ID to retrieve.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Iteration or null if not found.</returns>
    Task<ReviewIteration?> LoadIterationAsync(string iterationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Save runtime configuration overrides.
    /// </summary>
    /// <param name="options">Configuration to persist.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task SaveConfigAsync(ReviewAgentOptions options, CancellationToken cancellationToken = default);

    /// <summary>
    /// Load runtime configuration overrides.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Persisted config or null if not found.</returns>
    Task<ReviewAgentOptions?> LoadConfigAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Generate a new iteration ID for the current date.
    /// Format: {date}_{seq}, e.g., 2026-01-20_001
    /// </summary>
    /// <returns>New iteration ID.</returns>
    Task<string> GenerateIterationIdAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Get the total count of stored iterations.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Total iteration count.</returns>
    Task<int> GetIterationCountAsync(CancellationToken cancellationToken = default);
}
