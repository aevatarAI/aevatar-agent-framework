using Aevatar.Agents.AI.Core;
using Aevatar.Agents.Core.Runtime;
using Google.Protobuf.WellKnownTypes;

namespace InterruptibleChatWebDemo;

public interface IInterruptibleChatAgent
{
    Task ChatSlowAsync(string message, CancellationToken ct = default);
}

public sealed class InterruptibleChatAgent : AIGAgentBase, IInterruptibleChatAgent
{
    public InterruptibleChatAgent()
    {
    }

    public InterruptibleChatAgent(string id) : base(id)
    {
    }

    public override Task<string> GetDescriptionAsync()
        => Task.FromResult("InterruptibleChatWebDemo:InterruptibleChatAgent");

    public async Task ChatSlowAsync(string message, CancellationToken ct = default)
    {
        message = (message ?? string.Empty).Replace("\r", "").Trim();
        if (message.Length == 0)
            return;

        // RunId is bound by runtime (RunContextScope) when invoked via RPC with run_id metadata.
        var runId = RunContextScope.Value?.RunId ?? "no-run";

        // Cursor-like UX: always emit a short "thinking" marker, then stream tokens.
        await PublishAsync(new StringValue { Value = $"{runId}|assistant|Thinking...\n" }, ct: ct);

        try
        {
            // This uses AIGAgentBase's history + tool pipeline (if enabled) and respects CancellationToken.
            await foreach (var chunk in GenerateResponseStreamAsync(message, ct))
            {
                ct.ThrowIfCancellationRequested();
                if (string.IsNullOrEmpty(chunk)) continue;
                await PublishAsync(new StringValue { Value = $"{runId}|assistant|{chunk}" }, ct: ct);
            }

            await PublishAsync(new StringValue { Value = $"{runId}|assistant|\n" }, ct: ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Normal: interrupted by a newer run (latest-wins).
            throw;
        }
        catch (Exception ex)
        {
            await PublishAsync(new StringValue { Value = $"{runId}|assistant|\n[demo] LLM error: {ex.Message}\n" }, ct: ct);
        }
    }
}


