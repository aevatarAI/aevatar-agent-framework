using ScientificResearchAssistant.Vibe.Pivot.Messages;

namespace ScientificResearchAssistant.Vibe.Pivot;

/// <summary>
/// Interface for publishing pivot events and tracking acknowledgments from subagents.
/// </summary>
public interface IPivotEventPublisher
{
    /// <summary>
    /// Publishes a pivot event to all subscribed subagents.
    /// </summary>
    /// <param name="pivotEvent">The pivot event to publish.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Task representing the publish operation.</returns>
    Task PublishPivotEventAsync(PivotEvent pivotEvent, CancellationToken cancellationToken = default);

    /// <summary>
    /// Waits for acknowledgments from all registered subagents with timeout.
    /// </summary>
    /// <param name="pivotId">The pivot operation ID to wait for.</param>
    /// <param name="expectedAgents">List of agent IDs expected to acknowledge.</param>
    /// <param name="timeout">Maximum time to wait for acknowledgments.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// Collection of received acknowledgments (may be partial if timeout occurred).
    /// </returns>
    Task<IReadOnlyList<PivotAcknowledgment>> WaitForAcknowledgmentsAsync(
        string pivotId,
        IEnumerable<string> expectedAgents,
        TimeSpan timeout,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Records an acknowledgment from a subagent.
    /// </summary>
    /// <param name="acknowledgment">The acknowledgment to record.</param>
    void RecordAcknowledgment(PivotAcknowledgment acknowledgment);

    /// <summary>
    /// Subscribes to pivot events for a specific session.
    /// </summary>
    /// <param name="sessionId">Session identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Async enumerable of pivot events.</returns>
    IAsyncEnumerable<PivotEvent> SubscribeAsync(
        string sessionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the list of registered subagent IDs for a session.
    /// </summary>
    /// <param name="sessionId">Session identifier.</param>
    /// <returns>List of registered agent IDs.</returns>
    IReadOnlyList<string> GetRegisteredAgents(string sessionId);

    /// <summary>
    /// Registers a subagent as pivot-aware for a session.
    /// </summary>
    /// <param name="sessionId">Session identifier.</param>
    /// <param name="agentId">Agent identifier.</param>
    void RegisterAgent(string sessionId, string agentId);

    /// <summary>
    /// Unregisters a subagent from pivot notifications.
    /// </summary>
    /// <param name="sessionId">Session identifier.</param>
    /// <param name="agentId">Agent identifier.</param>
    void UnregisterAgent(string sessionId, string agentId);
}
