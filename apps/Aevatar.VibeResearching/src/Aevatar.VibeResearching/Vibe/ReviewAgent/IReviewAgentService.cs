namespace VibeResearching.Vibe.ReviewAgent;

/// <summary>
/// Service interface for the Review Agent.
/// Provides state management and review operations.
/// </summary>
public interface IReviewAgentService
{
    /// <summary>
    /// Get the current state of the Review Agent.
    /// </summary>
    ReviewAgentState GetState();

    /// <summary>
    /// Get current Review Agent settings.
    /// </summary>
    ReviewAgentOptions GetSettings();

    /// <summary>
    /// Update Review Agent settings.
    /// Changes take effect on the next review round.
    /// </summary>
    /// <param name="options">New settings to apply.</param>
    /// <returns>Updated settings.</returns>
    Task<ReviewAgentOptions> UpdateSettingsAsync(ReviewAgentOptions options);

    /// <summary>
    /// Execute a review round.
    /// Verifies all stale knowledge nodes and deactivates invalid ones.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The completed iteration record.</returns>
    Task<ReviewIteration> RunReviewRoundAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Execute a cleanup round.
    /// Removes deactivated nodes that have exceeded the delete threshold.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Number of nodes removed.</returns>
    Task<int> RunCleanupRoundAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Get paginated list of review iterations.
    /// </summary>
    /// <param name="limit">Maximum iterations to return (default: 50, max: 100).</param>
    /// <param name="offset">Number of iterations to skip.</param>
    /// <returns>Paginated iteration list.</returns>
    Task<IterationListResponse> GetIterationsAsync(int limit = 50, int offset = 0);

    /// <summary>
    /// Get details of a specific iteration.
    /// </summary>
    /// <param name="iterationId">Iteration ID to retrieve.</param>
    /// <returns>Iteration details or null if not found.</returns>
    Task<ReviewIteration?> GetIterationAsync(string iterationId);

    /// <summary>
    /// Set the next scheduled review time.
    /// </summary>
    /// <param name="nextScheduledAt">Time of next review round.</param>
    void SetNextScheduledAt(DateTimeOffset nextScheduledAt);

    /// <summary>
    /// Transition to error state.
    /// </summary>
    /// <param name="errorMessage">Error message to record.</param>
    void SetError(string errorMessage);

    /// <summary>
    /// Get graph data for review progress visualization.
    /// Returns all knowledge nodes with their review status and edges.
    /// </summary>
    /// <returns>Graph data with nodes and edges.</returns>
    ReviewGraphData GetGraphData();
}

/// <summary>
/// Graph data for review progress visualization.
/// </summary>
public sealed class ReviewGraphData
{
    public required IReadOnlyList<ReviewGraphNode> Nodes { get; init; }
    public required IReadOnlyList<ReviewGraphEdge> Edges { get; init; }
}

/// <summary>
/// A node in the review graph.
/// </summary>
public sealed class ReviewGraphNode
{
    public required string NodeId { get; init; }
    public required string Label { get; init; }
    public string? SessionId { get; init; }
    public bool IsActivated { get; init; } = true;
    public DateTimeOffset? LastReviewedAt { get; init; }
    public DateTimeOffset? DeactivatedTimestamp { get; init; }
    public string? DeactivatedReason { get; init; }
    public IReadOnlyList<string> DependsOn { get; init; } = [];
}

/// <summary>
/// An edge in the review graph.
/// </summary>
public sealed class ReviewGraphEdge
{
    public required string FromId { get; init; }
    public required string ToId { get; init; }
    public required string Type { get; init; }
}
