using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Core.Subscription;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.Core.Tests.Subscription;

/// <summary>
/// Mock implementation for testing BaseSubscriptionManager logic
/// </summary>
public class MockSubscriptionManager : BaseSubscriptionManager
{
    private readonly ConcurrentDictionary<Guid, MockStreamSubscription> _mockSubscriptions = new();
    private readonly ConcurrentDictionary<string, Func<EventEnvelope, Task>> _eventHandlers = new();
    
    // Control flags for testing
    public bool ShouldFailOnCreate { get; set; }
    public bool ShouldFailOnHealthCheck { get; set; }
    public bool ShouldFailOnReconnect { get; set; }
    public int CreateCallCount { get; private set; }
    public int HealthCheckCallCount { get; private set; }
    public int ReconnectCallCount { get; private set; }
    
    public MockSubscriptionManager(ILogger<MockSubscriptionManager>? logger = null) 
        : base(logger)
    {
    }

    protected override async Task<IMessageStreamSubscription?> CreateStreamSubscriptionAsync(
        string parentId, 
        string childId, 
        Func<EventEnvelope, Task> eventHandler, 
        CancellationToken cancellationToken)
    {
        CreateCallCount++;
        
        if (ShouldFailOnCreate)
        {
            throw new TimeoutException("Mock failure on create");
        }
        
        await Task.Delay(10, cancellationToken); // Simulate async operation
        
        var subscriptionId = Guid.NewGuid();
        var mockSubscription = new MockStreamSubscription(subscriptionId, parentId, childId);
        
        _mockSubscriptions[subscriptionId] = mockSubscription;
        _eventHandlers[childId] = eventHandler;
        
        Logger.LogDebug("Mock: Created subscription {SubscriptionId} for Child {ChildId} -> Parent {ParentId}", 
            subscriptionId, childId, parentId);
        
        return mockSubscription;
    }

    protected override async Task<bool> CheckStreamHealthAsync(ISubscriptionHandle subscription)
    {
        HealthCheckCallCount++;
        
        if (ShouldFailOnHealthCheck)
        {
            return false;
        }
        
        await Task.Delay(5); // 模拟异步操作
        
        // 检查是否有对应的mock订阅
        if (subscription.StreamSubscription is MockStreamSubscription mockSub)
        {
            return mockSub.IsActive && !mockSub.IsUnsubscribed;
        }
        
        return false;
    }

    protected override async Task ReconnectStreamAsync(
        SubscriptionHandle handle, 
        CancellationToken cancellationToken)
    {
        ReconnectCallCount++;
        
        if (ShouldFailOnReconnect)
        {
            throw new InvalidOperationException("Mock failure on reconnect");
        }
        
        await Task.Delay(10, cancellationToken); // 模拟异步操作
        
        // 模拟重连：创建新的订阅
        var newSubscriptionId = Guid.NewGuid();
        var mockSubscription = new MockStreamSubscription(newSubscriptionId, handle.ParentId, handle.ChildId);
        
        _mockSubscriptions[newSubscriptionId] = mockSubscription;
        handle.StreamSubscription = mockSubscription;
        
        Logger.LogDebug("Mock: Reconnected subscription for Child {ChildId} -> Parent {ParentId}", 
            handle.ChildId, handle.ParentId);
    }
    
    /// <summary>
    /// 模拟发送事件到订阅者（用于测试）
    /// </summary>
    public async Task SimulateEventAsync(string childId, EventEnvelope envelope)
    {
        if (_eventHandlers.TryGetValue(childId, out var handler))
        {
            await handler(envelope);
        }
    }
    
    /// <summary>
    /// 获取Mock订阅数量（用于验证）
    /// </summary>
    public int GetMockSubscriptionCount()
    {
        return _mockSubscriptions.Count;
    }
    
    /// <summary>
    /// 清理所有Mock订阅（用于测试清理）
    /// </summary>
    public void ClearMockSubscriptions()
    {
        _mockSubscriptions.Clear();
        _eventHandlers.Clear();
    }
}

/// <summary>
/// Mock的流订阅实现
/// </summary>
public class MockStreamSubscription : IMessageStreamSubscription
{
    public Guid SubscriptionId { get; }
    public string StreamId { get; }
    public bool IsActive { get; set; } = true;
    public bool IsUnsubscribed { get; private set; }
    public int UnsubscribeCallCount { get; private set; }
    
    public MockStreamSubscription(Guid subscriptionId, string parentId, string childId)
    {
        SubscriptionId = subscriptionId;
        StreamId = parentId; // 使用ParentId作为StreamId
    }

    public Task ResumeAsync()
    {
        IsActive = true;
        return Task.CompletedTask;
    }

    public Task UnsubscribeAsync()
    {
        UnsubscribeCallCount++;
        IsUnsubscribed = true;
        IsActive = false;
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        IsUnsubscribed = true;
        IsActive = false;
        return ValueTask.CompletedTask;
    }
}
