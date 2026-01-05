using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.Attributes;
using Aevatar.Agents.AI.Core;
using Aevatar.Agents.Core;
using Aevatar.Agents.Core.Extensions;
using Aevatar.Agents.ProtoActor.Tests.Messages;
using Aevatar.Agents.Runtime.ProtoActor;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Proto;

namespace Aevatar.Agents.ProtoActor.Tests.Streaming;

/// <summary>
/// ProtoActor Stream mechanism tests
/// </summary>
public class ProtoActorStreamTests : IDisposable
{
    private readonly ActorSystem _actorSystem;
    private readonly ProtoActorGAgentActorManager _manager;
    private readonly ProtoActorGAgentActorFactory _factory;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ProtoActorStreamTests> _logger;
    
    public ProtoActorStreamTests()
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => 
            builder.AddConsole().SetMinimumLevel(LogLevel.Debug));
        
        // Setup ProtoActor
        var systemConfig = ActorSystemConfig.Setup();
        _actorSystem = new ActorSystem(systemConfig);
        
        services.AddSingleton(_actorSystem);
        services.AddSingleton<ProtoActorMessageStreamRegistry>();
        services.AddSingleton<ProtoActorGAgentActorFactory>();
        services.AddGAgentActorFactoryProvider();  // Add factory provider
        services.AddSingleton<IGAgentFactory, AIGAgentFactory>();  // Add Agent factory
        
        _serviceProvider = services.BuildServiceProvider();
        _factory = _serviceProvider.GetRequiredService<ProtoActorGAgentActorFactory>();
        
        var loggerFactory = _serviceProvider.GetRequiredService<ILoggerFactory>();
        _logger = loggerFactory.CreateLogger<ProtoActorStreamTests>();
        
        _manager = new ProtoActorGAgentActorManager(
            _factory,
            _actorSystem.Root, 
            loggerFactory.CreateLogger<ProtoActorGAgentActorManager>());
    }
    
    /// <summary>
    /// Test ProtoActor parent-child relationship subscription
    /// </summary>
    [Fact]
    public async Task ProtoActor_Parent_Child_Subscription_Works()
    {
        // Arrange
        var parentId = Guid.NewGuid().ToString();
        var child1Id = Guid.NewGuid().ToString();
        var child2Id = Guid.NewGuid().ToString();
        
        var parentActor = await _manager.CreateAndRegisterAsync<ProtoTestParentAgent>(
            parentId, CancellationToken.None);
        var child1Actor = await _manager.CreateAndRegisterAsync<ProtoTestChildAgent>(
            child1Id, CancellationToken.None);
        var child2Actor = await _manager.CreateAndRegisterAsync<ProtoTestChildAgent>(
            child2Id, CancellationToken.None);
        
        // Act - Establish parent-child relationship (use actor.Id for Manager operations)
        await _manager.LinkParentChildAsync(parentActor.Id, child1Actor.Id);
        await _manager.LinkParentChildAsync(parentActor.Id, child2Actor.Id);
        
        // Child1 sends UP event (should broadcast to all siblings)
        var child1Agent = child1Actor.GetAgent() as ProtoTestChildAgent;
        await child1Agent!.SendUpMessage("Hello from Child1");
        
        // Wait for message propagation
        await Task.Delay(200);
        
        // Assert
        var child2Agent = child2Actor.GetAgent() as ProtoTestChildAgent;
        var parentAgent = parentActor.GetAgent() as ProtoTestParentAgent;
        
        // Verify all nodes received the message
        Assert.Contains("Hello from Child1", child1Agent.ReceivedMessages);
        Assert.Contains("Hello from Child1", child2Agent!.ReceivedMessages);
        Assert.Contains("Hello from Child1", parentAgent!.ReceivedMessages);
    }
    
    /// <summary>
    /// Test ProtoActor message routing
    /// </summary>
    [Fact]
    public async Task ProtoActor_Message_Routing_Works_Correctly()
    {
        // Arrange
        var rootContext = _actorSystem.Root;
        var registry = new ProtoActorMessageStreamRegistry(rootContext);
        
        // Create Actor PIDs
        var parentPid = rootContext.Spawn(Props.FromFunc(ctx => Task.CompletedTask));
        var childPid = rootContext.Spawn(Props.FromFunc(ctx => Task.CompletedTask));
        
        var parentStream = new ProtoActorMessageStream(Guid.NewGuid().ToString(), parentPid, rootContext);
        var childStream = new ProtoActorMessageStream(Guid.NewGuid().ToString(), childPid, rootContext);
        
        var receivedMessages = new List<string>();
        
        // Act - Subscribe to stream
        await parentStream.SubscribeAsync<EventEnvelope>(async envelope =>
        {
            receivedMessages.Add(envelope.Message);
            await Task.CompletedTask;
        });
        
        // Send message
        await parentStream.ProduceAsync(new EventEnvelope 
        { 
            Message = "Proto Message 1",
            Direction = EventDirection.Down 
        });
        
        await Task.Delay(100);
        
        // Assert
        Assert.Contains("Proto Message 1", receivedMessages);
    }
    
    /// <summary>
    /// Test ProtoActor Resume mechanism
    /// </summary>
    [Fact]
    public async Task ProtoActor_Resume_Works_After_Pause()
    {
        // Arrange
        var rootContext = _actorSystem.Root;
        var pid = rootContext.Spawn(Props.FromFunc(ctx => Task.CompletedTask));
        var stream = new ProtoActorMessageStream(Guid.NewGuid().ToString(), pid, rootContext);
        
        var receivedMessages = new List<string>();
        var subscription = await stream.SubscribeAsync<EventEnvelope>(
            async envelope =>
            {
                receivedMessages.Add(envelope.Message);
                await Task.CompletedTask;
            });
        
        // Act
        await stream.ProduceAsync(new EventEnvelope { Message = "Before pause" });
        await Task.Delay(50);
        
        // Pause
        await subscription.UnsubscribeAsync();
        
        await stream.ProduceAsync(new EventEnvelope { Message = "During pause" });
        await Task.Delay(50);
        
        // Resume
        await subscription.ResumeAsync();
        
        await stream.ProduceAsync(new EventEnvelope { Message = "After resume" });
        await Task.Delay(50);
        
        // Assert
        Assert.Contains("Before pause", receivedMessages);
        Assert.DoesNotContain("During pause", receivedMessages); // Messages during pause should not be received
        Assert.Contains("After resume", receivedMessages);
    }
    
    /// <summary>
    /// Test multi-level event propagation
    /// </summary>
    [Fact]
    public async Task ProtoActor_Multi_Level_Propagation_Works()
    {
        // Arrange - Create three-level structure
        var grandparentId = Guid.NewGuid().ToString();
        var parentId = Guid.NewGuid().ToString();
        var childId = Guid.NewGuid().ToString();
        
        var grandparent = await _manager.CreateAndRegisterAsync<ProtoTestParentAgent>(
            grandparentId, CancellationToken.None);
        var parent = await _manager.CreateAndRegisterAsync<ProtoTestParentAgent>(
            parentId, CancellationToken.None);
        var child = await _manager.CreateAndRegisterAsync<ProtoTestChildAgent>(
            childId, CancellationToken.None);
        
        // Establish hierarchy relationship (use actor.Id for Manager operations)
        await _manager.LinkParentChildAsync(grandparent.Id, parent.Id);
        await _manager.LinkParentChildAsync(parent.Id, child.Id);
        
        // Act - child sends UP event
        var childAgent = child.GetAgent() as ProtoTestChildAgent;
        await childAgent!.SendUpMessage("Bubble up from bottom");
        
        await Task.Delay(200);
        
        // Assert - 验证parent收到（通过parent stream广播）
        var parentAgent = parent.GetAgent() as ProtoTestParentAgent;
        Assert.Contains("Bubble up from bottom", parentAgent!.ReceivedMessages);
        
        // parent发送BOTH事件
        await parentAgent.SendBothMessage("Both direction from middle");
        await Task.Delay(200);
        
        // 验证grandparent和child都收到
        var grandparentAgent = grandparent.GetAgent() as ProtoTestParentAgent;
        Assert.Contains("Both direction from middle", grandparentAgent!.ReceivedMessages);
        Assert.Contains("Both direction from middle", childAgent.ReceivedMessages);
    }
    
    public void Dispose()
    {
        // ActorSystem doesn't have traditional disposal
        // Just nullify the reference
    }
}

