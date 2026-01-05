using System;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.CQRS;
using Aevatar.Agents.Abstractions.EventSourcing;
using Aevatar.Agents.AI.Core;
using Aevatar.Agents.Core.CQRS;
using Aevatar.Agents.Core.Context;
using Aevatar.Agents.Plugins.CQRS;
using Aevatar.Agents.Plugins.CQRS.Batching;
using Aevatar.Agents.Plugins.CQRS.Elasticsearch;
using Aevatar.Agents.Core.EventSourcing;
using Aevatar.Agents.Core.EventDeduplication;
using Aevatar.Agents.Core.Extensions;
using Aevatar.Agents.Runtime.Local;
using Aevatar.Agents.Runtime.Local.Subscription;
using Aevatar.Agents.Runtime.Orleans.Context;
using Aevatar.App.Controllers;
using Aevatar.App.HttpApi.Host.Services;
using Elastic.Clients.Elasticsearch;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Orleans;
using Serilog;

namespace Aevatar.App.HttpApi.Host.Extensions;

/// <summary>
/// Extension methods for configuring Agent Runtime
/// Supports Local (default) and Orleans runtimes
/// </summary>
public static class AgentRuntimeExtensions
{
    /// <summary>
    /// Add Agent Runtime to the service collection
    /// Automatically selects runtime based on configuration
    /// </summary>
    public static IServiceCollection AddAgentRuntime(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var runtimeOptions = configuration
            .GetSection(AgentRuntimeOptions.SectionName)
            .Get<AgentRuntimeOptions>() ?? new AgentRuntimeOptions();

        Log.Information("🤖 Configuring Agent Runtime:");
        Log.Information("  RuntimeType: {RuntimeType}", runtimeOptions.RuntimeType);

        // Register common services (shared by all runtimes)
        RegisterCommonServices(services, runtimeOptions.RuntimeType);
        
        // Register factory provider (required for agent creation)
        services.AddGAgentActorFactoryProvider();
        
        // Register default IGAgentFactory (used by factory provider)
        services.TryAddSingleton<IGAgentFactory, AIGAgentFactory>();

        // Register runtime-specific services
        switch (runtimeOptions.RuntimeType)
        {
            case AgentRuntimeType.Local:
                RegisterLocalRuntime(services);
                Log.Information("  ✅ Using Local Runtime (in-memory, fast)");
                break;

            case AgentRuntimeType.Orleans:
                RegisterOrleansRuntime(services, runtimeOptions.Orleans);
                Log.Information("  ✅ Using Orleans Runtime (distributed, scalable)");
                Log.Information("     ClusterId: {ClusterId}", runtimeOptions.Orleans.ClusterId);
                Log.Information("     ServiceId: {ServiceId}", runtimeOptions.Orleans.ServiceId);
                break;

            default:
                throw new InvalidOperationException($"Unknown runtime type: {runtimeOptions.RuntimeType}");
        }

        return services;
    }

    /// <summary>
    /// Register common services shared by all runtimes
    /// </summary>
    private static void RegisterCommonServices(IServiceCollection services, AgentRuntimeType runtimeType)
    {
        // Event Store for Event Sourcing
        services.AddSingleton<IEventStore, InMemoryEventStore>();

        // Event Deduplicator to prevent duplicate event processing
        services.AddSingleton<IEventDeduplicator>(sp =>
            new MemoryCacheEventDeduplicator(new DeduplicationOptions
            {
                EventExpiration = TimeSpan.FromMinutes(5),
                MaxCachedEvents = 10_000,
                EnableAutoCleanup = true
            }));
        
        // CQRS Services - State Projection and Query
        // Pass runtimeType to determine if Projector should be registered
        RegisterCQRSServices(services, runtimeType);
    }

