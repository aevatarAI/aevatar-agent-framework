using System;
using System.Threading;
using System.Threading.Tasks;
using Aevatar.Agents;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Core;
using Aevatar.Agents.Core.EventDeduplication;
using Aevatar.Agents.Core.Subscription;
using Aevatar.Agents.Runtime.Local;
using Aevatar.Agents.Runtime.Local.Subscription;
using Microsoft.Extensions.Logging;

namespace Demo.Agents;

/// <summary>
/// Demonstrate improved subscription mechanism and event deduplication
/// </summary>
public static class ImprovedSubscriptionDemo
{
    public static async Task RunDemo(ILogger logger)
    {
        logger.LogInformation("=== Improved Subscription & Deduplication Demo ===\n");
        
        // 1. Demonstrate event deduplication
        await DemonstrateEventDeduplication(logger);
        
        // 2. Demonstrate retry policies
        await DemonstrateRetryPolicies(logger);
        
        // 3. Demonstrate subscription management
        await DemonstrateSubscriptionManagement(logger);
        
        logger.LogInformation("\n=== Demo Completed ===");
    }
    
    /// <summary>
    /// Demonstrate event deduplication mechanism
    /// </summary>
    private static async Task DemonstrateEventDeduplication(ILogger logger)
    {
        logger.LogInformation("\n--- Event Deduplication Demo ---");
        
        var deduplicator = new MemoryCacheEventDeduplicator(
            new DeduplicationOptions
            {
                EventExpiration = TimeSpan.FromSeconds(5),
                MaxCachedEvents = 100
            });
        
        // 测试新事件
        var eventId1 = "event-001";
        var isNew1 = await deduplicator.TryRecordEventAsync(eventId1);
        logger.LogInformation("Event {EventId}: {Status}", eventId1, isNew1 ? "NEW" : "DUPLICATE");
        
        // 测试重复事件
        var isNew2 = await deduplicator.TryRecordEventAsync(eventId1);
        logger.LogInformation("Event {EventId}: {Status}", eventId1, isNew2 ? "NEW" : "DUPLICATE");
        
        // 批量测试
        var eventIds = new[] { "event-002", "event-003", "event-001", "event-004" };
        var newEvents = await deduplicator.TryRecordEventsAsync(eventIds);
        logger.LogInformation("Batch: {Total} events, {New} new, {Duplicate} duplicates",
            eventIds.Length, newEvents.Count, eventIds.Length - newEvents.Count);
        
        // 获取统计
        var stats = await deduplicator.GetStatisticsAsync();
        logger.LogInformation("Statistics: Total={Total}, Unique={Unique}, Duplicate={Duplicate}, DuplicateRate={Rate:P}",
            stats.TotalEvents, stats.UniqueEvents, stats.DuplicateEvents, stats.DuplicateRate);
        
        // 等待过期
        logger.LogInformation("Waiting for events to expire (5 seconds)...");
        await Task.Delay(TimeSpan.FromSeconds(6));
        
        // 过期后的事件变成新事件
        var isNewAfterExpiry = await deduplicator.TryRecordEventAsync(eventId1);
        logger.LogInformation("Event {EventId} after expiry: {Status}", 
            eventId1, isNewAfterExpiry ? "NEW (expired)" : "DUPLICATE");
    }
    
