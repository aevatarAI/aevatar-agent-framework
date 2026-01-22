using Aevatar.Agents.Abstractions;
using Google.Protobuf;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.AI.Core;

public interface IEventModule
{
    string Name { get; }
    int Priority { get; }
    bool CanHandle(EventEnvelope envelope);
    Task HandleAsync(EventEnvelope envelope, IEventModuleHost host, CancellationToken ct);
}

public interface IEventModuleHost
{
    string AgentId { get; }
    AIGAgentBase Agent { get; }
    ILogger Logger { get; }
    Task PublishAsync(IMessage evt, EventDirection direction, CancellationToken ct);
}
