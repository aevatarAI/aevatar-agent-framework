namespace Aevatar.VibeResearching.Agents;

/// <summary>
/// Constants for mesh orchestration and multi-agent coordination.
/// </summary>
public static class MeshConsts
{
    /// <summary>
    /// Maximum number of agents in a mesh definition.
    /// </summary>
    public const int MaxAgentsPerMesh = 50;

    /// <summary>
    /// Maximum number of edges (connections) in a mesh.
    /// </summary>
    public const int MaxEdgesPerMesh = 200;

    /// <summary>
    /// Maximum number of concurrent worker executions.
    /// </summary>
    public const int MaxConcurrentWorkers = 10;

    /// <summary>
    /// Default timeout in seconds for mesh execution.
    /// </summary>
    public const int DefaultMeshExecutionTimeoutSeconds = 300; // 5 minutes

    /// <summary>
    /// Maximum depth for mesh execution plan.
    /// </summary>
    public const int MaxMeshExecutionDepth = 20;
}
