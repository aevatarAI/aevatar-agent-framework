using Aevatar.VibeResearching.Agents.Contracts.Collab;

namespace Aevatar.VibeResearching.Knowledge;

/// <summary>
/// Policy interface for DAG node grounding decisions.
/// Determines which DAG nodes are safe to include as grounded context.
/// </summary>
public interface IDagGroundingPolicy
{
    /// <summary>
    /// Checks if a DAG node should be included for grounding (included in context).
    /// </summary>
    /// <param name="node">The DAG node to check.</param>
    /// <returns>True if the node should be grounded, false otherwise.</returns>
    bool ShouldIncludeForGrounding(SraDagNode node);
}
