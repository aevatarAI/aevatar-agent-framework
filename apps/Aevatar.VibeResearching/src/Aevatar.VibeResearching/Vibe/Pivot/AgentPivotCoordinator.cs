using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VibeResearching.Vibe.Pivot.Messages;
using VibeResearching.Vibe.Pivot.Models;

namespace VibeResearching.Vibe.Pivot;

/// <summary>
/// Coordinates pivot event handling across all subagents in a research session.
/// Handles parallel notification, acknowledgment tracking, and graceful cancellation.
/// </summary>
public sealed class AgentPivotCoordinator : IAgentPivotCoordinator
{
    private readonly IPivotEventPublisher _eventPublisher;
    private readonly IPivotOrchestrator _pivotOrchestrator;
    private readonly PivotOptions _options;
    private readonly ILogger<AgentPivotCoordinator> _logger;

    /// <summary>
    /// Standard agent IDs for the vibe research workflow.
    /// </summary>
    public static readonly IReadOnlyList<string> StandardAgents = new[]
    {
        "planner",
        "librarian",
        "reasoner",
        "verifier",
        "dag_builder",
        "paper_editor"
    };

    public AgentPivotCoordinator(
        IPivotEventPublisher eventPublisher,
        IPivotOrchestrator pivotOrchestrator,
        IOptions<PivotOptions> options,
        ILogger<AgentPivotCoordinator> logger)
    {
        _eventPublisher = eventPublisher ?? throw new ArgumentNullException(nameof(eventPublisher));
        _pivotOrchestrator = pivotOrchestrator ?? throw new ArgumentNullException(nameof(pivotOrchestrator));
        _options = options?.Value ?? new PivotOptions();
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<PivotCoordinationResult> CoordinatePivotAsync(
        DirectionChangeIntent intent,
        string? oldDirection = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(intent);

        var result = new PivotCoordinationResult
        {
            SessionId = intent.SessionId,
            StartedAt = DateTimeOffset.UtcNow
        };

        _logger.LogInformation(
            "Starting pivot coordination for session {SessionId}: {OldDirection} -> {NewDirection}",
            intent.SessionId, oldDirection ?? "(none)", intent.NewTopic ?? "(new)");

        try
        {
            // Step 1: Execute DAG operations first
            var dagOperation = await _pivotOrchestrator.ExecutePivotAsync(intent, oldDirection, cancellationToken);
            result.DagOperation = dagOperation;

            // Step 2: Create and publish pivot event to all agents
            var pivotEvent = new PivotEvent
            {
                SessionId = intent.SessionId,
                PivotId = dagOperation.PivotId,
                OldDirectionSummary = oldDirection ?? string.Empty,
                NewDirection = intent.NewTopic ?? string.Empty,
                CancelledNodeIds = dagOperation.CancelledNodeIds,
                PreservedNodeIds = dagOperation.PreservedNodeIds,
                PreserveAspects = intent.PreserveAspects
            };

            await _eventPublisher.PublishPivotEventAsync(pivotEvent, cancellationToken);

            // Step 3: Wait for acknowledgments with timeout
            var expectedAgents = _eventPublisher.GetRegisteredAgents(intent.SessionId);
            if (expectedAgents.Count == 0)
            {
                // No registered agents - use standard agent list as fallback
                expectedAgents = StandardAgents;
            }

            var timeout = TimeSpan.FromSeconds(_options.SubagentAckTimeoutSeconds);
            var acks = await _eventPublisher.WaitForAcknowledgmentsAsync(
                dagOperation.PivotId,
                expectedAgents,
                timeout,
                cancellationToken);

            result.Acknowledgments = acks;
            result.AllAgentsAcknowledged = acks.Count >= expectedAgents.Count;
            result.CompletedAt = DateTimeOffset.UtcNow;

            _logger.LogInformation(
                "Pivot coordination completed for session {SessionId}: " +
                "DAG={DagStatus}, Acks={AckCount}/{ExpectedCount}, AllAcked={AllAcked}",
                intent.SessionId,
                dagOperation.Status,
                acks.Count,
                expectedAgents.Count,
                result.AllAgentsAcknowledged);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Pivot coordination failed for session {SessionId}", intent.SessionId);
            result.Error = ex.Message;
            result.CompletedAt = DateTimeOffset.UtcNow;
            throw;
        }
    }

    /// <inheritdoc />
    public void RegisterSessionAgents(string sessionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        foreach (var agentId in StandardAgents)
        {
            _eventPublisher.RegisterAgent(sessionId, agentId);
        }

        _logger.LogDebug(
            "Registered {AgentCount} standard agents for session {SessionId}",
            StandardAgents.Count, sessionId);
    }

    /// <inheritdoc />
    public void UnregisterSessionAgents(string sessionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        foreach (var agentId in StandardAgents)
        {
            _eventPublisher.UnregisterAgent(sessionId, agentId);
        }

        _logger.LogDebug("Unregistered agents for session {SessionId}", sessionId);
    }

    /// <inheritdoc />
    public async Task<bool> NotifyAgentAsync(
        string sessionId,
        string agentId,
        PivotEvent pivotEvent,
        IPivotAwareAgent? agent,
        CancellationToken cancellationToken = default)
    {
        if (agent == null)
        {
            _logger.LogDebug(
                "Agent {AgentId} is not pivot-aware, skipping notification",
                agentId);

            // Record a synthetic acknowledgment for non-pivot-aware agents
            _eventPublisher.RecordAcknowledgment(new PivotAcknowledgment
            {
                SessionId = sessionId,
                PivotId = pivotEvent.PivotId,
                AgentId = agentId,
                Acknowledged = true,
                Status = "not_pivot_aware"
            });

            return true;
        }

        try
        {
            var ack = await agent.HandlePivotAsync(pivotEvent, cancellationToken);
            _eventPublisher.RecordAcknowledgment(ack);
            return ack.Acknowledged;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Agent {AgentId} failed to handle pivot", agentId);

            _eventPublisher.RecordAcknowledgment(new PivotAcknowledgment
            {
                SessionId = sessionId,
                PivotId = pivotEvent.PivotId,
                AgentId = agentId,
                Acknowledged = false,
                Status = "error",
                Message = ex.Message
            });

            return false;
        }
    }
}

/// <summary>
/// Interface for coordinating pivot events across subagents.
/// </summary>
public interface IAgentPivotCoordinator
{
    /// <summary>
    /// Coordinates a full pivot operation including DAG updates and agent notification.
    /// </summary>
    Task<PivotCoordinationResult> CoordinatePivotAsync(
        DirectionChangeIntent intent,
        string? oldDirection = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Registers standard agents for a session.
    /// </summary>
    void RegisterSessionAgents(string sessionId);

    /// <summary>
    /// Unregisters all agents for a session.
    /// </summary>
    void UnregisterSessionAgents(string sessionId);

    /// <summary>
    /// Notifies a specific agent of a pivot event.
    /// </summary>
    Task<bool> NotifyAgentAsync(
        string sessionId,
        string agentId,
        PivotEvent pivotEvent,
        IPivotAwareAgent? agent,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of pivot coordination across all subagents.
/// </summary>
public sealed class PivotCoordinationResult
{
    public required string SessionId { get; init; }
    public DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset CompletedAt { get; set; }
    public PivotOperation? DagOperation { get; set; }
    public IReadOnlyList<PivotAcknowledgment> Acknowledgments { get; set; } = [];
    public bool AllAgentsAcknowledged { get; set; }
    public string? Error { get; set; }

    public TimeSpan Duration => CompletedAt - StartedAt;
}
