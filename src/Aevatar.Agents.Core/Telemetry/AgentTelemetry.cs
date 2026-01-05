using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Aevatar.Agents.Core.Telemetry;

// ============================================================
//  Agent Telemetry - Comprehensive Agent Monitoring
//  OpenTelemetry + Aspire Dashboard Integration
// ============================================================

/// <summary>
/// Complete telemetry for agent lifecycle and operations.
/// Provides dual-track monitoring: Traces (ActivitySource) + Metrics (Meter).
/// </summary>
public static class AgentTelemetry
{
    // ============================================================
    //  Source Configuration
    // ============================================================

    public const string SourceName = "Aevatar.Agents";
    public const string MeterName = "Aevatar.Agents";
    public const string Version = "1.0.0";

    private static readonly ActivitySource ActivitySource = new(SourceName, Version);
    private static readonly Meter Meter = new(MeterName, Version);

    // ============================================================
    //  Counters - Cumulative Counts
    // ============================================================

    private static readonly Counter<long> AgentActivations = Meter.CreateCounter<long>(
        "aevatar.agent.activations.total",
        "activations",
        "Total agent activations");

    private static readonly Counter<long> AgentDeactivations = Meter.CreateCounter<long>(
        "aevatar.agent.deactivations.total",
        "deactivations",
        "Total agent deactivations");

    private static readonly Counter<long> EventsReceived = Meter.CreateCounter<long>(
        "aevatar.events.received.total",
        "events",
        "Total events received by agents");

    private static readonly Counter<long> EventsHandled = Meter.CreateCounter<long>(
        "aevatar.events.handled.total",
        "events",
        "Total events successfully handled");

    private static readonly Counter<long> EventsPublished = Meter.CreateCounter<long>(
        "aevatar.events.published.total",
        "events",
        "Total events published by agents");

    private static readonly Counter<long> EventsRouted = Meter.CreateCounter<long>(
        "aevatar.events.routed.total",
        "events",
        "Total events routed through hierarchy");

    private static readonly Counter<long> EventsDropped = Meter.CreateCounter<long>(
        "aevatar.events.dropped.total",
        "events",
        "Total events dropped (no handler)");

    private static readonly Counter<long> HandlersInvoked = Meter.CreateCounter<long>(
        "aevatar.handlers.invoked.total",
        "invocations",
        "Total event handler invocations");

    private static readonly Counter<long> HandlerErrors = Meter.CreateCounter<long>(
        "aevatar.handlers.errors.total",
        "errors",
        "Total handler execution errors");

    private static readonly Counter<long> HierarchyOperations = Meter.CreateCounter<long>(
        "aevatar.hierarchy.operations.total",
        "operations",
        "Total parent-child hierarchy operations");

    private static readonly Counter<long> StateChanges = Meter.CreateCounter<long>(
        "aevatar.state.changes.total",
        "changes",
        "Total state change operations");

    // ============================================================
    //  Histograms - Latency Distribution
    // ============================================================

    private static readonly Histogram<double> ActivationDuration = Meter.CreateHistogram<double>(
        "aevatar.agent.activation.duration",
        "ms",
        "Agent activation duration");

    private static readonly Histogram<double> EventHandlingDuration = Meter.CreateHistogram<double>(
        "aevatar.event.handling.duration",
        "ms",
        "Event handling duration (full pipeline)");

    private static readonly Histogram<double> HandlerExecutionDuration = Meter.CreateHistogram<double>(
        "aevatar.handler.execution.duration",
        "ms",
        "Single handler execution duration");

    private static readonly Histogram<double> EventPublishDuration = Meter.CreateHistogram<double>(
        "aevatar.event.publish.duration",
        "ms",
        "Event publish duration");

    private static readonly Histogram<double> EventRoutingDuration = Meter.CreateHistogram<double>(
        "aevatar.event.routing.duration",
        "ms",
        "Event routing through hierarchy duration");

    private static readonly Histogram<double> StateLoadDuration = Meter.CreateHistogram<double>(
        "aevatar.state.load.duration",
        "ms",
        "State loading duration");

    private static readonly Histogram<double> StateSaveDuration = Meter.CreateHistogram<double>(
        "aevatar.state.save.duration",
        "ms",
        "State saving duration");

    // ============================================================
    //  Gauges - Current State
    // ============================================================

    private static long _activeAgentCount;
    #pragma warning disable CS0649 // Reserved for future use
    private static long _pendingEventCount;
    #pragma warning restore CS0649
    private static long _subscriptionCount;

