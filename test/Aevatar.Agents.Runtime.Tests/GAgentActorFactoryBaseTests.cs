using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.Helpers;
using Aevatar.Agents.Core.Factory;
using Google.Protobuf;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Aevatar.Agents.Runtime.Tests;

public class GAgentActorFactoryBaseTests
{
    private sealed class TestAgent(string id) : IGAgent
    {
        public string Id { get; } = id;

        public string GetAgentCategory() => nameof(TestAgent);

        public Task<string> GetDescriptionAsync() => Task.FromResult("test-agent");

        public Task<List<Type>> GetAllSubscribedEventsAsync(bool includeAllEventHandler = false)
            => Task.FromResult(new List<Type>());

        public Task ActivateAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task DeactivateAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task HandleEventAsync(EventEnvelope envelope, CancellationToken ct = default)
            => Task.CompletedTask;
    }

    private sealed class FakeActor(string id) : IGAgentActor
    {
        public string Id { get; } = id;

        public IGAgent GetAgent() => throw new NotSupportedException();
        public Task<string> GetDescriptionAsync() => Task.FromResult($"fake-actor:{Id}");
        public Task<IReadOnlyList<string>> GetChildrenAsync() => Task.FromResult((IReadOnlyList<string>)Array.Empty<string>());
        public Task<string?> GetParentAsync() => Task.FromResult<string?>(null);
        public Task HandleEventAsync(EventEnvelope envelope, CancellationToken ct = default) => Task.CompletedTask;
        public Task ActivateAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task DeactivateAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task<byte[]> InvokeRpcAsync(byte[] requestBytes) => Task.FromResult(Array.Empty<byte>());
        public Task<string> PublishEventAsync<TEvent>(TEvent evt, EventDirection direction = EventDirection.Down,
            CancellationToken ct = default, bool isInternalCall = false) where TEvent : IMessage
            => Task.FromResult(string.Empty);
        public Task<string> SendToAsync<TEvent>(string targetAgentId, TEvent evt,
            EventDirection onArrivalDirection = EventDirection.Unspecified, CancellationToken ct = default,
            bool isInternalCall = false) where TEvent : IMessage
            => Task.FromResult(string.Empty);
    }

    private sealed class SingleFactoryProvider(
        Type targetType,
        Func<IGAgentActorFactory, string, CancellationToken, Task<IGAgentActor>> factory) : IGAgentActorFactoryProvider
    {
        public void RegisterFactory<TAgent>(Func<IGAgentActorFactory, string, CancellationToken, Task<IGAgentActor>> factory)
            where TAgent : IGAgent => throw new NotSupportedException();

        public void RegisterFactory(Type agentType, Func<IGAgentActorFactory, string, CancellationToken, Task<IGAgentActor>> factory)
            => throw new NotSupportedException();

        public Func<IGAgentActorFactory, string, CancellationToken, Task<IGAgentActor>>? GetFactory(Type agentType)
            => agentType == targetType ? factory : null;
    }

    private sealed class NullAgentFactory : IGAgentFactory
    {
        public IGAgent CreateGAgent(string id, Type agentType, CancellationToken ct = default)
            => throw new NotSupportedException();

        public TAgent CreateGAgent<TAgent>(string id, CancellationToken ct = default) where TAgent : IGAgent
            => throw new NotSupportedException();

        public TAgent CreateGAgent<TAgent>(CancellationToken ct = default) where TAgent : IGAgent
            => throw new NotSupportedException();
    }

    private sealed class TestFactory(IServiceProvider sp) : GAgentActorFactoryBase(sp, NullLogger.Instance)
    {
        public bool CreateActorInstanceCalled { get; private set; }

        protected override Task<IGAgentActor> CreateActorInstanceAsync(IGAgent agent, string id, CancellationToken ct = default)
        {
            CreateActorInstanceCalled = true;
            return Task.FromResult<IGAgentActor>(new FakeActor(id));
        }
    }

    [Fact]
    public async Task CreateGAgentActorAsync_ShouldUseCustomFactory_AndReturnNormalizedActorId()
    {
        var capturedActorId = string.Empty;

        Func<IGAgentActorFactory, string, CancellationToken, Task<IGAgentActor>> custom = (_, actorId, _) =>
        {
            capturedActorId = actorId;
            return Task.FromResult<IGAgentActor>(new FakeActor(actorId));
        };

        var sc = new ServiceCollection();
        sc.AddSingleton<IGAgentActorFactoryProvider>(new SingleFactoryProvider(typeof(TestAgent), custom));
        // Intentionally NOT registering IGAgentFactory: custom factory path must not require it.
        var sp = sc.BuildServiceProvider();

        var factory = new TestFactory(sp);

        var actor = await factory.CreateGAgentActorAsync<TestAgent>("raw-123");

        var expected = AgentId.Normalize(typeof(TestAgent), "raw-123");
        capturedActorId.ShouldBe(expected);
        actor.Id.ShouldBe(expected);
        factory.CreateActorInstanceCalled.ShouldBeFalse();
    }

    [Fact]
    public async Task CreateGAgentActorAsync_ShouldThrow_WhenNoIGAgentFactoryRegistered_AndNoCustomFactory()
    {
        var sp = new ServiceCollection().BuildServiceProvider();
        var factory = new TestFactory(sp);

        var ex = await Should.ThrowAsync<InvalidOperationException>(() =>
            factory.CreateGAgentActorAsync<TestAgent>("raw-1"));

        ex.Message.ShouldContain("No IGAgentFactory registered");
    }
}


