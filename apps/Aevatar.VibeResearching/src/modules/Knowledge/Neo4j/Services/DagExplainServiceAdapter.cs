using Aevatar.VibeResearching.Agents.Contracts.Collab;

namespace Aevatar.VibeResearching.Knowledge.Neo4j.Services;

/// <summary>
/// Instance-based adapter for the static DagExplainService.
/// Loads DAG snapshot and delegates to DagExplainService.Explain().
/// </summary>
public sealed class DagExplainServiceAdapter : IDagExplainService
{
    private readonly IDagRepository _dagRepository;

    public DagExplainServiceAdapter(IDagRepository dagRepository)
    {
        _dagRepository = dagRepository ?? throw new ArgumentNullException(nameof(dagRepository));
    }

    public async Task<SraDagExplain> ExplainNodeAsync(string sessionId, string nodeId, CancellationToken ct)
    {
        var snapshot = await _dagRepository.LoadSnapshotAsync(sessionId, ct);
        return DagExplainService.Explain(snapshot, nodeId);
    }
}
