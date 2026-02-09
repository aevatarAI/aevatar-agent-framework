using Aevatar.Agents.Abstractions;

namespace Aevatar.Agents.AI.Core;

public interface IEventRouteEvaluator
{
    bool TryGetEventType(EventEnvelope envelope, out string typeName);
    bool TryGetStepType(EventEnvelope envelope, out string stepType);
}

public sealed class DefaultEventRouteEvaluator : IEventRouteEvaluator
{
    public bool TryGetEventType(EventEnvelope envelope, out string typeName)
    {
        typeName = string.Empty;
        var typeUrl = envelope.Payload?.TypeUrl ?? string.Empty;
        if (typeUrl.Length == 0)
            return false;

        var idx = typeUrl.LastIndexOf('/');
        typeName = idx >= 0 ? typeUrl[(idx + 1)..] : typeUrl;
        return typeName.Length > 0;
    }

    public bool TryGetStepType(EventEnvelope envelope, out string stepType)
    {
        stepType = string.Empty;
        return false;
    }
}
