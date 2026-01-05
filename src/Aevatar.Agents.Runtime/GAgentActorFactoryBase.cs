using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.Context;
using Aevatar.Agents.Abstractions.Helpers;
using Aevatar.Agents.AI.Core.Helpers;
using Aevatar.Agents.Core.Helpers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.Core.Factory;

/// <summary>
/// Abstract base class for GAgent Actor factory, contains common logic
/// </summary>
public abstract class GAgentActorFactoryBase : IGAgentActorFactory
{
    protected readonly IServiceProvider _serviceProvider;
    protected readonly ILogger _logger;
    protected readonly IGAgentActorFactoryProvider? _factoryProvider;
    protected readonly IGAgentFactory? _agentFactory;

    protected GAgentActorFactoryBase(
        IServiceProvider serviceProvider,
        ILogger logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _factoryProvider = serviceProvider.GetService<IGAgentActorFactoryProvider>();
        _agentFactory = serviceProvider.GetService<IGAgentFactory>();
    }

    public async Task<IGAgentActor> CreateGAgentActorAsync<TAgent>(string? id = null, CancellationToken ct = default)
        where TAgent : IGAgent
    {
        // ============================================================
        //  AgentId Normalization
        //
        //  - Input: Allows RawId (usually Guid string) or already concatenated ActorId
        //  - Output: Unified ActorId = "AgentTypeShortName:RawId"
        //
        //  This ensures Manager/Stream/Hierarchy keys are always consistent, no more "guessing format" across runtimes.
        // ============================================================
        var inputId = string.IsNullOrWhiteSpace(id) ? Guid.NewGuid().ToString("D") : id.Trim();

        var agentType = typeof(TAgent);
        var actorId = AgentId.Normalize(agentType, inputId);

        if (string.IsNullOrEmpty(actorId))
        {
            throw new InvalidOperationException("Failed to normalize actor id.");
        }

        if (_factoryProvider != null)
        {
            var customFactory = _factoryProvider.GetFactory(agentType);
            if (customFactory != null)
            {
                _logger.LogDebug("Using custom factory for type {AgentType}, inputId={InputId}, actorId={ActorId}",
                    agentType.Name, inputId, actorId);
                return await customFactory(this, actorId, ct);
            }
        }

        _logger.LogDebug("Using default creation process for type {AgentType}, inputId={InputId}, actorId={ActorId}",
            agentType.Name, inputId, actorId);

        if (_agentFactory == null)
        {
            throw new InvalidOperationException(
                $"No IGAgentFactory registered. Cannot create agent of type {agentType.Name}");
        }

        // NOTE:
        // - Agent.Id / Actor.Id uniformly use actorId (full format) to ensure PublisherId/self-handling/StreamKey consistency
        var agent = _agentFactory.CreateGAgent(actorId, agentType, ct);

        await agent.ActivateAsync(ct);

        // Template Method Pattern:
        // 1. Create uninitialized actor instance (implemented by subclasses)
        var actor = await CreateActorInstanceAsync(agent, actorId, ct);

        // 2. Inject dependencies (Logger, EventRouterFactory, StateProjector, ContextAccessor)
        LoggerInjector.InjectLogger(actor, _serviceProvider);
        LoggerInjector.InjectLogger(agent, _serviceProvider);
        EventRouterFactoryInjector.InjectEventRouterFactory(actor, _serviceProvider);
        StateProjectorInjector.InjectStateProjector(agent, _serviceProvider);

        // Inject context accessor and propagator
        if (actor is Core.GAgentActorBase actorBase)
        {
            AgentContextAccessorInjector.InjectContext(agent, actorBase, _serviceProvider);
        }
        else
        {
            // For non-GAgentActorBase actors, only inject accessor
            var contextAccessor = _serviceProvider.GetService<IAgentContextAccessor>();
            if (contextAccessor != null)
            {
                AgentContextAccessorInjector.InjectContextAccessor(agent, contextAccessor);
            }
        }

        // 3. Activate actor (starts streams, loads state, etc.)
        await actor.ActivateAsync(ct);

        return actor;
    }

    /// <summary>
    /// Create an uninitialized Actor instance for the Agent
    /// Subclasses should only create the instance, dependency injection and activation are handled by the base class
    /// </summary>
    protected abstract Task<IGAgentActor> CreateActorInstanceAsync(IGAgent agent, string id,
        CancellationToken ct = default);
}