    static AgentTelemetry()
    {
        Meter.CreateObservableGauge(
            "aevatar.agents.active",
            () => Interlocked.Read(ref _activeAgentCount),
            "agents",
            "Currently active agents");

        Meter.CreateObservableGauge(
            "aevatar.events.pending",
            () => Interlocked.Read(ref _pendingEventCount),
            "events",
            "Events pending processing");

        Meter.CreateObservableGauge(
            "aevatar.subscriptions.active",
            () => Interlocked.Read(ref _subscriptionCount),
            "subscriptions",
            "Active stream subscriptions");
    }

    // ============================================================
    //  Agent Lifecycle Traces + Metrics
    // ============================================================

    /// <summary>
    /// Start agent activation tracing.
    /// </summary>
    public static Activity? StartAgentActivation(Guid agentId, string agentType)
    {
        var activity = ActivitySource.StartActivity("agent.activate", ActivityKind.Internal);
        activity?.SetTag("agent.id", agentId.ToString());
        activity?.SetTag("agent.type", agentType);
        activity?.SetTag("agent.category", "lifecycle");
        return activity;
    }

    /// <summary>
    /// Record agent activation completion.
    /// </summary>
    public static void RecordAgentActivation(
        Activity? activity,
        Guid agentId,
        string agentType,
        double durationMs,
        bool success = true,
        string? error = null)
    {
        // Metrics
        AgentActivations.Add(1,
            new KeyValuePair<string, object?>("agent.type", agentType),
            new KeyValuePair<string, object?>("success", success));

        ActivationDuration.Record(durationMs,
            new KeyValuePair<string, object?>("agent.type", agentType));

        if (success)
        {
            Interlocked.Increment(ref _activeAgentCount);
        }

        // Trace status
        if (activity != null)
        {
            activity.SetTag("duration.ms", durationMs);
            if (success)
            {
                activity.SetStatus(ActivityStatusCode.Ok);
            }
            else
            {
                activity.SetStatus(ActivityStatusCode.Error, error ?? "Activation failed");
                activity.SetTag("error.message", error);
            }
        }
    }

    /// <summary>
    /// Start agent deactivation tracing.
    /// </summary>
    public static Activity? StartAgentDeactivation(Guid agentId, string agentType)
    {
        var activity = ActivitySource.StartActivity("agent.deactivate", ActivityKind.Internal);
        activity?.SetTag("agent.id", agentId.ToString());
        activity?.SetTag("agent.type", agentType);
        activity?.SetTag("agent.category", "lifecycle");
        return activity;
    }

    /// <summary>
    /// Record agent deactivation completion.
    /// </summary>
    public static void RecordAgentDeactivation(string agentType)
    {
        AgentDeactivations.Add(1,
            new KeyValuePair<string, object?>("agent.type", agentType));
        Interlocked.Decrement(ref _activeAgentCount);
    }

    // ============================================================
    //  Event Handling Traces + Metrics
    // ============================================================

    /// <summary>
    /// Start event handling tracing (full pipeline).
    /// </summary>
    public static Activity? StartEventHandling(
        Guid agentId,
        string agentType,
        string eventId,
        string eventType,
        string? correlationId = null)
    {
        var activity = ActivitySource.StartActivity("agent.event.handle", ActivityKind.Consumer);
        activity?.SetTag("agent.id", agentId.ToString());
        activity?.SetTag("agent.type", agentType);
        activity?.SetTag("event.id", eventId);
        activity?.SetTag("event.type", eventType);

        if (!string.IsNullOrEmpty(correlationId))
        {
            activity?.SetTag("correlation.id", correlationId);
        }

        // Metrics
        EventsReceived.Add(1,
            new KeyValuePair<string, object?>("agent.type", agentType),
            new KeyValuePair<string, object?>("event.type", eventType));

        return activity;
    }

    /// <summary>
    /// Record event handling completion.
    /// </summary>
    public static void RecordEventHandled(
        Activity? activity,
        string agentType,
        string eventType,
        double durationMs,
        int handlerCount,
        bool success = true,
        string? error = null)
    {
        // Metrics
        if (success)
        {
            EventsHandled.Add(1,
                new KeyValuePair<string, object?>("agent.type", agentType),
                new KeyValuePair<string, object?>("event.type", eventType));
        }

        EventHandlingDuration.Record(durationMs,
            new KeyValuePair<string, object?>("agent.type", agentType),
            new KeyValuePair<string, object?>("event.type", eventType));

        // Trace
        if (activity != null)
        {
            activity.SetTag("duration.ms", durationMs);
            activity.SetTag("handlers.count", handlerCount);

            if (success)
            {
                activity.SetStatus(ActivityStatusCode.Ok);
            }
            else
            {
                activity.SetStatus(ActivityStatusCode.Error, error ?? "Handler failed");
                activity.SetTag("error.message", error);
            }
        }
    }

