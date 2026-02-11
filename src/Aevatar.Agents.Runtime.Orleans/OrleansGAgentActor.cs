using Aevatar.Agents;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.Context;
using Aevatar.Agents.Abstractions.Helpers;
using Aevatar.Agents.Core;
using Aevatar.Agents.Core.Context;
using Aevatar.Agents.Core.Internal;
using Aevatar.Agents.Runtime.Orleans.Stream;
using Google.Protobuf;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans.Streams;

namespace Aevatar.Agents.Runtime.Orleans;

/// <summary>
/// Orleans Agent Actor - Lightweight Grain Proxy
/// 
/// This actor acts as a proxy to the OrleansGAgentGrain.
/// All business logic is executed in the Grain (Silo) side.
/// 
/// Responsibilities:
/// 1. Forward events to Grain via Stream (non-blocking, async)
/// 2. Use RPC only for synchronous operations (GetDescription, hierarchy)
/// 3. Provide IGAgentActor interface for HttpApi layer
/// </summary>
public class OrleansGAgentActor : IGAgentActor, IActorHierarchyOperations
{
    private readonly IGrainFactory _grainFactory;
    private readonly IStreamProvider? _orleansStreamProvider;
    private readonly StreamingOptions _streamingOptions;
    private readonly IMessageStreamProvider? _externalStreamProvider;
    private readonly MessageStreamProviderOptions _providerOptions;
    private readonly AgentContextPropagator? _contextPropagator;
    
    // Cached Grain reference (for RPC operations only)
    private IGAgentGrain? _grain;
    
    // Stream for sending events (non-blocking)
    private IMessageStream? _myStream;
    
    // Agent metadata (from Grain)
    private string _id;
    private string _agentTypeName;
    
    // Logger
    protected ILogger Logger { get; }

    /// <summary>
    /// Returns the full GrainKey format (AgentTypeShortName:AgentId).
    /// This is consistent with how grains are addressed in Orleans.
    /// </summary>
    public string Id => $"{AgentId.GetAgentTypeShortName(_agentTypeName)}:{_id}";

    public OrleansGAgentActor(
        string id,
        string agentTypeName,
        IGrainFactory grainFactory,
        IStreamProvider? orleansStreamProvider,
        StreamingOptions streamingOptions,
        ILogger<OrleansGAgentActor> logger,
        IMessageStreamProvider? externalStreamProvider = null,
        IOptions<MessageStreamProviderOptions>? providerOptions = null,
        AgentContextPropagator? contextPropagator = null)
    {
        _id = id;
        _agentTypeName = agentTypeName;
        _grainFactory = grainFactory ?? throw new ArgumentNullException(nameof(grainFactory));
        _orleansStreamProvider = orleansStreamProvider;
        _streamingOptions = streamingOptions ?? new StreamingOptions();
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _externalStreamProvider = externalStreamProvider;
        _providerOptions = providerOptions?.Value ?? new MessageStreamProviderOptions();
        _contextPropagator = contextPropagator;
    }

    #region IGAgentActor Implementation

    /// <summary>
    /// Activate actor - Initialize Grain with Agent type
    /// Business State is stored via IStateStore (per-type collections)
    /// Orleans Grain State only stores metadata (AgentId, ParentId, Children)
    /// </summary>
    public async Task ActivateAsync(CancellationToken ct = default)
    {
        Logger.LogInformation("Activating Orleans Actor proxy {ActorId}, AgentType: {AgentType}", _id, _agentTypeName);

        // Get non-generic Grain reference
        // Grain ID = AgentTypeShortName:AgentId
        //
        // IMPORTANT:
        // AgentId is NOT guaranteed to be globally unique across different Agent types in this framework.
        // Using only agentId as the grain key would cause type collisions (wrong Agent instance reused).
        var agentTypeShortName = AgentId.GetAgentTypeShortName(_agentTypeName);
        var grainId = $"{agentTypeShortName}:{_id}";
        _grain = _grainFactory.GetGrain<IGAgentGrain>(grainId);

        // Initialize Agent in Grain (Silo side)
        // Agent ID is derived from Grain's PrimaryKey, no need to pass separately
        var success = await _grain.InitializeAgentAsync(_agentTypeName);
        if (!success)
        {
            throw new InvalidOperationException(
                $"Failed to initialize Agent {_agentTypeName} in Grain {grainId}");
        }

        // Initialize Stream for async message sending
        await InitializeStreamAsync(ct);

        Logger.LogInformation("✅ Orleans Actor proxy {GrainId} activated, Agent running in Silo", grainId);
    }

