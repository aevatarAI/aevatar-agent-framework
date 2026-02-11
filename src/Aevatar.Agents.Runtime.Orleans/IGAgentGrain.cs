using Aevatar.Agents.Abstractions;
using Google.Protobuf;
using Orleans;
using Orleans.Concurrency;

namespace Aevatar.Agents.Runtime.Orleans;

/// <summary>
/// Orleans Grain interface (base interface)
/// Agent business logic executes within Grain (Silo)
/// </summary>
public interface IGAgentGrain : IGrainWithStringKey
{
    /// <summary>
    /// Get associated Agent ID
    /// [AlwaysInterleave] allows this to execute even when Grain is processing other requests
    /// </summary>
    [AlwaysInterleave]
    Task<string> GetIdAsync();

    /// <summary>
    /// Initialize Agent instance (created within Silo)
    /// Agent ID obtained from Grain's PrimaryKey (Grain ID = Agent ID)
    /// </summary>
    /// <param name="agentTypeName">Assembly qualified name of Agent type</param>
    /// <returns>Whether initialization succeeded</returns>
    Task<bool> InitializeAgentAsync(string agentTypeName);

    /// <summary>
    /// Check if Agent is initialized
    /// [AlwaysInterleave] allows concurrent read access
    /// </summary>
    [AlwaysInterleave]
    Task<bool> IsInitializedAsync();

    /// <summary>
    /// Get Agent description
    /// [AlwaysInterleave] allows this read-only operation to execute without waiting for other calls
    /// </summary>
    [AlwaysInterleave]
    Task<string> GetDescriptionAsync();

    /// <summary>
    /// Handle event (execute business logic within Silo)
    /// </summary>
    Task HandleEventAsync(byte[] envelopeBytes);

    /// <summary>
    /// Publish event by envelope bytes (non-blocking, via Stream).
    /// Used by Silo-internal actor to keep a single IGAgentActor API surface.
    /// </summary>
    /// <param name="envelopeBytes">EventEnvelope serialized bytes</param>
    /// <param name="direction">Propagation direction</param>
    /// <param name="isInternalCall">
    /// If true, keeps PublisherId for self-handling check; if false, clears PublisherId so Agent can handle the event.
    /// </param>
    /// <returns>Event ID</returns>
    Task<string> PublishEventAsync(byte[] envelopeBytes, EventDirection direction = EventDirection.Down, bool isInternalCall = false);

    /// <summary>
    /// Point-to-point send by envelope bytes (non-blocking, via Stream).
    /// Used by Silo-internal actor to keep a single IGAgentActor API surface.
    /// </summary>
    /// <param name="targetAgentId">Target agent id (full ActorId)</param>
    /// <param name="envelopeBytes">EventEnvelope serialized bytes</param>
    /// <param name="onArrivalDirection">Propagation direction after arrival</param>
    /// <param name="isInternalCall">Same semantics as PublishEventAsync</param>
    /// <returns>Event ID</returns>
    Task<string> SendToAsync(string targetAgentId, byte[] envelopeBytes, EventDirection onArrivalDirection = EventDirection.Unspecified, bool isInternalCall = false);

    /// <summary>
    /// Add child Agent
    /// </summary>
    Task AddChildAsync(string childId);

    /// <summary>
    /// Remove child Agent
    /// </summary>
    Task RemoveChildAsync(string childId);

    /// <summary>
    /// Set parent Agent
    /// </summary>
    Task SetParentAsync(string parentId);

    /// <summary>
    /// Clear parent Agent
    /// </summary>
    Task ClearParentAsync();

    /// <summary>
    /// Get all child Agent IDs
    /// [AlwaysInterleave] allows concurrent read access
    /// </summary>
    [AlwaysInterleave]
    Task<IReadOnlyList<string>> GetChildrenAsync();

    /// <summary>
    /// Get parent Agent ID
    /// [AlwaysInterleave] allows concurrent read access
    /// </summary>
    [AlwaysInterleave]
    Task<string?> GetParentAsync();

    /// <summary>
    /// Deactivate
    /// </summary>
    Task DeactivateAsync();

    /// <summary>
    /// Protobuf RPC method invocation
    /// </summary>
    /// <param name="requestBytes">RpcRequest serialized bytes</param>
    /// <returns>RpcResponse serialized bytes</returns>
    Task<byte[]> InvokeRpcAsync(byte[] requestBytes);
    
    /// <summary>
    /// Protobuf RPC method invocation (for read-only operations)
    /// [AlwaysInterleave] allows concurrent execution - safe for read-only operations
    /// Use this for methods marked with [ReadOnly] attribute
    /// </summary>
    /// <param name="requestBytes">RpcRequest serialized bytes</param>
    /// <returns>RpcResponse serialized bytes</returns>
    [AlwaysInterleave]
    Task<byte[]> InvokeReadOnlyRpcAsync(byte[] requestBytes);
}