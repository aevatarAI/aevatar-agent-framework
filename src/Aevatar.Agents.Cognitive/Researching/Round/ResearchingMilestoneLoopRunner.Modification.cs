using Aevatar.Agents.Knowledge.Graph;
using Aevatar.Agents.Knowledge.Graph.Models;
using Aevatar.Agents.Cognitive.Researching.Sessions;
using VibeResearching.Contracts.Collab;

namespace Aevatar.Agents.Cognitive.Researching.Round;

public sealed partial class ResearchingMilestoneLoopRunner
{
    // ============================================================
    //  Milestone Modification (Phase 5: Direction Change)
    // ============================================================

    /// <summary>
    /// Modifies pending milestones based on user's direction change request.
    /// </summary>
    private async Task ModifyPendingMilestonesAsync(
        ResearchSession session,
        InterruptionContext interruptionContext,
        UserIntentAnalysis intentAnalysis,
        string? providerOverride,
        Action<string> emitAssistantDelta,
        CancellationToken ct)
    {
        try
        {
            // Load current Brief
            var briefSnapshot = await _brief.LoadAsync(session.Id, ct);
            var existingMilestones = briefSnapshot.Milestones
                .Where(m => !string.IsNullOrWhiteSpace(m?.ExpectedOutput))
                .OrderBy(m => m.RoundIndex)
                .ToList();

            if (existingMilestones.Count == 0)
            {
                _logger.LogWarning("[MilestoneLoop] No existing milestones to modify");
                return;
            }

            // Get completed milestones (don't modify these)
            var graphClient = _graphFactory.CreateClient(session.Id);
            var completedIds = await GetCompletedMilestoneIdsAsync(graphClient, session.Id, existingMilestones, ct);

            // Separate completed and pending milestones
            var completedMilestones = new List<SraResearchMilestone>();
            var pendingMilestones = new List<SraResearchMilestone>();

            for (var i = 0; i < existingMilestones.Count; i++)
            {
                var ms = existingMilestones[i];
                var nodeId = GetMilestoneNodeId(session.Id, ms.RoundIndex, i + 1);
                if (completedIds.Contains(nodeId))
                    completedMilestones.Add(ms);
                else
                    pendingMilestones.Add(ms);
            }

            if (pendingMilestones.Count == 0)
            {
                _logger.LogInformation("[MilestoneLoop] All milestones completed, cannot modify");
                emitAssistantDelta("\n**Note**: All existing milestones are completed. The new direction will be applied via auto-extension.\n\n");
                return;
            }

            // Generate modified milestones using LLM
            var modifiedMilestones = await GenerateModifiedMilestonesAsync(
                session,
                briefSnapshot.RewrittenQuestion,
                completedMilestones,
                pendingMilestones,
                intentAnalysis,
                providerOverride,
                ct);

            if (modifiedMilestones.Count == 0)
            {
                _logger.LogWarning("[MilestoneLoop] Failed to generate modified milestones");
                emitAssistantDelta("\n**Note**: Could not generate modified milestones. Continuing with original plan.\n\n");
                return;
            }

            // Update the Brief with modified milestones
            var allMilestones = completedMilestones.Concat(modifiedMilestones).ToList();
            await _brief.UpdateMilestonesAsync(session.Id, allMilestones, ct);

            // Update Plan Nodes in graph (pass original pending count for orphan handling)
            await UpdatePlanNodesForModifiedMilestonesAsync(
                graphClient,
                session.Id,
                completedMilestones.Count,
                modifiedMilestones,
                pendingMilestones.Count,  // Original pending count for orphan detection
                ct);

            // Emit UI update
            emitAssistantDelta("\n## Research Direction Updated\n\n");
            emitAssistantDelta($"**Your request**: {intentAnalysis.Summary}\n\n");
            emitAssistantDelta($"**Modified plan** ({modifiedMilestones.Count} milestones):\n");
            for (var i = 0; i < modifiedMilestones.Count; i++)
            {
                var ms = modifiedMilestones[i];
                emitAssistantDelta($"{completedMilestones.Count + i + 1}. {ms.ExpectedOutput}\n");
            }
            emitAssistantDelta("\n");

            _logger.LogInformation(
                "[MilestoneLoop] Modified {Count} pending milestones based on direction change",
                modifiedMilestones.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[MilestoneLoop] Failed to modify milestones");
            emitAssistantDelta("\n**Note**: Failed to modify milestones. Continuing with original plan.\n\n");
        }
    }

    /// <summary>
    /// Generates modified milestones based on user's direction change.
    /// </summary>
    private async Task<List<SraResearchMilestone>> GenerateModifiedMilestonesAsync(
        ResearchSession session,
        string originalQuestion,
        List<SraResearchMilestone> completedMilestones,
        List<SraResearchMilestone> pendingMilestones,
        UserIntentAnalysis intentAnalysis,
        string? providerOverride,
        CancellationToken ct)
    {
        try
        {
            var (verifier, verifierId) = await _runtime.GetVerifierAgentAsync(session.Id, providerOverride, ct);

            var completedSummary = string.Join("\n", completedMilestones.Select((m, i) => $"- [DONE] {m.ExpectedOutput}"));
            var pendingSummary = string.Join("\n", pendingMilestones.Select((m, i) => $"- [PENDING] {m.ExpectedOutput}"));

            var prompt = $$"""
                # Modify Research Milestones

                The user wants to change the research direction. Modify the PENDING milestones to align with their new direction while keeping COMPLETED milestones unchanged.

                **Original Research Question**: {{originalQuestion}}

                **Current Milestones**:
                {{completedSummary}}
                {{pendingSummary}}

                **User's New Direction**: {{intentAnalysis.DirectionChangeDescription ?? intentAnalysis.Summary}}

                **Suggested New Milestones (if any)**:
                {{string.Join("\n", intentAnalysis.SuggestedNewMilestones ?? new List<string>())}}

                Generate 1-5 modified milestones that:
                1. Build upon the completed work
                2. Align with the user's new direction
                3. Are specific and actionable
                4. Can be completed in 1-2 research iterations each

                Respond with a JSON array of milestone objects:
                ```json
                [
                    { "roundIndex": {{completedMilestones.Count + 1}}, "expectedOutput": "Description of milestone 1" },
                    { "roundIndex": {{completedMilestones.Count + 2}}, "expectedOutput": "Description of milestone 2" }
                ]
                ```
                """;

            var req = new Aevatar.Agents.AI.ChatRequest
            {
                Message = prompt,
                RequestId = Guid.NewGuid().ToString("N"),
                StageHint = "session:vibe:milestone_modification"
            };
            req.Context["agent_id"] = verifierId;

            var resp = await verifier.ChatAsync(req, ct);
            var content = resp.Content ?? string.Empty;

            return ParseModifiedMilestonesResponse(content, completedMilestones.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[MilestoneLoop] Failed to generate modified milestones");
            return new List<SraResearchMilestone>();
        }
    }

    private List<SraResearchMilestone> ParseModifiedMilestonesResponse(string content, int startingRoundIndex)
    {
        var milestones = new List<SraResearchMilestone>();

        try
        {
            var jsonStart = content.IndexOf('[');
            var jsonEnd = content.LastIndexOf(']');
            if (jsonStart >= 0 && jsonEnd > jsonStart)
            {
                var json = content[jsonStart..(jsonEnd + 1)];
                var parsed = System.Text.Json.JsonSerializer.Deserialize<List<MilestoneJson>>(json,
                    new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (parsed != null)
                {
                    var roundIndex = startingRoundIndex + 1;
                    foreach (var m in parsed)
                    {
                        if (!string.IsNullOrWhiteSpace(m.ExpectedOutput))
                        {
                            milestones.Add(new SraResearchMilestone
                            {
                                RoundIndex = m.RoundIndex > 0 ? m.RoundIndex : roundIndex,
                                ExpectedOutput = m.ExpectedOutput
                            });
                            roundIndex++;
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "[MilestoneLoop] Failed to parse modified milestones JSON");
        }

        return milestones;
    }

    private sealed class MilestoneJson
    {
        public int RoundIndex { get; init; }
        public string? ExpectedOutput { get; init; }
    }

    /// <summary>
    /// Updates Plan Nodes in the graph to reflect modified milestones.
    /// Handles content updates for existing nodes and marks orphaned nodes as Cancelled.
    /// </summary>
    /// <param name="graphClient">The knowledge graph client.</param>
    /// <param name="sessionId">The session ID.</param>
    /// <param name="completedCount">Number of completed milestones (these are not modified).</param>
    /// <param name="modifiedMilestones">The new/modified pending milestones.</param>
    /// <param name="originalPendingCount">Original number of pending milestones before modification.</param>
    /// <param name="ct">Cancellation token.</param>
    private async Task UpdatePlanNodesForModifiedMilestonesAsync(
        IKnowledgeGraphClient graphClient,
        string sessionId,
        int completedCount,
        List<SraResearchMilestone> modifiedMilestones,
        int originalPendingCount,
        CancellationToken ct)
    {
        try
        {
            // Get existing plan nodes
            var existingNodes = await graphClient.GetPlanNodesAsync(ct);
            var existingNodeIds = existingNodes.Select(n => n.Id).ToHashSet(StringComparer.Ordinal);

            // 1. Update or create plan nodes for modified milestones
            for (var i = 0; i < modifiedMilestones.Count; i++)
            {
                var ms = modifiedMilestones[i];
                var nodeId = GetMilestoneNodeId(sessionId, ms.RoundIndex, completedCount + i + 1);

                if (existingNodeIds.Contains(nodeId))
                {
                    // Update existing node: update content + reset status to Pending
                    try
                    {
                        var existingNode = existingNodes.FirstOrDefault(n => n.Id == nodeId);
                        if (existingNode != null && existingNode.Status != PlanNodeStatus.Completed)
                        {
                            // Update content (CoreDescription and DetailedDescription)
                            await graphClient.UpdatePlanNodeContentAsync(
                                nodeId,
                                coreDescription: ms.ExpectedOutput ?? $"Milestone {completedCount + i + 1}",
                                detailedDescription: $"Modified milestone: {ms.ExpectedOutput}",
                                cancellationToken: ct);

                            // Reset status to Pending
                            await graphClient.UpdatePlanNodeStatusAsync(
                                nodeId,
                                PlanNodeStatus.Pending,
                                "Re-opened after direction change",
                                ct);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "[MilestoneLoop] Failed to update plan node {NodeId}", nodeId);
                    }
                }
                else
                {
                    // Create new plan node
                    try
                    {
                        await graphClient.CreatePlanNodeAsync(
                            nodeId,
                            ms.ExpectedOutput ?? $"Milestone {completedCount + i + 1}",
                            $"Modified milestone: {ms.ExpectedOutput}",
                            methodology: null,
                            sequentialOrder: completedCount + i + 1,
                            cancellationToken: ct);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "[MilestoneLoop] Failed to create plan node {NodeId}", nodeId);
                    }
                }
            }

            // 2. Mark orphaned plan nodes as Cancelled (if new milestone count < original pending count)
            if (modifiedMilestones.Count < originalPendingCount)
            {
                _logger.LogInformation(
                    "[MilestoneLoop] New milestones ({NewCount}) < original pending ({OrigCount}), marking {OrphanCount} orphans as Cancelled",
                    modifiedMilestones.Count, originalPendingCount, originalPendingCount - modifiedMilestones.Count);

                for (var i = modifiedMilestones.Count; i < originalPendingCount; i++)
                {
                    // Calculate the original milestone index (1-based, after completed)
                    var orphanIndex = completedCount + i + 1;
                    // We need to find the original roundIndex for this orphan
                    // Since milestones are ordered by roundIndex, we use the index to estimate
                    var orphanNodeId = GetMilestoneNodeId(sessionId, orphanIndex, orphanIndex);

                    // Also try with the sequential node ID pattern used in the existing code
                    var alternateOrphanNodeId = GetMilestoneNodeId(sessionId, completedCount + i + 1, completedCount + i + 1);

                    try
                    {
                        // Try to remove with the first pattern
                        if (existingNodeIds.Contains(orphanNodeId))
                        {
                            var removed = await graphClient.RemoveNodeAsync(orphanNodeId, ct);
                            if (removed)
                            {
                                _logger.LogInformation("[MilestoneLoop] Removed orphan plan node {NodeId} due to direction change", orphanNodeId);
                            }
                        }
                        else if (existingNodeIds.Contains(alternateOrphanNodeId) && alternateOrphanNodeId != orphanNodeId)
                        {
                            var removed = await graphClient.RemoveNodeAsync(alternateOrphanNodeId, ct);
                            if (removed)
                            {
                                _logger.LogInformation("[MilestoneLoop] Removed orphan plan node {NodeId} due to direction change", alternateOrphanNodeId);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "[MilestoneLoop] Failed to remove orphan plan node");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[MilestoneLoop] Failed to update plan nodes for modified milestones");
        }
    }
}

