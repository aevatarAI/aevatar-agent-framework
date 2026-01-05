using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.Extensions;
using Aevatar.Agents.Abstractions.Rpc;
using Aevatar.Agents.AGUI;
using Aevatar.Agents.AI;
using Aevatar.Agents.Rpc;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Aevatar.Agents.AGUI.Tests;

public class AgUiBootstrapTests
{
    // ============================================================
    //  Minimal fakes (no runtime, no DI, no network)
    //
    //  We only implement the surface needed by AgUiBootstrap:
    //  - IGAgentActorManager.GetActorAsync
    //  - IGAgentActor.InvokeRpcAsync (for "GetState")
    //
    //  Everything else throws to make unintended coupling obvious.
    // ============================================================

    private sealed class FakeActorManager : IGAgentActorManager
    {
        private readonly Dictionary<string, IGAgentActor> _actors = new(StringComparer.Ordinal);

        public FakeActorManager Add(IGAgentActor actor)
        {
            _actors[actor.Id] = actor;
            return this;
        }

        public Task<IGAgentActor?> GetActorAsync(string id)
            => Task.FromResult(_actors.TryGetValue(id, out var a) ? a : null);

        public Task<IGAgentActor> CreateAndRegisterAsync<TAgent>(string id, CancellationToken ct = default)
            where TAgent : IGAgent => throw new NotSupportedException();

        public Task<IReadOnlyList<IGAgentActor>> CreateBatchAsync<TAgent>(IEnumerable<string> ids,
            CancellationToken ct = default) where TAgent : IGAgent => throw new NotSupportedException();

