using System.Text;
using System.Text.Json;
using Aevatar.Agents.AGUI;
using Aevatar.Agents.AI;
using Google.Protobuf.Collections;
using Google.Protobuf.WellKnownTypes;
using VibeResearching.Api.Paper;
using VibeResearching.Api.Sessions;
using VibeResearching.Api.Vibe.Brief;
using VibeResearching.Api.Vibe.Delivery;
using VibeResearching.Api.Vibe.Dag;
using VibeResearching.Api.Vibe.Goals;
using VibeResearching.Api.Vibe.Trace;
using VibeResearching.Api.Workspace;
using VibeResearching.Contracts.Collab;

namespace VibeResearching.Api.Vibe;

internal sealed partial class VibeOrchestrator
{
    // ============================================================
    //  DAG consensus + persistence
    // ============================================================

    private async Task<DagRoundResult> RunDagApplyAsync(
        ResearchSession session,
        string runId,
        SraDagSnapshot currentDag,
        IReadOnlyDictionary<string, string> outputs,
        Action<string> emit,
        CancellationToken ct)
    {
        // Query the currently Active milestone from Neo4j.
        // PlanNodes are stored with session.Id (not EffectiveDagId), so we use session.Id here.
        var activeMilestoneId = await GetActiveMilestoneFromGraphAsync(session.Id, ct);

        // Parse candidate mutation from dag_builder output (JSON).
        // Pass currentDag to validate motivatedByPlanNodeId references against existing milestones.
        // Pass activeMilestoneId as default for knowledge nodes without motivatedByPlanNodeId.
        var candidateText = outputs.TryGetValue("dag_builder", out var x) ? x : string.Empty;
        var candidate = TryParseDagBuilderCandidate(session.Id, candidateText, currentDag, activeMilestoneId);

        if (candidate == null)
        {
            EmitSection(emit, "### DAG Apply (no verification)\n");
            emit("_No DAG candidate produced._\n\n");
            return new DagRoundResult(false, false, null, null, null, [], null);
        }

        // EMPTY mutation means "no change" (do not stage / do not block).
        if (candidate.UpsertNodes.Count == 0 && candidate.UpsertEdges.Count == 0)
        {
            EmitSection(emit, "### DAG Apply (no verification)\n");
            emit("_No DAG changes proposed._\n\n");
            return new DagRoundResult(false, false, candidate, null, null, [], null);
        }

        // ------------------------------------------------------------
        // No verification / no consensus:
        // - Apply candidate directly.
        // - Future: we can attach a verifier pass that annotates nodes, but not block writes.
        // ------------------------------------------------------------
        try
        {
            var dagId = session.EffectiveDagId;
            var applied = await _core.Dag.ApplyMutationAsync(dagId, candidate, ct);

            session.Events.Publish(new CustomEvent
            {
                Timestamp = NowMs(),
                Name = "aevatar.vibe.dag_updated",
                Value = new
                {
                    sessionId = session.Id,
                    dagId,
                    runId,
                    mutationId = candidate.MutationId,
                    nodes = candidate.UpsertNodes.Count,
                    edges = candidate.UpsertEdges.Count,
                    updatedAt = applied.UpdatedAt?.ToDateTime().ToUniversalTime().ToString("O") ?? ""
                }
            });

            EmitSection(emit, "### DAG Apply (no verification)\n");
            emit($"**Applied** (mutationId: `{candidate.MutationId}`)\n\n");

            return new DagRoundResult(true, false, candidate, candidate, null, [], null);
        }
        catch (Exception ex)
        {
            EmitSection(emit, "### DAG Apply (no verification)\n");
            emit($"[dag apply error] {ex.Message}\n\n");
            return new DagRoundResult(false, true, candidate, null, null, ["apply_exception"], null);
        }
    }

    private async Task<int> PredictNextRoundIndexAsync(string sessionId, CancellationToken ct)
    {
        try
        {
            var prev = await _core.Trace.LoadLatestAsync(sessionId, max: 1, ct);
            return prev.Count == 0 ? 0 : prev[^1].RoundIndex + 1;
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>
    /// Query the currently Active milestone from Neo4j for the given session.
    /// Returns the node ID of the Active milestone, or null if none found.
    /// </summary>
    private async Task<string?> GetActiveMilestoneFromGraphAsync(string sessionId, CancellationToken ct)
    {
        try
        {
            var graphClient = _core.GraphFactory.CreateClient(sessionId);
            var planNodes = await graphClient.GetPlanNodesAsync(ct);

            var activeMilestone = planNodes.FirstOrDefault(p =>
                p.Status == Aevatar.Agents.Knowledge.Graph.Models.PlanNodeStatus.Active);

            return activeMilestone?.Id;
        }
        catch (Exception ex)
        {
            _host.Logger.LogDebug(ex, "[DagConsensus] Failed to query active milestone (best-effort)");
            return null;
        }
    }

}
