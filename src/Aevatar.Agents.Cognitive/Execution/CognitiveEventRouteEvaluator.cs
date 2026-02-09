using Aevatar.Agents.Abstractions;
using Aevatar.Agents.AI.Core;
using Aevatar.Agents.Cognitive.Messages;

namespace Aevatar.Agents.Cognitive.Execution;

public sealed class CognitiveEventRouteEvaluator : IEventRouteEvaluator
{
    public bool TryGetEventType(EventEnvelope envelope, out string typeName)
        => new DefaultEventRouteEvaluator().TryGetEventType(envelope, out typeName);

    public bool TryGetStepType(EventEnvelope envelope, out string stepType)
    {
        stepType = string.Empty;
        if (envelope.Payload == null)
            return false;

        if (!envelope.Payload.Is(ExecuteStepRequestEvent.Descriptor))
            return false;

        var evt = envelope.Payload.Unpack<ExecuteStepRequestEvent>();
        stepType = evt.StepType ?? string.Empty;
        return stepType.Length > 0;
    }
}
