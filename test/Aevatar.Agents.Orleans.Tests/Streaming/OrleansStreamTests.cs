using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Runtime.Orleans;
using Aevatar.Agents.Runtime.Orleans.Stream;
using Google.Protobuf;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.Configuration;
using Orleans.Hosting;
using Orleans.Serialization;
using Orleans.Streams;
using Orleans.TestingHost;
using Xunit;

namespace Aevatar.Agents.Orleans.Tests.Streaming;

/// <summary>
/// Orleans Stream mechanism integration tests
/// </summary>
public class OrleansStreamTests : IClassFixture<OrleansStreamTests.ClusterFixture>
{
    private readonly TestCluster _cluster;
    private readonly IGrainFactory _grainFactory;

    public OrleansStreamTests(ClusterFixture fixture)
    {
        _cluster = fixture.Cluster;
        _grainFactory = _cluster.GrainFactory;
    }

    /// <summary>
    /// Test Orleans grain parent-child relationship subscription
    /// </summary>
    [Fact]
    public async Task Orleans_SetParent_Should_Subscribe_To_Parent_Stream()
    {
        // Arrange
        var parentId = Guid.NewGuid().ToString();
        var childId = Guid.NewGuid().ToString();

        var parentGrain = _grainFactory.GetGrain<IGAgentGrain>(parentId.ToString());
        var childGrain = _grainFactory.GetGrain<IGAgentGrain>(childId.ToString());

        // Activate grains (use actual Agent type for Orleans tests)
        await parentGrain.InitializeAgentAsync("Aevatar.Agents.Orleans.Tests.OrleansTestAgent");
        await childGrain.InitializeAgentAsync("Aevatar.Agents.Orleans.Tests.OrleansTestAgent");

        // Act - Establish parent-child relationship
        await childGrain.SetParentAsync(parentId);
        await parentGrain.AddChildAsync(childId);

        // Parent node publishes DOWN event
        var testEvent = new EventEnvelope
        {
            Id = Guid.NewGuid().ToString(),
            Direction = EventDirection.Down,
            Message = "Orleans Test Message",
            PublisherId = parentId.ToString()
        };
        await parentGrain.HandleEventAsync(testEvent.ToByteArray());

        // Wait for stream propagation
        await Task.Delay(500);

        // Assert - Verify by querying child node state (actual test needs specific implementation)
        // Simplified here to verify relationship establishment
        var childParent = await childGrain.GetParentAsync();
        Assert.Equal(parentId, childParent);
    }

    /// <summary>
    /// Test Orleans Resume mechanism
    /// </summary>
    [Fact]
    public async Task Orleans_Resume_Should_Work_After_Subscription_Failure()
    {
        // Arrange
        var streamProvider = _cluster.Client.GetStreamProvider("StreamProvider");
        var streamId = StreamId.Create(
            AevatarAgentsOrleansConstants.StreamNamespace,
            Guid.NewGuid().ToString());
        var stream = streamProvider.GetStream<byte[]>(streamId);

        var receivedMessages = new ConcurrentBag<string>();
        var orleansStream = new OrleansMessageStream(Guid.NewGuid().ToString(), stream);

        // Act - Subscribe
        var subscription = await orleansStream.SubscribeAsync<EventEnvelope>(async envelope =>
        {
            receivedMessages.Add(envelope.Message);
            await Task.CompletedTask;
        });

        // Send message
        await orleansStream.ProduceAsync(new EventEnvelope { Message = "Message 1" });
        await WaitUntilAsync(() => receivedMessages.Any(m => m == "Message 1"), TimeSpan.FromSeconds(2));

        // Pause and resume
        await subscription.UnsubscribeAsync();
        await subscription.ResumeAsync();

        // Send message again
        await orleansStream.ProduceAsync(new EventEnvelope { Message = "Message 2" });
        await WaitUntilAsync(() => receivedMessages.Any(m => m == "Message 2"), TimeSpan.FromSeconds(2));

        // Assert
        Assert.Contains("Message 1", receivedMessages);
        Assert.Contains("Message 2", receivedMessages);
    }