    /// <summary>
    /// Initialize stream for sending events
    /// Uses MassTransit if configured, otherwise falls back to Orleans Stream
    /// </summary>
    private async Task InitializeStreamAsync(CancellationToken ct)
    {
        var providerType = _providerOptions.Provider;
        if (_providerOptions.Runtime.TryGetValue("Orleans", out var runtimeProvider))
        {
            providerType = runtimeProvider;
        }

        if (providerType == "MassTransit" && _externalStreamProvider != null)
        {
            // Use MassTransit stream with full GrainKey format (AgentType:AgentId)
            // This ensures OrleansMassTransitEventHandler routes to correct Grain
            var agentTypeShortName = AgentId.GetAgentTypeShortName(_agentTypeName);
            var grainKey = $"{agentTypeShortName}:{_id}";
            _myStream = _externalStreamProvider.GetStream(grainKey, agentTypeShortName);
            Logger.LogDebug("Using MassTransit stream for Actor {ActorId}, GrainKey: {GrainKey}", _id, grainKey);
        }
        else if (_orleansStreamProvider != null)
        {
            // Use Orleans stream - use same StreamId format as Grain (AgentType:AgentId)
            var streamNamespace = _streamingOptions.DefaultStreamNamespace ?? "AevatarAgents";
            var agentTypeShortName = AgentId.GetAgentTypeShortName(_agentTypeName);
            var streamKey = $"{agentTypeShortName}:{_id}";  // Must match Grain's grainKey
            var orleansStream = _orleansStreamProvider.GetStream<byte[]>(StreamId.Create(streamNamespace, streamKey));
            _myStream = new OrleansMessageStream(_id, orleansStream);
            Logger.LogDebug("Using Orleans stream for Actor {ActorId}, StreamKey: {StreamKey}", _id, streamKey);
        }

        if (_myStream == null)
        {
            throw new InvalidOperationException(
                $"No stream provider available for Actor {_id}. " +
                "Configure either MassTransit or Orleans stream provider.");
        }
    }

    /// <summary>
    /// Deactivate actor
    /// </summary>
    public async Task DeactivateAsync(CancellationToken ct = default)
    {
        Logger.LogInformation("Deactivating Orleans Actor proxy {ActorId}", _id);

        if (_grain != null)
        {
            await _grain.DeactivateAsync();
        }
    }

    /// <summary>
    /// Publish event - Send to own stream (Grain subscribes and processes)
    /// This is async and non-blocking - returns immediately after sending to stream
    /// </summary>
    /// <param name="isInternalCall">If true, keeps PublisherId; if false (default), clears it for external calls</param>
    public async Task<string> PublishEventAsync<TEvent>(
        TEvent evt, 
        EventDirection direction = EventDirection.Down, 
        CancellationToken ct = default,
        bool isInternalCall = false) 
        where TEvent : IMessage
    {
        // Create EventEnvelope
        var envelope = new EventEnvelope
        {
            Id = Guid.NewGuid().ToString(),
            // External calls: empty PublisherId allows Agent to handle the event
            // Internal calls: set to actor ID for self-handling check
            PublisherId = isInternalCall ? _id : "",
            Payload = Google.Protobuf.WellKnownTypes.Any.Pack(evt),
            Direction = direction,
            Timestamp = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(DateTime.UtcNow),
            CorrelationId = Guid.NewGuid().ToString()
        };

        // Inject context into envelope for propagation across agent boundaries
        _contextPropagator?.InjectContext(envelope);

        // Send via Stream (async, non-blocking)
        if (_myStream == null)
        {
            throw new InvalidOperationException(
                $"No stream available for Actor {_id}. Stream must be initialized before publishing events.");
        }

        await _myStream.ProduceAsync(envelope, ct);
        Logger.LogDebug("Published event {EventId} via stream for Actor {ActorId}", envelope.Id, _id);

        return envelope.Id;
    }

    /// <summary>
    /// Point-to-point send - Send to target agent's stream
    /// This is async and non-blocking - returns immediately after sending to stream
    /// </summary>
    /// <param name="isInternalCall">If true, keeps PublisherId; if false (default), clears it for external calls</param>
    public async Task<string> SendToAsync<TEvent>(
        string targetAgentId,
        TEvent evt,
        EventDirection onArrivalDirection = EventDirection.Unspecified,
        CancellationToken ct = default,
        bool isInternalCall = false)
        where TEvent : IMessage
    {
        // Create EventEnvelope for P2P
        var envelope = new EventEnvelope
        {
            Id = Guid.NewGuid().ToString(),
            PublisherId = isInternalCall ? _id : "",
            Payload = Google.Protobuf.WellKnownTypes.Any.Pack(evt),
            Direction = onArrivalDirection,
            Timestamp = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(DateTime.UtcNow),
            CorrelationId = Guid.NewGuid().ToString(),
            TargetAgentId = targetAgentId,
            OnArrivalDirection = onArrivalDirection
        };

        Logger.LogDebug(
            "Actor {ActorId} sending P2P event {EventId} to {TargetAgentId}, onArrival={OnArrivalDirection}",
            _id, envelope.Id, targetAgentId, onArrivalDirection);

        // Get target agent's stream and send
        var targetStream = GetTargetStream(targetAgentId);
        if (targetStream == null)
        {
            throw new InvalidOperationException(
                $"No stream available for target Agent {targetAgentId}. Stream provider must be configured.");
        }

        await targetStream.ProduceAsync(envelope, ct);
        Logger.LogDebug("Sent P2P event {EventId} via stream to {TargetAgentId}", envelope.Id, targetAgentId);

        return envelope.Id;
    }

