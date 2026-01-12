using ScientificResearchAssistant.Vibe.Pivot.Messages;

namespace ScientificResearchAssistant.Vibe.Pivot;

/// <summary>
/// Interface for agents that can handle research direction pivot events.
/// </summary>
public interface IPivotAwareAgent
{
    /// <summary>
    /// Unique identifier for this agent instance.
    /// </summary>
    string AgentId { get; }

    /// <summary>
    /// Handles a pivot event by cancelling current work and acknowledging.
    /// </summary>
    /// <param name="pivotEvent">The pivot event to handle.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Acknowledgment indicating agent's response to the pivot.</returns>
    Task<PivotAcknowledgment> HandlePivotAsync(
        PivotEvent pivotEvent,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Requests the agent to cancel its current operation gracefully.
    /// </summary>
    /// <param name="reason">Reason for cancellation.</param>
    /// <returns>True if cancellation was successful or already idle.</returns>
    Task<bool> RequestCancellationAsync(string reason);

    /// <summary>
    /// Gets the current state of the agent.
    /// </summary>
    PivotAgentState CurrentState { get; }
}

/// <summary>
/// Agent states for pivot coordination.
/// </summary>
public enum PivotAgentState
{
    /// <summary>Agent is idle and ready for work.</summary>
    Idle,

    /// <summary>Agent is actively processing a task.</summary>
    Working,

    /// <summary>Agent is cancelling its current operation.</summary>
    Cancelling,

    /// <summary>Agent has completed pivot handling and is ready.</summary>
    Ready
}
