using System.Text;
using System.Text.Json;
using Aevatar.Agents.AGUI;
using Aevatar.Agents.AI;
using Google.Protobuf.Collections;
using Google.Protobuf.WellKnownTypes;
using ScientificResearchAssistant.Api.Materials;
using ScientificResearchAssistant.Api.Paper;
using ScientificResearchAssistant.Api.Sessions;
using ScientificResearchAssistant.Api.Vibe.Brief;
using ScientificResearchAssistant.Api.Vibe.Delivery;
using ScientificResearchAssistant.Api.Vibe.Dag;
using ScientificResearchAssistant.Api.Vibe.Goals;
using ScientificResearchAssistant.Api.Vibe.Trace;
using ScientificResearchAssistant.Api.Workspace;
using ScientificResearchAssistant.Contracts.Collab;

namespace ScientificResearchAssistant.Api.Vibe;

internal sealed partial class VibeOrchestrator
{
    // ============================================================
    //  DAG consensus + persistence
    // ============================================================

    private async Task<DagRoundResult> RunDagConsensusAsync(
        ResearchSession session,
        string runId,
        MaterialsSnapshot materials,
        SraDagSnapshot currentDag,
        IReadOnlyDictionary<string, string> outputs,
        string? providerOverride,
        Action<string> emit,
        CancellationToken ct)
    {
        // Parse candidate mutation from dag_builder output (JSON).
        var candidateText = outputs.TryGetValue("dag_builder", out var x) ? x : string.Empty;
        var candidate = TryParseDagBuilderCandidate(session.Id, candidateText);

        if (candidate == null)
        {
            EmitSection(emit, "### DAG Consensus\n");
            emit("_No DAG candidate produced._\n\n");
            return new DagRoundResult(false, false, null, null, null, [], null);
        }

        // EMPTY mutation means "no change" (do not stage / do not block).
        if (candidate.UpsertNodes.Count == 0 && candidate.UpsertEdges.Count == 0)
        {
            EmitSection(emit, "### DAG Consensus\n");
            emit("_No DAG changes proposed._\n\n");
            return new DagRoundResult(false, false, candidate, null, null, [], null);
        }

        DagConsensusRunner.ConsensusResult cr;
        try
        {
            cr = await _consensus.RunAsync(new DagConsensusRunner.ConsensusInput(
                SessionId: session.Id,
                RunId: runId,
                Current: currentDag,
                Candidate: candidate,
                MaterialsContext: materials.RenderedContext,
                ProviderName: providerOverride,
                ConsensusK: null,
                MaxRounds: null,
                WorkerCount: null,
                MaxDepth: null), ct);
        }
        catch (Exception ex)
        {
            EmitSection(emit, "### DAG Consensus\n");
            emit($"[consensus error] {ex.Message}\n\n");
            return new DagRoundResult(false, true, candidate, null, null, ["consensus_exception"], null);
        }

        EmitSection(emit, $"### DAG Consensus ({cr.Workflow})\n");

        if (!cr.Ok || cr.Mutation == null)
        {
            var stagedPath = await _dag.WriteStagedAsync(session.Id, candidate, ct);

            session.Events.Publish(new CustomEvent
            {
                Timestamp = NowMs(),
                Name = "aevatar.vibe.consensus_blocked",
                Value = new
                {
                    sessionId = session.Id,
                    runId,
                    stagedPath,
                    redFlags = cr.RedFlags,
                    artifactPath = cr.ArtifactPath ?? ""
                }
            });

            emit($"**Blocked** ({cr.Workflow}, staged: `{stagedPath}`)\n\n");
            if (cr.RedFlags.Count > 0)
                emit($"RedFlags: {string.Join(", ", cr.RedFlags)}\n\n");

            return new DagRoundResult(false, true, candidate, null, stagedPath, cr.RedFlags, cr.ArtifactPath);
        }

        // Apply accepted mutation to snapshot.
        var applied = await _dag.ApplyMutationAsync(session.Id, cr.Mutation, ct);

        session.Events.Publish(new CustomEvent
        {
            Timestamp = NowMs(),
            Name = "aevatar.vibe.dag_updated",
            Value = new
            {
                sessionId = session.Id,
                runId,
                mutationId = cr.Mutation.MutationId,
                nodes = cr.Mutation.UpsertNodes.Count,
                edges = cr.Mutation.UpsertEdges.Count,
                updatedAt = applied.UpdatedAt?.ToDateTime().ToUniversalTime().ToString("O") ?? ""
            }
        });

        emit($"**Accepted** ({cr.Workflow}, mutationId: `{cr.Mutation.MutationId}`)\n\n");

        return new DagRoundResult(true, false, candidate, cr.Mutation, null, [], cr.ArtifactPath);
    }

}
