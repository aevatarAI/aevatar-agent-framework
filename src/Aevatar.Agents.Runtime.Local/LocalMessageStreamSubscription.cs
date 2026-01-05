using Aevatar.Agents.Abstractions;

namespace Aevatar.Agents.Runtime.Local;

/// <summary>
/// Local message stream subscription implementation
/// Manages subscription lifecycle for local message streams
/// </summary>
internal class LocalMessageStreamSubscription : IMessageStreamSubscription
{
    private readonly Func<EventEnvelope, Task> _handler;
    private readonly Action _onDisposed;
    private bool _isActive;

    public Guid SubscriptionId { get; }
    public string StreamId { get; }
    public bool IsActive => _isActive;

    public LocalMessageStreamSubscription(
        Guid subscriptionId,
        string streamId,
        Func<EventEnvelope, Task> handler,
        Action onDisposed)
    {
        SubscriptionId = subscriptionId;
        StreamId = streamId;
        _handler = handler;
        _onDisposed = onDisposed;
        _isActive = true;
    }

    /// <summary>
    /// Handle received message
    /// </summary>
    public async Task HandleMessageAsync(EventEnvelope envelope)
    {
        if (!_isActive)
        {
            return;
        }

        await _handler(envelope);
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
        // Don't call _onDisposed, keep subscription in dictionary to support Resume
        // Only actually remove in DisposeAsync
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

        // Local stream is based on in-memory Channel
        // Resuming subscription only needs to reactivate processing flag
        _isActive = true;
        
        // Note: If Channel is already closed, cannot resume
        // Caller should handle this case and create new subscription
        
        return Task.CompletedTask;
    }

    /// <summary>
    /// Asynchronously dispose resources
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (!_isActive)
        {
            // Even if already unsubscribed, ensure removal from dictionary
            _onDisposed?.Invoke();
            return;
        }
        
        _isActive = false;
        _onDisposed?.Invoke(); // Actually remove from dictionary
    }
}
