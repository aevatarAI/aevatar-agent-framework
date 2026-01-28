namespace Aevatar.VibeResearching.Agents.ReviewAgent;

/// <summary>
/// Storage interface for Review Agent persistence.
/// Manages state, config, and iteration history.
/// </summary>
public interface IReviewAgentStorage
{
    /// <summary>
    /// Save current agent state.
    /// </summary>
    Task SaveStateAsync(ReviewAgentState state, CancellationToken cancellationToken = default);

    /// <summary>
    /// Load current agent state.
    /// </summary>
    Task<ReviewAgentState?> LoadStateAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Save a completed iteration.
    /// </summary>
    Task SaveIterationAsync(ReviewIteration iteration, CancellationToken cancellationToken = default);

    /// <summary>
    /// Load paginated list of iteration summaries.
    /// </summary>
    Task<IterationListResponse> LoadIterationsAsync(int limit = 50, int offset = 0, CancellationToken cancellationToken = default);

    /// <summary>
    /// Load full details of a specific iteration.
    /// </summary>
    Task<ReviewIteration?> LoadIterationAsync(string iterationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Save agent configuration options.
    /// </summary>
    Task SaveConfigAsync(ReviewAgentOptions options, CancellationToken cancellationToken = default);

    /// <summary>
    /// Load agent configuration options.
    /// </summary>
    Task<ReviewAgentOptions?> LoadConfigAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Generate a unique iteration ID.
    /// </summary>
    Task<string> GenerateIterationIdAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Get total count of stored iterations.
    /// </summary>
    Task<int> GetIterationCountAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Get iterations list (convenience method for app service).
    /// </summary>
    Task<object> GetIterationsAsync(int limit, int offset, CancellationToken ct);

    /// <summary>
    /// Get a specific iteration (convenience method for app service).
    /// </summary>
    Task<object?> GetIterationAsync(string iterationId, CancellationToken ct);

    /// <summary>
    /// Get current iteration entries (convenience method for app service).
    /// </summary>
    Task<object> GetCurrentIterationEntriesAsync(CancellationToken ct);
}
