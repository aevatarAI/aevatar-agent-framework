using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.Helpers;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Orleans;

namespace Aevatar.Agents.Runtime.Orleans;

/// <summary>
/// Silo-side IGAgentActorFactory implementation (for OrleansGrain internal use).
/// Creates <see cref="SiloGAgentActor"/> which delegates to <see cref="IGAgentGrain"/> via <see cref="IGrainFactory"/>.
/// Uses MemoryCache with size limit and sliding expiration to avoid unbounded growth.
/// </summary>
public sealed class SiloGAgentActorFactory : IGAgentActorFactory
{
    private readonly IGrainFactory _grainFactory;
    private readonly ILogger<SiloGAgentActorFactory> _logger;
    
    /// <summary>
    /// Bounded cache for actor instances with LRU eviction via MemoryCache.
    /// </summary>
    private readonly MemoryCache _actorCache = new(new MemoryCacheOptions
    {
        SizeLimit = 10_000
    });

    private static readonly MemoryCacheEntryOptions CacheEntryOptions = new MemoryCacheEntryOptions()
        .SetSize(1)
        .SetSlidingExpiration(TimeSpan.FromMinutes(30));

    public SiloGAgentActorFactory(IGrainFactory grainFactory, ILogger<SiloGAgentActorFactory> logger)
    {
        _grainFactory = grainFactory ?? throw new ArgumentNullException(nameof(grainFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<IGAgentActor> CreateGAgentActorAsync<TAgent>(string? id = null, CancellationToken ct = default)
        where TAgent : IGAgent
    {
        var agentType = typeof(TAgent);
        var inputId = string.IsNullOrWhiteSpace(id) ? Guid.NewGuid().ToString("D") : id.Trim();

        var actorId = AgentId.Normalize(agentType, inputId);
        var rawId = AgentId.ExtractRawId(actorId);
        var agentTypeName = agentType.AssemblyQualifiedName ?? agentType.FullName ?? agentType.Name;
        
        var agentTypeShortName = AgentId.GetAgentTypeShortName(agentTypeName);
        var cacheKey = $"{agentTypeShortName}:{rawId}";
        
        // Fast path: cache hit
        if (_actorCache.TryGetValue(cacheKey, out IGAgentActor? cachedActor) && cachedActor != null)
        {
            _logger.LogDebug("[SiloActorCache] HIT cacheKey={CacheKey}", cacheKey);
            return cachedActor;
        }

        _logger.LogDebug("[SiloActorCache] MISS cacheKey={CacheKey} - Creating SiloGAgentActor for {AgentType}",
            cacheKey, agentType.Name);

        var actor = new SiloGAgentActor(rawId, agentTypeName, _grainFactory, _logger);
        await actor.ActivateAsync(ct);
        
        _actorCache.Set(cacheKey, actor, CacheEntryOptions);
        
        return actor;
    }
}
