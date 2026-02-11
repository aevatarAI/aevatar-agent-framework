using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.Helpers;
using Aevatar.Agents.Core.Context;
using Microsoft.Extensions.Caching.Memory;
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
/// 
/// Uses MemoryCache with size limit and sliding expiration to avoid unbounded growth.
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
    
    /// <summary>
    /// Bounded cache for actor proxies with LRU eviction via MemoryCache.
    /// </summary>
    private readonly MemoryCache _actorCache = new(new MemoryCacheOptions
    {
        SizeLimit = 10_000
    });

    private static readonly MemoryCacheEntryOptions CacheEntryOptions = new MemoryCacheEntryOptions()
        .SetSize(1)
        .SetSlidingExpiration(TimeSpan.FromMinutes(30));
    
    /// <summary>
    /// Unique identifier for this factory instance (for debugging singleton behavior)
    /// </summary>
    private readonly string _factoryInstanceId = Guid.NewGuid().ToString("N")[..8];

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
        
        _logger.LogInformation("[ActorFactory] Created new instance: {FactoryId}", _factoryInstanceId);

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
        var inputId = string.IsNullOrWhiteSpace(id) ? Guid.NewGuid().ToString("D") : id.Trim();
        var actorId = AgentId.Normalize(agentType, inputId);
        var rawId = AgentId.ExtractRawId(actorId);
        var agentTypeName = agentType.AssemblyQualifiedName ?? agentType.FullName ?? agentType.Name;
        
        var agentTypeShortName = AgentId.GetAgentTypeShortName(agentTypeName);
        var cacheKey = $"{agentTypeShortName}:{rawId}";
        
        // Fast path: cache hit
        if (_actorCache.TryGetValue(cacheKey, out IGAgentActor? cachedActor) && cachedActor != null)
        {
            _logger.LogDebug("[ActorCache] HIT cacheKey={CacheKey}", cacheKey);
            return cachedActor;
        }

        _logger.LogDebug("[ActorCache] MISS cacheKey={CacheKey} - Creating new Actor proxy for {AgentType}",
            cacheKey, agentType.Name);

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

        await actor.ActivateAsync(ct);
        
        _actorCache.Set(cacheKey, actor, CacheEntryOptions);

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
