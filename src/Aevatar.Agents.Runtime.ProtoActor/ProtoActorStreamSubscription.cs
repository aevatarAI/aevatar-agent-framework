using Aevatar.Agents.Abstractions;
using Google.Protobuf;
using Proto;

namespace Aevatar.Agents.Runtime.ProtoActor;

/// <summary>
/// Proto.Actor stream subscription implementation
/// Since Proto.Actor is based on message passing, this mainly provides subscription management interface
/// </summary>
internal class ProtoActorStreamSubscription : IMessageStreamSubscription
{
    private readonly Func<IMessage, Task> _handler;
    private readonly Func<IMessage, bool>? _filter;
    private readonly PID _targetPid;
    private readonly IRootContext _rootContext;
    private readonly Action _onDisposed;
    private bool _isActive;

    public Guid SubscriptionId { get; }
    public string StreamId { get; }
    public bool IsActive => _isActive;

    public ProtoActorStreamSubscription(
        Guid subscriptionId,
        string streamId,
        Func<IMessage, Task> handler,
        Func<IMessage, bool>? filter,
        PID targetPid,
        IRootContext rootContext,
        Action onDisposed)
    {
        SubscriptionId = subscriptionId;
        StreamId = streamId;
        _handler = handler;
        _filter = filter;
        _targetPid = targetPid;
        _rootContext = rootContext;
        _onDisposed = onDisposed;
        _isActive = true;
    }

    /// <summary>
    /// Handle received message
    /// </summary>
    public async Task HandleMessageAsync(IMessage message)
    {
        if (!_isActive)
        {
            return;
        }

        // Apply filter
        if (_filter != null && !_filter(message))
        {
            return;
        }

        await _handler(message);
    }

    /// <summary>
    /// Unsubscribe
    /// </summary>
    public Task UnsubscribeAsync()
    {
        if (!_isActive)
        {
            return Task.CompletedTask;
        }

        _isActive = false;
        // Note: Don't call _onDisposed, keep subscription in dictionary to support Resume
        // _onDisposed is only called when truly disposing
        
        // In Proto.Actor, unsubscribing means stopping message processing
        // Actual message routing is managed by Actor system
        return Task.CompletedTask;
    }

    /// <summary>
    /// Resume subscription
    /// </summary>
    public Task ResumeAsync()
    {
        if (_isActive)
        {
            return Task.CompletedTask;
        }

        // Proto.Actor subscriptions are memory-based
        // Only need to reactivate flag to resume message processing
        _isActive = true;
        
        // Optional: Send a resume notification to target Actor
        // _rootContext.Send(_targetPid, new SubscriptionResumed { SubscriptionId = SubscriptionId });
        
        return Task.CompletedTask;
    }

    /// <summary>
    /// Asynchronously dispose resources
    /// </summary>
    public ValueTask DisposeAsync()
    {
        if (_isActive)
        {
            _isActive = false;
        }
        
        // Only remove from dictionary when truly disposing
        _onDisposed?.Invoke();
        
        return ValueTask.CompletedTask;
    }
}
