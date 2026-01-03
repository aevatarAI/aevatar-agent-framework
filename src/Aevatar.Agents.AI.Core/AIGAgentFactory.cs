using Aevatar.Agents.Abstractions;
using Aevatar.Agents.AI.Core.Helpers;
using Aevatar.Agents.Core;
using Aevatar.Agents.Core.Helpers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.AI.Core;

public class AIGAgentFactory : IGAgentFactory
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<AIGAgentFactory>? _logger;

    public AIGAgentFactory(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
        _logger = serviceProvider.GetService<ILogger<AIGAgentFactory>>();
    }

    public IGAgent CreateGAgent(string id, Type agentType, CancellationToken ct = default)
    {
        // Create Agent instance, support multiple constructor patterns
        IGAgent agent;

        // Try to find suitable constructor
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

            // Set ID for agents created with parameterless constructor or DI
            // This allows recovery scenarios without requiring ID constructor
            if (agent is GAgentBase baseAgent)
            {
                baseAgent.Id = id;
            }
        }

        LoggerInjector.InjectLogger(agent, _serviceProvider);
        AgentStateStoreInjector.InjectStateStore(agent, _serviceProvider);
        AgentConfigStoreInjector.InjectConfigStore(agent, _serviceProvider);
        ExecutionTraceStoreInjector.InjectExecutionTraceStore(agent, _serviceProvider);
        MemoryStoreInjector.InjectMemoryStore(agent, _serviceProvider);
        MemoryVectorIndexInjector.InjectMemoryVectorIndex(agent, _serviceProvider);
        MemoryGraphStoreInjector.InjectMemoryGraphStore(agent, _serviceProvider);
        AIAgentToolManagerInjector.InjectToolManager(agent, _serviceProvider);
        AIAgentStateQueryServiceInjector.InjectStateQueryService(agent, _serviceProvider);

        // Will be replaced when this agent is wrapped by an actor.
        AgentEventPublisherInjector.InjectEventPublisher(agent, NullEventPublisher.Instance);

        if (AIAgentLLMProviderFactoryInjector.HasLLMProviderFactory(agent))
        {
            AIAgentLLMProviderFactoryInjector.InjectLLMProviderFactory(agent, _serviceProvider);
        }

        if (AIAgentEmbeddingFactoryInjector.HasEmbeddingFactory(agent))
        {
            AIAgentEmbeddingFactoryInjector.InjectEmbeddingFactory(agent, _serviceProvider);
        }

        if (AgentEventStoreInjector.HasEventStore(agent))
        {
            AgentEventStoreInjector.InjectEventStore(agent, _serviceProvider);
        }

        return agent;
    }

    public TAgent CreateGAgent<TAgent>(string id, CancellationToken ct = default) where TAgent : IGAgent
    {
        return (TAgent)CreateGAgent(id, typeof(TAgent), ct);
    }

    public TAgent CreateGAgent<TAgent>(CancellationToken ct = default) where TAgent : IGAgent
    {
        return CreateGAgent<TAgent>(Guid.NewGuid().ToString(), ct);
    }
}