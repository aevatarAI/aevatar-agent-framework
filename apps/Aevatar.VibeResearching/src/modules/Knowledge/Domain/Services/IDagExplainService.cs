using Aevatar.VibeResearching.Agents.Contracts.Collab;

namespace Aevatar.VibeResearching.Knowledge;

/// <summary>
/// Domain service interface for DAG node explanation and dependency analysis.
/// Analyzes node dependencies, cycles, missing dependencies, and provability.
/// </summary>
public interface IDagExplainService
{
    /// <summary>
    /// Explains a DAG node including its dependencies, cycles, and provability status.
    /// </summary>
    /// <param name="sessionId">Session ID to load the DAG from.</param>
    /// <param name="nodeId">Node ID to explain.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>DAG explanation with dependencies, cycle detection, and provability analysis.</returns>
    Task<SraDagExplain> ExplainNodeAsync(string sessionId, string nodeId, CancellationToken ct);
}
