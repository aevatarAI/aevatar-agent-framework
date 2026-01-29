using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.Attributes;
using Aevatar.Agents.Abstractions.Context;
using Aevatar.Agents.Abstractions.Helpers;
using Aevatar.Agents.Core.Context;
using Aevatar.Agents.Core.EventSourcing;
using Aevatar.Agents.Core.Helpers;
using Aevatar.Agents.Core.Observability;
using Aevatar.Agents.Core.StateProtection;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Type = System.Type;

namespace Aevatar.Agents.Core;

/// <summary>
/// Non-generic base class for all GAgents.
/// Provides event handler auto-discovery and invocation infrastructure.
/// This class focuses solely on event processing without state management concerns.
/// </summary>
public abstract class GAgentBase : IGAgent
{
    // ============ Fields ============

    /// <summary>
    /// Agent unique identifier (**unified format**).
    ///
    /// Format: <c>"AgentTypeShortName:RawId"</c>
    /// Example: <c>"ChatAgent:12345678-..."</c>
    ///
    /// NOTE:
    /// - RawId (usually Guid string) can be passed during creation, Factory will automatically
    ///   prepend type prefix via <see cref="AgentId.Normalize(System.Type,string)"/>
    /// - If directly <c>new</c> Agent (bypassing Actor/Factory), this value may only be RawId;
    ///   once crossing boundaries (Stream/DB/Hierarchy), normalize first
    /// </summary>
    public string Id { get; internal set; } = string.Empty;

    /// <summary>
    /// Event publisher for sending events
    /// </summary>
    protected IEventPublisher? EventPublisher;

    /// <summary>
    /// Logger property - supports automatic injection
    /// </summary>
    protected ILogger Logger { get; set; } = NullLogger.Instance;

    /// <summary>
    /// Agent context accessor for request-scoped data.
    /// Internal to allow injection via AgentContextAccessorInjector.
    /// </summary>
    internal IAgentContextAccessor? ContextAccessor;

    /// <summary>
    /// Convenience property to get current agent context.
    /// Returns null if not in a context scope.
    /// </summary>
    protected IAgentContext? Context => ContextAccessor?.Context;

    // Event handler cache (type -> metadata list)
    private static readonly ConcurrentDictionary<Type, EventHandlerMetadata[]> HandlerCache = new();
    private static readonly IEventHandlerDiscoverer DefaultDiscoverer = new ReflectionEventHandlerDiscoverer();

    // Cached Unpack method info to avoid repeated reflection lookups
    private static MethodInfo? _cachedUnpackMethod;
    private static bool _cachedUnpackMethodIsInstance;
    private static readonly object _unpackMethodLock = new();

    /// <summary>
    /// Finds the Unpack method definition using reflection.
    /// This method is cached after first successful lookup.
    /// </summary>
    /// <param name="isInstanceMethod">Output: whether the found method is an instance method</param>
    /// <returns>The MethodInfo for Unpack, or null if not found</returns>
    private static MethodInfo? FindUnpackMethodDefinition(out bool isInstanceMethod)
    {
        lock (_unpackMethodLock)
        {
            if (_cachedUnpackMethod != null)
            {
                isInstanceMethod = _cachedUnpackMethodIsInstance;
                return _cachedUnpackMethod;
            }

            // 1. Try instance method Unpack<T>() first
            var instanceMethod = typeof(Any).GetMethod("Unpack", Type.EmptyTypes);
            if (instanceMethod is { IsGenericMethod: true })
            {
                _cachedUnpackMethod = instanceMethod;
                _cachedUnpackMethodIsInstance = true;
                isInstanceMethod = true;
                return instanceMethod;
            }

            // 2. Fallback: Find Unpack<T> extension method dynamically
            var extensionMethod = typeof(Any).Assembly
                .GetTypes()
                .SelectMany(t => t.GetMethods(BindingFlags.Static | BindingFlags.Public))
                .FirstOrDefault(m => m is { Name: "Unpack", IsGenericMethod: true }
                    && m.GetParameters().Length == 1
                    && m.GetParameters()[0].ParameterType == typeof(Any));

            if (extensionMethod != null)
            {
                _cachedUnpackMethod = extensionMethod;
                _cachedUnpackMethodIsInstance = false;
                isInstanceMethod = false;
                return extensionMethod;
            }

            isInstanceMethod = false;
            return null;
        }
    }

