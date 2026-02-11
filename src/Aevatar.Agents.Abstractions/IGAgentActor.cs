using Google.Protobuf;

namespace Aevatar.Agents.Abstractions;

/// <summary>
/// Agent Actor runtime abstraction interface.
/// Responsibilities: Hierarchy management, Stream subscription, Event routing, Lifecycle management.
/// </summary>
public interface IGAgentActor : IEventPublisher
{
    /// <summary>
    /// Actor Identifier (equivalent to associated <see cref="IGAgent.Id"/>, **unified format**).
    ///
    /// Format: <c>"AgentTypeShortName:RawId"</c>
    /// Example: <c>"ChatAgent:12345678-..."</c>
    ///
    /// NOTE:
    /// - RawId (usually Guid string) can be passed during creation, system will automatically prepend prefix
    /// - All subsequent Manager/Stream/Hierarchy operations should use this full format
    /// </summary>
    string Id { get; }

    /// <summary>
    /// Get the associated Agent instance.
    /// Note: In Orleans mode, Agent runs in Silo, so this may throw NotSupportedException.
    /// Use GetDescriptionAsync() for safe remote access.
    /// </summary>
    IGAgent GetAgent();

    /// <summary>
    /// Get Agent description (safe for remote access).
    /// </summary>
    Task<string> GetDescriptionAsync();

    // ============ Hierarchy Inspection ============

    /// <summary>
    /// Get all child Agent IDs. Hierarchy mutations should be performed via ActorHierarchyCoordinator
    /// or IGAgentActorManager.LinkParentChildAsync to keep parent/child routers in sync.
    /// </summary>
    Task<IReadOnlyList<string>> GetChildrenAsync();

    /// <summary>
    /// Get the parent Agent ID.
    /// </summary>
    Task<string?> GetParentAsync();

    // ============ Event Publishing and Routing ============

    /// <summary>
    /// Handle received events.
    /// </summary>
    /// <param name="envelope">Event envelope.</param>
    /// <param name="ct">Cancellation token.</param>
    Task HandleEventAsync(EventEnvelope envelope, CancellationToken ct = default);

    // ============ Lifecycle ============

    /// <summary>
    /// Activate the Actor.
    /// </summary>
    Task ActivateAsync(CancellationToken ct = default);

    /// <summary>
    /// Deactivate the Actor.
    /// </summary>
    Task DeactivateAsync(CancellationToken ct = default);

    /// <summary>
    /// Invoke RPC method on Agent via Protobuf (for write operations).
    /// For Local runtime, this may directly call the method.
    /// For Orleans runtime, this calls the Grain RPC method (serial execution).
    /// </summary>
    /// <param name="requestBytes">RpcRequest serialized bytes</param>
    /// <returns>RpcResponse serialized bytes</returns>
    Task<byte[]> InvokeRpcAsync(byte[] requestBytes);
    
    /// <summary>
    /// Invoke read-only RPC method on Agent via Protobuf.
    /// For Local runtime, same as InvokeRpcAsync.
    /// For Orleans runtime, this calls the [AlwaysInterleave] Grain method (concurrent execution).
    /// Use this for methods marked with [ReadOnly] attribute.
    /// </summary>
    /// <param name="requestBytes">RpcRequest serialized bytes</param>
    /// <returns>RpcResponse serialized bytes</returns>
    Task<byte[]> InvokeReadOnlyRpcAsync(byte[] requestBytes);
}