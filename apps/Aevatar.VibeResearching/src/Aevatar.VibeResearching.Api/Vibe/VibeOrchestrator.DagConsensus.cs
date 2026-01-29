using System.Text;
using System.Text.Json;
using Aevatar.Agents.AGUI;
using Aevatar.Agents.AI;
using Aevatar.Agents.Abstractions.Tracing;
using Aevatar.Agents.Cognitive.Core;
using Google.Protobuf.Collections;
using Google.Protobuf.WellKnownTypes;
using VibeResearching.Api.Materials;
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
        MaterialsSnapshot materials,
        SraDagSnapshot currentDag,
        IReadOnlyDictionary<string, string> outputs,
        Action<string> emit,
        string? providerName,
        CancellationToken ct)
    {
        // Query the currently Active milestone from Neo4j.
        // PlanNodes are stored with session.Id (not EffectiveDagId), so we use session.Id here.
        var activeMilestoneId = await GetActiveMilestoneFromGraphAsync(session.Id, ct);

        // Parse candidate mutation from dag_builder output (JSON).
        // Pass currentDag to validate motivatedByPlanNodeId references against existing milestones.
        // Pass activeMilestoneId as default for knowledge nodes without motivatedByPlanNodeId.
        var candidateText = outputs.TryGetValue("dag_builder", out var x) ? x : string.Empty;
        
        // Debug logging
        _host.Logger.LogInformation("[DagConsensus] dag_builder output length: {Length}, hasValue: {HasValue}", 
            candidateText.Length, outputs.TryGetValue("dag_builder", out _));
        if (candidateText.Length > 0)
        {
            _host.Logger.LogDebug("[DagConsensus] dag_builder output preview: {Preview}", 
                candidateText.Length > 500 ? candidateText[..500] + "..." : candidateText);
        }
        
        var candidate = TryParseDagBuilderCandidate(session.Id, candidateText, currentDag, activeMilestoneId);

        if (candidate == null)
        {
            _host.Logger.LogWarning("[DagConsensus] Failed to parse dag_builder output. Output length: {Length}", candidateText.Length);
            EmitSection(emit, "### DAG Apply (no verification)\n");
            emit("_No DAG candidate produced._\n\n");
            return new DagRoundResult(false, false, null, null, null, [], null);
        }
        
        _host.Logger.LogInformation("[DagConsensus] Parsed candidate: nodes={NodeCount}, edges={EdgeCount}", 
            candidate.UpsertNodes.Count, candidate.UpsertEdges.Count);
        
        // EMPTY mutation means "no change" (do not stage / do not block).
        if (candidate.UpsertNodes.Count == 0 && candidate.UpsertEdges.Count == 0)
        {
            _host.Logger.LogWarning("[DagConsensus] Parsed candidate is empty (no nodes or edges)");
            EmitSection(emit, "### DAG Apply (no verification)\n");
            emit("_No DAG changes proposed._\n\n");
            return new DagRoundResult(false, false, candidate, null, null, [], null);
        }

        // EMPTY mutation means "no change" (do not stage / do not block).
        if (candidate.UpsertNodes.Count == 0 && candidate.UpsertEdges.Count == 0)
        {
            EmitSection(emit, "### DAG Apply (no verification)\n");
            emit("_No DAG changes proposed._\n\n");
            return new DagRoundResult(false, false, candidate, null, null, [], null);
        }

        // ------------------------------------------------------------
        // Consensus gate (verifier-quorum or maker via Cognitive DSL)
        // ------------------------------------------------------------
        DagConsensusRunner.ConsensusResult consensus;
        try
        {
            var progress = BuildDagConsensusAgUiProgress(session, runId, workflowName: "maker");
            
            // Extract verifier output and proof from worker outputs
            var verifierOutput = outputs.TryGetValue("verifier", out var v) ? v : null;
            var verifierProof = outputs.TryGetValue("verifier_proof", out var vp) ? vp : null;
            
            consensus = await _core.DagConsensus.RunAsync(new DagConsensusRunner.ConsensusInput(
                session.Id,
                runId,
                currentDag,
                candidate,
                MaterialsContext: materials?.RenderedContext,
                ProviderName: providerName ?? session.ProviderName,
                VerifierOutput: verifierOutput,
                VerifierProof: verifierProof,
                Progress: progress), ct);
        }
        catch (Exception ex)
        {
            EmitSection(emit, "### DAG Consensus\n");
            emit($"[dag consensus error] {ex.Message}\n\n");
            return new DagRoundResult(false, true, candidate, null, null, ["consensus_exception"], null);
        }

        if (!consensus.Ok || consensus.Mutation == null)
        {
            EmitSection(emit, "### DAG Consensus (blocked)\n");
            var flags = consensus.RedFlags.Count == 0 ? "unknown" : string.Join(", ", consensus.RedFlags);
            emit($"**Blocked** (workflow: `{consensus.Workflow}`) redFlags=[{flags}]\n\n");
            return new DagRoundResult(false, true, candidate, null, consensus.ArtifactPath, consensus.RedFlags, consensus.ArtifactPath);
        }

        // Apply accepted mutation
        try
        {
            var dagId = session.EffectiveDagId;
            var accepted = consensus.Mutation;

            if (!string.IsNullOrWhiteSpace(consensus.Workflow))
                accepted.Labels["consensus_workflow"] = consensus.Workflow;
            if (!string.IsNullOrWhiteSpace(consensus.ArtifactPath))
                accepted.Labels["consensus_artifact"] = consensus.ArtifactPath;

            var applied = await _core.Dag.ApplyMutationAsync(dagId, accepted, ct);

            session.Events.Publish(new CustomEvent
            {
                Timestamp = NowMs(),
                Name = "aevatar.vibe.dag_updated",
                Value = new
                {
                    sessionId = session.Id,
                    dagId,
                    runId,
                    mutationId = accepted.MutationId,
                    nodes = accepted.UpsertNodes.Count,
                    edges = accepted.UpsertEdges.Count,
                    consensusWorkflow = consensus.Workflow,
                    consensusArtifact = consensus.ArtifactPath ?? string.Empty,
                    updatedAt = applied.UpdatedAt?.ToDateTime().ToUniversalTime().ToString("O") ?? ""
                }
            });

            EmitSection(emit, "### DAG Consensus (accepted)\n");
            emit($"**Applied** (mutationId: `{accepted.MutationId}`, workflow: `{consensus.Workflow}`)\n\n");

            return new DagRoundResult(true, false, candidate, accepted, null, [], consensus.ArtifactPath);
        }
        catch (Exception ex)
        {
            EmitSection(emit, "### DAG Consensus (apply failed)\n");
            emit($"[dag apply error] {ex.Message}\n\n");
            return new DagRoundResult(false, true, candidate, null, null, ["apply_exception"], consensus.ArtifactPath);
        }
    }

    private static IProgress<ReasoningProgress> BuildDagConsensusAgUiProgress(
        ResearchSession session,
        string runId,
        string workflowName)
    {
        var lastStatus = new Dictionary<string, string>(StringComparer.Ordinal);
        var gate = new object();

        return new Progress<ReasoningProgress>(progress =>
        {
            try
            {
                var evt = BuildExecutionTraceEvent(progress, runId, workflowName);
                var status = ResolveStatus(progress);
                var stepName = string.IsNullOrWhiteSpace(evt.NodeId) ? (evt.Phase ?? string.Empty) : evt.NodeId;

                var mapped = AgUiTraceProjector.Map(evt);
                var toPublish = new List<AgUiEvent>(mapped.Count);

                lock (gate)
                {
                    foreach (var e in mapped)
                    {
                        if (e is StepStartedEvent or StepFinishedEvent)
                        {
                            if (string.IsNullOrWhiteSpace(stepName) || string.IsNullOrWhiteSpace(status))
                                continue;

                            if (lastStatus.TryGetValue(stepName, out var prev) && string.Equals(prev, status, StringComparison.Ordinal))
                                continue;
                        }

                        toPublish.Add(e);
                    }

                    if (!string.IsNullOrWhiteSpace(stepName) && !string.IsNullOrWhiteSpace(status))
                        lastStatus[stepName] = status;
                }

                for (var i = 0; i < toPublish.Count; i++)
                    session.Events.Publish(toPublish[i]);
            }
            catch
            {
                // best-effort
            }
        });
    }

    private static ExecutionTraceEvent BuildExecutionTraceEvent(
        ReasoningProgress progress,
        string runId,
        string workflowName)
    {
        var phase = !string.IsNullOrWhiteSpace(progress.StepType) ? progress.StepType : progress.Phase;
        var nodeCore = !string.IsNullOrWhiteSpace(progress.StepId)
            ? progress.StepId
            : (progress.Phase ?? "step");
        var nodeId = $"dag_consensus:{nodeCore}";

        var evt = new ExecutionTraceEvent
        {
            Timestamp = Timestamp.FromDateTimeOffset(progress.Timestamp),
            Phase = phase ?? string.Empty,
            Message = progress.Message ?? string.Empty,
            NodeId = nodeId
        };

        evt.Fields[ExecutionTraceEventFields.Status] =
            ExecutionTraceEventFieldValue.FromString(ResolveStatus(progress));
        evt.Fields[ExecutionTraceEventFields.Progress] =
            ExecutionTraceEventFieldValue.FromDouble(progress.ProgressPercent);
        evt.Fields[ExecutionTraceEventFields.ExecutionId] =
            ExecutionTraceEventFieldValue.FromString(runId);
        evt.Fields[ExecutionTraceEventFields.WorkflowName] =
            ExecutionTraceEventFieldValue.FromString(workflowName);

        if (!string.IsNullOrWhiteSpace(progress.StepType))
            evt.Fields[ExecutionTraceEventFields.StepType] = ExecutionTraceEventFieldValue.FromString(progress.StepType);
        if (progress.Depth.HasValue)
            evt.Fields[ExecutionTraceEventFields.Depth] = ExecutionTraceEventFieldValue.FromInt(progress.Depth.Value);
        if (!string.IsNullOrWhiteSpace(progress.TaskId))
            evt.Fields[ExecutionTraceEventMakerFields.WorkerId] = ExecutionTraceEventFieldValue.FromString(progress.TaskId);

        if (progress.VoteRound.HasValue)
            evt.Fields[ExecutionTraceEventMakerFields.VoteRound] = ExecutionTraceEventFieldValue.FromInt(progress.VoteRound.Value);
        if (progress.VoteMaxRounds.HasValue)
            evt.Fields[ExecutionTraceEventMakerFields.VoteMaxRounds] = ExecutionTraceEventFieldValue.FromInt(progress.VoteMaxRounds.Value);
        if (progress.VoteK.HasValue)
            evt.Fields[ExecutionTraceEventMakerFields.VoteK] = ExecutionTraceEventFieldValue.FromInt(progress.VoteK.Value);
        if (progress.VoteCurrentVotes.HasValue)
            evt.Fields[ExecutionTraceEventMakerFields.VoteCurrentVotes] = ExecutionTraceEventFieldValue.FromInt(progress.VoteCurrentVotes.Value);

        if (!string.IsNullOrWhiteSpace(progress.WinnerProposalId))
            evt.Fields[ExecutionTraceEventMakerFields.WinnerProposalId] = ExecutionTraceEventFieldValue.FromString(progress.WinnerProposalId);
        if (!string.IsNullOrWhiteSpace(progress.WinnerHash))
            evt.Fields[ExecutionTraceEventMakerFields.WinnerHash] = ExecutionTraceEventFieldValue.FromString(progress.WinnerHash);
        if (progress.WinnerVotes.HasValue)
            evt.Fields[ExecutionTraceEventMakerFields.WinnerVotes] = ExecutionTraceEventFieldValue.FromInt(progress.WinnerVotes.Value);
        if (progress.WinnerRunnerUpVotes.HasValue)
            evt.Fields[ExecutionTraceEventMakerFields.WinnerRunnerUpVotes] = ExecutionTraceEventFieldValue.FromInt(progress.WinnerRunnerUpVotes.Value);
        if (progress.WinnerClusterCount.HasValue)
            evt.Fields[ExecutionTraceEventMakerFields.WinnerClusterCount] = ExecutionTraceEventFieldValue.FromInt(progress.WinnerClusterCount.Value);
        if (progress.WinnerSemantic.HasValue)
            evt.Fields[ExecutionTraceEventMakerFields.WinnerSemantic] = ExecutionTraceEventFieldValue.FromBool(progress.WinnerSemantic.Value);
        if (progress.WinnerIsConsensus.HasValue)
            evt.Fields[ExecutionTraceEventMakerFields.WinnerIsConsensus] = ExecutionTraceEventFieldValue.FromBool(progress.WinnerIsConsensus.Value);

        if (progress.ParallelTotal.HasValue)
            evt.Fields[ExecutionTraceEventFields.ParallelTotal] = ExecutionTraceEventFieldValue.FromInt(progress.ParallelTotal.Value);
        if (progress.ParallelCompleted.HasValue)
            evt.Fields[ExecutionTraceEventFields.ParallelCompleted] = ExecutionTraceEventFieldValue.FromInt(progress.ParallelCompleted.Value);
        if (progress.ParallelFailed.HasValue)
            evt.Fields[ExecutionTraceEventFields.ParallelFailed] = ExecutionTraceEventFieldValue.FromInt(progress.ParallelFailed.Value);

        if (progress.TotalLlmCalls.HasValue)
            evt.Fields[ExecutionTraceEventFields.LlmCalls] = ExecutionTraceEventFieldValue.FromInt(progress.TotalLlmCalls.Value);

        if (progress.TotalPromptTokens.HasValue)
            evt.Fields[ExecutionTraceEventFields.PromptTokens] = ExecutionTraceEventFieldValue.FromLong(progress.TotalPromptTokens.Value);
        if (progress.TotalCompletionTokens.HasValue)
            evt.Fields[ExecutionTraceEventFields.CompletionTokens] = ExecutionTraceEventFieldValue.FromLong(progress.TotalCompletionTokens.Value);

        if (progress.TotalPromptTokens.HasValue || progress.TotalCompletionTokens.HasValue)
        {
            var total = (progress.TotalPromptTokens ?? 0) + (progress.TotalCompletionTokens ?? 0);
            evt.Fields[ExecutionTraceEventFields.TokensUsed] = ExecutionTraceEventFieldValue.FromLong(total);
        }

        if (!string.IsNullOrWhiteSpace(progress.SystemPrompt))
            evt.Fields[ExecutionTraceEventFields.SystemPrompt] = ExecutionTraceEventFieldValue.FromString(progress.SystemPrompt);
        if (!string.IsNullOrWhiteSpace(progress.UserPrompt))
            evt.Fields[ExecutionTraceEventFields.UserPrompt] = ExecutionTraceEventFieldValue.FromString(progress.UserPrompt);
        if (!string.IsNullOrWhiteSpace(progress.AssistantResponse))
            evt.Fields[ExecutionTraceEventFields.AssistantResponse] = ExecutionTraceEventFieldValue.FromString(progress.AssistantResponse);

        return evt;
    }

    private static string ResolveStatus(ReasoningProgress progress)
    {
        var status = (progress.StepStatus ?? string.Empty).Trim().ToLowerInvariant();
        if (status.Length > 0)
        {
            return status switch
            {
                "pending" => ExecutionTraceEventStatus.Pending,
                "running" => ExecutionTraceEventStatus.Running,
                "completed" => ExecutionTraceEventStatus.Completed,
                "failed" => ExecutionTraceEventStatus.Failed,
                "skipped" => ExecutionTraceEventStatus.Cancelled,
                _ => ExecutionTraceEventStatus.Running
            };
        }

        var phase = (progress.Phase ?? string.Empty).Trim().ToLowerInvariant();
        if (phase is "complete" or "completed")
            return ExecutionTraceEventStatus.Completed;
        if (phase is "failed" or "error")
            return ExecutionTraceEventStatus.Failed;

        return ExecutionTraceEventStatus.Running;
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
