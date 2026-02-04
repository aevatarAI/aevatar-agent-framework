using Aevatar.Agents.Cognitive.Researching.Sessions;

namespace Aevatar.Agents.Cognitive.Researching.Round;

public sealed partial class ResearchingMilestoneLoopRunner
{
    // ============================================================
    //  Deep Research Helpers
    // ============================================================

    private static string BuildDeepResearchPrompt(
        string originalQuestion,
        string milestoneGoal,
        int milestoneIndex,
        int totalMilestones,
        int iterationCount,
        string milestoneNodeId)
    {
        var iterationGuidance = iterationCount switch
        {
            1 => """
                This is your FIRST iteration. Focus on:
                1. Understanding the goal deeply
                2. Identifying key concepts and theories to research
                3. Using web search to find authoritative evidence
                4. Building foundational knowledge
                """,
            2 => """
                This is your SECOND iteration. Focus on:
                1. Deepening your analysis based on initial findings
                2. Deriving mathematical formulas or logical proofs if applicable
                3. Cross-referencing multiple evidence items
                4. Identifying gaps in your understanding
                """,
            3 or 4 => """
                This is a REFINEMENT iteration. Focus on:
                1. Filling remaining knowledge gaps
                2. Verifying your conclusions with additional evidence
                3. Synthesizing findings into coherent knowledge
                4. Ensuring completeness of your research
                """,
            _ => """
                This is an EXTENDED iteration. The quality gate has not yet been met.
                Focus on:
                1. Addressing specific gaps identified in previous evaluations
                2. Finding additional corroborating evidence
                3. Resolving any contradictions or inconsistencies
                4. Achieving a comprehensive understanding of the milestone goal
                """
        };

        return $"""
            # Research Context
            **Original Question**: {originalQuestion}

            # Current Milestone ({milestoneIndex}/{totalMilestones})
            **Goal**: {milestoneGoal}
            **Milestone Node ID**: {milestoneNodeId}

            IMPORTANT: When creating knowledge nodes, use "{milestoneNodeId}" as the motivatedByPlanNodeId value.
            This links the knowledge to this specific milestone in the research DAG.

            # Research Instructions
            You are conducting deep, autonomous research on this milestone.

            {iterationGuidance}

            ## Required Actions
            - **Analyze**: Break down the goal into specific sub-questions
            - **Research**: Use web search to find relevant papers, theories, and data
            - **Derive**: Work through mathematical derivations or logical reasoning step-by-step
            - **Synthesize**: Connect findings into coherent knowledge nodes
            - **Verify**: Cross-check conclusions against multiple evidence items

            ## Quality Standards
            - Do NOT provide superficial summaries
            - DO provide detailed analysis with citations
            - DO show your reasoning process
            - DO identify what you still need to learn

            Focus on achieving the milestone goal with rigor and depth.
            """;
    }

    private async Task<MilestoneEvaluation> EvaluateMilestoneCompletionAsync(
        ResearchSession session,
        string milestoneGoal,
        int iterationCount,
        string? providerOverride,
        CancellationToken ct)
    {
        try
        {
            // Use verifier agent to evaluate completion
            var (verifier, verifierId) = await _runtime.GetVerifierAgentAsync(session.Id, providerOverride, ct);

            var evaluationPrompt = $$"""
                # Milestone Completion Evaluation

                **Milestone Goal**: {{milestoneGoal}}
                **Iterations Completed**: {{iterationCount}}

                Based on the research conducted in this session, evaluate whether the milestone goal has been achieved.

                Respond in this exact JSON format:
                ```json
                {
                    "isComplete": true/false,
                    "completionPercentage": 0-100,
                    "summary": "Brief assessment of current progress",
                    "achievedAspects": ["list", "of", "achieved", "items"],
                    "missingAspects": ["list", "of", "missing", "items"],
                    "nextSteps": "What needs to be done next if not complete"
                }
                ```

                Be strict in your evaluation. Only mark isComplete=true if:
                1. The core question/goal has been thoroughly addressed
                2. Key derivations or proofs have been completed (if applicable)
                3. Findings are supported by credible evidence
                4. Knowledge has been properly synthesized
                """;

            var req = new Aevatar.Agents.AI.ChatRequest
            {
                Message = evaluationPrompt,
                RequestId = Guid.NewGuid().ToString("N"),
                StageHint = "session:vibe:milestone_evaluation"
            };
            req.Context["agent_id"] = verifierId;

            var resp = await verifier.ChatAsync(req, ct);
            var content = resp.Content ?? string.Empty;

            // Parse JSON response
            return ParseEvaluationResponse(content, iterationCount);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[MilestoneLoop] Evaluation failed, defaulting to continue");
            return new MilestoneEvaluation
            {
                IsComplete = iterationCount >= 3, // Default: complete after 3 iterations if evaluation fails
                Summary = "Evaluation unavailable",
                NextSteps = "Continue research"
            };
        }
    }

    private MilestoneEvaluation ParseEvaluationResponse(string content, int iterationCount)
    {
        try
        {
            // Extract JSON from response
            var jsonStart = content.IndexOf('{');
            var jsonEnd = content.LastIndexOf('}');
            if (jsonStart >= 0 && jsonEnd > jsonStart)
            {
                var json = content[jsonStart..(jsonEnd + 1)];
                var parsed = System.Text.Json.JsonSerializer.Deserialize<EvaluationJson>(json,
                    new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (parsed != null)
                {
                    return new MilestoneEvaluation
                    {
                        IsComplete = parsed.IsComplete,
                        CompletionPercentage = parsed.CompletionPercentage,
                        Summary = parsed.Summary ?? "No summary",
                        NextSteps = parsed.NextSteps ?? "Continue research"
                    };
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "[MilestoneLoop] Failed to parse evaluation JSON");
        }

        // Fallback: check for keywords - fully rely on quality gate, no auto-complete fallback
        var isComplete = content.Contains("\"isComplete\": true", StringComparison.OrdinalIgnoreCase) ||
                         content.Contains("\"isComplete\":true", StringComparison.OrdinalIgnoreCase) ||
                         content.Contains("goal achieved", StringComparison.OrdinalIgnoreCase);

        return new MilestoneEvaluation
        {
            IsComplete = isComplete,  // No auto-complete fallback - fully driven by quality gate
            Summary = "Evaluation parsed from content",
            NextSteps = isComplete ? "Goal achieved" : "Continue research"
        };
    }

    private sealed class EvaluationJson
    {
        public bool IsComplete { get; init; }
        public int CompletionPercentage { get; init; }
        public string? Summary { get; init; }
        public List<string>? AchievedAspects { get; init; }
        public List<string>? MissingAspects { get; init; }
        public string? NextSteps { get; init; }
    }
}

internal sealed class MilestoneEvaluation
{
    public bool IsComplete { get; init; }
    public int CompletionPercentage { get; init; }
    public string Summary { get; init; } = string.Empty;
    public string NextSteps { get; init; } = string.Empty;
}

