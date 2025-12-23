using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.Helpers;
using Aevatar.Agents.Core.Context;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans.Streams;

namespace Aevatar.Agents.Runtime.Orleans;

/// <summary>
/// Orleans Agent Actor Factory
/// 
/// Creates lightweight actor proxies that forward to Grains.
/// Agent instances are created and executed in the Grain (Silo) side.
/// </summary>
public class OrleansGAgentActorFactory : IGAgentActorFactory
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IClusterClient _clusterClient;
    private readonly ILogger<OrleansGAgentActorFactory> _logger;
    private readonly IStreamProvider? _streamProvider;
    private readonly StreamingOptions _streamingOptions;
    private readonly IMessageStreamProvider? _messageStreamProvider;
    private readonly IOptions<MessageStreamProviderOptions>? _providerOptions;

    public OrleansGAgentActorFactory(
        IServiceProvider serviceProvider,
        IClusterClient clusterClient,
        ILogger<OrleansGAgentActorFactory> logger,
        IMessageStreamProvider? messageStreamProvider = null,
        IOptions<MessageStreamProviderOptions>? providerOptions = null)
    {
        _serviceProvider = serviceProvider;
        _clusterClient = clusterClient;
        _logger = logger;
        _messageStreamProvider = messageStreamProvider;
        _providerOptions = providerOptions;

        // Get StreamingOptions from configuration
        _streamingOptions = serviceProvider.GetService<IOptions<StreamingOptions>>()?.Value
                            ?? new StreamingOptions();

        // Orleans Stream Provider
        var streamProviderName = _streamingOptions.StreamProviderName;
        try 
        {
            _streamProvider = clusterClient.GetStreamProvider(streamProviderName);
        }
        catch (Exception ex)
        {
            if (messageStreamProvider == null || providerOptions?.Value.Provider != "MassTransit")
            {
                _logger.LogWarning(ex, "Stream provider '{StreamProviderName}' not found", streamProviderName);
            }
        }
    }

    /// <summary>
    /// Create agent actor by type
    /// </summary>
    public async Task<IGAgentActor> CreateGAgentActorAsync(
        Type agentType, 
        string? id = null, 
        CancellationToken ct = default)
    {
        // Orleans 下 ActorId 必须包含类型前缀（避免跨类型 id 冲突）：
        // ActorId = "AgentTypeShortName:RawId"
        var inputId = string.IsNullOrWhiteSpace(id) ? Guid.NewGuid().ToString("D") : id.Trim();
        var actorId = AgentId.Normalize(agentType, inputId);
        var rawId = AgentId.ExtractRawId(actorId);
        var agentTypeName = agentType.AssemblyQualifiedName ?? agentType.FullName ?? agentType.Name;

        _logger.LogInformation(
            "Creating Orleans Actor proxy for Agent - Type: {AgentType}, inputId={InputId}, actorId={ActorId}",
            agentType.Name, inputId, actorId);

        // Create lightweight actor proxy (Agent will be created in Grain/Silo)
        var contextPropagator = _serviceProvider.GetService<AgentContextPropagator>();
        var actor = new OrleansGAgentActor(
            rawId,
            agentTypeName,
            _clusterClient,
            _streamProvider,
            _streamingOptions,
            _serviceProvider.GetRequiredService<ILogger<OrleansGAgentActor>>(),
            _messageStreamProvider,
            _providerOptions,
            contextPropagator);

        // Activate - This will initialize Agent in the Grain (Silo side)
        await actor.ActivateAsync(ct);

        _logger.LogInformation("✅ Created Orleans Actor proxy {ActorId}, Agent running in Silo", actor.Id);

        return actor;
    }

    /// <summary>
    /// Create agent actor by generic type
    /// </summary>
    public Task<IGAgentActor> CreateGAgentActorAsync<TAgent>(
        string? id = null, 
        CancellationToken ct = default) 
        where TAgent : IGAgent
    {
        return CreateGAgentActorAsync(typeof(TAgent), id, ct);
    }
}
