using System.Text;
using Aevatar.Agents.Knowledge.Graph;
using Aevatar.Agents.Knowledge.Graph.Models;
using Aevatar.Agents.Cognitive.Researching.Sessions;
using VibeResearching.Contracts.Collab;

namespace Aevatar.Agents.Cognitive.Researching.Round;

public sealed partial class ResearchingMilestoneLoopRunner
{
    private static string GetMilestoneNodeId(string sessionId, int roundIndex, int index)
    {
        var suffix = roundIndex > 0 ? $"r{roundIndex}" : $"i{index}";
        // IMPORTANT: Must match SanitizeId logic in VibeRoundServices.PlanDag exactly
        // - Replace ALL non-alphanumeric chars with '_'
        // - Convert to lowercase
        // - Max 64 chars
        return SanitizeId($"plan_{sessionId}_ms_{suffix}");
    }

    private static string SanitizeId(string s)
    {
        var t = (s ?? string.Empty).Trim();
        if (t.Length == 0) return string.Empty;
        var sb = new StringBuilder(t.Length);
        foreach (var ch in t)
            sb.Append(char.IsLetterOrDigit(ch) ? char.ToLowerInvariant(ch) : '_');
        // keep it bounded to avoid huge ids
        var outId = sb.ToString().Trim('_');
        return outId.Length <= 64 ? outId : outId[..64];
    }

    private async Task TryUpdateMilestoneStatusAsync(
        IKnowledgeGraphClient client,
        string nodeId,
        PlanNodeStatus status,
        string progressText,
        CancellationToken ct)
    {
        try
        {
            await client.UpdatePlanNodeStatusAsync(nodeId, status, progressText, ct);
            _logger.LogDebug("[MilestoneLoop] Updated milestone {NodeId} status to {Status}", nodeId, status);
        }
        catch (Exception ex)
        {
            // Best-effort: don't fail the research loop if status update fails
            _logger.LogWarning(ex, "[MilestoneLoop] Failed to update milestone {NodeId} status (best-effort)", nodeId);
        }
    }

    /// <summary>
    /// Links orphaned knowledge nodes (those without a MOTIVATED_BY edge) to the specified milestone.
    /// This is needed after the planning round creates knowledge nodes before milestones exist.
    /// </summary>
    private async Task TryLinkOrphanedKnowledgeNodesToMilestoneAsync(
        string dagId,
        string milestoneNodeId,
        CancellationToken ct)
    {
        try
        {
            var graphClient = _graphFactory.CreateClient(dagId);

            // Query for knowledge nodes without MOTIVATED_BY edge
            var orphanedNodes = await graphClient.GetOrphanedKnowledgeNodesAsync(ct);

            if (orphanedNodes.Count == 0)
            {
                _logger.LogDebug("[MilestoneLoop] No orphaned knowledge nodes found.");
                return;
            }

            _logger.LogInformation("[MilestoneLoop] Linking {Count} orphaned knowledge nodes to milestone {MilestoneId}",
                orphanedNodes.Count, milestoneNodeId);

            // Link each orphan to the milestone
            foreach (var orphanNodeId in orphanedNodes)
            {
                await graphClient.LinkKnowledgeToPlanAsync(orphanNodeId, milestoneNodeId, ct);
            }

            _logger.LogInformation("[MilestoneLoop] Successfully linked {Count} orphaned knowledge nodes to milestone {MilestoneId}",
                orphanedNodes.Count, milestoneNodeId);
        }
        catch (Exception ex)
        {
            // Best-effort: don't fail the research loop if linking fails
            _logger.LogWarning(ex, "[MilestoneLoop] Failed to link orphaned knowledge nodes to milestone (best-effort)");
        }
    }

    /// <summary>
    /// Updates the session's tracking context so that if an interruption occurs,
    /// the new run knows exactly where the old run was in the milestone sequence.
    /// </summary>
    private static void UpdateInterruptionTrackingContext(
        ResearchSession session,
        string currentMilestoneNodeId,
        int currentMilestoneIndex,
        int totalMilestones,
        int completedMilestones)
    {
        // Update the last interruption context if one exists (it will be overwritten
        // when a new interruption occurs, so we just keep the tracking info fresh)
        var existingCtx = session.GetLastInterruption();
        if (existingCtx != null)
        {
            existingCtx.InterruptedAtMilestoneNodeId = currentMilestoneNodeId;
            existingCtx.InterruptedAtMilestoneIndex = currentMilestoneIndex;
            existingCtx.TotalMilestones = totalMilestones;
            existingCtx.CompletedMilestones = completedMilestones;
        }

        // Also store in session's workspace for other components to access
        session.Workspace.Vibe.CurrentMilestoneIndex = currentMilestoneIndex;
        session.Workspace.Vibe.TotalMilestones = totalMilestones;
        session.Workspace.Vibe.CompletedMilestones = completedMilestones;
    }

    /// <summary>
    /// Gets the set of milestone node IDs that are already marked as Completed in the graph.
    /// Used for resume logic to skip completed milestones.
    /// </summary>
    private async Task<HashSet<string>> GetCompletedMilestoneIdsAsync(
        IKnowledgeGraphClient graphClient,
        string sessionId,
        List<SraResearchMilestone> milestones,
        CancellationToken ct)
    {
        var completed = new HashSet<string>(StringComparer.Ordinal);

        try
        {
            // Get all plan nodes from the graph
            var planNodes = await graphClient.GetPlanNodesAsync(ct);
            if (planNodes.Count == 0)
                return completed;

            // Build a lookup of plan node IDs to their status
            var statusLookup = planNodes.ToDictionary(p => p.Id, p => p.Status, StringComparer.Ordinal);

            // Check each milestone
            for (var i = 0; i < milestones.Count; i++)
            {
                var milestone = milestones[i];
                var milestoneNodeId = GetMilestoneNodeId(sessionId, milestone.RoundIndex, i + 1);

                if (statusLookup.TryGetValue(milestoneNodeId, out var status) &&
                    status == PlanNodeStatus.Completed)
                {
                    completed.Add(milestoneNodeId);
                }
            }

            return completed;
        }
        catch (Exception ex)
        {
            // Best-effort: don't fail if we can't get plan node status
            _logger.LogWarning(ex, "[MilestoneLoop] Failed to get completed milestone status (best-effort)");
            return completed;
        }
    }
}

