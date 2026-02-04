using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.Attributes;
using Aevatar.Agents.Abstractions.Extensions;
using Aevatar.Agents.Abstractions.Helpers;
using Aevatar.Agents.Core;
using Aevatar.Agents.Core.Factory;
using Aevatar.Agents.Core.Extensions;
using Aevatar.Agents.Core.Hierarchy;
using Aevatar.Agents.Runtime.Orleans;
using Aevatar.Agents.TestBase;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orleans.Streams;
using Xunit;

namespace Aevatar.Agents.Orleans.Tests;

/// <summary>
/// Tests for Orleans GAgent Actor Factory
/// Focuses on factory functionality and Orleans integration
/// </summary>
public class OrleansActorFactoryTests : AevatarAgentsTestBase
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IClusterClient _clusterClient;
    private readonly IStreamProvider _streamProvider;

    public OrleansActorFactoryTests(ClusterFixture fixture) : base(fixture)
    {
        _serviceProvider = ServiceProvider;
        _clusterClient = ClusterClient;
        _streamProvider = ServiceProvider.GetService<IStreamProvider>()!;
    }

    private OrleansGAgentActorFactory CreateFactory(out IServiceProvider serviceProvider)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IGAgentFactory, DefaultGAgentFactory>();
        services.AddGAgentActorFactoryProvider(); // Use auto-discovery

        serviceProvider = services.BuildServiceProvider();
        var logger = serviceProvider.GetRequiredService<ILogger<OrleansGAgentActorFactory>>();

        return new OrleansGAgentActorFactory(
            serviceProvider,
            _clusterClient,
            logger);
    }

    [Fact]
    public async Task Factory_Should_Create_Actor_With_Orleans_Grain()
    {
        // Arrange
        var factory = CreateFactory(out _);

        var agentId = Guid.NewGuid().ToString();

        // Act
        var actor = await factory.CreateGAgentActorAsync<OrleansTestAgent>(agentId);

        // Assert
        Assert.NotNull(actor);
        Assert.IsAssignableFrom<IGAgentActor>(actor);
        Assert.IsType<OrleansGAgentActor>(actor);
        Assert.Equal(AgentId.Normalize<OrleansTestAgent>(agentId), actor.Id);
    }

    [Fact]
    public async Task Factory_Should_Create_Actor_With_Single_Generic_Parameter()
    {
        // Arrange
        var factory = CreateFactory(out _);

        var agentId = Guid.NewGuid().ToString();

        // Act
        var actor = await factory.CreateGAgentActorAsync<OrleansTestAgent>(agentId);

        // Assert
        Assert.NotNull(actor);
        Assert.IsAssignableFrom<IGAgentActor>(actor);
        Assert.Equal(AgentId.Normalize<OrleansTestAgent>(agentId), actor.Id);

        // In new architecture, Agent runs in Silo (Grain)
        // GetAgent() is not available on client-side actor
        // Verify actor can get description through RPC
        var description = await actor.GetDescriptionAsync();
        Assert.NotNull(description);
    }

    [Fact]
    public async Task Factory_Created_Actors_Should_Support_Hierarchical_Relationships()
    {
        // Arrange
        var factory = CreateFactory(out _);

        var parentId = Guid.NewGuid().ToString();
        var childId = Guid.NewGuid().ToString();

        // Act
        var parent = await factory.CreateGAgentActorAsync<OrleansTestAgent>(parentId);
        var child = await factory.CreateGAgentActorAsync<OrleansTestAgent>(childId);

        await ActorHierarchyCoordinator.LinkAsync(parent, child);

        // Assert
        var childrenIds = await parent.GetChildrenAsync();
        var parentFromChild = await child.GetParentAsync();

        Assert.Contains(child.Id, childrenIds);
        Assert.Equal(parent.Id, parentFromChild);
    }

    [Fact]
    public async Task Factory_Created_Actors_Should_Support_Event_Publishing()
    {
        // Arrange
        var factory = CreateFactory(out _);

        var agentId = Guid.NewGuid().ToString();
        var actor = await factory.CreateGAgentActorAsync<OrleansTestAgent>(agentId);

        // Act - Publish events with different directions (should not throw)
        var testEvent = new StringValue { Value = "test message" };

        var eventId1 = await actor.PublishEventAsync(testEvent, EventDirection.Down);
        var eventId2 = await actor.PublishEventAsync(testEvent, EventDirection.Up);
        var eventId3 = await actor.PublishEventAsync(testEvent, EventDirection.Both);

        // Assert
        Assert.NotNull(eventId1);
        Assert.NotNull(eventId2);
        Assert.NotNull(eventId3);
        Assert.NotEqual(eventId1, eventId2);
        Assert.NotEqual(eventId2, eventId3);
    }

    [Fact]
    public async Task Multiple_Actors_Should_Work_Independently()
    {
        // Arrange
        var factory = CreateFactory(out _);

        var agentId1 = Guid.NewGuid().ToString();
        var agentId2 = Guid.NewGuid().ToString();

        // Act
        var actor1 = await factory.CreateGAgentActorAsync<OrleansTestAgent>(agentId1);
        var actor2 = await factory.CreateGAgentActorAsync<OrleansTestAgent>(agentId2);

        // Add different children to each
        var child1 = Guid.NewGuid().ToString();
        var child2 = Guid.NewGuid().ToString();
        var child3 = Guid.NewGuid().ToString();

        var childActor1 = await factory.CreateGAgentActorAsync<OrleansTestAgent>(child1);
        var childActor2 = await factory.CreateGAgentActorAsync<OrleansTestAgent>(child2);
        var childActor3 = await factory.CreateGAgentActorAsync<OrleansTestAgent>(child3);

        await ActorHierarchyCoordinator.LinkAsync(actor1, childActor1);
        await ActorHierarchyCoordinator.LinkAsync(actor2, childActor2);
        await ActorHierarchyCoordinator.LinkAsync(actor2, childActor3);

        // Assert - Each actor maintains its own state
        var children1 = await actor1.GetChildrenAsync();
        var children2 = await actor2.GetChildrenAsync();

        Assert.Single(children1);
        Assert.Contains(childActor1.Id, children1);

        Assert.Equal(2, children2.Count);
        Assert.Contains(childActor2.Id, children2);
        Assert.Contains(childActor3.Id, children2);
    }

    [Fact]
    public async Task Factory_Should_Handle_Concurrent_Creation()
    {
        // Arrange
        var factory = CreateFactory(out _);

        // Act - Create multiple actors concurrently
        var tasks = new Task<IGAgentActor>[5];
        for (int i = 0; i < 5; i++)
        {
            var id = Guid.NewGuid().ToString();
            tasks[i] = factory.CreateGAgentActorAsync<OrleansTestAgent>(id);
        }

        var actors = await Task.WhenAll(tasks);

        // Assert - All actors should be created successfully
        Assert.Equal(5, actors.Length);
        foreach (var actor in actors)
        {
            Assert.NotNull(actor);
            Assert.IsType<OrleansGAgentActor>(actor);
        }

        // Verify all actors have unique IDs
        var ids = new HashSet<string>();
        foreach (var actor in actors)
        {
            Assert.True(ids.Add(actor.Id), $"Duplicate actor ID found: {actor.Id}");
        }
    }

    // ============ RPC Tests ============

    [Fact]
    public async Task RPC_Should_Invoke_Agent_Methods_Via_Interface()
    {
        // Arrange
        var factory = CreateFactory(out _);
        var agentId = Guid.NewGuid().ToString();
        var actor = await factory.CreateGAgentActorAsync<OrleansRpcTestAgent>(agentId);

        // Act - Use type-safe proxy
        var proxy = actor.As<IOrleansRpcTestAgent>();
        await proxy.SetMessageAsync("Hello Orleans RPC!");
        await proxy.IncrementAsync(42);

        var message = await proxy.GetMessageAsync();
        var count = await proxy.GetCountAsync();

        // Assert
        Assert.Equal("Hello Orleans RPC!", message);
        Assert.Equal(42, count);
    }

    [Fact]
    public async Task RPC_Should_Handle_Multiple_Calls()
    {
        // Arrange
        var factory = CreateFactory(out _);
        var agentId = Guid.NewGuid().ToString();
        var actor = await factory.CreateGAgentActorAsync<OrleansRpcTestAgent>(agentId);

        // Act - Multiple increments via proxy
        var proxy = actor.As<IOrleansRpcTestAgent>();
        await proxy.IncrementAsync(10);
        await proxy.IncrementAsync(20);
        await proxy.IncrementAsync(30);

        var finalCount = await proxy.GetCountAsync();

        // Assert
        Assert.Equal(60, finalCount);
    }

    [Fact]
    public async Task RPC_Should_Reject_Non_Existent_Methods()
    {
        // Arrange
        var factory = CreateFactory(out _);
        var agentId = Guid.NewGuid().ToString();
        var actor = await factory.CreateGAgentActorAsync<OrleansRpcTestAgent>(agentId);

        // Act & Assert - Non-existent method should throw (using dynamic API)
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => actor.InvokeAsync("SomeNonExistentMethod"));

        Assert.Contains("RPC call", exception.Message);
    }

    [Fact]
    public async Task RPC_Should_Handle_Concurrent_Calls()
    {
        // Arrange
        var factory = CreateFactory(out _);
        var agentId = Guid.NewGuid().ToString();
        var actor = await factory.CreateGAgentActorAsync<OrleansRpcTestAgent>(agentId);

        // Act - Concurrent RPC calls via proxy
        var proxy = actor.As<IOrleansRpcTestAgent>();
        var tasks = new List<Task>();
        for (int i = 0; i < 10; i++)
        {
            tasks.Add(proxy.IncrementAsync(1));
        }
        await Task.WhenAll(tasks);

        var finalCount = await proxy.GetCountAsync();

        // Assert
        Assert.Equal(10, finalCount);
    }
}

