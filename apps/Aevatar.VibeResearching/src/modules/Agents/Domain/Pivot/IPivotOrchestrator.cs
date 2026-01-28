using Microsoft.Extensions.Logging;
using Aevatar.Agents.Knowledge.Graph;
using Aevatar.Agents.Knowledge.Graph.Models;
using Aevatar.VibeResearching.Agents.Pivot.Models;

namespace Aevatar.VibeResearching.Agents.Pivot;

/// <summary>
/// Interface for orchestrating research direction pivot operations.
/// </summary>
public interface IPivotOrchestrator
{
    /// <summary>
    /// Executes a research direction pivot operation.
    /// </summary>
    /// <param name="intent">The detected direction change intent.</param>
    /// <param name="oldDirection">Optional description of the previous research direction.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The completed pivot operation with results.</returns>
    Task<PivotOperation> ExecutePivotAsync(
        DirectionChangeIntent intent,
        string? oldDirection = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Classifies nodes in the graph for pivot handling.
    /// </summary>
    /// <param name="client">Knowledge graph client for the session.</param>
    /// <param name="snapshot">Current graph snapshot.</param>
    /// <param name="intent">Direction change intent with preservation hints.</param>
    /// <param name="pivotId">Unique pivot operation identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// Tuple of (cancelled node IDs, preserved node IDs, superseded node IDs).
    /// </returns>
    Task<(IReadOnlyList<string> Cancelled, IReadOnlyList<string> Preserved, IReadOnlyList<string> Superseded)>
        ClassifyNodesForPivotAsync(
            IKnowledgeGraphClient client,
            GraphSnapshot snapshot,
            DirectionChangeIntent intent,
            string pivotId,
            CancellationToken cancellationToken = default);

    /// <summary>
    /// Determines if a node should be preserved based on aspect matching.
    /// </summary>
    /// <param name="node">The node to evaluate.</param>
    /// <param name="preserveAspects">List of aspects/keywords to preserve.</param>
    /// <returns>True if node matches any preservation aspect.</returns>
    bool ShouldPreserveNode(KnowledgeNode node, IReadOnlyList<string> preserveAspects);

    /// <summary>
    /// Creates a new plan node for the new research direction.
    /// </summary>
    /// <param name="sessionId">Session identifier.</param>
    /// <param name="nodeId">Unique node identifier.</param>
    /// <param name="coreDescription">Core description of the plan.</param>
    /// <param name="detailedDescription">Detailed description.</param>
    /// <param name="directionContext">Research direction context.</param>
    /// <param name="dependsOn">Optional dependencies.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The created plan node.</returns>
    Task<PlanNode> CreatePlanNodeAsync(
        string sessionId,
        string nodeId,
        string coreDescription,
        string detailedDescription,
        string? directionContext = null,
        IEnumerable<string>? dependsOn = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Rolls back to a specific pivot point.
    /// </summary>
    Task<object> RollbackToPivotAsync(
        string sessionId,
        string pivotId,
        bool preserveNewCompleted,
        CancellationToken ct = default);

    /// <summary>
    /// Rolls back to the most recent pivot point.
    /// </summary>
    Task<object> RollbackToMostRecentAsync(
        string sessionId,
        bool preserveNewCompleted,
        CancellationToken ct = default);
}
