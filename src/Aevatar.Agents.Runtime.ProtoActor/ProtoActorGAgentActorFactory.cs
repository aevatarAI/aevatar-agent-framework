using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Core;
using Aevatar.Agents.Core.Factory;
using Microsoft.Extensions.Logging;
using Proto;

namespace Aevatar.Agents.Runtime.ProtoActor;

/// <summary>
/// Proto.Actor runtime Agent Actor factory
/// </summary>
public class ProtoActorGAgentActorFactory : GAgentActorFactoryBase
{
    private readonly ActorSystem _actorSystem;
    private readonly ProtoActorMessageStreamRegistry _streamRegistry;

    public ProtoActorGAgentActorFactory(
        IServiceProvider serviceProvider,
        ActorSystem actorSystem,
        ILogger<ProtoActorGAgentActorFactory> logger)
        : base(serviceProvider, logger)
    {
        _actorSystem = actorSystem;
        _streamRegistry = new ProtoActorMessageStreamRegistry(actorSystem.Root);
    }

    /// <summary>
    /// Create Actor for existing Agent instance (internal method, for auto-discovery use)
    /// </summary>
    protected override Task<IGAgentActor> CreateActorInstanceAsync(IGAgent agent, string id,
        CancellationToken ct = default)
    {
        _logger.LogDebug("[Factory] Creating ProtoActor Actor for Agent - Type: {AgentType}, Id: {Id}",
            agent.GetType().Name, id);

        // Create ProtoActor Actor
        var props = Props.FromProducer(() => new AgentActor());
        var actorPid = _actorSystem.Root.Spawn(props);

        var actor = new ProtoActorGAgentActor(
            agent,
            _actorSystem.Root,
            actorPid,
            _streamRegistry);

        _logger.LogInformation("Created ProtoActor agent actor instance {Id}", id);

        return Task.FromResult<IGAgentActor>(actor);
    }
}