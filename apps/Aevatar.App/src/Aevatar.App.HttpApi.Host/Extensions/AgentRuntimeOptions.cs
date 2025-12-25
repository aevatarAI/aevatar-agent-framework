namespace Aevatar.App.HttpApi.Host.Extensions;

/// <summary>
/// Agent Runtime Type Enum
/// Defines which runtime implementation to use for agents
/// </summary>
public enum AgentRuntimeType
{
    /// <summary>
    /// Local runtime (in-memory, single process)
    /// Fast, no external dependencies
    /// Perfect for development and testing
    /// </summary>
    Local,

    /// <summary>
    /// Orleans runtime (distributed, clustered)
    /// Persistent state, horizontal scaling
    /// Production-ready for distributed systems
    /// </summary>
    Orleans
}

/// <summary>
/// Agent Runtime Configuration Options
/// Controls which agent runtime implementation to use
/// </summary>
public class AgentRuntimeOptions
{
    public const string SectionName = "AgentRuntime";

    /// <summary>
    /// Runtime type to use
    /// Default: Local (no external dependencies)
    /// </summary>
    public AgentRuntimeType RuntimeType { get; set; } = AgentRuntimeType.Local;

    /// <summary>
    /// Orleans configuration (only used when RuntimeType is Orleans)
    /// </summary>
    public OrleansRuntimeOptions Orleans { get; set; } = new();
}

/// <summary>
/// Orleans Runtime Configuration (Client-side)
/// Note: SiloPort/GatewayPort/UseLocalhostClustering are Silo-side configs, not needed here
/// </summary>
public class OrleansRuntimeOptions
{
    /// <summary>
    /// Cluster ID for Orleans (must match Silo config)
    /// </summary>
    public string ClusterId { get; set; } = "aevatar-cluster";

    /// <summary>
    /// Service ID for Orleans (must match Silo config)
    /// </summary>
    public string ServiceId { get; set; } = "aevatar-service";
    
    /// <summary>
    /// Reserved for Orleans streaming (not used when MessageStream.Provider=MassTransit)
    /// </summary>
    // Intentionally removed: StreamProviderName (we use MassTransit for all streaming)
}

