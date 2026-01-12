using Aevatar.Agents;
using Aevatar.Agents.Abstractions.Attributes;
using Aevatar.Agents.Core;
using Microsoft.Extensions.Logging;

namespace Demo.Agents;

// Stream processing agent
public class StreamProcessorAgent : GAgentBase<StreamState>
{
    private int _messageCount = 0;
    
    [EventHandler]
    public Task HandleStreamMessage(StreamMessage message)
    {
        _messageCount++;
        Logger?.LogInformation("StreamProcessor {Id} received message #{Count}: {Content}", Id, _messageCount, message.Content);
        State.MessagesProcessed++;
        State.LastMessageTime = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTimeOffset(DateTimeOffset.UtcNow);
        return Task.CompletedTask;
    }
    
    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult($"StreamProcessor {Id}: Processed {_messageCount} messages");
    }
}

// StreamState is defined in demo_messages.proto

// Publisher agent
public class PublisherAgent : GAgentBase<PublisherState>
{
    
    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult($"Publisher {Id}: Published {State.MessagesPublished} messages");
    }
    
    [EventHandler]
    public Task HandlePublishEvent(PublishMessage publishMsg)
    {
        State.MessagesPublished++;
        Logger?.LogInformation("Publisher {Id} publishing message #{Count} to topic {Topic}", 
            Id, State.MessagesPublished, publishMsg.Topic);
        return Task.CompletedTask;
    }
}

// PublisherState 已在 demo_messages.proto 中定义

// 订阅者Agent
public class SubscriberAgent : GAgentBase<SubscriberState>
{
    
    [AllEventHandler]
    public Task HandleSubscribedMessage(EventEnvelope envelope)
    {
        State.MessagesReceived++;
        Logger?.LogInformation("Subscriber {Id} received message #{Count}", Id, State.MessagesReceived);
        return Task.CompletedTask;
    }
    
    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult($"Subscriber {Id}: Received {State.MessagesReceived} messages");
    }
}

// SubscriberState 已在 demo_messages.proto 中定义
