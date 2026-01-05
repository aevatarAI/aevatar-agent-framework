using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using MongoDB.Driver;
using Aevatar.Silo.Extensions;
using Aevatar.App.Agents.Agents; // CRITICAL: Reference to force assembly load
using Aevatar.Agents.Runtime.Orleans.Extensions;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Core.Extensions;
using Aevatar.Agents.Persistence.MongoDB;
using Aevatar.Agents.Persistence.MongoDB.GAgent;
using Aevatar.Agents.Runtime.Orleans.EventSourcing;
using Aevatar.Agents.Runtime.Orleans.MongoDB;
using Aevatar.Agents.Orleans.MongoDB;
using Aevatar.Agents.Plugins.MassTransit.DependencyInjection;
using Aevatar.Agents.Plugins.CQRS;
using Aevatar.Agents.Plugins.CQRS.Batching;
using Aevatar.Agents.Plugins.CQRS.Elasticsearch;  // Use Core's CQRS implementation
using Aevatar.Agents.AI.Abstractions.Configuration;
using Aevatar.Agents.AI.MEAI.DependencyInjection;

namespace Aevatar.Silo;

public class Program
{
    public static async Task<int> Main(string[] args)
    {
        // Configure Serilog
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .AddJsonFile($"appsettings.{Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production"}.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        Log.Logger = new LoggerConfiguration()
            .ReadFrom.Configuration(configuration)
            .Enrich.FromLogContext()
            .CreateLogger();

        try
        {
            Log.Information("🚀 Starting Aevatar Silo");
            Log.Information("  Environment: {Environment}", Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production");
            Log.Information("  Storage Provider: {Provider}", configuration["Storage:Provider"] ?? "Memory");
            Log.Information("  Streaming Provider: {Provider}", configuration["Streaming:Provider"] ?? "OrleansStream");
            
            var host = CreateHostBuilder(args).Build();
            
            await host.RunAsync();
            
            Log.Information("✅ Silo shutdown completed");
            return 0;
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "❌ Silo terminated unexpectedly!");
            return 1;
        }
        finally
        {
            await Log.CloseAndFlushAsync();
        }
    }

    private static IHostBuilder CreateHostBuilder(string[] args)
    {
        return Host.CreateDefaultBuilder(args)
            .UseSerilog()
            .UseOrleansConfiguration() // Extension method from OrleansHostExtension
            .ConfigureWebHostDefaults(webBuilder =>
            {
                webBuilder.Configure(app =>
                {
                    // Map health check endpoints for Kubernetes
                    app.MapOrleansHealthChecks();
                });
                
                webBuilder.ConfigureKestrel(options =>
                {
                    // Health check endpoint port
                    options.ListenAnyIP(8081);
                });
            })
            .ConfigureServices((context, services) =>
            {
                services.AddOrleansHealthChecks();
                
                // Configure MongoDB BSON serializers (must be first)
                MongoDBServiceCollectionExtensions.ConfigureBsonSerializers();
                
                // MongoDB configuration
                var mongoConnectionString = context.Configuration.GetConnectionString("Orleans") 
                    ?? "mongodb://localhost:27017/AevatarBusiness";
                var databaseName = context.Configuration.GetSection("Storage")
                    .GetValue("DatabaseName", "AevatarBusiness");
                
                services.AddSingleton<IMongoClient>(sp => new MongoClient(mongoConnectionString));
                services.AddSingleton<IMongoDatabase>(sp => 
                    sp.GetRequiredService<IMongoClient>().GetDatabase(databaseName));
                
                // MongoDB Event Repository for EventStore
                services.AddSingleton<IEventRepository>(sp => new MongoEventRepository(
                    sp.GetRequiredService<IMongoClient>(),
                    new MongoEventRepositoryOptions
                    {
                        DatabaseName = databaseName,
                        CollectionName = "agent_events",
                        EnableDetailedLogging = true
                    },
                    sp.GetRequiredService<ILogger<MongoEventRepository>>()));

                // Configure MessageStreamProviderOptions
                services.Configure<MessageStreamProviderOptions>(context.Configuration.GetSection("MessageStream"));
                
                // Configure LLM Providers for AI Agents
                services.Configure<LLMProvidersConfig>(context.Configuration.GetSection("LLMProviders"));
                services.AddMEAI();
                Log.Information("🤖 LLM Providers configured from appsettings.json");

                // MassTransit Stream Plugin - ONLY if MessageStream.Provider is "MassTransit"
                var messageStreamProvider = context.Configuration.GetSection("MessageStream").GetValue("Provider", "Orleans");
                Log.Information("  MessageStream Provider: {Provider}", messageStreamProvider);
                
                if (messageStreamProvider == "MassTransit")
                {
                    Log.Information("🔌 Registering MassTransit Stream Plugin (Kafka Consumer + Producer)");
                    services.AddMassTransitStreamPlugin(context.Configuration);
                }
                else
                {
                    Log.Information("📡 Using Orleans Kafka Stream (no MassTransit)");
                }

                // Aevatar Agent System with MongoDB stores
                services.AddAevatarAgentSystem(options =>
                {
                    options.StateStoreType = typeof(MongoDBStateStore<>);
                    options.ConfigStoreType = typeof(MongoDbConfigStore<>);
                    options.EventRouterStoreType = typeof(MongoDBEventRouterStore);
                    options.EventStoreType = typeof(OrleansEventStore);
                }, builder => builder.UseOrleansRuntime());
                
                Log.Information("✅ Aevatar Agent System configured with MongoDB stores");

                // CQRS State Projection
                // When MassTransit.CQRS.Enabled=true in config:
                //   - IStateDispatcher is registered (publishes to MassTransit InMemory bus)
                //   - StateProjectionConsumer consumes messages and calls IStateProjector
                //   - This provides async, non-blocking state projection
                // When MassTransit.CQRS.Enabled=false:
                //   - IStateProjector is called directly (sync/batched)
                //
                // Configure IStateProjector (Elasticsearch via BatchedStateProjector)
                var esUrl = context.Configuration.GetValue<string>("Elasticsearch:Url") ?? "http://localhost:9200";
                var esPrefix = context.Configuration.GetValue<string>("Elasticsearch:IndexPrefix") ?? "aevatar-state";
                
                services.AddCQRS(options =>
                {
                    options.UseElasticsearch(es =>
                    {
                        es.Url = esUrl;
                        es.IndexPrefix = esPrefix;
                    });
                    options.UseBatchedProjection(batch =>
                    {
                        var cqrsConfig = context.Configuration.GetSection("CQRS:Projector");
                        batch.BatchSize = cqrsConfig.GetValue("BatchSize", 50);
                        batch.MaxBatchSize = cqrsConfig.GetValue("MaxBatchSize", 200);
                        batch.BatchTimeoutSeconds = Math.Max(1, cqrsConfig.GetValue("FlushIntervalMs", 1000) / 1000);
                        batch.MaxRetryCount = cqrsConfig.GetValue("MaxRetryCount", 3);
                    });
                });
                
                Log.Information("✅ CQRS configured with Core.BatchedStateProjector (ES: {EsUrl})", esUrl);
                
                // Note: ManagerOptions configuration removed - not available in Silo project
                // ManagerIds configuration is handled in HttpApi.Host project
                
                Log.Information("✅ Silo configuration completed");
            });
    }
}

