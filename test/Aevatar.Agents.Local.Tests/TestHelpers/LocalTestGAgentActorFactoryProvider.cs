using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Core.Helpers;
using Aevatar.Agents.Runtime.Local;
using Microsoft.Extensions.DependencyInjection;

namespace Aevatar.Agents.Local.Tests.TestHelpers;

/// <summary>
/// Agent factory provider for testing
/// Provides simple factory implementation for tests, avoiding complex DI configuration
/// </summary>
public class LocalTestGAgentActorFactoryProvider : IGAgentActorFactoryProvider
{
    private readonly IServiceProvider _serviceProvider;
    private readonly LocalMessageStreamRegistry _streamRegistry;

    public LocalTestGAgentActorFactoryProvider(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
        _streamRegistry = _serviceProvider.GetService<LocalMessageStreamRegistry>()
                          ?? new LocalMessageStreamRegistry();
    }

    public void RegisterFactory<TAgent>(Func<IGAgentActorFactory, string, CancellationToken, Task<IGAgentActor>> factory)
        where TAgent : IGAgent
    {
        // No registration needed in tests, auto-create
    }

    public void RegisterFactory(Type agentType, Func<IGAgentActorFactory, string, CancellationToken, Task<IGAgentActor>> factory)
    {
        // No registration needed in tests, auto-create
    }

    public Func<IGAgentActorFactory, string, CancellationToken, Task<IGAgentActor>>? GetFactory(Type agentType)
    {
        // Provide simple factory implementation for all test Agents
        return async (factory, id, ct) =>
        {
            // Check if already exists
            if (_streamRegistry.StreamExists(id))
            {
                throw new InvalidOperationException($"Agent with id {id} already exists");
            }

            // Create Agent instance - directly use Activator
            var agent = Activator.CreateInstance(agentType, id) as IGAgent;
            if (agent == null)
            {
                throw new InvalidOperationException($"Failed to create agent instance of type {agentType.Name}");
            }

            // 自动注入Logger
            LoggerInjector.InjectLogger(agent, _serviceProvider);

            // 创建LocalGAgentActor（用于测试）
            var actor = new LocalGAgentActor(
                agent,
                _streamRegistry
            );

            // 自动注入Actor的Logger
            LoggerInjector.InjectLogger(actor, _serviceProvider);

            // 激活
            await actor.ActivateAsync(ct);

            return actor;
        };
    }
}