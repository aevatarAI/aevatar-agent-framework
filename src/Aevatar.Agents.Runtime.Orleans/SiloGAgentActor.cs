using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.Helpers;
using Google.Protobuf;
using Microsoft.Extensions.Logging;
using Orleans;

namespace Aevatar.Agents.Runtime.Orleans;

/// <summary>
/// Silo-side IGAgentActor implementation for Grain-to-Grain communication.
///
/// Goals:
/// - Keep ONE public API for users: IGAgentActor + IGAgentActorFactory
/// - When running inside Silo, avoid creating client proxies (IClusterClient)
/// - Delegate all operations to the underlying IGAgentGrain
/// </summary>
public sealed class SiloGAgentActor : IGAgentActor
{
    private readonly IGrainFactory _grainFactory;
    private readonly string _rawId;
    private readonly string _agentTypeName;
    private readonly ILogger _logger;
    private IGAgentGrain? _grain;

    public string Id { get; }

    public SiloGAgentActor(
        string rawId,
        string agentTypeName,
        IGrainFactory grainFactory,
        ILogger logger)
    {
        _rawId = rawId;
        _agentTypeName = agentTypeName;
        _grainFactory = grainFactory ?? throw new ArgumentNullException(nameof(grainFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        var agentTypeShortName = AgentId.GetAgentTypeShortName(_agentTypeName);
        Id = string.IsNullOrWhiteSpace(agentTypeShortName)
            ? _rawId
            : $"{agentTypeShortName}:{_rawId}";
    }

    public async Task ActivateAsync(CancellationToken ct = default)
    {
        _logger.LogDebug("Activating SiloGAgentActor {ActorId}, AgentType={AgentType}", Id, _agentTypeName);

        _grain = _grainFactory.GetGrain<IGAgentGrain>(Id);

        var isInitialized = await _grain.IsInitializedAsync();
        if (!isInitialized)
        {
            var ok = await _grain.InitializeAgentAsync(_agentTypeName);
            if (!ok)
                throw new InvalidOperationException($"Failed to initialize Agent {_agentTypeName} in Grain {Id}");
        }
    }

    public async Task DeactivateAsync(CancellationToken ct = default)
    {
        EnsureGrain();
        await _grain!.DeactivateAsync();
    }

    public IGAgent GetAgent()
    {
        // In Orleans runtime, agent lives inside Silo, no direct instance access.
        throw new NotSupportedException("Agent instance is not available in Orleans runtime. Use RPC via As<T>() instead.");
    }

    public Task<string> GetDescriptionAsync()
    {
        EnsureGrain();
        return _grain!.GetDescriptionAsync();
    }

    public Task<IReadOnlyList<string>> GetChildrenAsync()
    {
        EnsureGrain();
        return _grain!.GetChildrenAsync();
    }

    public Task<string?> GetParentAsync()
    {
        EnsureGrain();
        return _grain!.GetParentAsync();
    }

    public async Task HandleEventAsync(EventEnvelope envelope, CancellationToken ct = default)
    {
        EnsureGrain();

        using var ms = new MemoryStream();
        using var cos = new CodedOutputStream(ms);
        envelope.WriteTo(cos);
        cos.Flush();

        await _grain!.HandleEventAsync(ms.ToArray());
    }

    public async Task<byte[]> InvokeRpcAsync(byte[] requestBytes)
    {
        EnsureGrain();
        return await _grain!.InvokeRpcAsync(requestBytes);
    }

    public async Task<byte[]> InvokeReadOnlyRpcAsync(byte[] requestBytes)
    {
        EnsureGrain();
        return await _grain!.InvokeReadOnlyRpcAsync(requestBytes);
    }

    public async Task<string> PublishEventAsync<TEvent>(
        TEvent evt,
        EventDirection direction = EventDirection.Down,
        CancellationToken ct = default,
        bool isInternalCall = false)
        where TEvent : IMessage
    {
        EnsureGrain();

        var envelope = new EventEnvelope
        {
            Id = Guid.NewGuid().ToString(),
            PublisherId = isInternalCall ? Id : "",
            Payload = Google.Protobuf.WellKnownTypes.Any.Pack(evt),
            Direction = direction,
            Timestamp = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(DateTime.UtcNow),
            CorrelationId = Guid.NewGuid().ToString()
        };

        using var ms = new MemoryStream();
        using var cos = new CodedOutputStream(ms);
        envelope.WriteTo(cos);
        cos.Flush();

        return await _grain!.PublishEventAsync(ms.ToArray(), direction, isInternalCall);
    }

    public async Task<string> SendToAsync<TEvent>(
        string targetAgentId,
        TEvent evt,
        EventDirection onArrivalDirection = EventDirection.Unspecified,
        CancellationToken ct = default,
        bool isInternalCall = false)
        where TEvent : IMessage
    {
        EnsureGrain();

        var envelope = new EventEnvelope
        {
            Id = Guid.NewGuid().ToString(),
            PublisherId = isInternalCall ? Id : "",
            Payload = Google.Protobuf.WellKnownTypes.Any.Pack(evt),
            Direction = onArrivalDirection,
            Timestamp = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(DateTime.UtcNow),
            CorrelationId = Guid.NewGuid().ToString(),
            TargetAgentId = targetAgentId,
            OnArrivalDirection = onArrivalDirection
        };

        using var ms = new MemoryStream();
        using var cos = new CodedOutputStream(ms);
        envelope.WriteTo(cos);
        cos.Flush();

        return await _grain!.SendToAsync(targetAgentId, ms.ToArray(), onArrivalDirection, isInternalCall);
    }

    private void EnsureGrain()
    {
        if (_grain == null)
            throw new InvalidOperationException("Actor not activated. Call ActivateAsync first.");
    }
}


