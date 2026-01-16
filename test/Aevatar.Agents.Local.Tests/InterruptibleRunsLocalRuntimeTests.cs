using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.Rpc;
using Aevatar.Agents.Core;
using Aevatar.Agents.Runtime.Local;
using Aevatar.Agents.Rpc;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;

namespace Aevatar.Agents.Local.Tests;

public class InterruptibleRunsLocalRuntimeTests
{
    public interface IRunOutputAgent
    {
        Task EmitTicksAsync(int ticks, int delayMs, CancellationToken ct = default);
    }

    private sealed class RunOutputAgent : GAgentBase<LLMAgentState>, IRunOutputAgent
    {
        public RunOutputAgent()
        {
        }

        public RunOutputAgent(string id) : base(id)
        {
        }

        public override Task<string> GetDescriptionAsync() => Task.FromResult("run-output-agent");

        public async Task EmitTicksAsync(int ticks, int delayMs, CancellationToken ct = default)
        {
            ticks = Math.Clamp(ticks, 0, 10_000);
            delayMs = Math.Clamp(delayMs, 0, 5_000);

            for (var i = 0; i < ticks; i++)
            {
                ct.ThrowIfCancellationRequested();

                await PublishAsync(new StringValue { Value = $"tick:{i}" }, ct: ct);

                if (delayMs > 0)
                    await Task.Delay(delayMs, ct);
            }
        }
    }

    [Fact]
    public async Task NewRun_ShouldCancelPreviousRun_AndStopFurtherPublishedOutput()
    {
        // Arrange
        var registry = new LocalMessageStreamRegistry();

        var agentId = Guid.NewGuid().ToString("N");
        var agent = new RunOutputAgent(agentId);

        var actor = new LocalGAgentActor(agent, registry);
        await actor.ActivateAsync();

        var received = new List<string>();
        var receivedLock = new object();

        var stream = registry.GetOrCreateStream(agentId);
        var sub = await stream.SubscribeAsync<EventEnvelope>(
            envelope =>
            {
                if (!envelope.Payload.Is(StringValue.Descriptor))
                    return Task.CompletedTask;

                var v = envelope.Payload.Unpack<StringValue>().Value ?? string.Empty;
                lock (receivedLock)
                {
                    received.Add(v);
                }

                return Task.CompletedTask;
            },
            null,
            CancellationToken.None);

        // Helper to invoke RPC with metadata (run_id/run_scope_id).
        async Task<RpcResponse> InvokeAsync(string runId, int ticks, int delayMs)
        {
            var req = new RpcRequest
            {
                MethodName = nameof(IRunOutputAgent.EmitTicksAsync),
                CorrelationId = Guid.NewGuid().ToString("N")
            };
            req.Metadata["run_id"] = runId;
            req.Metadata["run_scope_id"] = agentId;
            req.Args.Add(ProtobufPacker.Pack(ticks));
            req.Args.Add(ProtobufPacker.Pack(delayMs));

            var bytes = await actor.InvokeRpcAsync(req.ToByteArray());
            return RpcResponse.Parser.ParseFrom(bytes);
        }

        // Act
        var runA = "runA";
        var runB = "runB";

        var taskA = InvokeAsync(runA, ticks: 1000, delayMs: 10);

        // Wait until we see some output from runA.
        await Task.Delay(80);

        // Start runB: should cancel runA (latest-wins) via runtime binding.
        var respB = await InvokeAsync(runB, ticks: 1, delayMs: 0);
        Assert.True(respB.Success, respB.Error?.Message ?? "runB failed");

        // Let cancellation propagate.
        await Task.Delay(120);

        var respA = await taskA;
        // runA is expected to be canceled (RPC may report failure).
        Assert.False(respA.Success);

        int count1;
        int count2;
        lock (receivedLock)
        {
            count1 = received.Count;
        }

        // Wait a bit more and ensure runA doesn't keep producing output.
        await Task.Delay(120);
        lock (receivedLock)
        {
            count2 = received.Count;
        }

        // Assert
        Assert.True(count1 > 0);
        Assert.Equal(count1, count2);

        await sub.UnsubscribeAsync();
        await actor.DeactivateAsync();
    }
}