// Test Agent Implementation for Orleans
public class OrleansTestAgent : GAgentBase<OrleansTestState>
{

    // Default constructor
    public OrleansTestAgent() : base()
    {
    }

    // Constructor with ID parameter for dependency injection
    public OrleansTestAgent(string id) : base(id)
    {
    }

    public Task InitializeAsync()
    {
        GetState().IsInitialized = true;
        return Task.CompletedTask;
    }

    public Task<string> ProcessAsync(IMessage message)
    {
        GetState().ProcessedCount++;
        return Task.FromResult($"Processed by Orleans agent {Id}");
    }

    public new OrleansTestState GetState()
    {
        return base.GetState();
    }

    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult($"OrleansTestAgent {Id}");
    }

    [EventHandler]
    public Task HandleTestEvent(StringValue message)
    {
        var state = GetState();
        state.LastMessage = message.Value;
        state.EventCount++;
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        GetState().IsDisposed = true;
    }
}

/// <summary>
/// RPC-enabled test agent with interface
/// </summary>
public interface IOrleansRpcTestAgent : IGAgent
{
    Task<int> GetCountAsync();
    Task IncrementAsync(int amount);
    Task<string> GetMessageAsync();
    Task SetMessageAsync(string message);
}

public class OrleansRpcTestAgent : GAgentBase<OrleansTestState>, IOrleansRpcTestAgent
{
    public OrleansRpcTestAgent() : base() { }
    public OrleansRpcTestAgent(string id) : base(id) { }

    public Task<int> GetCountAsync() => Task.FromResult(State.ProcessedCount);
    
    public Task IncrementAsync(int amount)
    {
        State.ProcessedCount += amount;
        return Task.CompletedTask;
    }

    public Task<string> GetMessageAsync() => Task.FromResult(State.LastMessage ?? "");
    
    public Task SetMessageAsync(string message)
    {
        State.LastMessage = message;
        return Task.CompletedTask;
    }

    public override Task<string> GetDescriptionAsync()
        => Task.FromResult($"OrleansRpcTestAgent {Id}: Count={State.ProcessedCount}, Message={State.LastMessage}");
}