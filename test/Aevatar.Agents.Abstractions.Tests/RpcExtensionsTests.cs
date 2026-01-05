using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.Extensions;
using Aevatar.Agents.Abstractions.Rpc;
using Aevatar.Agents.Rpc;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Shouldly;
using Xunit;

namespace Aevatar.Agents.Abstractions.Tests;

public class RpcExtensionsTests
{
    private interface IDescriptionView
    {
        Task<string> GetDescriptionAsync();
    }

    private interface ICalculator
    {
        Task<int> AddAsync(int a, int b);
        Task PingAsync();
        Task<int> FailAsync();
    }

    private sealed class FakeRpcActor(string id = "FooAgent:raw-1") : IGAgentActor
    {
        public string Id { get; } = id;

        public bool InvokeRpcCalled { get; private set; }

        public List<RpcRequest> CapturedRequests { get; } = new();

        public IGAgent GetAgent() => throw new NotSupportedException();

        public Task<string> GetDescriptionAsync() => Task.FromResult("fake-desc");

        public Task<IReadOnlyList<string>> GetChildrenAsync() => throw new NotSupportedException();

        public Task<string?> GetParentAsync() => throw new NotSupportedException();

        public Task HandleEventAsync(EventEnvelope envelope, CancellationToken ct = default) => throw new NotSupportedException();

        public Task ActivateAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task DeactivateAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task<string> PublishEventAsync<TEvent>(TEvent evt, EventDirection direction = EventDirection.Down,
            CancellationToken ct = default, bool isInternalCall = false) where TEvent : IMessage
            => throw new NotSupportedException();

        public Task<string> SendToAsync<TEvent>(string targetAgentId, TEvent evt,
            EventDirection onArrivalDirection = EventDirection.Unspecified, CancellationToken ct = default,
            bool isInternalCall = false) where TEvent : IMessage
            => throw new NotSupportedException();

        public Task<byte[]> InvokeRpcAsync(byte[] requestBytes)
        {
            InvokeRpcCalled = true;

            var req = RpcRequest.Parser.ParseFrom(requestBytes);
            CapturedRequests.Add(req);

            RpcResponse resp;
            switch (req.MethodName)
            {
                case "Add":
                {
                    var a = ProtobufPacker.Unpack<int>(req.Args[0]);
                    var b = ProtobufPacker.Unpack<int>(req.Args[1]);
                    resp = Ok(req, a + b);
                    break;
                }
                case "VoidOk":
                {
                    resp = new RpcResponse
                    {
                        Success = true,
                        Result = Any.Pack(new Empty()),
                        CorrelationId = req.CorrelationId ?? string.Empty
                    };
                    break;
                }
                case "AddAsync":
                {
                    var a = ProtobufPacker.Unpack<int>(req.Args[0]);
                    var b = ProtobufPacker.Unpack<int>(req.Args[1]);
                    resp = Ok(req, a + b);
                    break;
                }
                case "PingAsync":
                {
                    resp = new RpcResponse
                    {
                        Success = true,
                        Result = Any.Pack(new Empty()),
                        CorrelationId = req.CorrelationId ?? string.Empty
                    };
                    break;
                }
                case "FailAsync":
                {
                    resp = new RpcResponse
                    {
                        Success = false,
                        Error = new RpcError { ErrorType = "Boom", Message = "boom" },
                        CorrelationId = req.CorrelationId ?? string.Empty
                    };
                    break;
                }
                default:
                {
                    resp = new RpcResponse
                    {
                        Success = false,
                        Error = new RpcError { ErrorType = "NotSupported", Message = $"Method '{req.MethodName}' not supported" },
                        CorrelationId = req.CorrelationId ?? string.Empty
                    };
                    break;
                }
            }

            return Task.FromResult(resp.ToByteArray());
        }

        private static RpcResponse Ok<T>(RpcRequest req, T result)
        {
            return new RpcResponse
            {
                Success = true,
                Result = ProtobufPacker.Pack(result),
                CorrelationId = req.CorrelationId ?? string.Empty
            };
        }
    }

    [Fact]
    public async Task As_ShouldDelegateActorMethods_Directly_WithoutRpc()
    {
        var actor = new FakeRpcActor();

        var view = actor.As<IDescriptionView>();
        var desc = await view.GetDescriptionAsync();

        desc.ShouldBe("fake-desc");
        actor.InvokeRpcCalled.ShouldBeFalse();
    }

    [Fact]
    public async Task As_ShouldInvokeBusinessMethods_ViaRpc()
    {
        var actor = new FakeRpcActor();

        var calc = actor.As<ICalculator>();
        var sum = await calc.AddAsync(1, 2);

        sum.ShouldBe(3);
        actor.InvokeRpcCalled.ShouldBeTrue();
        actor.CapturedRequests.Any(r => r.MethodName == "AddAsync").ShouldBeTrue();
    }

    [Fact]
    public async Task As_ShouldThrow_WhenRpcReturnsFailure()
    {
        var actor = new FakeRpcActor();
        var calc = actor.As<ICalculator>();

        var ex = await Should.ThrowAsync<InvalidOperationException>(() => calc.FailAsync());
        ex.Message.ShouldContain("FailAsync");
        ex.Message.ShouldContain("boom");
    }

    [Fact]
    public async Task InvokeAsync_Generic_ShouldRoundtripArguments_AndReturnValue()
    {
        var actor = new FakeRpcActor();
        var sum = await actor.InvokeAsync<int>("Add", 5, 7);

        sum.ShouldBe(12);
        actor.CapturedRequests.Last().MethodName.ShouldBe("Add");
    }

    [Fact]
    public async Task InvokeAsync_Void_ShouldSucceed()
    {
        var actor = new FakeRpcActor();
        await actor.InvokeAsync("VoidOk");

        actor.CapturedRequests.Last().MethodName.ShouldBe("VoidOk");
    }

    [Fact]
    public void As_ShouldReject_NonInterfaceTypes()
    {
        var actor = new FakeRpcActor();
        Should.Throw<ArgumentException>(() => actor.As<string>());
    }
}


