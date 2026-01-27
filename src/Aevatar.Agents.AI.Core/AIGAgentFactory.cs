using Aevatar.Agents.Abstractions;
using Aevatar.Agents.AI.Core.Helpers;
using Aevatar.Agents.AI.Core.Hooks;
using Aevatar.Agents.AI.Core.ToolPacks;
using Aevatar.Agents.Core;
using Aevatar.Agents.Core.Helpers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

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
        AIAgentHttpClientFactoryInjector.InjectHttpClientFactory(agent, _serviceProvider);
        AIAgentHostConfigurationInjector.InjectHostConfiguration(agent, _serviceProvider);
        AIAgentWebSearchProviderInjector.InjectWebSearchProvider(agent, _serviceProvider);

        // ============================================================
        //  Hook/Harness injection (explicit, type-safe, best-effort)
        //
        //  中文 + ASCII:
        //  - Prefer explicit injection over reflection for discoverability.
        //  - Do NOT fail agent creation if DI is not configured.
        // ============================================================
        if (agent is AIGAgentBase aiAgent)
        {
            try
            {
                var options = _serviceProvider.GetService<IOptions<AevatarAgentHookOptions>>()?.Value
                              ?? _serviceProvider.GetService<AevatarAgentHookOptions>();
                aiAgent.InjectHookOptions(options);

                var hooks = _serviceProvider.GetServices<IAevatarAgentHook>();
                aiAgent.InjectAdditionalHooks(hooks);
            }
            catch
            {
                // Best-effort: never fail agent creation due to hook injection.
            }

            try
            {
                var packs = _serviceProvider.GetServices<IAevatarToolPack>();
                aiAgent.InjectToolPacks(packs);
            }
            catch
            {
                // Best-effort: tool pack injection should not block agent creation.
            }
        }

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