    /// <summary>
    /// 演示重试策略
    /// </summary>
    private static async Task DemonstrateRetryPolicies(ILogger logger)
    {
        logger.LogInformation("\n--- Retry Policies Demo ---");
        
        // 固定间隔重试
        var fixedPolicy = RetryPolicyFactory.CreateFixedInterval(
            maxRetries: 3,
            interval: TimeSpan.FromMilliseconds(500));
        
        logger.LogInformation("Fixed Interval Policy:");
        for (int i = 1; i <= 3; i++)
        {
            var delay = fixedPolicy.GetDelay(i);
            logger.LogInformation("  Attempt {Attempt}: Delay = {Delay}ms", i, delay.TotalMilliseconds);
        }
        
        // 指数退避重试
        var exponentialPolicy = RetryPolicyFactory.CreateExponentialBackoff(
            maxRetries: 5,
            initialDelay: TimeSpan.FromMilliseconds(100),
            useJitter: false);
        
        logger.LogInformation("\nExponential Backoff Policy (no jitter):");
        for (int i = 1; i <= 5; i++)
        {
            var delay = exponentialPolicy.GetDelay(i);
            logger.LogInformation("  Attempt {Attempt}: Delay = {Delay}ms", i, delay.TotalMilliseconds);
        }
        
        // 带抖动的指数退避
        var jitterPolicy = RetryPolicyFactory.CreateExponentialBackoff(
            maxRetries: 3,
            initialDelay: TimeSpan.FromMilliseconds(100),
            useJitter: true);
        
        logger.LogInformation("\nExponential Backoff Policy (with jitter):");
        for (int i = 1; i <= 3; i++)
        {
            var delay = jitterPolicy.GetDelay(i);
            logger.LogInformation("  Attempt {Attempt}: Delay = {Delay}ms (with jitter)", i, delay.TotalMilliseconds);
        }
        
        // 线性退避
        var linearPolicy = RetryPolicyFactory.CreateLinearBackoff(
            maxRetries: 4,
            delayIncrement: TimeSpan.FromMilliseconds(200));
        
        logger.LogInformation("\nLinear Backoff Policy:");
        for (int i = 1; i <= 4; i++)
        {
            var delay = linearPolicy.GetDelay(i);
            logger.LogInformation("  Attempt {Attempt}: Delay = {Delay}ms", i, delay.TotalMilliseconds);
        }
    }
    
    /// <summary>
    /// 演示订阅管理
    /// </summary>
    private static async Task DemonstrateSubscriptionManagement(ILogger logger)
    {
        logger.LogInformation("\n--- Subscription Management Demo ---");
        
        // 创建stream registry和订阅管理器
        var streamRegistry = new LocalMessageStreamRegistry();
        var subscriptionManager = new LocalSubscriptionManager(streamRegistry);
        
        var parentId = Guid.NewGuid().ToString();
        var childId = Guid.NewGuid().ToString();
        
        // 模拟事件处理器
        var eventCount = 0;
        Func<EventEnvelope, Task> eventHandler = async (envelope) =>
        {
            Interlocked.Increment(ref eventCount);
            logger.LogInformation("Processed event {EventId}, Total: {Count}", 
                envelope.Id, eventCount);
            await Task.CompletedTask;
        };
        
        // 创建订阅（带重试）
        logger.LogInformation("\nCreating subscription with exponential backoff retry...");
        var subscription = await subscriptionManager.SubscribeWithRetryAsync(
            parentId: parentId,
            childId: childId,
            eventHandler: eventHandler,
            retryPolicy: RetryPolicyFactory.CreateExponentialBackoff(3));
        
        logger.LogInformation("Subscription created: {SubscriptionId}", subscription.SubscriptionId);
        
        // 检查健康状态
        var isHealthy = await subscriptionManager.IsSubscriptionHealthyAsync(subscription);
        logger.LogInformation("Subscription health: {Health}", isHealthy ? "Healthy" : "Unhealthy");
        
        // 模拟发送事件
        var parentStream = streamRegistry.GetOrCreateStream(parentId);
        logger.LogInformation("\nSending test events through parent stream...");
        
        for (int i = 1; i <= 5; i++)
        {
            var envelope = new EventEnvelope
            {
                Id = $"test-event-{i}",
                PublisherId = parentId.ToString(),
                Payload = Google.Protobuf.WellKnownTypes.Any.Pack(new TeamMessageEvent 
                { 
                    From = "Parent",
                    Message = $"Test message {i}"
                })
            };
            
            await parentStream.ProduceAsync(envelope);
            await Task.Delay(100); // 短暂延迟以观察处理
        }
        
        await Task.Delay(500); // 等待所有事件处理完成
        
        // 获取活跃订阅
        var activeSubscriptions = await subscriptionManager.GetActiveSubscriptionsAsync();
        logger.LogInformation("\nActive subscriptions: {Count}", activeSubscriptions.Count);
        
        // 清理
        logger.LogInformation("\nCleaning up...");
        await subscriptionManager.UnsubscribeAsync(subscription);
        logger.LogInformation("Subscription unsubscribed");
    }
}

