using System.Collections.Concurrent;
using Aevatar.Agents.AI.Abstractions;
using Aevatar.Agents.AI.WithProcessStrategy.Abstractions;
using Aevatar.Agents.AI.WithProcessStrategy.Messages;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.AI.WithProcessStrategy.Strategies;

/// <summary>
/// AI processing strategy factory
/// Responsible for creating and managing different processing strategy instances
/// </summary>
public class AevatarAIProcessingStrategyFactory : IAevatarAIProcessingStrategyFactory
{
    private readonly IServiceProvider? _serviceProvider;
    private readonly ILogger<AevatarAIProcessingStrategyFactory>? _logger;
    private readonly ConcurrentDictionary<AevatarAIProcessingMode, IAevatarAIProcessingStrategy> _strategies;
    private readonly Dictionary<AevatarAIProcessingMode, Type> _strategyTypes;
    
    /// <summary>
    /// Constructor
    /// </summary>
    public AevatarAIProcessingStrategyFactory(
        IServiceProvider? serviceProvider = null,
        ILogger<AevatarAIProcessingStrategyFactory>? logger = null)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _strategies = new ConcurrentDictionary<AevatarAIProcessingMode, IAevatarAIProcessingStrategy>();
        
        // Register built-in strategy types
        _strategyTypes = new Dictionary<AevatarAIProcessingMode, Type>
        {
            [AevatarAIProcessingMode.Standard] = typeof(StandardProcessingStrategy),
            [AevatarAIProcessingMode.ChainOfThought] = typeof(ChainOfThoughtProcessingStrategy),
            [AevatarAIProcessingMode.ReAct] = typeof(ReActProcessingStrategy),
            [AevatarAIProcessingMode.TreeOfThoughts] = typeof(TreeOfThoughtsProcessingStrategy)
        };
        
        _logger?.LogDebug("AI Processing Strategy Factory initialized with {Count} strategy types", _strategyTypes.Count);
    }
    
    /// <summary>
    /// Get processing strategy
    /// </summary>
    public IAevatarAIProcessingStrategy GetStrategy(AevatarAIProcessingMode mode)
    {
        // Try to get from cache
        if (_strategies.TryGetValue(mode, out var cachedStrategy))
        {
            _logger?.LogDebug("Using cached strategy for mode {Mode}", mode);
            return cachedStrategy;
        }
        
        // Create new strategy
        var strategy = CreateStrategy(mode);
        
        // Cache strategy (strategies are stateless, can be reused)
        _strategies.TryAdd(mode, strategy);
        
        return strategy;
    }
    
    /// <summary>
    /// Get or create processing strategy (with dependency injection)
    /// </summary>
    public IAevatarAIProcessingStrategy GetOrCreateStrategy(
        AevatarAIProcessingMode mode,
        bool useCache = true)
    {
        if (useCache && _strategies.TryGetValue(mode, out var cachedStrategy))
        {
            return cachedStrategy;
        }
        
        var strategy = CreateStrategy(mode);
        
        if (useCache)
        {
            _strategies.TryAdd(mode, strategy);
        }
        
        return strategy;
    }
    
    /// <summary>
    /// Register custom strategy type
    /// </summary>
    public void RegisterStrategyType(AevatarAIProcessingMode mode, Type strategyType)
    {
        if (!typeof(IAevatarAIProcessingStrategy).IsAssignableFrom(strategyType))
        {
            throw new ArgumentException(
                $"Type {strategyType.Name} must implement IAevatarAIProcessingStrategy",
                nameof(strategyType));
        }
        
        _strategyTypes[mode] = strategyType;
        
        // Clear cached strategy instance
        _strategies.TryRemove(mode, out _);
        
        _logger?.LogInformation("Registered custom strategy type {Type} for mode {Mode}", 
            strategyType.Name, mode);
    }
    
    /// <summary>
    /// Register custom strategy instance
    /// </summary>
    public void RegisterStrategy(AevatarAIProcessingMode mode, IAevatarAIProcessingStrategy strategy)
    {
        _strategies[mode] = strategy ?? throw new ArgumentNullException(nameof(strategy));
        _logger?.LogInformation("Registered custom strategy instance for mode {Mode}", mode);
    }
    
    /// <summary>
    /// Get all available processing modes
    /// </summary>
    public IEnumerable<AevatarAIProcessingMode> GetAvailableModes()
    {
        return _strategyTypes.Keys;
    }
    
    /// <summary>
    /// Clear strategy cache
    /// </summary>
    public void ClearCache()
    {
        _strategies.Clear();
        _logger?.LogDebug("Strategy cache cleared");
    }
    
    /// <summary>
    /// Create strategy instance
    /// </summary>
    private IAevatarAIProcessingStrategy CreateStrategy(AevatarAIProcessingMode mode)
    {
        if (!_strategyTypes.TryGetValue(mode, out var strategyType))
        {
            var message = $"No strategy registered for mode {mode}. Available modes: {string.Join(", ", _strategyTypes.Keys)}";
            _logger?.LogError(message);
            throw new NotSupportedException(message);
        }
        
        _logger?.LogDebug("Creating strategy instance for mode {Mode} using type {Type}", 
            mode, strategyType.Name);
        
        // Prefer using DI container to create
        if (_serviceProvider != null)
        {
            try
            {
                var strategy = _serviceProvider.GetService(strategyType) as IAevatarAIProcessingStrategy;
                if (strategy != null)
                {
                    _logger?.LogDebug("Created strategy using DI container");
                    return strategy;
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to create strategy using DI container, falling back to Activator");
            }
        }
        
        // Fallback to Activator creation
        try
        {
            var strategy = Activator.CreateInstance(strategyType) as IAevatarAIProcessingStrategy;
            if (strategy != null)
            {
                _logger?.LogDebug("Created strategy using Activator");
                return strategy;
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to create strategy instance for type {Type}", strategyType.Name);
            throw;
        }
        
        throw new InvalidOperationException($"Failed to create strategy instance for mode {mode}");
    }
}

/// <summary>
/// DI extension methods
/// </summary>
public static class AevatarAIProcessingStrategyExtensions
{
    /// <summary>
    /// Register AI processing strategy services
    /// </summary>
    public static IServiceCollection AddAevatarAIProcessingStrategies(this IServiceCollection services)
    {
        // Register factory
        services.AddSingleton<IAevatarAIProcessingStrategyFactory, AevatarAIProcessingStrategyFactory>();

        // Register each strategy as transient service (they are stateless)
        services.AddTransient<StandardProcessingStrategy>();
        services.AddTransient<ChainOfThoughtProcessingStrategy>();
        services.AddTransient<ReActProcessingStrategy>();
        services.AddTransient<TreeOfThoughtsProcessingStrategy>();

        // Register generic strategy resolution
        services.AddTransient<IAevatarAIProcessingStrategy>(provider =>
        {
            // Default returns standard strategy
            return provider.GetRequiredService<StandardProcessingStrategy>();
        });

        return services;
    }
}