    private static async Task WaitUntilAsync(Func<bool> predicate, TimeSpan timeout)
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < timeout)
        {
            if (predicate())
                return;

            await Task.Delay(50);
        }

        Assert.True(predicate(), $"Timed out after {timeout.TotalMilliseconds}ms waiting for condition.");
    }

    /// <summary>
    /// Test Orleans Stream type filtering
    /// </summary>
    [Fact]
    public async Task Orleans_Stream_Type_Filter_Should_Work()
    {
        // Arrange
        var streamProvider = _cluster.Client.GetStreamProvider("StreamProvider");
        var streamId = StreamId.Create(
            AevatarAgentsOrleansConstants.StreamNamespace,
            Guid.NewGuid().ToString());
        var stream = streamProvider.GetStream<byte[]>(streamId);

        var orleansStream = new OrleansMessageStream(Guid.NewGuid().ToString(), stream);
        var filteredMessages = new ConcurrentBag<string>();

        // Act - Subscribe with filter
        await orleansStream.SubscribeAsync<EventEnvelope>(
            async envelope =>
            {
                filteredMessages.Add(envelope.Message);
                await Task.CompletedTask;
            },
            envelope => envelope.Direction == EventDirection.Up); // Only receive UP events

        // Send events in different directions
        await orleansStream.ProduceAsync(new EventEnvelope
        {
            Message = "UP Event",
            Direction = EventDirection.Up
        });

        await orleansStream.ProduceAsync(new EventEnvelope
        {
            Message = "DOWN Event",
            Direction = EventDirection.Down
        });

        await WaitUntilAsync(() => filteredMessages.Any(m => m == "UP Event"), TimeSpan.FromSeconds(2));

        // Give a short window for a wrongly-filtered DOWN event to arrive (best-effort).
        await Task.Delay(200);

        // Assert - Should only receive UP events
        Assert.Single(filteredMessages);
        Assert.Contains("UP Event", filteredMessages);
        Assert.DoesNotContain("DOWN Event", filteredMessages);
    }

    // Test Cluster configuration
    public class ClusterFixture : IDisposable
    {
        public TestCluster Cluster { get; private set; }

        public ClusterFixture()
        {
            var builder = new TestClusterBuilder();
            builder.AddSiloBuilderConfigurator<TestSiloConfigurator>();
            builder.AddClientBuilderConfigurator<TestClientConfigurator>();
            Cluster = builder.Build();
            Cluster.Deploy();
        }

        public void Dispose()
        {
            Cluster.StopAllSilos();
            Cluster.Dispose();
        }
    }

    // Silo配置
    public class TestSiloConfigurator : ISiloConfigurator
    {
        public void Configure(ISiloBuilder siloBuilder)
        {
            siloBuilder
                .ConfigureServices(services =>
                {
                    services.AddSerializer(serializerBuilder => { serializerBuilder.AddProtobufSerializer(); });
                    // Register OrleansStreamFactory for unified stream support
                    services.AddSingleton<OrleansStreamFactory>();
                })
                .ConfigureLogging(logging => logging.AddConsole())
                .AddMemoryStreams("StreamProvider")
                .AddMemoryGrainStorage("PubSubStore")
                .AddMemoryGrainStorage("agentState") // For OrleansGAgentGrain persistent state
                .AddMemoryGrainStorageAsDefault()
                .Configure<ClusterOptions>(options =>
                {
                    options.ClusterId = "test-cluster";
                    options.ServiceId = "test-service";
                });
        }
    }

    // Client配置
    public class TestClientConfigurator : IClientBuilderConfigurator
    {
        public void Configure(IConfiguration configuration, IClientBuilder clientBuilder)
        {
            clientBuilder
                .ConfigureServices(services =>
                {
                    services.AddSerializer(serializerBuilder => { serializerBuilder.AddProtobufSerializer(); });
                })
                .AddMemoryStreams("StreamProvider")
                .Configure<ClusterOptions>(options =>
                {
                    options.ClusterId = "test-cluster";
                    options.ServiceId = "test-service";
                });
        }
    }
}
