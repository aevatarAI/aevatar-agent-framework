using Microsoft.Extensions.Logging;
using ScientificResearchAssistant.Vibe.Pivot.Messages;

namespace ScientificResearchAssistant.Vibe.Pivot;

/// <summary>
/// Base class for agents that support pivot event handling.
/// Provides template implementation for graceful cancellation and acknowledgment.
/// </summary>
public abstract class PivotAwareAgentBase : IPivotAwareAgent
{
    private readonly object _stateLock = new();
    private PivotAgentState _currentState = PivotAgentState.Idle;
    private CancellationTokenSource? _currentOperationCts;

    protected ILogger Logger { get; }

    /// <summary>
    /// Unique identifier for this agent instance.
    /// </summary>
    public abstract string AgentId { get; }

    /// <summary>
    /// Current agent state for pivot coordination.
    /// </summary>
    public PivotAgentState CurrentState
    {
        get
        {
            lock (_stateLock)
            {
                return _currentState;
            }
        }
        protected set
        {
            lock (_stateLock)
            {
                _currentState = value;
            }
        }
    }

    protected PivotAwareAgentBase(ILogger logger)
    {
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public virtual async Task<PivotAcknowledgment> HandlePivotAsync(
        PivotEvent pivotEvent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pivotEvent);

        Logger.LogInformation(
            "Agent {AgentId} handling pivot {PivotId}: {OldDirection} -> {NewDirection}",
            AgentId, pivotEvent.PivotId, pivotEvent.OldDirectionSummary, pivotEvent.NewDirection);

        var status = "ready";
        string? message = null;

        try
        {
            // Request cancellation of current work
            var cancelSuccess = await RequestCancellationAsync($"Pivot {pivotEvent.PivotId}");

            if (cancelSuccess)
            {
                // Perform any pivot-specific cleanup
                await OnPivotAsync(pivotEvent, cancellationToken);
                status = "ready";
                message = "Successfully pivoted to new direction";
            }
            else
            {
                status = "cancelling";
                message = "Cancellation in progress";
            }

            CurrentState = PivotAgentState.Ready;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Agent {AgentId} failed to handle pivot {PivotId}", AgentId, pivotEvent.PivotId);
            status = "error";
            message = ex.Message;
        }

        return new PivotAcknowledgment
        {
            SessionId = pivotEvent.SessionId,
            PivotId = pivotEvent.PivotId,
            AgentId = AgentId,
            Acknowledged = status != "error",
            Status = status,
            Message = message
        };
    }

    /// <inheritdoc />
    public virtual Task<bool> RequestCancellationAsync(string reason)
    {
        lock (_stateLock)
        {
            if (_currentState == PivotAgentState.Idle || _currentState == PivotAgentState.Ready)
            {
                Logger.LogDebug("Agent {AgentId} is idle, cancellation not needed", AgentId);
                return Task.FromResult(true);
            }

            _currentState = PivotAgentState.Cancelling;
        }

        Logger.LogInformation("Agent {AgentId} requesting cancellation: {Reason}", AgentId, reason);

        // Cancel the current operation token
        _currentOperationCts?.Cancel();

        return Task.FromResult(true);
    }

    /// <summary>
    /// Called when a pivot event is received. Override to perform agent-specific cleanup.
    /// </summary>
    /// <param name="pivotEvent">The pivot event.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    protected virtual Task OnPivotAsync(PivotEvent pivotEvent, CancellationToken cancellationToken)
    {
        // Default: no additional cleanup needed
        return Task.CompletedTask;
    }

    /// <summary>
    /// Creates a linked cancellation token for the current operation.
    /// Call this at the start of any long-running operation to enable pivot cancellation.
    /// </summary>
    /// <param name="parentToken">Parent cancellation token.</param>
    /// <returns>Linked cancellation token that will be cancelled on pivot.</returns>
    protected CancellationToken BeginOperation(CancellationToken parentToken)
    {
        lock (_stateLock)
        {
            _currentOperationCts?.Dispose();
            _currentOperationCts = CancellationTokenSource.CreateLinkedTokenSource(parentToken);
            _currentState = PivotAgentState.Working;
        }

        return _currentOperationCts.Token;
    }

    /// <summary>
    /// Marks the current operation as complete.
    /// Call this at the end of any operation started with BeginOperation.
    /// </summary>
    protected void EndOperation()
    {
        lock (_stateLock)
        {
            _currentOperationCts?.Dispose();
            _currentOperationCts = null;

            if (_currentState == PivotAgentState.Working)
            {
                _currentState = PivotAgentState.Idle;
            }
        }
    }

    /// <summary>
    /// Checks if the current operation should be cancelled due to a pivot.
    /// </summary>
    /// <returns>True if a pivot cancellation has been requested.</returns>
    protected bool IsPivotCancellationRequested()
    {
        lock (_stateLock)
        {
            return _currentState == PivotAgentState.Cancelling ||
                   (_currentOperationCts?.IsCancellationRequested ?? false);
        }
    }
}
