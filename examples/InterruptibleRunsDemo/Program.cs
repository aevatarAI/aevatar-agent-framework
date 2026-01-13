using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.Rpc;
using Aevatar.Agents;
using Aevatar.Agents.Core;
using Aevatar.Agents.Core.Runtime;
using Aevatar.Agents.Runtime.Local;
using Aevatar.Agents.Rpc;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;

namespace InterruptibleRunsDemo;

public static class Program
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

        public override Task<string> GetDescriptionAsync() => Task.FromResult("InterruptibleRunsDemo:RunOutputAgent");

        public async Task EmitTicksAsync(int ticks, int delayMs, CancellationToken ct = default)
        {
            ticks = Math.Clamp(ticks, 0, 10_000);
            delayMs = Math.Clamp(delayMs, 0, 5_000);

            for (var i = 0; i < ticks; i++)
            {
                ct.ThrowIfCancellationRequested();

                var runId = RunContextScope.Value?.RunId ?? "no-run";
                await PublishAsync(new StringValue { Value = $"{runId}:tick:{i}" }, ct: ct);

                if (delayMs > 0)
                    await Task.Delay(delayMs, ct);
            }
        }
    }

    public static async Task Main()
    {
        Console.WriteLine("=== Interruptible Runs Demo (Local runtime, console-only) ===");
        Console.WriteLine("This demo starts a long runA, then starts runB to interrupt it (latest-wins).");
        Console.WriteLine();

        var registry = new LocalMessageStreamRegistry();

        // Use a raw id; LocalGAgentActor will use it as its Actor Id.
        var agentId = Guid.NewGuid().ToString("N");
        var agent = new RunOutputAgent(agentId);
        var actor = new LocalGAgentActor(agent, registry);
        await actor.ActivateAsync();

        // Subscribe to this agent's stream to print published ticks.
        var stream = registry.GetOrCreateStream(agentId);
        var sub = await stream.SubscribeAsync<EventEnvelope>(
            envelope =>
            {
                if (envelope.Payload?.Is(StringValue.Descriptor) != true)
                    return Task.CompletedTask;

                var v = envelope.Payload.Unpack<StringValue>().Value ?? string.Empty;
                Console.WriteLine($"[stream] {v}");
                return Task.CompletedTask;
            },
            null,
            CancellationToken.None);

        async Task<RpcResponse> InvokeEmitTicksAsync(string runId, int ticks, int delayMs)
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

        var runA = "runA";
        var runB = "runB";

        Console.WriteLine($"→ start {runA} (expected: outputs runA:tick:*, then get interrupted)");
        var taskA = Task.Run(() => InvokeEmitTicksAsync(runA, ticks: 1_000, delayMs: 30));

        // Let runA output a few ticks.
        await Task.Delay(180);

        Console.WriteLine();
        Console.WriteLine($"→ start {runB} (expected: interrupts runA, then outputs runB:tick:0)");
        var respB = await InvokeEmitTicksAsync(runB, ticks: 1, delayMs: 0);
        Console.WriteLine($"runB rpc success={respB.Success}");

        // Wait for runA to observe cancellation (it will return a failed RpcResponse).
        var respA = await taskA;
        Console.WriteLine($"runA rpc success={respA.Success} (expected false due to cancellation)");
        if (!respA.Success)
        {
            Console.WriteLine($"runA rpc errorType={respA.Error?.ErrorType}, message={respA.Error?.Message}");
        }

        Console.WriteLine();
        Console.WriteLine("If you saw runA ticks stop after runB started, interruptible runs are working.");

        await sub.UnsubscribeAsync();
        await actor.DeactivateAsync();
    }
}