    /// <summary>
    /// Register CQRS services for state projection and query
    /// </summary>
    /// <param name="services">Service collection</param>
    /// <param name="runtimeType">Runtime type - determines if projector is registered</param>
    /// <remarks>
    /// For Orleans mode: Only query services are registered here.
    /// IStateProjector is registered in Silo since Agent runs there.
    /// For Local mode: All services including IStateProjector are registered here.
    /// </remarks>
    private static void RegisterCQRSServices(IServiceCollection services, AgentRuntimeType runtimeType)
    {
        Log.Information("📊 Configuring CQRS Services...");
        
        // Get ES configuration from IConfiguration
        var config = services.BuildServiceProvider().GetRequiredService<IConfiguration>();
        var esUrl = config.GetValue<string>("Elasticsearch:Url") ?? "http://localhost:9200";
        var esPrefix = config.GetValue<string>("Elasticsearch:IndexPrefix") ?? "aevatar-state";
        
        Log.Information("   Elasticsearch URL: {Url}", esUrl);
        Log.Information("   Index Prefix: {Prefix}", esPrefix);
        Log.Information("   Runtime Type: {RuntimeType}", runtimeType);
        
        // Elasticsearch client - needed for both query and projection
        services.AddSingleton(sp =>
        {
            var settings = new ElasticsearchClientSettings(new Uri(esUrl))
                .DefaultIndex("aevatar-state")
                .RequestTimeout(TimeSpan.FromSeconds(30));
            return new ElasticsearchClient(settings);
        });
        
        // State Index Service - Elasticsearch (needed for query)
        services.AddSingleton<IStateIndexService>(sp =>
        {
            var client = sp.GetRequiredService<ElasticsearchClient>();
            var logger = sp.GetRequiredService<ILogger<ElasticsearchStateIndexService>>();
            var options = new ElasticsearchOptions { IndexPrefix = esPrefix };
            return new ElasticsearchStateIndexService(client, logger, options);
        });

        // CQRS query facade (framework-level)
        services.TryAddSingleton<IStateQueryService, StateQueryService>();
        
        // State Projector - ONLY for Local mode
        // In Orleans mode, Agent runs in Silo, so Silo registers IStateProjector
        if (runtimeType == AgentRuntimeType.Local)
        {
            var useBatching = config.GetValue<bool>("CQRS:UseBatching", true);
            Log.Information("   Use Batching: {UseBatching} (Local mode - projector registered here)", useBatching);
            
            if (useBatching)
            {
                Log.Information("   Using BatchedStateProjector (high throughput)");
                
                // Configure batch options from config
                services.Configure<BatchProjectorOptions>(opts =>
                {
                    opts.BatchSize = config.GetValue("CQRS:BatchSize", 15);
                    opts.BatchTimeoutSeconds = config.GetValue("CQRS:BatchTimeoutSeconds", 1);
                    opts.MaxBatchSize = config.GetValue("CQRS:MaxBatchSize", 100);
                    opts.MaxRetryCount = config.GetValue("CQRS:MaxRetryCount", 3);
                });
                
                services.AddSingleton<IStateProjector, BatchedStateProjector>();
            }
            else
            {
                Log.Information("   Using ElasticsearchStateProjector (direct)");
                services.AddSingleton<IStateProjector>(sp =>
                {
                    var indexService = sp.GetRequiredService<IStateIndexService>();
                    var logger = sp.GetRequiredService<ILogger<ElasticsearchStateProjector>>();
                    return new ElasticsearchStateProjector(indexService, logger);
                });
            }
        }
        else
        {
            Log.Information("   ⚠️ Orleans mode - IStateProjector NOT registered here (Silo handles it)");
        }
        
        // State Query Service - bridges Controller to IStateIndexService
        services.AddSingleton<IStateQueryService, StateQueryService>();
        Log.Information("   ✅ StateQueryService registered");
        
        Log.Information("   ✅ CQRS query services configured");
    }

    /// <summary>
    /// Register Local Runtime services
    /// </summary>
    private static void RegisterLocalRuntime(IServiceCollection services)
    {
        // Local runtime uses in-memory message streams
        services.AddSingleton<LocalMessageStreamRegistry>();
        
        // Actor factory for creating agents
        services.AddSingleton<IGAgentActorFactory, LocalGAgentActorFactory>();
        
        // Actor manager for lifecycle management
        services.AddSingleton<IGAgentActorManager, Aevatar.Agents.Runtime.Local.LocalGAgentActorManager>();
        
        // Subscription manager for stream subscriptions
        services.AddSingleton<ISubscriptionManager>(sp =>
            new LocalSubscriptionManager(
                sp.GetRequiredService<LocalMessageStreamRegistry>(),
                sp.GetRequiredService<ILogger<LocalSubscriptionManager>>()));

        // Agent Context for Local runtime (AsyncLocal-based)
        services.AddAgentContext();
        Log.Information("   ✅ Agent Context configured (AsyncLocal)");
    }

    /// <summary>
    /// Register Orleans Runtime services
    /// </summary>
    private static void RegisterOrleansRuntime(IServiceCollection services, OrleansRuntimeOptions orleansOptions)
    {
        // Orleans runtime requires Orleans Silo to be configured via UseOrleansClient
        // The actual grain factory comes from Orleans
        services.AddSingleton<IGAgentActorFactory, Aevatar.Agents.Runtime.Orleans.OrleansGAgentActorFactory>();

        // Register Orleans Actor Manager
        services.AddSingleton<IGAgentActorManager, Aevatar.Agents.Runtime.Orleans.OrleansGAgentActorManager>();

        // Ensure IGrainFactory is available (forward from IClusterClient if needed)
        services.TryAddSingleton<IGrainFactory>(sp => sp.GetRequiredService<IClusterClient>());
        
        // Configure MessageStreamProviderOptions from configuration (required for OrleansGAgentActor to use MassTransit)
        var config = services.BuildServiceProvider().GetRequiredService<IConfiguration>();
        services.Configure<MessageStreamProviderOptions>(config.GetSection("MessageStream"));
        Log.Information("   ✅ MessageStreamProviderOptions configured");

        // Agent Context for Orleans runtime (bridges with Orleans RequestContext)
        services.AddOrleansAgentContext();
        Log.Information("   ✅ Agent Context configured (Orleans RequestContext bridge)");

        // Orleans subscription manager (optional - for advanced stream management)
        // services.AddSingleton<ISubscriptionManager>(...);
    }
}
