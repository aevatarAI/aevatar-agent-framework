using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Core.Factory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aevatar.Agents.Runtime.Local;

/// <summary>
/// Local runtime Agent Actor factory
/// </summary>
public class LocalGAgentActorFactory : GAgentActorFactoryBase
{
    private readonly LocalMessageStreamRegistry _streamRegistry;
    private readonly IMessageStreamProvider? _externalStreamProvider;
    private readonly IOptions<MessageStreamProviderOptions>? _providerOptions;
    private readonly ILoggerFactory _loggerFactory;

    public LocalGAgentActorFactory(
        IServiceProvider serviceProvider,
        ILogger<LocalGAgentActorFactory> logger,
        ILoggerFactory loggerFactory,
        IMessageStreamProvider? externalStreamProvider = null,
        IOptions<MessageStreamProviderOptions>? providerOptions = null)
        : base(serviceProvider, logger)
    {
        _streamRegistry = new LocalMessageStreamRegistry();
        _externalStreamProvider = externalStreamProvider;
        _providerOptions = providerOptions;
        _loggerFactory = loggerFactory;
    }

    protected override Task<IGAgentActor> CreateActorInstanceAsync(IGAgent agent, string id,
        CancellationToken ct = default)
    {
        if (_streamRegistry.StreamExists(id))
        {
            _logger.LogWarning("[Factory] Stream already exists for Agent {Id}. Removing old stream to allow recreation.", id);
            _streamRegistry.RemoveStream(id);
        }

        _logger.LogDebug("[Factory] Creating Actor for Agent - Type: {AgentType}, Id: {Id}",
            agent.GetType().Name, id);

        var actor = new LocalGAgentActor(
            agent,
            _streamRegistry,
            _externalStreamProvider,
            _providerOptions);

        _logger.LogInformation("Created agent actor instance {Id}", id);

        return Task.FromResult<IGAgentActor>(actor);
    }
}
