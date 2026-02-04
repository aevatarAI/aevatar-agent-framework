using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Core.Helpers;
using Microsoft.Extensions.DependencyInjection;

namespace Aevatar.Agents.Core.Factory;

/// <summary>
/// Default <see cref="IGAgentFactory"/> for non-AI agents.
///
/// 中文 + ASCII:
/// - This factory lives in the Foundation layer (Core) and must NOT depend on AI.* packages.
/// - It performs basic DI-friendly construction and injects core infrastructure dependencies.
/// - AI-specific injections (tools/LLM/hook packs) are handled by <see cref="Aevatar.Agents.AI.Core.AIGAgentFactory"/>
///   when apps opt-in to AI packages.
/// </summary>
public sealed class DefaultGAgentFactory : IGAgentFactory
{
    private readonly IServiceProvider _serviceProvider;

    public DefaultGAgentFactory(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
    }

    public IGAgent CreateGAgent(string id, Type agentType, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(agentType);

        // Create Agent instance, support multiple constructor patterns.
        IGAgent agent;

        var constructors = agentType.GetConstructors();

        var ctorWithOptionalString = constructors.FirstOrDefault(c =>
        {
            var parameters = c.GetParameters();
            return parameters.Length == 1 &&
                   parameters[0].ParameterType == typeof(string) &&
                   parameters[0].HasDefaultValue;
        });

        if (ctorWithOptionalString != null)
        {
            agent = (IGAgent)ctorWithOptionalString.Invoke([id]);
        }
        else if (constructors.Any(c =>
                 {
                     var parameters = c.GetParameters();
                     return parameters.Length == 1 && parameters[0].ParameterType == typeof(string);
                 }))
        {
            var ctorWithString = agentType.GetConstructor([typeof(string)]);
            agent = (IGAgent)ctorWithString!.Invoke([id]);
        }
        else
        {
            // Use ActivatorUtilities to support Dependency Injection (e.g. IConfiguration)
            agent = (IGAgent)ActivatorUtilities.CreateInstance(_serviceProvider, agentType);

            // Set ID for agents created with parameterless constructor or DI.
            if (agent is GAgentBase baseAgent)
            {
                baseAgent.Id = id;
            }
        }

        // Core injections (best-effort, no AI.* dependencies).
        LoggerInjector.InjectLogger(agent, _serviceProvider);
        AgentStateStoreInjector.InjectStateStore(agent, _serviceProvider);
        AgentConfigStoreInjector.InjectConfigStore(agent, _serviceProvider);
        ExecutionTraceStoreInjector.InjectExecutionTraceStore(agent, _serviceProvider);
        MemoryStoreInjector.InjectMemoryStore(agent, _serviceProvider);
        MemoryVectorIndexInjector.InjectMemoryVectorIndex(agent, _serviceProvider);
        MemoryGraphStoreInjector.InjectMemoryGraphStore(agent, _serviceProvider);

        if (AgentEventStoreInjector.HasEventStore(agent))
        {
            AgentEventStoreInjector.InjectEventStore(agent, _serviceProvider);
        }

        // Will be replaced when this agent is wrapped by an actor.
        AgentEventPublisherInjector.InjectEventPublisher(agent, NullEventPublisher.Instance);

        return agent;
    }

    public TAgent CreateGAgent<TAgent>(string id, CancellationToken ct = default) where TAgent : IGAgent
        => (TAgent)CreateGAgent(id, typeof(TAgent), ct);

    public TAgent CreateGAgent<TAgent>(CancellationToken ct = default) where TAgent : IGAgent
        => CreateGAgent<TAgent>(Guid.NewGuid().ToString(), ct);
}

