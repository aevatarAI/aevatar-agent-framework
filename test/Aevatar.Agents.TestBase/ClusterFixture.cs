using System;
using System.Collections.Concurrent;
using System.Linq;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.EventSourcing;
using Aevatar.Agents.AI.Core;
using Aevatar.Agents.Core.Extensions;
using Aevatar.Agents.Runtime.Orleans.EventSourcing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.Hosting;
using Orleans.Serialization;
using Orleans.TestingHost;

namespace Aevatar.Agents.TestBase;

/// <summary>
/// Shared Orleans test cluster fixture for all agent tests
/// </summary>
public class ClusterFixture : IDisposable
{
    public TestCluster Cluster { get; }

    // Shared IEventRepository instance for both Silo and Client
    // This ensures OrleansEventStore (in Silo) and tests (using Client) use the same repository
    private readonly InMemoryEventRepository _sharedEventRepository;

    // Static dictionary to share repository with configurators (thread-safe)
    private static readonly ConcurrentDictionary<int, InMemoryEventRepository>
        _sharedRepositories = new();

    private readonly int _fixtureId;

    public ClusterFixture()
    {
        // Create a shared InMemoryEventRepository instance BEFORE building the cluster
        _sharedEventRepository = new InMemoryEventRepository();
        _fixtureId = GetHashCode();
        _sharedRepositories[_fixtureId] = _sharedEventRepository;

        var builder = new TestClusterBuilder();
        builder.AddSiloBuilderConfigurator<TestSiloConfigurations>();
        builder.AddClientBuilderConfigurator<TestClientBuilderConfigurator>();
        Cluster = builder.Build();
        Cluster.Deploy();
    }

    /// <summary>
    /// Get the shared IEventRepository instance used by both Silo and Client
    /// </summary>
    public InMemoryEventRepository GetSharedEventRepository()
    {
        return _sharedEventRepository;
    }

    public void Dispose()
    {
        Cluster?.StopAllSilos();
        _sharedRepositories.TryRemove(_fixtureId, out _);
    }

    private class TestSiloConfigurations : ISiloConfigurator
    {
        public void Configure(ISiloBuilder hostBuilder)
        {
            var configuration = new ConfigurationBuilder()
                .AddJsonFile("appsettings.json", optional: true)
                .AddAevatarUserConfig()
                .AddJsonFile("appsettings.secrets.json", optional: true)
                .Build();

            hostBuilder
                .ConfigureServices(services =>
                {
                    services.AddSingleton<IConfiguration>(configuration);

                    // Add logging
                    services.AddLogging(logging =>
                    {
                        logging.AddConsole();
                        logging.SetMinimumLevel(LogLevel.Warning);
                    });

                    // Register shared IEventRepository instance (use most recent)
                    var sharedRepo = _sharedRepositories.Values.LastOrDefault();
                    if (sharedRepo != null)
                    {
                        services.AddSingleton<IEventRepository>(sharedRepo);
                    }

                    // Register OrleansEventStore
                    services.AddSingleton<IEventStore, OrleansEventStore>();

                    services.AddSingleton<IGAgentFactory, AIGAgentFactory>();

                    services.AddSerializer(serializerBuilder => { serializerBuilder.AddProtobufSerializer(); });
                })
                // Stream Providers
                .AddMemoryStreams("StreamProvider")
                .AddMemoryStreams("AevatarAgents") // Used by OrleansGAgentActorFactory and OrleansGAgentGrain
                // Grain Storage Providers
                .AddMemoryGrainStorage("PubSubStore") // Required by Orleans Streams for PubSub
                .AddMemoryGrainStorage("agentState") // Used by OrleansGAgentGrain for persistent state
                .AddMemoryGrainStorageAsDefault(); // Default storage for grains without explicit provider name
        }
    }

    private class TestClientBuilderConfigurator : IClientBuilderConfigurator
    {
        public void Configure(IConfiguration configuration, IClientBuilder clientBuilder)
        {
            clientBuilder
                .AddMemoryStreams("StreamProvider")
                .AddMemoryStreams("AevatarAgents") // Used by OrleansGAgentActorFactory
                .ConfigureServices(services =>
                {
                    // Register shared IEventRepository instance (use most recent)
                    var sharedRepo = _sharedRepositories.Values.LastOrDefault();
                    if (sharedRepo != null)
                    {
                        services
                            .AddSingleton<IEventRepository>(sharedRepo);
                    }

                    // Register OrleansEventStore
                    services.AddSingleton<IEventStore, OrleansEventStore>();

                    services.AddSingleton<IGAgentFactory, AIGAgentFactory>();

                    services.AddSerializer(serializerBuilder => { serializerBuilder.AddProtobufSerializer(); });
                });
        }
    }
}
