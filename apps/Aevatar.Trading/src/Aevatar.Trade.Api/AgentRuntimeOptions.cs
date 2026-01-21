namespace Aevatar.Trade.Api;

/// <summary>
/// Agent runtime configuration
/// </summary>
public class AgentRuntimeOptions
{
    public const string SectionName = "AgentRuntime";

    public AgentRuntimeType RuntimeType { get; set; } = AgentRuntimeType.Local;
    public OrleansOptions Orleans { get; set; } = new();
}

/// <summary>
/// Runtime type
/// </summary>
public enum AgentRuntimeType
{
    Local,
    Orleans,
    ProtoActor
}

/// <summary>
/// Orleans configuration
/// </summary>
public class OrleansOptions
{
    public string ClusterId { get; set; } = "trade-cluster";
    public string ServiceId { get; set; } = "trade-service";
    public bool UseLocalhostClustering { get; set; } = true;
    public int SiloPort { get; set; } = 11111;
    public int GatewayPort { get; set; } = 30000;
}