// 测试用的Agent类
public class ProtoTestParentAgent : GAgentBase<Messages.TestState>
{
    public List<string> ReceivedMessages { get; } = new();
    
    public ProtoTestParentAgent(string id) : base(id) { }
    public ProtoTestParentAgent() : base() { }
    
    public override Task<string> GetDescriptionAsync() => 
        Task.FromResult("Proto Test Parent");
    
    public async Task SendDownMessage(string message)
    {
        await PublishAsync(new ProtoTestEvent { Message = message }, EventDirection.Down);
    }
    
    public async Task SendBothMessage(string message)
    {
        await PublishAsync(new ProtoTestEvent { Message = message }, EventDirection.Both);
    }
    
    [EventHandler(AllowSelfHandling = true)]
    public async Task HandleEvent(ProtoTestEvent evt)
    {
        ReceivedMessages.Add(evt.Message);
        await Task.CompletedTask;
    }
}

public class ProtoTestChildAgent : GAgentBase<Messages.TestState>
{
    public List<string> ReceivedMessages { get; } = new();

    public ProtoTestChildAgent(string id) : base(id)
    {
    }

    public ProtoTestChildAgent() : base()
    {
    }

    public override Task<string> GetDescriptionAsync() =>
        Task.FromResult("Proto Test Child");

    public async Task SendUpMessage(string message)
    {
        await PublishAsync(new ProtoTestEvent { Message = message }, EventDirection.Up);
    }

    [EventHandler(AllowSelfHandling = true)]
    public async Task HandleEvent(ProtoTestEvent evt)
    {
        ReceivedMessages.Add(evt.Message);
        await Task.CompletedTask;
    }
}
