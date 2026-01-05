using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.EventRouting;
using Aevatar.Agents.Abstractions.Helpers;
using Aevatar.Agents.Core.Observability;
using Aevatar.Agents.Core.Telemetry;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aevatar.Agents.Core.EventRouting;

/// <summary>
/// Event Router
/// Provides standard implementation of event propagation logic
/// </summary>
public class EventRouter(
    string agentId,
    Func<string, EventEnvelope, CancellationToken, Task> sendToActorAsync,
    Func<EventEnvelope, CancellationToken, Task> sendToSelfAsync,
    ILogger? logger = null,
    IEventRouterStore? store = null,
    EventRouterOptions? options = null)
{
    private readonly ILogger _logger = logger ?? NullLogger.Instance;
    private readonly IEventRouterStore? _store = store;
    private readonly EventRouterOptions _options = options ?? EventRouterOptions.Default;

    // Hierarchy relationships
    private string? _parentId;
    private readonly HashSet<string> _childrenIds = new();

    // Event sending delegates
    private readonly Func<string, EventEnvelope, CancellationToken, Task> _sendToActorAsync =
        sendToActorAsync ?? throw new ArgumentNullException(nameof(sendToActorAsync));

    private readonly Func<EventEnvelope, CancellationToken, Task> _sendToSelfAsync =
        sendToSelfAsync ?? throw new ArgumentNullException(nameof(sendToSelfAsync));

    // ============ Hierarchy Management ============

    public async Task AddChildAsync(string childId, CancellationToken ct = default)
    {
        using var activity = AgentTelemetry.StartHierarchyOperation("add_child", agentId, childId);
        using var logScope = LoggingScope.CreateHierarchyScope(_logger, agentId, "AddChild", childId);

        _childrenIds.Add(childId);
        AgentLogMessages.AddingChild(_logger, agentId, childId);

        await SaveHierarchyAsync(ct);
        AgentTelemetry.RecordHierarchyOperation(activity, "add_child");
    }

    public async Task RemoveChildAsync(string childId, CancellationToken ct = default)
    {
        using var activity = AgentTelemetry.StartHierarchyOperation("remove_child", agentId, childId);
        using var logScope = LoggingScope.CreateHierarchyScope(_logger, agentId, "RemoveChild", childId);

        _childrenIds.Remove(childId);
        _logger.LogDebug("Agent {AgentId} removed child {ChildId}", agentId, childId);

        await SaveHierarchyAsync(ct);
        AgentTelemetry.RecordHierarchyOperation(activity, "remove_child");
    }

    public async Task SetParentAsync(string parentId, CancellationToken ct = default)
    {
        using var activity = AgentTelemetry.StartHierarchyOperation("set_parent", agentId, parentId);
        using var logScope = LoggingScope.CreateHierarchyScope(_logger, agentId, "SetParent", parentId);

        _parentId = parentId;
        AgentLogMessages.SettingParent(_logger, agentId, parentId);

        await SaveHierarchyAsync(ct);
        AgentTelemetry.RecordHierarchyOperation(activity, "set_parent");
    }

    public async Task ClearParentAsync(CancellationToken ct = default)
    {
        using var activity = AgentTelemetry.StartHierarchyOperation("clear_parent", agentId);
        using var logScope = LoggingScope.CreateHierarchyScope(_logger, agentId, "ClearParent");

        _parentId = null;
        _logger.LogDebug("Agent {AgentId} cleared parent", agentId);

        await SaveHierarchyAsync(ct);
        AgentTelemetry.RecordHierarchyOperation(activity, "clear_parent");
    }

    public string? GetParent() => _parentId;

    public IReadOnlyList<string> GetChildren() => _childrenIds.ToList();

    // ============ Persistence Support ============

    /// <summary>
    /// Load hierarchy from store
    /// </summary>
    public async Task LoadHierarchyAsync(CancellationToken ct = default)
    {
        if (_store == null)
        {
            _logger.LogDebug("Agent {AgentId} has no store configured, skipping hierarchy load", agentId);
            return;
        }

        var hierarchy = await _store.LoadAsync(agentId, ct);
        if (hierarchy == null)
        {
            _logger.LogDebug("No persisted hierarchy found for agent {AgentId}", agentId);
            return;
        }

        _parentId = hierarchy.ParentId;
        _childrenIds.Clear();
        foreach (var childId in hierarchy.ChildrenIds)
        {
            _childrenIds.Add(childId);
        }

        _logger.LogInformation(
            "Loaded hierarchy for agent {AgentId}: Parent={ParentId}, Children={ChildrenCount}",
            agentId, _parentId, _childrenIds.Count);
    }

    /// <summary>
    /// Save current hierarchy to store
    /// </summary>
    private async Task SaveHierarchyAsync(CancellationToken ct = default)
    {
        if (_store == null)
        {
            return;
        }

        var hierarchy = new EventRouterHierarchy
        {
            ParentId = _parentId,
            ChildrenIds = new HashSet<string>(_childrenIds)
        };

        await _store.SaveAsync(agentId, hierarchy, ct);
        _logger.LogDebug("Saved hierarchy for agent {AgentId}", agentId);
    }


    // ============ Event Creation ============

    public EventEnvelope CreateEventEnvelope<TEvent>(
        TEvent evt,
        EventDirection direction) where TEvent : IMessage
    {
        var eventId = Guid.NewGuid().ToString();

        var envelope = new EventEnvelope
        {
            Id = eventId,
            Timestamp = TimestampHelper.GetUtcNow(),
            Version = 1,
            Payload = Any.Pack(evt),
            CorrelationId = Guid.NewGuid().ToString(), // TODO: Get from context
            PublisherId = agentId,
            Direction = direction,
            ShouldStopPropagation = false,
            MaxHopCount = _options.DefaultMaxHopCount,
            CurrentHopCount = 0,
            MinHopCount = _options.DefaultMinHopCount,
            Message = $"Published by {agentId}",
        };

        envelope.Publishers.Add(agentId);

        return envelope;
    }

    // ============ Event Routing (Core Logic) ============

    public async Task RouteEventAsync(EventEnvelope envelope, CancellationToken ct = default)
    {
        var direction = envelope.Direction.ToString();
        var childCount = _childrenIds.Count;
        var stopwatch = Stopwatch.StartNew();

        // Start routing Telemetry
        using var activity = AgentTelemetry.StartEventRouting(agentId, envelope.Id, direction, childCount);
        using var logScope = LoggingScope.CreateEventRoutingScope(_logger, agentId, envelope.Id, direction, childCount);

        _logger.LogDebug("Agent {AgentId} routing event {EventId} with direction {Direction}",
            agentId, envelope.Id, envelope.Direction);

        var targetCount = 0;

        // First, the publisher itself should always process the event (unless the event handler explicitly rejects it)
        // This follows the "publish-subscribe" pattern semantics
        await _sendToSelfAsync(envelope, ct);
        targetCount++;

        // Then propagate to other nodes based on direction
        switch (envelope.Direction)
        {
            case EventDirection.Up:
                if (_parentId != null) targetCount++;
                await SendToParentAsync(envelope, ct);
                break;

            case EventDirection.Down:
                targetCount += _childrenIds.Count;
                await SendToChildrenAsync(envelope, ct);
                break;

            case EventDirection.Both:
                // For Both direction, need to send Up and Down events separately
                var upEnvelope = envelope.Clone();
                upEnvelope.Direction = EventDirection.Up;
                if (_parentId != null) targetCount++;
                await SendToParentAsync(upEnvelope, ct);

                var downEnvelope = envelope.Clone();
                downEnvelope.Direction = EventDirection.Down;
                targetCount += _childrenIds.Count;
                await SendToChildrenAsync(downEnvelope, ct);
                break;
        }

        // Record routing Telemetry
        stopwatch.Stop();
        AgentTelemetry.RecordEventRouted(activity, direction, targetCount, stopwatch.ElapsedMilliseconds);
        AgentLogMessages.EventRouting(_logger, agentId, envelope.Id, targetCount);
    }

    /// <summary>
    /// Send event to parent node
    /// </summary>
    private async Task SendToParentAsync(EventEnvelope envelope, CancellationToken ct)
    {
        if (_parentId == null)
        {
            // No parent node, event propagation ends (follows Up direction semantics)
            _logger.LogDebug("Event {EventId} has no parent, propagation ends", envelope.Id);
            return;
        }

        // UP direction: Check if a cycle will be formed
        // If parent node is already in Publishers list, it indicates a cycle
        if (envelope.Publishers.Contains(_parentId))
        {
            _logger.LogWarning("Event {EventId} already visited parent {ParentId}, skipping to avoid loop",
                envelope.Id, _parentId);
            return;
        }

        _logger.LogDebug("Sending event {EventId} to parent {ParentId}",
            envelope.Id, _parentId);

        // Create copy and increment HopCount, add current node to Publishers list (if not already added)
        var parentEnvelope = envelope.Clone();
        parentEnvelope.CurrentHopCount++;

        // Only add current node ID if not already in Publishers list, avoid duplicates
        if (!parentEnvelope.Publishers.Contains(agentId))
        {
            parentEnvelope.Publishers.Add(agentId);
        }

        await _sendToActorAsync(_parentId!, parentEnvelope, ct);
    }

    /// <summary>
    /// Send event to all child nodes
    /// </summary>
    private async Task SendToChildrenAsync(EventEnvelope envelope, CancellationToken ct)
    {
        if (_childrenIds.Count == 0)
        {
            // No children, event propagation ends (follows Down direction semantics)
            _logger.LogDebug("Event {EventId} has no children, propagation ends", envelope.Id);
            return;
        }

        // Check max hop count limit to prevent infinite recursion
        if (envelope.MaxHopCount > 0 && envelope.CurrentHopCount >= envelope.MaxHopCount)
        {
            _logger.LogWarning("Event {EventId} reached max hop count {MaxHop}, stopping DOWN propagation",
                envelope.Id, envelope.MaxHopCount);
            return;
        }

        // Safety check: If current hop count is abnormally high, force stop to prevent stack overflow
        if (envelope.CurrentHopCount >= _options.SafetyMaxHopCount)
        {
            _logger.LogError(
                "Event {EventId} exceeded safety max hop count {SafetyMax}, force stopping to prevent stack overflow",
                envelope.Id, _options.SafetyMaxHopCount);
            return;
        }

        foreach (var childId in _childrenIds)
        {
            // DOWN direction also needs to check for cycles: if child node is already in Publishers list, it indicates a cycle
            if (envelope.Publishers.Contains(childId))
            {
                _logger.LogWarning(
                    "Event {EventId} already visited child {ChildId}, skipping to avoid loop in DOWN direction",
                    envelope.Id, childId);
                continue;
            }

            // Prevent node from sending event to itself (case of incorrect parent-child relationship configuration)
            if (childId == agentId)
            {
                _logger.LogError("Agent {AgentId} attempted to send event to itself as child, skipping to prevent loop",
                    agentId);
                continue;
            }

            _logger.LogDebug("Sending event {EventId} to child {ChildId}",
                envelope.Id, childId);

            // Create copy and increment HopCount, add current node to Publishers list (if not already added)
            var childEnvelope = envelope.Clone();
            childEnvelope.CurrentHopCount++;

            // Only add current node ID if not already in Publishers list, avoid duplicates
            if (!childEnvelope.Publishers.Contains(agentId))
            {
                childEnvelope.Publishers.Add(agentId);
            }

            await _sendToActorAsync(childId, childEnvelope, ct);
        }
    }

    /// <summary>
    /// Check if event should be processed
    /// </summary>
    public bool ShouldProcessEvent(EventEnvelope envelope)
    {
        // Check if propagation should stop
        if (envelope.ShouldStopPropagation)
            return false;

        // Check MaxHopCount
        if (envelope.MaxHopCount > 0 && envelope.CurrentHopCount >= envelope.MaxHopCount)
        {
            _logger.LogDebug("Event {EventId} reached max hop count {MaxHop}",
                envelope.Id, envelope.MaxHopCount);
            return false;
        }

        // Check MinHopCount
        bool shouldProcess = envelope.MinHopCount <= 0 || envelope.CurrentHopCount >= envelope.MinHopCount;

        return shouldProcess;
    }

    /// <summary>
    /// Continue propagation of received event
    /// </summary>
    public async Task ContinuePropagationAsync(EventEnvelope envelope, CancellationToken ct)
    {
        // Continue propagation based on direction (recursive propagation)
        switch (envelope.Direction)
        {
            case EventDirection.Down:
                // Down direction: continue propagating downward to child nodes
                await SendToChildrenAsync(envelope, ct);
                break;

            case EventDirection.Up:
                // Up direction: only continue propagating upward when event was not received from parent node stream
                // If Publishers list contains parent node ID, the event has already been broadcast through parent node stream
                if (!string.IsNullOrEmpty(_parentId) && !envelope.Publishers.Contains(_parentId))
                {
                    await SendToParentAsync(envelope, ct);
                }
                else if (string.IsNullOrEmpty(_parentId))
                {
                    // Also try sending when there's no parent node (might be root node)
                    await SendToParentAsync(envelope, ct);
                }

                break;

            case EventDirection.Both:
                // Bidirectional propagation
                // Up direction also needs to check if already in parent node stream
                if (!string.IsNullOrEmpty(_parentId) && !envelope.Publishers.Contains(_parentId))
                {
                    await SendToParentAsync(envelope, ct);
                }
                else if (string.IsNullOrEmpty(_parentId))
                {
                    await SendToParentAsync(envelope, ct);
                }

                await SendToChildrenAsync(envelope, ct);
                break;
        }
    }
}