    /// <summary>
    /// Record event dropped (no handler).
    /// </summary>
    public static void RecordEventDropped(string agentType, string eventType)
    {
        EventsDropped.Add(1,
            new KeyValuePair<string, object?>("agent.type", agentType),
            new KeyValuePair<string, object?>("event.type", eventType));
    }

    // ============================================================
    //  Handler Execution Traces + Metrics
    // ============================================================

    /// <summary>
    /// Start single handler execution tracing.
    /// </summary>
    public static Activity? StartHandlerExecution(
        Guid agentId,
        string agentType,
        string handlerName,
        string eventType)
    {
        var activity = ActivitySource.StartActivity("agent.handler.execute", ActivityKind.Internal);
        activity?.SetTag("agent.id", agentId.ToString());
        activity?.SetTag("agent.type", agentType);
        activity?.SetTag("handler.name", handlerName);
        activity?.SetTag("event.type", eventType);
        return activity;
    }

    /// <summary>
    /// Record handler execution completion.
    /// </summary>
    public static void RecordHandlerExecution(
        Activity? activity,
        string agentType,
        string handlerName,
        string eventType,
        double durationMs,
        bool success = true,
        string? error = null)
    {
        // Metrics
        HandlersInvoked.Add(1,
            new KeyValuePair<string, object?>("agent.type", agentType),
            new KeyValuePair<string, object?>("handler.name", handlerName),
            new KeyValuePair<string, object?>("event.type", eventType));

        HandlerExecutionDuration.Record(durationMs,
            new KeyValuePair<string, object?>("agent.type", agentType),
            new KeyValuePair<string, object?>("handler.name", handlerName));

        if (!success)
        {
            HandlerErrors.Add(1,
                new KeyValuePair<string, object?>("agent.type", agentType),
                new KeyValuePair<string, object?>("handler.name", handlerName),
                new KeyValuePair<string, object?>("error.type", error ?? "unknown"));
        }

        // Trace
        if (activity != null)
        {
            activity.SetTag("duration.ms", durationMs);
            if (success)
            {
                activity.SetStatus(ActivityStatusCode.Ok);
            }
            else
            {
                activity.SetStatus(ActivityStatusCode.Error, error);
                activity.SetTag("error.message", error);
            }
        }
    }

    // ============================================================
    //  Event Publishing Traces + Metrics
    // ============================================================

    /// <summary>
    /// Start event publishing tracing.
    /// </summary>
    public static Activity? StartEventPublish(
        Guid agentId,
        string eventType,
        string direction)
    {
        var activity = ActivitySource.StartActivity("agent.event.publish", ActivityKind.Producer);
        activity?.SetTag("agent.id", agentId.ToString());
        activity?.SetTag("event.type", eventType);
        activity?.SetTag("event.direction", direction);
        return activity;
    }

    /// <summary>
    /// Record event publishing completion.
    /// </summary>
    public static void RecordEventPublished(
        Activity? activity,
        string agentType,
        string eventType,
        string direction,
        double durationMs)
    {
        // Metrics
        EventsPublished.Add(1,
            new KeyValuePair<string, object?>("agent.type", agentType),
            new KeyValuePair<string, object?>("event.type", eventType),
            new KeyValuePair<string, object?>("direction", direction));

        EventPublishDuration.Record(durationMs,
            new KeyValuePair<string, object?>("event.type", eventType),
            new KeyValuePair<string, object?>("direction", direction));

        // Trace
        activity?.SetTag("duration.ms", durationMs);
        activity?.SetStatus(ActivityStatusCode.Ok);
    }

    // ============================================================
    //  Event Routing Traces + Metrics
    // ============================================================

    /// <summary>
    /// Start event routing tracing.
    /// </summary>
    public static Activity? StartEventRouting(
        Guid agentId,
        string eventId,
        string direction,
        int childCount)
    {
        // Backward-compatible overload (legacy guid-only id).
        return StartEventRouting(agentId.ToString(), eventId, direction, childCount);
    }
    
    /// <summary>
    /// Start event routing tracing (unified ActorId: "Type:RawId").
    /// </summary>
    public static Activity? StartEventRouting(
        string agentId,
        string eventId,
        string direction,
        int childCount)
    {
        var activity = ActivitySource.StartActivity("agent.event.route", ActivityKind.Internal);
        activity?.SetTag("agent.id", agentId);
        activity?.SetTag("event.id", eventId);
        activity?.SetTag("routing.direction", direction);
        activity?.SetTag("routing.children", childCount);
        return activity;
    }

