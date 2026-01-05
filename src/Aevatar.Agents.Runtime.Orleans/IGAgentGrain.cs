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
}