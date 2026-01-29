using Aevatar.Agents;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.Tracing;
using Aevatar.Agents.AI.Core;
using Aevatar.Agents.AI.Core.Messages;
using Google.Protobuf.WellKnownTypes;

namespace ProgressHookChatWebDemo;

public sealed class DemoEventModuleFactory : IEventModuleFactory
{
    public bool TryCreate(string name, out IEventModule module)
    {
        var key = (name ?? string.Empty).Trim().ToLowerInvariant();
        if (key == DemoChatTraceModule.ModuleName)
        {
            module = new DemoChatTraceModule();
            return true;
        }

        module = null!;
        return false;
    }
}

internal sealed class DemoChatTraceModule : IEventModule
{
    public const string ModuleName = "demo_chat_trace";

    public string Name => ModuleName;
    public int Priority => 0;

    public bool CanHandle(EventEnvelope envelope)
    {
        return envelope.Payload?.Is(ChatRequestEvent.Descriptor) == true ||
               envelope.Payload?.Is(ChatResponseEvent.Descriptor) == true;
    }

    public Task HandleAsync(EventEnvelope envelope, IEventModuleHost host, CancellationToken ct)
    {
        if (envelope.Payload == null)
            return Task.CompletedTask;

        ExecutionTraceEvent trace;
        if (envelope.Payload.Is(ChatRequestEvent.Descriptor))
        {
            var evt = envelope.Payload.Unpack<ChatRequestEvent>();
            trace = BuildTrace(
                host.AgentId,
                ExecutionTraceEventPhase.LlmRequest,
                $"[agent.yaml] chat request: {Trim(evt.Message)}");
        }
        else if (envelope.Payload.Is(ChatResponseEvent.Descriptor))
        {
            var evt = envelope.Payload.Unpack<ChatResponseEvent>();
            var length = evt.Content?.Length ?? 0;
            trace = BuildTrace(
                host.AgentId,
                ExecutionTraceEventPhase.LlmResponse,
                $"[agent.yaml] chat response (len={length})");
        }
        else
        {
            return Task.CompletedTask;
        }

        return host.PublishAsync(trace, EventDirection.Down, ct);
    }

    private static ExecutionTraceEvent BuildTrace(string agentId, string phase, string message)
    {
        var trace = new ExecutionTraceEvent
        {
            Timestamp = Timestamp.FromDateTime(DateTime.UtcNow),
            Phase = phase,
            Message = message,
            NodeId = agentId
        };

        trace.Fields[ExecutionTraceEventFields.AgentId] =
            ExecutionTraceEventFieldValue.FromString(agentId);

        return trace;
    }

    private static string Trim(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "(empty)";

        var trimmed = value.Trim();
        return trimmed.Length <= 120
            ? trimmed
            : $"{trimmed[..120]}...";
    }
}