    /// <summary>
    /// Record event routing completion.
    /// </summary>
    public static void RecordEventRouted(
        Activity? activity,
        string direction,
        int targetCount,
        double durationMs)
    {
        // Metrics
        EventsRouted.Add(targetCount,
            new KeyValuePair<string, object?>("direction", direction));

        EventRoutingDuration.Record(durationMs,
            new KeyValuePair<string, object?>("direction", direction),
            new KeyValuePair<string, object?>("target.count", targetCount));

        // Trace
        if (activity != null)
        {
            activity.SetTag("duration.ms", durationMs);
            activity.SetTag("targets.actual", targetCount);
            activity.SetStatus(ActivityStatusCode.Ok);
        }
    }

    // ============================================================
    //  Hierarchy Operations Traces + Metrics
    // ============================================================

    /// <summary>
    /// Start hierarchy operation tracing.
    /// </summary>
    public static Activity? StartHierarchyOperation(
        string operation,
        string agentId,
        string? targetId = null)
    {
        var activity = ActivitySource.StartActivity($"agent.hierarchy.{operation}", ActivityKind.Internal);
        activity?.SetTag("agent.id", agentId.ToString());
        activity?.SetTag("hierarchy.operation", operation);

        if (targetId != null)
        {
            activity?.SetTag("target.id", targetId);
        }

        return activity;
    }

    /// <summary>
    /// Record hierarchy operation completion.
    /// </summary>
    public static void RecordHierarchyOperation(
        Activity? activity,
        string operation,
        bool success = true)
    {
        HierarchyOperations.Add(1,
            new KeyValuePair<string, object?>("operation", operation),
            new KeyValuePair<string, object?>("success", success));

        activity?.SetStatus(success ? ActivityStatusCode.Ok : ActivityStatusCode.Error);
    }

    // ============================================================
    //  State Operations Traces + Metrics
    // ============================================================

    /// <summary>
    /// Start state loading tracing.
    /// </summary>
    public static Activity? StartStateLoad(Guid agentId, string agentType)
    {
        var activity = ActivitySource.StartActivity("agent.state.load", ActivityKind.Client);
        activity?.SetTag("agent.id", agentId.ToString());
        activity?.SetTag("agent.type", agentType);
        return activity;
    }

    /// <summary>
    /// Record state loading completion.
    /// </summary>
    public static void RecordStateLoad(
        Activity? activity,
        string agentType,
        double durationMs,
        bool found)
    {
        StateLoadDuration.Record(durationMs,
            new KeyValuePair<string, object?>("agent.type", agentType),
            new KeyValuePair<string, object?>("state.found", found));

        activity?.SetTag("duration.ms", durationMs);
        activity?.SetTag("state.found", found);
        activity?.SetStatus(ActivityStatusCode.Ok);
    }

    /// <summary>
    /// Start state saving tracing.
    /// </summary>
    public static Activity? StartStateSave(Guid agentId, string agentType)
    {
        var activity = ActivitySource.StartActivity("agent.state.save", ActivityKind.Client);
        activity?.SetTag("agent.id", agentId.ToString());
        activity?.SetTag("agent.type", agentType);
        return activity;
    }

    /// <summary>
    /// Record state saving completion.
    /// </summary>
    public static void RecordStateSave(
        Activity? activity,
        string agentType,
        double durationMs)
    {
        StateSaveDuration.Record(durationMs,
            new KeyValuePair<string, object?>("agent.type", agentType));

        StateChanges.Add(1,
            new KeyValuePair<string, object?>("agent.type", agentType));

        activity?.SetTag("duration.ms", durationMs);
        activity?.SetStatus(ActivityStatusCode.Ok);
    }

    // ============================================================
    //  Subscription Management
    // ============================================================

    /// <summary>
    /// Record subscription created.
    /// </summary>
    public static void RecordSubscriptionCreated(string streamType)
    {
        Interlocked.Increment(ref _subscriptionCount);
    }

    /// <summary>
    /// Record subscription removed.
    /// </summary>
    public static void RecordSubscriptionRemoved(string streamType)
    {
        Interlocked.Decrement(ref _subscriptionCount);
    }

    // ============================================================
    //  Exception Recording
    // ============================================================

    /// <summary>
    /// Record exception to current activity.
    /// </summary>
    public static void RecordException(Activity? activity, Exception exception, string operation)
    {
        if (activity == null) return;

        activity.SetStatus(ActivityStatusCode.Error, exception.Message);
        activity.SetTag("exception.type", exception.GetType().FullName);
        activity.SetTag("exception.message", exception.Message);
        activity.SetTag("exception.operation", operation);

        // Add full stack trace (truncated to avoid excessive length)
        var stackTrace = exception.StackTrace ?? "";
        if (stackTrace.Length > 2000)
        {
            stackTrace = stackTrace[..2000] + "... (truncated)";
        }
        activity.SetTag("exception.stacktrace", stackTrace);
    }
}
