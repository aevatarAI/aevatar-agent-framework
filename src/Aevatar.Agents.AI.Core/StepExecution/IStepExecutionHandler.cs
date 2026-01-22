using Aevatar.Agents.Abstractions;
using Google.Protobuf;

namespace Aevatar.Agents.AI.Core;

public sealed record StepExecutionResult(IMessage Response, EventDirection Direction);

public interface IStepExecutionHandler
{
    bool CanHandle(EventEnvelope envelope);

    Task<StepExecutionResult?> HandleAsync(
        EventEnvelope envelope,
        AIGAgentBase agent,
        CancellationToken ct);
}