    /// <summary>
    /// Metadata for cached event handlers to avoid repeated reflection
    /// </summary>
    public class EventHandlerMetadata
    {
        public MethodInfo Method { get; }
        public Type ParameterType { get; }
        public bool IsAllEventHandler { get; }
        public bool AllowSelfHandling { get; }
        public bool OnlySelfHandling { get; }
        public Func<Any, IMessage>? Unpacker { get; }

        public EventHandlerMetadata(MethodInfo method)
        {
            Method = method;
            ParameterType = method.GetParameters()[0].ParameterType;

            var allHandlerAttr = method.GetCustomAttribute<AllEventHandlerAttribute>();
            var eventHandlerAttr = method.GetCustomAttribute<EventHandlerAttribute>();

            IsAllEventHandler = allHandlerAttr != null;
            AllowSelfHandling = eventHandlerAttr?.AllowSelfHandling ?? allHandlerAttr?.AllowSelfHandling ?? false;
            OnlySelfHandling = eventHandlerAttr?.OnlySelfHandling ?? false;

            // Pre-compile Unpack delegate for specific message types
            if (!IsAllEventHandler && typeof(IMessage).IsAssignableFrom(ParameterType))
            {
                try
                {
                    var unpackMethodDef = FindUnpackMethodDefinition(out var isInstanceMethod);

                    if (unpackMethodDef != null)
                    {
                        var unpackMethod = unpackMethodDef.MakeGenericMethod(ParameterType);

                        var anyParam = System.Linq.Expressions.Expression.Parameter(typeof(Any), "any");

                        System.Linq.Expressions.MethodCallExpression call;
                        if (isInstanceMethod)
                        {
                            call = System.Linq.Expressions.Expression.Call(anyParam, unpackMethod);
                        }
                        else
                        {
                            call = System.Linq.Expressions.Expression.Call(unpackMethod, anyParam);
                        }

                        var cast = System.Linq.Expressions.Expression.Convert(call, typeof(IMessage));
                        var lambda = System.Linq.Expressions.Expression.Lambda<Func<Any, IMessage>>(cast, anyParam);
                        Unpacker = lambda.Compile();
                    }
                }
                catch (Exception)
                {
                    // Fallback or ignore if unpacker cannot be created
                    Unpacker = null;
                }
            }
        }
    }

    // ============ Constructors ============

    /// <summary>
    /// Default constructor - generates a new ID
    /// </summary>
    public GAgentBase()
    {
        Id = Guid.NewGuid().ToString();
    }

    /// <summary>
    /// Constructor with specific ID
    /// </summary>
    public GAgentBase(string id)
    {
        Id = id;
    }

    // ============ IGAgent Implementation ============

    /// <summary>
    /// Get agent category for routing.
    /// Defaults to [StreamTopic] attribute value, or the simple type name if not present.
    /// </summary>
    public virtual string GetAgentCategory()
    {
        var attr = GetType().GetCustomAttribute<StreamTopicAttribute>();
        if (attr != null)
        {
            return attr.Topic;
        }
        return GetType().Name;
    }

    /// <summary>
    /// Get agent description
    /// </summary>
    public virtual string GetDescription()
    {
        return GetType().Name;
    }

    /// <summary>
    /// Get agent description - async version
    /// </summary>
    /// <returns></returns>
    public virtual Task<string> GetDescriptionAsync()
    {
        return Task.FromResult(GetDescription());
    }

