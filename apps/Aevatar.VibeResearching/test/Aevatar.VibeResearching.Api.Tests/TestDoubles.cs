using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Aevatar.Agents.AI.Abstractions;
using Aevatar.Agents.AI.Abstractions.Configuration;
using Aevatar.Agents.AI.Abstractions.Providers;
using Aevatar.Agents.AI.Core.Embeddings;
using Aevatar.Agents.Core.Secrets;
using Aevatar.Agents.Knowledge.Graph;
using Microsoft.Extensions.AI;
using VibeResearching.Vibe.Pivot;
using VibeResearching.Vibe.Pivot.Messages;
using VibeResearching.Vibe.Pivot.Models;

namespace VibeResearching.Api.Tests;

internal sealed class TestSecretsStore : IAevatarUserSecretsStore
{
    private readonly Dictionary<string, string> _data = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, string> GetAll() => _data;

    public bool TryGet(string key, out string value) => _data.TryGetValue(key, out value!);

    public void Set(string key, string value) => _data[key] = value;

    public bool Remove(string key) => _data.Remove(key);
}

internal sealed class NullEmbeddingFactory : IAIAgentEmbeddingFactory
{
    public Task<IEmbeddingGenerator<string, Embedding<float>>?> CreateAsync(
        LLMProviderConfig providerConfig,
        CancellationToken cancellationToken = default)
        => Task.FromResult<IEmbeddingGenerator<string, Embedding<float>>?>(null);
}

internal sealed class TestLLMProviderFactory : ILLMProviderFactory
{
    private readonly TestLLMProvider _provider = new();

    public IAevatarLLMProvider GetProvider(string providerName)
        => _provider;

    public IAevatarLLMProvider GetDefaultProvider()
        => _provider;

    public IReadOnlyList<string> GetAvailableProviderNames()
        => new[] { "test-provider" };

    public bool HasProvider(string providerName)
        => !string.IsNullOrWhiteSpace(providerName);

    public LLMProviderConfig GetProviderConfig(string providerName)
        => WorkflowTestHelpers.BuildProviderConfig(providerName);

    public LLMProviderConfig GetDefaultProviderConfig()
        => WorkflowTestHelpers.BuildProviderConfig("test-provider");

    public IAevatarLLMProvider CreateProvider(
        LLMProviderConfig providerConfig,
        CancellationToken cancellationToken = default)
        => _provider;

    public Task<IAevatarLLMProvider> GetProviderAsync(
        string providerName,
        CancellationToken cancellationToken = default)
        => Task.FromResult<IAevatarLLMProvider>(_provider);

    public Task<IAevatarLLMProvider> GetDefaultProviderAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IAevatarLLMProvider>(_provider);
}

internal sealed class TestLLMProvider : IAevatarLLMProvider
{
    public Task<AevatarLLMResponse> GenerateAsync(
        AevatarLLMRequest request,
        CancellationToken cancellationToken = default)
    {
        var response = new AevatarLLMResponse
        {
            Content = "pong",
            AevatarStopReason = AevatarStopReason.Complete
        };
        return Task.FromResult(response);
    }

    public async IAsyncEnumerable<AevatarLLMToken> GenerateStreamAsync(
        AevatarLLMRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        yield return new AevatarLLMToken
        {
            Content = "pong",
            IsComplete = true
        };
        await Task.CompletedTask;
    }
}

internal sealed class NoopDirectionChangeDetector : IDirectionChangeDetector
{
    public Task<DirectionChangeIntent> DetectAsync(
        string sessionId,
        string messageId,
        string userMessage,
        string? currentDirection = null,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new DirectionChangeIntent
        {
            SessionId = sessionId,
            MessageId = messageId,
            IsDirectionChange = false,
            Confidence = 0.1,
            NewTopic = null,
            NeedsClarification = false
        });
    }
}

internal sealed class NoopPivotQueue : IPivotQueue
{
    public Task<PivotQueueResult> EnqueueAsync(
        DirectionChangeIntent intent,
        Func<CancellationToken, Task<PivotOperation>> pivotExecutor,
        CancellationToken cancellationToken = default)
        => Task.FromResult(new PivotQueueResult
        {
            SessionId = intent.SessionId,
            Queued = false,
            Position = 0,
            QueueFull = false,
            Operation = null
        });

    public int GetQueueDepth(string sessionId) => 0;

    public bool IsProcessing(string sessionId) => false;

    public IReadOnlyList<PivotQueueStatus> GetAllQueueStatuses() => Array.Empty<PivotQueueStatus>();

    public void CleanupSession(string sessionId)
    {
    }
}

internal sealed class NoopPivotOrchestrator : IPivotOrchestrator
{
    public Task<PivotOperation> ExecutePivotAsync(
        DirectionChangeIntent intent,
        string? oldDirection = null,
        CancellationToken cancellationToken = default)
        => Task.FromResult(PivotOperation.Create(intent));

    public Task<(IReadOnlyList<string> Cancelled, IReadOnlyList<string> Preserved, IReadOnlyList<string> Superseded)>
        ClassifyNodesForPivotAsync(
            IKnowledgeGraphClient client,
            Aevatar.Agents.Knowledge.Graph.Models.GraphSnapshot snapshot,
            DirectionChangeIntent intent,
            string pivotId,
            CancellationToken cancellationToken = default)
        => Task.FromResult<(IReadOnlyList<string>, IReadOnlyList<string>, IReadOnlyList<string>)>(([], [], []));

    public bool ShouldPreserveNode(
        Aevatar.Agents.Knowledge.Graph.Models.KnowledgeNode node,
        IReadOnlyList<string> preserveAspects)
        => false;

    public Task<Aevatar.Agents.Knowledge.Graph.Models.PlanNode> CreatePlanNodeAsync(
        string sessionId,
        string nodeId,
        string coreDescription,
        string detailedDescription,
        string? directionContext = null,
        IEnumerable<string>? dependsOn = null,
        CancellationToken cancellationToken = default)
        => Task.FromResult(new Aevatar.Agents.Knowledge.Graph.Models.PlanNode
        {
            Id = nodeId,
            SessionId = sessionId,
            CoreDescription = coreDescription,
            DetailedDescription = detailedDescription
        });
}

internal sealed class NoopAgentPivotCoordinator : IAgentPivotCoordinator
{
    public Task<PivotCoordinationResult> CoordinatePivotAsync(
        DirectionChangeIntent intent,
        string? oldDirection = null,
        CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        return Task.FromResult(new PivotCoordinationResult
        {
            SessionId = intent.SessionId,
            StartedAt = now,
            CompletedAt = now,
            DagOperation = PivotOperation.Create(intent),
            AllAgentsAcknowledged = true
        });
    }

    public void RegisterSessionAgents(string sessionId)
    {
    }

    public void UnregisterSessionAgents(string sessionId)
    {
    }

    public Task<bool> NotifyAgentAsync(
        string sessionId,
        string agentId,
        PivotEvent pivotEvent,
        IPivotAwareAgent? agent,
        CancellationToken cancellationToken = default)
        => Task.FromResult(true);
}
