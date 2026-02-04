using Aevatar.Agents.Knowledge.Graph;
using Aevatar.Agents.Knowledge.Graph.Models;
using Aevatar.Agents.Cognitive.Researching.Materials;
using Aevatar.Agents.Cognitive.Researching.Sessions;
using VibeResearching.Contracts.Collab;

namespace Aevatar.Agents.Cognitive.Researching.Round;

public sealed partial class ResearchingMilestoneLoopRunner
{
    // ============================================================
    //  Auto-Extension (Phase 6)
    // ============================================================

    private sealed class AutoExtensionResult
    {
        public List<SraResearchMilestone> ExtendedMilestones { get; init; } = new();
        public string Reason { get; init; } = string.Empty;
    }

    /// <summary>
    /// Attempts to auto-extend the research by generating additional milestones.
    /// </summary>
    private async Task<AutoExtensionResult> TryAutoExtendAsync(
        ResearchSession session,
        SraResearchBriefSnapshot briefSnapshot,
        List<SraResearchMilestone> completedMilestones,
        string? providerOverride,
        Action<string> emitAssistantDelta,
        CancellationToken ct)
    {
        try
        {
            var (verifier, verifierId) = await _runtime.GetVerifierAgentAsync(session.Id, providerOverride, ct);

            // Build summary of completed work
            var completedSummary = string.Join("\n", completedMilestones.Select((m, i) =>
                $"- Milestone {i + 1}: {m.ExpectedOutput}"));

            var remainingCapacity = MaxMilestones - completedMilestones.Count;
            var maxNewMilestones = Math.Min(3, remainingCapacity);

            var prompt = $$"""
                # Auto-Extension Analysis

                The research has completed all planned milestones. Analyze whether further exploration would be valuable.

                **Original Research Question**: {{briefSnapshot.RewrittenQuestion}}

                **Scope**: {{briefSnapshot.Scope}}

                **Completed Milestones** ({{completedMilestones.Count}} total):
                {{completedSummary}}

                **Task**: Determine if the research would benefit from additional milestones.

                Consider:
                1. Are there unexplored aspects of the original question?
                2. Did the completed milestones reveal new interesting directions?
                3. Would additional research deepen understanding significantly?
                4. Is the research scope sufficiently covered?

                **Constraints**:
                - Generate 0-{{maxNewMilestones}} new milestones
                - Each milestone should be specific and actionable
                - Don't repeat completed work
                - Only extend if genuinely valuable

                Respond with a JSON object:
                ```json
                {
                    "shouldExtend": true/false,
                    "reason": "Brief explanation for the decision",
                    "newMilestones": [
                        { "roundIndex": {{completedMilestones.Count + 1}}, "expectedOutput": "Description" }
                    ]
                }
                ```

                If shouldExtend is false, newMilestones should be an empty array.
                """;

            var req = new Aevatar.Agents.AI.ChatRequest
            {
                Message = prompt,
                RequestId = Guid.NewGuid().ToString("N"),
                StageHint = "session:vibe:auto_extension"
            };
            req.Context["agent_id"] = verifierId;

            var resp = await verifier.ChatAsync(req, ct);
            var content = resp.Content ?? string.Empty;

            return ParseAutoExtensionResponse(content, completedMilestones.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[MilestoneLoop] Auto-extension failed");
            return new AutoExtensionResult { Reason = "Extension analysis failed" };
        }
    }

    private AutoExtensionResult ParseAutoExtensionResponse(string content, int startingIndex)
    {
        try
        {
            var jsonStart = content.IndexOf('{');
            var jsonEnd = content.LastIndexOf('}');
            if (jsonStart >= 0 && jsonEnd > jsonStart)
            {
                var json = content[jsonStart..(jsonEnd + 1)];
                var parsed = System.Text.Json.JsonSerializer.Deserialize<AutoExtensionJson>(json,
                    new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (parsed != null && parsed.ShouldExtend && parsed.NewMilestones?.Count > 0)
                {
                    var milestones = new List<SraResearchMilestone>();
                    var roundIndex = startingIndex + 1;

                    foreach (var m in parsed.NewMilestones)
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

                    return new AutoExtensionResult
                    {
                        ExtendedMilestones = milestones,
                        Reason = parsed.Reason ?? "Research extended with new milestones"
                    };
                }

                return new AutoExtensionResult
                {
                    Reason = parsed?.Reason ?? "No extension needed"
                };
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "[MilestoneLoop] Failed to parse auto-extension JSON");
        }

        return new AutoExtensionResult { Reason = "Failed to parse extension response" };
    }

    private sealed class AutoExtensionJson
    {
        public bool ShouldExtend { get; init; }
        public string? Reason { get; init; }
        public List<MilestoneJson>? NewMilestones { get; init; }
    }

    /// <summary>
    /// Executes a single milestone with deep research loop (multiple iterations until goal achieved).
    /// </summary>
    private async Task ExecuteMilestoneWithDeepResearchAsync(
        ResearchSession session,
        string runId,
        ResearchingInput input,
        string question,
        MaterialsSnapshot materials,
        SraResearchMilestone milestone,
        string milestoneNodeId,
        string dagId,
        IKnowledgeGraphClient graphClient,
        string? providerOverride,
        Action<string> emitAssistantDelta,
        CancellationToken ct)
    {
        var iterationCount = 0;
        var milestoneAchieved = false;

        while (!milestoneAchieved && iterationCount < AbsoluteMaxIterationsPerMilestone)
        {
            ct.ThrowIfCancellationRequested();
            iterationCount++;

            emitAssistantDelta($"\n### Iteration {iterationCount}\n\n");

            // Build iteration-specific prompt with deep research instructions
            var iterationPrompt = BuildDeepResearchPrompt(
                question,
                milestone.ExpectedOutput,
                1, // milestoneIndex - not important for auto-extension
                1, // totalMilestones - not important for auto-extension
                iterationCount,
                milestoneNodeId);

            // Execute research round
            var runInput = input;
            await _runner.ExecuteRoundAsync(
                session,
                runId,
                runInput,
                iterationPrompt,
                materials,
                providerOverride,
                emitAssistantDelta,
                ct);

            // Evaluate if milestone goal is achieved
            var evaluation = await EvaluateMilestoneCompletionAsync(
                session,
                milestone.ExpectedOutput,
                iterationCount,
                providerOverride,
                ct);

            emitAssistantDelta($"\n**Self-Assessment**: {evaluation.Summary}\n");

            if (evaluation.IsComplete)
            {
                milestoneAchieved = true;
                emitAssistantDelta($"\n✓ **Goal achieved after {iterationCount} iteration(s).**\n");
            }
            else if (iterationCount >= AbsoluteMaxIterationsPerMilestone)
            {
                emitAssistantDelta(
                    $"\n⚠ **Safety iteration limit reached ({AbsoluteMaxIterationsPerMilestone}).** " +
                    $"Moving to next milestone with current progress.\n");
            }
            else
            {
                emitAssistantDelta(
                    $"\n→ **Quality gate not yet met.** Continuing research... (Need: {evaluation.NextSteps})\n");
            }
        }

        // Mark milestone as Completed
        await TryUpdateMilestoneStatusAsync(graphClient, milestoneNodeId, PlanNodeStatus.Completed,
            $"Completed milestone after {iterationCount} iterations", ct);

        emitAssistantDelta($"\n\n**Milestone completed.**\n\n---\n");
    }

    /// <summary>
    /// Creates Plan Nodes in the graph for newly extended milestones.
    /// </summary>
    private async Task CreatePlanNodesForExtendedMilestonesAsync(
        IKnowledgeGraphClient graphClient,
        string sessionId,
        int existingCount,
        List<SraResearchMilestone> newMilestones,
        CancellationToken ct)
    {
        try
        {
            for (var i = 0; i < newMilestones.Count; i++)
            {
                var ms = newMilestones[i];
                var nodeId = GetMilestoneNodeId(sessionId, ms.RoundIndex, existingCount + i + 1);

                try
                {
                    await graphClient.CreatePlanNodeAsync(
                        nodeId,
                        ms.ExpectedOutput ?? $"Extended Milestone {existingCount + i + 1}",
                        $"Auto-extended milestone: {ms.ExpectedOutput}",
                        methodology: null,
                        sequentialOrder: existingCount + i + 1,
                        cancellationToken: ct);

                    _logger.LogDebug("[MilestoneLoop] Created extended plan node {NodeId}", nodeId);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "[MilestoneLoop] Failed to create extended plan node {NodeId}", nodeId);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[MilestoneLoop] Failed to create plan nodes for extended milestones");
        }
    }
}

