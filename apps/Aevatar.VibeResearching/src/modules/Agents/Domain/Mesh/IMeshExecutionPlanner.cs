using Aevatar.CognitiveMesh.Dsl.Models;
using Aevatar.VibeResearching.Agents.Mesh.Services;

namespace Aevatar.VibeResearching.Agents.Mesh;

/// <summary>
/// Domain interface for mesh execution planner.
/// Implementation lives in infrastructure layer.
/// </summary>
public interface IMeshExecutionPlanner
{
    /// <summary>
    /// Plans mesh execution.
    /// </summary>
    MeshPlanResult Plan(string sessionId, string runId, MeshDefinition definition);
}