    /// <summary>
    /// Get stream for target agent.
    /// Expects targetAgentId in full GrainKey format (AgentTypeShortName:AgentId)
    /// to match Grain's subscription.
    /// </summary>
    private IMessageStream? GetTargetStream(string targetAgentId)
    {
        var providerType = _providerOptions.Provider;
        if (_providerOptions.Runtime.TryGetValue("Orleans", out var runtimeProvider))
        {
            providerType = runtimeProvider;
        }

        if (providerType == "MassTransit" && _externalStreamProvider != null)
        {
            // targetAgentId is already in full GrainKey format (TypeName:Id)
            return _externalStreamProvider.GetStream(targetAgentId, null);
        }
        else if (_orleansStreamProvider != null)
        {
            // targetAgentId is already in full GrainKey format (TypeName:Id)
            // Use it directly as the StreamKey
            var streamNamespace = _streamingOptions.DefaultStreamNamespace ?? "AevatarAgents";
            var orleansStream = _orleansStreamProvider.GetStream<byte[]>(StreamId.Create(streamNamespace, targetAgentId));
            return new OrleansMessageStream(targetAgentId, orleansStream);
        }

        return null;
    }

    /// <summary>
    /// Handle event - Forward to Grain for processing in Silo
    /// </summary>
    public async Task HandleEventAsync(EventEnvelope envelope, CancellationToken ct = default)
    {
        EnsureGrain();

        Logger.LogDebug("Forwarding event {EventId} to Grain {GrainId}", envelope.Id, _id);

        // Serialize and forward to Grain
        using var stream = new MemoryStream();
        using var codedOutput = new CodedOutputStream(stream);
        envelope.WriteTo(codedOutput);
        codedOutput.Flush();

        await _grain!.HandleEventAsync(stream.ToArray());
    }

    /// <summary>
    /// Get agent description from Grain
    /// </summary>
    public async Task<string> GetDescriptionAsync()
    {
        EnsureGrain();
        return await _grain!.GetDescriptionAsync();
    }

    #endregion

    #region Hierarchy Management

    public async Task SetParentAsync(string parentId)
    {
        EnsureGrain();
        await _grain!.SetParentAsync(parentId);
    }

    public async Task SetParentAsync(string parentId, CancellationToken ct)
    {
        EnsureGrain();
        await _grain!.SetParentAsync(parentId);
    }

    public async Task ClearParentAsync()
    {
        EnsureGrain();
        await _grain!.ClearParentAsync();
    }

    public async Task ClearParentAsync(CancellationToken ct)
    {
        EnsureGrain();
        await _grain!.ClearParentAsync();
    }

    public async Task AddChildAsync(string childId)
    {
        EnsureGrain();
        await _grain!.AddChildAsync(childId);
    }

    public async Task AddChildAsync(string childId, CancellationToken ct)
    {
        EnsureGrain();
        await _grain!.AddChildAsync(childId);
    }

    public async Task RemoveChildAsync(string childId)
    {
        EnsureGrain();
        await _grain!.RemoveChildAsync(childId);
    }

    public async Task RemoveChildAsync(string childId, CancellationToken ct)
    {
        EnsureGrain();
        await _grain!.RemoveChildAsync(childId);
    }

    public async Task<string?> GetParentAsync()
    {
        EnsureGrain();
        return await _grain!.GetParentAsync();
    }

    public async Task<IReadOnlyList<string>> GetChildrenAsync()
    {
        EnsureGrain();
        return await _grain!.GetChildrenAsync();
    }

    #endregion

    #region Helper Methods

    private void EnsureGrain()
    {
        if (_grain == null)
        {
            var agentTypeShortName = AgentId.GetAgentTypeShortName(_agentTypeName);
            var grainId = $"{agentTypeShortName}:{_id}";
            _grain = _grainFactory.GetGrain<IGAgentGrain>(grainId);
        }
    }

    /// <summary>
    /// Get Agent instance - Not available in Orleans mode
    /// Agent runs in Silo, not in Client
    /// </summary>
    public IGAgent GetAgent()
    {
        throw new NotSupportedException(
            "Agent instance is not available on client side. " +
            "In Orleans mode, Agent runs in Silo (Grain). " +
            "Use Grain RPC methods instead.");
    }

    /// <summary>
    /// Invoke RPC method on Agent via Protobuf
    /// </summary>
    /// <param name="requestBytes">RpcRequest serialized bytes</param>
    /// <returns>RpcResponse serialized bytes</returns>
    public async Task<byte[]> InvokeRpcAsync(byte[] requestBytes)
    {
        EnsureGrain();
        return await _grain!.InvokeRpcAsync(requestBytes);
    }

    /// <summary>
    /// Invoke read-only RPC method (uses [AlwaysInterleave] for concurrent execution)
    /// </summary>
    public async Task<byte[]> InvokeReadOnlyRpcAsync(byte[] requestBytes)
    {
        EnsureGrain();
        return await _grain!.InvokeReadOnlyRpcAsync(requestBytes);
    }

    #endregion
}