    /// <summary>
    /// Get all subscribed event types
    /// </summary>
    public virtual Task<List<Type>> GetAllSubscribedEventsAsync(bool includeAllEventHandler = false)
    {
        var handlers = GetEventHandlers();
        var eventTypes = new HashSet<Type>();

        foreach (var handler in handlers)
        {
            // Skip EventEnvelope (AllEventHandler) if not requested
            if (!includeAllEventHandler && handler.IsAllEventHandler)
                continue;

            // Only include IMessage types
            if (typeof(IMessage).IsAssignableFrom(handler.ParameterType))
            {
                eventTypes.Add(handler.ParameterType);
            }
        }

        return Task.FromResult(eventTypes.ToList());
    }

    public async Task ActivateAsync(CancellationToken ct = default)
    {
        // Allow State modification during agent activation
        // This is necessary for initializing agent state before event processing begins
        using (StateProtectionContext.BeginInitializationScope())
        {
            await OnActivateAsync(ct);
        }
    }

    public async Task DeactivateAsync(CancellationToken ct = default)
    {
        await OnDeactivateAsync(ct);
    }

    // ============ Event Publishing ============

    /// <summary>
    /// Publish event (delegates to EventPublisher) - Broadcast mode
    /// </summary>
    protected async Task<string> PublishAsync<TEvent>(
        TEvent evt,
        EventDirection direction = EventDirection.Down,
        CancellationToken ct = default)
        where TEvent : IMessage
    {
        if (EventPublisher == null)
        {
            throw new InvalidOperationException(
                "EventPublisher is not set. Make sure the Actor layer has initialized this agent.");
        }

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var eventId = await EventPublisher.PublishEventAsync(evt, direction, ct, isInternalCall: true);

            // Record publish metrics
            stopwatch.Stop();
            AgentMetrics.RecordEventPublished(typeof(TEvent).Name, Id);
            AgentMetrics.EventPublishLatency.Record(stopwatch.ElapsedMilliseconds,
                new KeyValuePair<string, object?>("event.type", typeof(TEvent).Name),
                new KeyValuePair<string, object?>("agent.id", Id));

            return eventId;
        }
        catch (Exception ex)
        {
            // Record exception metrics
            AgentMetrics.RecordException(ex.GetType().Name, Id, "PublishEvent");
            throw;
        }
    }

    /// <summary>
    /// Point-to-point send - Direct delivery mode.
    /// Sends directly to specified agent, bypassing hierarchical broadcast.
    /// </summary>
    /// <param name="targetAgentId">Target agent ID (format varies by runtime)</param>
    /// <param name="evt">Event message</param>
    /// <param name="onArrivalDirection">
    /// Propagation direction after arrival:
    /// - Unspecified: Pure P2P, only target processes, no propagation
    /// - Down: Target processes then broadcasts to all its children
    /// - Up: Target processes then propagates up to its parent
    /// - Both: Target processes then propagates in both directions
    /// </param>
    /// <param name="ct">Cancellation token</param>
    /// <typeparam name="TEvent">Event type</typeparam>
    /// <returns>Event ID</returns>
    protected async Task<string> SendToAsync<TEvent>(
        string targetAgentId,
        TEvent evt,
        EventDirection onArrivalDirection = EventDirection.Unspecified,
        CancellationToken ct = default)
        where TEvent : IMessage
    {
        if (EventPublisher == null)
        {
            throw new InvalidOperationException(
                "EventPublisher is not set. Make sure the Actor layer has initialized this agent.");
        }

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var eventId = await EventPublisher.SendToAsync(targetAgentId, evt, onArrivalDirection, ct);

            // Record send metrics
            stopwatch.Stop();
            AgentMetrics.RecordEventPublished(typeof(TEvent).Name, Id);
            AgentMetrics.EventPublishLatency.Record(stopwatch.ElapsedMilliseconds,
                new KeyValuePair<string, object?>("event.type", typeof(TEvent).Name),
                new KeyValuePair<string, object?>("agent.id", Id),
                new KeyValuePair<string, object?>("mode", "point-to-point"));

            return eventId;
        }
        catch (Exception ex)
        {
            // Record exception metrics
            AgentMetrics.RecordException(ex.GetType().Name, Id, "SendToEvent");
            throw;
        }
    }

    // EventPublisher is now injected via EventPublisherInjector
    // No public setter method needed

    // ============ Event Handler Discovery ============

    /// <summary>
    /// Get all event handler metadata (cached)
    /// </summary>
    public EventHandlerMetadata[] GetEventHandlers()
    {
        var type = GetType();
        return HandlerCache.GetOrAdd(type, _ =>
        {
            var methods = GetEventHandlerDiscoverer().DiscoverEventHandlers(type);
            var metadata = methods.Select(m => new EventHandlerMetadata(m)).ToArray();
            Logger.LogDebug("Discovered {Count} event handlers for {Type}", metadata.Length, type.Name);
            return metadata;
        });
    }

    /// <summary>
    /// Get the event handler discoverer to use.
    /// Defaults to ReflectionEventHandlerDiscoverer.
    /// </summary>
    protected virtual IEventHandlerDiscoverer GetEventHandlerDiscoverer()
    {
        return DefaultDiscoverer;
    }

    // ============ Event Handler Invocation ============

    /// <summary>
    /// Handle event - entry point that can be overridden by derived classes
    /// Derived classes should override this to add state management
    /// </summary>
    public virtual async Task HandleEventAsync(EventEnvelope envelope, CancellationToken ct = default)
    {
        await HandleEventCoreAsync(envelope, ct);
    }

    /// <summary>
    /// Core event handling implementation without state management
    /// This method contains the actual event processing logic
    /// </summary>
    protected virtual async Task HandleEventCoreAsync(EventEnvelope envelope, CancellationToken ct = default)
    {
        var eventType = ResolveEnvelopeEventType(envelope);

        using var loggingScope = LoggingScope.CreateEventHandlingScope(
            Logger,
            Id,
            envelope.Id,
            eventType,
            envelope.CorrelationId);

        using var contextScope = ContextAccessor?.CreateScope(envelope) ?? AgentContextScope.Empty;

        var stopwatch = Stopwatch.StartNew();
        var handled = await DispatchEventHandlersAsync(envelope, ct);
        stopwatch.Stop();

        RecordEventHandlingMetrics(eventType, handled, stopwatch.ElapsedMilliseconds);
    }

    private static string ResolveEnvelopeEventType(EventEnvelope envelope)
        => envelope.Payload?.TypeUrl?.Split('/').LastOrDefault() ?? "Unknown";

    private async Task<bool> DispatchEventHandlersAsync(EventEnvelope envelope, CancellationToken ct)
    {
        var handled = false;
        var handlers = GetEventHandlers();
        foreach (var handler in handlers)
        {
            if (await TryHandleEventWithHandlerAsync(handler, envelope, ct))
                handled = true;
        }
        return handled;
    }

    private async Task<bool> TryHandleEventWithHandlerAsync(
        EventHandlerMetadata handler,
        EventEnvelope envelope,
        CancellationToken ct)
    {
        if (!ShouldHandleEvent(handler, envelope))
            return false;

        try
        {
            if (handler.IsAllEventHandler)
                return await InvokeAllEventHandlerAsync(handler, envelope, ct);

            return await InvokeTypedEventHandlerAsync(handler, envelope, ct);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error handling event in {Handler}", handler.Method.Name);
            AgentMetrics.RecordException(ex.GetType().Name, Id, $"HandleEvent:{handler.Method.Name}");
            await PublishExceptionEventAsync(envelope, handler.Method.Name, ex);
            return false;
        }
    }

    private async Task<bool> InvokeAllEventHandlerAsync(
        EventHandlerMetadata handler,
        EventEnvelope envelope,
        CancellationToken ct)
    {
        await InvokeHandlerWithHooksAsync(handler, envelope, envelope, ct);
        return true;
    }

    private async Task<bool> InvokeTypedEventHandlerAsync(
        EventHandlerMetadata handler,
        EventEnvelope envelope,
        CancellationToken ct)
    {
        if (envelope.Payload == null)
            return false;

        var message = TryUnpackMessage(handler, envelope);
        if (message == null)
        {
            LogUnpackSkip(handler, envelope);
            return false;
        }

        Logger.LogDebug("Invoking handler {HandlerName} with message {MessageType}", handler.Method.Name, message.GetType().Name);
        await InvokeHandlerWithHooksAsync(handler, envelope, message, ct);
        return true;
    }

    private IMessage? TryUnpackMessage(EventHandlerMetadata handler, EventEnvelope envelope)
    {
        if (envelope.Payload == null)
            return null;

        try
        {
            if (handler.Unpacker != null)
            {
                var message = handler.Unpacker(envelope.Payload);
                Logger.LogDebug("Unpacked message of type {MessageType} for handler {HandlerName} using Unpacker",
                    message?.GetType().Name ?? "null", handler.Method.Name);
                return message;
            }

            Logger.LogDebug("Unpacker is null for handler {HandlerName}. Attempting reflection fallback.", handler.Method.Name);
            return TryUnpackWithReflection(handler, envelope.Payload);
        }
        catch (Exception ex)
        {
            Logger.LogTrace(ex, "Failed to unpack event payload for handler {Handler}", handler.Method.Name);
            return null;
        }
    }

    private IMessage? TryUnpackWithReflection(EventHandlerMetadata handler, Any payload)
    {
        try
        {
            var unpackMethodDef = FindUnpackMethodDefinition(out var isInstanceMethod);
            if (unpackMethodDef == null)
            {
                Logger.LogError("CRITICAL: Could not find Any.Unpack method via reflection for handler {HandlerName}",
                    handler.Method.Name);
                return null;
            }

            var genericUnpack = unpackMethodDef.MakeGenericMethod(handler.ParameterType);
            var message = isInstanceMethod
                ? (IMessage?)genericUnpack.Invoke(payload, null)
                : (IMessage?)genericUnpack.Invoke(null, new object?[] { payload });
            Logger.LogDebug("Unpacked message of type {MessageType} for handler {HandlerName} using reflection fallback",
                message?.GetType().Name ?? "null", handler.Method.Name);
            return message;
        }
        catch (Exception ex)
        {
            Logger.LogDebug(ex, "Failed to unpack payload for handler {HandlerName} using reflection fallback.",
                handler.Method.Name);
            return null;
        }
    }

    private void LogUnpackSkip(EventHandlerMetadata handler, EventEnvelope envelope)
    {
        if (envelope.Payload == null)
            return;

        var actualTypeUrl = envelope.Payload?.TypeUrl ?? "null";
        var msg = $"Skipping handler {handler.Method.Name} because message could not be unpacked (Type mismatch or Unpack failure). Expected: {handler.ParameterType.FullName}, Actual URL: {actualTypeUrl}";
        Logger.LogDebug(msg);
    }

    private async Task InvokeHandlerWithHooksAsync(
        EventHandlerMetadata handler,
        EventEnvelope envelope,
        object payload,
        CancellationToken ct)
    {
        var handlerStopwatch = Stopwatch.StartNew();
        Exception? handlerException = null;
        await SafeOnEventHandlerStartAsync(envelope, handler, payload, ct);
        try
        {
            await InvokeHandler(handler.Method, payload, ct);
        }
        catch (Exception ex)
        {
            handlerException = ex;
            throw;
        }
        finally
        {
            handlerStopwatch.Stop();
            await SafeOnEventHandlerEndAsync(
                envelope,
                handler,
                payload,
                handlerStopwatch.Elapsed,
                handlerException,
                ct);
        }
    }

    private void RecordEventHandlingMetrics(string eventType, bool handled, long elapsedMs)
    {
        if (handled)
        {
            AgentMetrics.RecordEventHandled(eventType, Id, elapsedMs);
            return;
        }

        AgentMetrics.EventsDropped.Add(1,
            new KeyValuePair<string, object?>("event.type", eventType),
            new KeyValuePair<string, object?>("agent.id", Id));
    }

    /// <summary>
    /// Event handler hook (best-effort). Override to plug in hook pipeline.
    /// </summary>
    protected virtual Task OnEventHandlerStartAsync(
        EventEnvelope envelope,
        EventHandlerMetadata handler,
        object? payload,
        CancellationToken ct)
        => Task.CompletedTask;

    /// <summary>
    /// Event handler hook (best-effort). Override to plug in hook pipeline.
    /// </summary>
    protected virtual Task OnEventHandlerEndAsync(
        EventEnvelope envelope,
        EventHandlerMetadata handler,
        object? payload,
        TimeSpan duration,
        Exception? exception,
        CancellationToken ct)
        => Task.CompletedTask;

    private async Task SafeOnEventHandlerStartAsync(
        EventEnvelope envelope,
        EventHandlerMetadata handler,
        object? payload,
        CancellationToken ct)
    {
        // Skip hook execution if already cancelled
        if (ct.IsCancellationRequested)
        {
            Logger.LogTrace("Skipping event handler start hook (cancelled). Handler={Handler} EventId={EventId}",
                handler.Method.Name, envelope.Id);
            return;
        }

        try
        {
            await OnEventHandlerStartAsync(envelope, handler, payload, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            Logger.LogTrace("Event handler start hook cancelled. Handler={Handler} EventId={EventId}",
                handler.Method.Name, envelope.Id);
        }
        catch (Exception ex)
        {
            Logger.LogDebug(ex,
                "Event handler hook (start) failed. Handler={Handler} EventId={EventId}",
                handler.Method.Name, envelope.Id);
        }
    }

    private async Task SafeOnEventHandlerEndAsync(
        EventEnvelope envelope,
        EventHandlerMetadata handler,
        object? payload,
        TimeSpan duration,
        Exception? exception,
        CancellationToken ct)
    {
        // Skip hook execution if already cancelled - avoid noisy OperationCanceledException logs
        if (ct.IsCancellationRequested)
        {
            Logger.LogTrace("Skipping event handler end hook (cancelled). Handler={Handler} EventId={EventId}",
                handler.Method.Name, envelope.Id);
            return;
        }

        try
        {
            await OnEventHandlerEndAsync(envelope, handler, payload, duration, exception, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Expected during graceful cancellation - log at trace level
            Logger.LogTrace("Event handler end hook cancelled. Handler={Handler} EventId={EventId}",
                handler.Method.Name, envelope.Id);
        }
        catch (Exception ex)
        {
            Logger.LogDebug(ex,
                "Event handler hook (end) failed. Handler={Handler} EventId={EventId}",
                handler.Method.Name, envelope.Id);
        }
    }

    /// <summary>
    /// Determine if an event should be handled (can be used by subclasses)
    /// </summary>
    private bool ShouldHandleEvent(EventHandlerMetadata handler, EventEnvelope envelope)
    {
        // OnlySelfHandling 蕴含 AllowSelfHandling
        var allowSelf = handler.AllowSelfHandling || handler.OnlySelfHandling;
        
        // If self-handling is not allowed and publisher is self, skip
        if (!allowSelf && envelope.PublisherId == Id)
        {
            return false;
        }

        // If only-self-handling is set, only handle events with EventDirection.Self
        // (ignore events from parent/children streams)
        if (handler.OnlySelfHandling && envelope.Direction != EventDirection.Self)
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Invoke handler method (can be used by subclasses)
    /// </summary>
    protected async Task InvokeHandler(MethodInfo handler, object parameter, CancellationToken ct)
    {
        Logger.LogDebug("Invoking handler method {HandlerName} on {AgentType} with parameter type {ParameterType}",
            handler.Name, GetType().Name, parameter.GetType().Name);

        // Create event handler scope to allow State modifications
        using var scope = StateProtectionContext.BeginEventHandlerScope();

        try
        {
            var result = handler.Invoke(this, new[] { parameter });

            if (result is Task task)
            {
                await task;
            }

            Logger.LogDebug("Handler method {HandlerName} completed", handler.Name);
        }
        catch (TargetInvocationException tie) when (tie.InnerException != null)
        {
            // Unwrap the TargetInvocationException to get the actual exception
            throw tie.InnerException;
        }
    }

    // ============ Resource Management ============

    /// <summary>
    /// Prepare resource context
    /// </summary>
    public virtual Task PrepareResourceContextAsync(ResourceContext context, CancellationToken ct = default)
    {
        Logger.LogDebug("Preparing resource context for Agent {Id} with {ResourceCount} resources",
            Id, context.Count);

        return OnPrepareResourceContextAsync(context, ct);
    }

    /// <summary>
    /// Resource context preparation callback (overridden by subclasses)
    /// </summary>
    protected virtual Task OnPrepareResourceContextAsync(ResourceContext context, CancellationToken ct = default)
    {
        // Default implementation: do nothing
        // Subclasses can override to handle resources
        return Task.CompletedTask;
    }

    // ============ Exception Handling ============

    /// <summary>
    /// Publish exception event
    /// </summary>
    protected virtual async Task PublishExceptionEventAsync(
        EventEnvelope originalEnvelope,
        string handlerName,
        Exception exception)
    {
        try
        {
            if (EventPublisher == null)
                return;

            // Build complete exception message including inner exceptions
            var fullExceptionMessage = ExceptionFormatter.BuildFullExceptionMessage(exception);

            var exceptionEvent = new EventHandlerExceptionEvent
            {
                AgentId = Id,
                EventId = originalEnvelope.Id,
                HandlerName = handlerName,
                EventType = originalEnvelope.Payload?.TypeUrl ?? "Unknown",
                ExceptionMessage = fullExceptionMessage,
                StackTrace = exception.StackTrace ?? string.Empty,
                Timestamp = TimestampHelper.GetUtcNow()
            };

            Logger.LogDebug("Publishing exception event for handler {Handler}", handlerName);

            await EventPublisher.PublishEventAsync(exceptionEvent, EventDirection.Up, default, isInternalCall: true);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error publishing exception event");
        }
    }

    /// <summary>
    /// Publish framework exception event
    /// </summary>
    protected virtual async Task PublishFrameworkExceptionAsync(
        string operation,
        Exception exception)
    {
        try
        {
            if (EventPublisher == null)
                return;

            var exceptionEvent = new GAgentBaseExceptionEvent
            {
                AgentId = Id,
                Operation = operation,
                ExceptionMessage = ExceptionFormatter.BuildFullExceptionMessage(exception),
                StackTrace = exception.StackTrace ?? string.Empty,
                Timestamp = TimestampHelper.GetUtcNow()
            };

            Logger.LogDebug("Publishing framework exception event for operation {Operation}", operation);

            await EventPublisher.PublishEventAsync(exceptionEvent, EventDirection.Up, default, isInternalCall: true);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error publishing framework exception event");
        }
    }

    // ============ Lifecycle Callbacks (optional override) ============

    /// <summary>
    /// Activation callback
    /// </summary>
    protected virtual Task OnActivateAsync(CancellationToken ct = default)
    {
        Logger.LogDebug("Agent {Id} of type {AgentType} activated", Id, GetType().Name);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Deactivation callback
    /// </summary>
    protected virtual Task OnDeactivateAsync(CancellationToken ct = default)
    {
        Logger.LogDebug("Agent {Id} deactivated", Id);
        return Task.CompletedTask;
    }
}