        public Task DeactivateAndUnregisterAsync(string id, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task DeactivateBatchAsync(IEnumerable<string> ids, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task DeactivateAllAsync(CancellationToken ct = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<IGAgentActor>> GetActorsAsync(IEnumerable<string> ids)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<IGAgentActor>> GetAllActorsAsync() => throw new NotSupportedException();

        public Task<IReadOnlyList<IGAgentActor>> GetActorsByTypeAsync<TAgent>() where TAgent : IGAgent
            => throw new NotSupportedException();

        public Task<IReadOnlyList<IGAgentActor>> GetActorsByTypeNameAsync(string typeName)
            => throw new NotSupportedException();

        public Task<bool> ExistsAsync(string id) => Task.FromResult(_actors.ContainsKey(id));

        public Task<int> GetCountAsync() => Task.FromResult(_actors.Count);

        public Task<int> GetCountByTypeAsync<TAgent>() where TAgent : IGAgent => throw new NotSupportedException();

        public Task LinkParentChildAsync(string parentId, string childId, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task UnlinkParentChildAsync(string childId, string? parentId = null, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<ActorHealthStatus> GetHealthStatusAsync(string id) => throw new NotSupportedException();

        public Task<ActorManagerStatistics> GetStatisticsAsync() => throw new NotSupportedException();
    }

    private sealed class FakeActor(string id, AevatarAIAgentState state) : IGAgentActor
    {
        private readonly AevatarAIAgentState _state = state;

        public string Id { get; } = id;

        public IGAgent GetAgent() => throw new NotSupportedException();

        public Task<string> GetDescriptionAsync() => Task.FromResult($"fake-actor:{Id}");

        public Task<IReadOnlyList<string>> GetChildrenAsync() => Task.FromResult((IReadOnlyList<string>)Array.Empty<string>());

        public Task<string?> GetParentAsync() => Task.FromResult<string?>(null);

        public Task HandleEventAsync(EventEnvelope envelope, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task ActivateAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task DeactivateAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task<string> PublishEventAsync<TEvent>(TEvent evt, EventDirection direction = EventDirection.Down,
            CancellationToken ct = default, bool isInternalCall = false) where TEvent : IMessage
            => Task.FromResult(string.Empty);

        public Task<string> SendToAsync<TEvent>(string targetAgentId, TEvent evt,
            EventDirection onArrivalDirection = EventDirection.Unspecified, CancellationToken ct = default,
            bool isInternalCall = false) where TEvent : IMessage
            => Task.FromResult(string.Empty);

        public Task<byte[]> InvokeRpcAsync(byte[] requestBytes)
        {
            var req = RpcRequest.Parser.ParseFrom(requestBytes);

            if (string.Equals(req.MethodName, "GetState", StringComparison.Ordinal))
            {
                var ok = new RpcResponse
                {
                    Success = true,
                    Result = ProtobufPacker.Pack(_state),
                    CorrelationId = req.CorrelationId ?? string.Empty
                };
                return Task.FromResult(ok.ToByteArray());
            }

            var bad = new RpcResponse
            {
                Success = false,
                Error = new RpcError { ErrorType = "NotSupported", Message = $"Method '{req.MethodName}' not supported" },
                CorrelationId = req.CorrelationId ?? string.Empty
            };
            return Task.FromResult(bad.ToByteArray());
        }
    }

    [Fact]
    public async Task CollectAssistantMessagesAsync_ShouldReturnEmpty_WhenThreadIdMissing()
    {
        var manager = new FakeActorManager();
        var actors = new[]
        {
            new AgUiActor { ActorId = "a1", LaneId = "lane-1" }
        };

        var options = new AgUiMessageSnapshotOptions { ThreadId = "   " };

        var messages = await AgUiBootstrap.CollectAssistantMessagesAsync(manager, actors, options);
        messages.ShouldBeEmpty();
    }

    [Fact]
    public async Task CollectAssistantMessagesAsync_ShouldReturnEmpty_WhenMaxAssistantMessagesNonPositive()
    {
        var state = new AevatarAIAgentState();
        state.History.Add(new AevatarChatMessage
        {
            Role = AevatarChatRole.Assistant,
            Content = "hello",
            Timestamp = Timestamp.FromDateTimeOffset(DateTimeOffset.UtcNow),
            Metadata = { ["step_id"] = "s1" }
        });

        var manager = new FakeActorManager().Add(new FakeActor("a1", state));

        var messages = await AgUiBootstrap.CollectAssistantMessagesAsync(
            manager,
            new[] { new AgUiActor { ActorId = "a1", LaneId = "lane-1" } },
            new AgUiMessageSnapshotOptions { ThreadId = "thread-0", MaxAssistantMessages = 0 });

        messages.ShouldBeEmpty();
    }

    [Fact]
    public async Task CollectAssistantMessagesAsync_ShouldIgnoreAssistant_WhenStepIdMissing()
    {
        var state = new AevatarAIAgentState();
        state.History.Add(new AevatarChatMessage
        {
            Role = AevatarChatRole.Assistant,
            Content = "no-step-id",
            Timestamp = Timestamp.FromDateTimeOffset(DateTimeOffset.UtcNow)
        });

        var manager = new FakeActorManager().Add(new FakeActor("a1", state));

        var messages = await AgUiBootstrap.CollectAssistantMessagesAsync(
            manager,
            new[] { new AgUiActor { ActorId = "a1", LaneId = "lane-1" } },
            new AgUiMessageSnapshotOptions { ThreadId = "thread-1", MaxAssistantMessages = 60 });

        messages.ShouldBeEmpty();
    }

    [Fact]
    public async Task CollectAssistantMessagesAsync_ShouldTreatSameStepIdAcrossDifferentLanes_AsDifferentMessages()
    {
        var ts = Timestamp.FromDateTimeOffset(DateTimeOffset.UtcNow);

        var s1 = new AevatarAIAgentState();
        s1.History.Add(new AevatarChatMessage
        {
            Role = AevatarChatRole.Assistant,
            Content = "lane-a",
            Timestamp = ts,
            Metadata = { ["step_id"] = "s1" }
        });

        var s2 = new AevatarAIAgentState();
        s2.History.Add(new AevatarChatMessage
        {
            Role = AevatarChatRole.Assistant,
            Content = "lane-b",
            Timestamp = ts,
            Metadata = { ["step_id"] = "s1" }
        });

        var manager = new FakeActorManager()
            .Add(new FakeActor("a1", s1))
            .Add(new FakeActor("a2", s2));

        var messages = await AgUiBootstrap.CollectAssistantMessagesAsync(
            manager,
            new[]
            {
                new AgUiActor { ActorId = "a1", LaneId = "lane-a" },
                new AgUiActor { ActorId = "a2", LaneId = "lane-b" }
            },
            new AgUiMessageSnapshotOptions { ThreadId = "thread-x", MaxAssistantMessages = 60 });

        messages.Count.ShouldBe(2);
        messages.Any(m => m.Id == "msg:thread-x:lane-a:s1" && m.Content == "lane-a").ShouldBeTrue();
        messages.Any(m => m.Id == "msg:thread-x:lane-b:s1" && m.Content == "lane-b").ShouldBeTrue();
    }

    [Fact]
    public async Task CollectAssistantMessagesAsync_ShouldSelectLatestAssistantPerStep_AndOrderByTimestamp()
    {
        var t0 = DateTimeOffset.UtcNow.AddMinutes(-10);
        var t1 = DateTimeOffset.UtcNow.AddMinutes(-5);
        var t2 = DateTimeOffset.UtcNow.AddMinutes(-1);

        var state = new AevatarAIAgentState();
        state.History.Add(new AevatarChatMessage
        {
            Role = AevatarChatRole.User,
            Content = "ignored-user",
            Timestamp = Timestamp.FromDateTimeOffset(t0),
            Metadata = { ["step_id"] = "s1" }
        });
        state.History.Add(new AevatarChatMessage
        {
            Role = AevatarChatRole.Assistant,
            Content = "old-answer",
            Timestamp = Timestamp.FromDateTimeOffset(t0),
            Metadata = { ["step_id"] = "s1" }
        });
        state.History.Add(new AevatarChatMessage
        {
            Role = AevatarChatRole.Assistant,
            Content = "new-answer",
            Timestamp = Timestamp.FromDateTimeOffset(t2),
            Metadata = { ["step_id"] = "s1" }
        });
        state.History.Add(new AevatarChatMessage
        {
            Role = AevatarChatRole.Assistant,
            Content = "another-step",
            Timestamp = Timestamp.FromDateTimeOffset(t1),
            Metadata = { ["step_id"] = "s2" }
        });

        var actor = new FakeActor("a1", state);
        var manager = new FakeActorManager().Add(actor);

        // Guard: our fake RPC must roundtrip state correctly.
        var roundtrip = await actor.InvokeAsync<AevatarAIAgentState>("GetState");
        roundtrip.History.Count.ShouldBe(state.History.Count);

        var messages = await AgUiBootstrap.CollectAssistantMessagesAsync(
            manager,
            new[] { new AgUiActor { ActorId = "a1", LaneId = "lane-1" } },
            new AgUiMessageSnapshotOptions { ThreadId = "thread-1", MaxAssistantMessages = 60 });

        messages.Count.ShouldBe(2);
        messages[0].Content.ShouldBe("another-step"); // t1
        messages[1].Content.ShouldBe("new-answer");   // t2, latest for s1

        messages[0].Id.ShouldBe("msg:thread-1:lane-1:s2");
        messages[1].Id.ShouldBe("msg:thread-1:lane-1:s1");
    }

    [Fact]
    public async Task CollectAssistantMessagesAsync_ShouldRespectLaneResolver_AndMaxTailWindow()
    {
        var t0 = DateTimeOffset.UtcNow.AddMinutes(-3);
        var t1 = DateTimeOffset.UtcNow.AddMinutes(-2);
        var t2 = DateTimeOffset.UtcNow.AddMinutes(-1);

        var state = new AevatarAIAgentState();
        state.History.Add(new AevatarChatMessage
        {
            Role = AevatarChatRole.Assistant,
            Content = "m0",
            Timestamp = Timestamp.FromDateTimeOffset(t0),
            Metadata = { ["step_id"] = "s0" }
        });
        state.History.Add(new AevatarChatMessage
        {
            Role = AevatarChatRole.Assistant,
            Content = "m1",
            Timestamp = Timestamp.FromDateTimeOffset(t1),
            Metadata = { ["step_id"] = "s1" }
        });
        state.History.Add(new AevatarChatMessage
        {
            Role = AevatarChatRole.Assistant,
            Content = "m2",
            Timestamp = Timestamp.FromDateTimeOffset(t2),
            Metadata = { ["step_id"] = "s2", ["lane"] = "lane-x" }
        });

        var actor = new FakeActor("a1", state);
        var manager = new FakeActorManager().Add(actor);

        // Guard: our fake RPC must roundtrip state correctly.
        var roundtrip = await actor.InvokeAsync<AevatarAIAgentState>("GetState");
        roundtrip.History.Count.ShouldBe(state.History.Count);

        var options = new AgUiMessageSnapshotOptions
        {
            ThreadId = "thread-2",
            MaxAssistantMessages = 2,
            ResolveLaneId = (stepId, meta, defaultLaneId) =>
                meta.TryGetValue("lane", out var lane) && !string.IsNullOrWhiteSpace(lane) ? lane : defaultLaneId
        };

        var messages = await AgUiBootstrap.CollectAssistantMessagesAsync(
            manager,
            new[] { new AgUiActor { ActorId = "a1", LaneId = "lane-default" } },
            options);

        // Keep last 2 by timestamp: m1, m2
        messages.Count.ShouldBe(2);
        messages[0].Id.ShouldBe("msg:thread-2:lane-default:s1");
        messages[1].Id.ShouldBe("msg:thread-2:lane-x:s2");
    }

    [Fact]
    public async Task CollectAssistantMessagesAsync_ShouldTryOrleansStyleId_First()
    {
        var state = new AevatarAIAgentState();
        state.History.Add(new AevatarChatMessage
        {
            Role = AevatarChatRole.Assistant,
            Content = "ok",
            Timestamp = Timestamp.FromDateTimeOffset(DateTimeOffset.UtcNow),
            Metadata = { ["step_id"] = "s1" }
        });

        // Stored as Orleans-style: "{TypeName}:{RawId}"
        var actor = new FakeActor("MyAgent:raw-1", state);
        var manager = new FakeActorManager().Add(actor);

        // Guard: our fake RPC must roundtrip state correctly.
        var roundtrip = await actor.InvokeAsync<AevatarAIAgentState>("GetState");
        roundtrip.History.Count.ShouldBe(state.History.Count);

        var actors = new[]
        {
            new AgUiActor { ActorId = "raw-1", ActorTypeName = "MyAgent", LaneId = "lane-1" }
        };

        var messages = await AgUiBootstrap.CollectAssistantMessagesAsync(
            manager,
            actors,
            new AgUiMessageSnapshotOptions { ThreadId = "thread-3" });

        messages.Single().Content.ShouldBe("ok");
    }

    [Fact]
    public async Task CollectAssistantMessagesAsync_ShouldUseCreateAsync_WhenActorMissing()
    {
        var state = new AevatarAIAgentState();
        state.History.Add(new AevatarChatMessage
        {
            Role = AevatarChatRole.Assistant,
            Content = "created",
            Timestamp = Timestamp.FromDateTimeOffset(DateTimeOffset.UtcNow),
            Metadata = { ["step_id"] = "s1" }
        });

        var manager = new FakeActorManager();

        var actors = new[]
        {
            new AgUiActor
            {
                ActorId = "raw-2",
                ActorTypeName = "MyAgent",
                LaneId = "lane-1",
                CreateAsync = (_, _) => Task.FromResult<IGAgentActor?>(new FakeActor("MyAgent:raw-2", state))
            }
        };

        var messages = await AgUiBootstrap.CollectAssistantMessagesAsync(
            manager,
            actors,
            new AgUiMessageSnapshotOptions { ThreadId = "thread-4" });

        messages.Single().Content.ShouldBe("created");
    }
}


