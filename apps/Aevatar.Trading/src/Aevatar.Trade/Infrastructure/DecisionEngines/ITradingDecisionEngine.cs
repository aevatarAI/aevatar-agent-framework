namespace Aevatar.Trade.Infrastructure.DecisionEngines;

/// <summary>
/// Trading decision engine abstraction.
///
/// - Direct: Coordinator calls LLM directly.
/// - CognitiveMesh: Coordinator delegates to Cognitive Mesh service and gets back a JSON decision.
///
/// Output contract:
/// - Must return a JSON string that <see cref="Aevatar.Trade.Agents.Coordinator.TradingCoordinatorAgent"/>
///   can parse into <see cref="TradingDecisionEvent"/>.
/// </summary>
public interface ITradingDecisionEngine
{
    string Name { get; }

    Task<string> GetDecisionJsonAsync(
        string prompt,
        string? cycleId = null,
        CancellationToken ct = default);
}


