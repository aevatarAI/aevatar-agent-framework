using Aevatar.Agents.AGUI;
using Aevatar.Agents.Knowledge.Graph;
using Aevatar.Agents.Knowledge.Graph.Models;
using ScientificResearchAssistant.Api.Materials;
using ScientificResearchAssistant.Api.Sessions;
using ScientificResearchAssistant.Api.Vibe.Brief;
using ScientificResearchAssistant.Api.Vibe.Dag;
using ScientificResearchAssistant.Contracts.Collab;

namespace ScientificResearchAssistant.Api.Vibe;

// ============================================================
//  VibeMilestoneLoopRunner (milestone-driven research loop)
//
//  Purpose:
//  - Execute research by iterating through milestones in order
//  - Each milestone is a PlanNode in the DAG
//  - Update milestone status as research progresses:
//      Pending -> Active -> Completed
//  - Stop when all milestones are completed or timeout
//
//  Flow:
//  1. Load milestones from Brief
//  2. For each milestone (ordered by roundIndex):
//     a. Set milestone status to Active
//     b. Execute one research round focused on this milestone
//     c. Set milestone status to Completed
//  3. Publish completion event
// ============================================================

internal sealed class VibeMilestoneLoopRunner
{
    private const int DefaultMaxTotalDurationMs = 2 * 60 * 60 * 1000; // 2 hours for all milestones

    private readonly VibeOrchestrator _vibe;
    private readonly BriefStore _brief;
    private readonly DagStore _dag;
    private readonly IKnowledgeGraphClientFactory _graphFactory;
    private readonly ILogger<VibeMilestoneLoopRunner> _logger;

    public VibeMilestoneLoopRunner(
        VibeOrchestrator vibe,
        BriefStore brief,
        DagStore dag,
        IKnowledgeGraphClientFactory graphFactory,
        ILogger<VibeMilestoneLoopRunner> logger)
    {
        _vibe = vibe ?? throw new ArgumentNullException(nameof(vibe));
        _brief = brief ?? throw new ArgumentNullException(nameof(brief));
        _dag = dag ?? throw new ArgumentNullException(nameof(dag));
        _graphFactory = graphFactory ?? throw new ArgumentNullException(nameof(graphFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<MilestoneLoopResult> ExecuteByMilestonesAsync(
        ResearchSession session,
        string runId,
        SessionInputInDto input,
        string question,
        MaterialsSnapshot materials,
        string? providerOverride,
        Action<string> emitAssistantDelta,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(materials);
        emitAssistantDelta ??= _ => { };

        var maxTotalMs = Math.Clamp(input.Loop?.MaxTotalDurationMs ?? DefaultMaxTotalDurationMs, 1_000, 2 * 60 * 60 * 1000);
        var startedAt = DateTimeOffset.UtcNow;
        var stopReason = "completed";
        var milestonesExecuted = 0;
        var totalMilestones = 0;

        try
        {
            // Load milestones from Brief
            var briefSnapshot = await _brief.LoadAsync(session.Id, ct);
            var milestones = briefSnapshot.Milestones
                .Where(m => !string.IsNullOrWhiteSpace(m?.ExpectedOutput))
                .OrderBy(m => m.RoundIndex)
                .ToList();

            totalMilestones = milestones.Count;

            if (totalMilestones == 0)
            {
                _logger.LogInformation("[MilestoneLoop] No milestones found. Running initial round to generate Brief with milestones.");

                // Run initial round to generate Brief (including milestones)
                emitAssistantDelta("\n## Initializing Research Plan...\n\n");
                await _vibe.ExecuteOneRoundAsync(
                    session, runId, input, question, materials,
                    providerOverride, emitAssistantDelta, ct);

                // Reload Brief after initial round - now it should have milestones
                briefSnapshot = await _brief.LoadAsync(session.Id, ct);
                milestones = briefSnapshot.Milestones
                    .Where(m => !string.IsNullOrWhiteSpace(m?.ExpectedOutput))
                    .OrderBy(m => m.RoundIndex)
                    .ToList();

                totalMilestones = milestones.Count;
                milestonesExecuted = 1; // Count initial round as first milestone

                if (totalMilestones == 0)
                {
                    _logger.LogWarning("[MilestoneLoop] Still no milestones after initial round. Research complete.");
                    return new MilestoneLoopResult
                    {
                        Ok = true,
                        StopReason = "no_milestones",
                        MilestonesExecuted = 1,
                        TotalMilestones = 0,
                        MaxTotalDurationMs = maxTotalMs
                    };
                }

                _logger.LogInformation("[MilestoneLoop] Brief generated with {Count} milestones. Continuing with remaining milestones.", totalMilestones);
                emitAssistantDelta($"\n\n## Research Plan Generated: {totalMilestones} Milestones\n\n");
                for (var i = 0; i < milestones.Count; i++)
                {
                    var ms = milestones[i];
                    emitAssistantDelta($"{i + 1}. **Round {ms.RoundIndex}**: {ms.ExpectedOutput}\n");
                }
                emitAssistantDelta("\n---\n\n");

                // Skip first milestone since we already executed it
                if (milestones.Count > 0)
                {
                    milestones = milestones.Skip(1).ToList();
                }
            }

            var dagId = session.EffectiveDagId;
            var graphClient = _graphFactory.CreateClient(dagId);

            // Only show research plan if we didn't already show it after initial round
            if (milestonesExecuted == 0)
            {
                emitAssistantDelta($"\n\n## Research Plan: {totalMilestones} Milestones\n\n");
                for (var i = 0; i < milestones.Count; i++)
                {
                    var ms = milestones[i];
                    emitAssistantDelta($"{i + 1}. **Round {ms.RoundIndex}**: {ms.ExpectedOutput}\n");
                }
                emitAssistantDelta("\n---\n\n");
            }

            // Execute each milestone in order
            for (var i = 0; i < milestones.Count; i++)
            {
                ct.ThrowIfCancellationRequested();

                var elapsedMs = (int)(DateTimeOffset.UtcNow - startedAt).TotalMilliseconds;
                if (elapsedMs >= maxTotalMs)
                {
                    stopReason = "timeout";
                    _logger.LogInformation("[MilestoneLoop] Timeout after {Elapsed}ms, completed {Done}/{Total} milestones",
                        elapsedMs, milestonesExecuted, totalMilestones);
                    break;
                }

                var milestone = milestones[i];
                var milestoneNodeId = GetMilestoneNodeId(session.Id, milestone.RoundIndex, i + 1);
                var stepName = $"vibe.milestone.round_{milestone.RoundIndex}";

                // Publish milestone started
                session.Events.Publish(new StepStartedEvent
                {
                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    StepName = stepName
                });

                // The current milestone number is milestonesExecuted + 1 (1-based, accounting for initial round)
                var currentMilestoneNum = milestonesExecuted + 1;

                session.Events.Publish(new CustomEvent
                {
                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    Name = "aevatar.vibe.milestone_started",
                    Value = new
                    {
                        sessionId = session.Id,
                        dagId,
                        milestoneNodeId,  // For UI to highlight the active plan node
                        milestoneIndex = currentMilestoneNum,
                        totalMilestones,
                        roundIndex = milestone.RoundIndex,
                        expectedOutput = milestone.ExpectedOutput
                    }
                });

                // Update milestone status to Active
                await TryUpdateMilestoneStatusAsync(graphClient, milestoneNodeId, PlanNodeStatus.Active,
                    $"Starting research for milestone {currentMilestoneNum}/{totalMilestones}", ct);

                emitAssistantDelta($"\n## Milestone {currentMilestoneNum}/{totalMilestones} (Round {milestone.RoundIndex})\n\n");
                emitAssistantDelta($"**Goal**: {milestone.ExpectedOutput}\n\n");

                try
                {
                    // ============================================================
                    // Deep Research Loop for this milestone
                    // - Execute multiple iterations until goal is achieved
                    // - Each iteration: research -> evaluate -> decide continue/done
                    // ============================================================
                    const int maxIterationsPerMilestone = 5;
                    var iterationCount = 0;
                    var milestoneAchieved = false;

                    while (!milestoneAchieved && iterationCount < maxIterationsPerMilestone)
                    {
                        ct.ThrowIfCancellationRequested();
                        iterationCount++;

                        // Check timeout
                        var iterElapsedMs = (int)(DateTimeOffset.UtcNow - startedAt).TotalMilliseconds;
                        if (iterElapsedMs >= maxTotalMs)
                        {
                            _logger.LogInformation("[MilestoneLoop] Timeout during milestone {Index} iteration {Iter}", i + 1, iterationCount);
                            stopReason = "timeout";
                            break;
                        }

                        emitAssistantDelta($"\n### Iteration {iterationCount}/{maxIterationsPerMilestone}\n\n");

                        // Build iteration-specific prompt with deep research instructions
                        var iterationPrompt = BuildDeepResearchPrompt(
                            question,
                            milestone.ExpectedOutput,
                            currentMilestoneNum,
                            totalMilestones,
                            iterationCount,
                            maxIterationsPerMilestone,
                            milestoneNodeId);

                        // Execute research round
                        await _vibe.ExecuteOneRoundAsync(
                            session,
                            runId,
                            input,
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
                        else if (iterationCount < maxIterationsPerMilestone)
                        {
                            emitAssistantDelta($"\n→ **Continuing research...** (Need: {evaluation.NextSteps})\n");
                        }
                        else
                        {
                            emitAssistantDelta($"\n⚠ **Max iterations reached.** Moving to next milestone.\n");
                        }
                    }

                    milestonesExecuted++;

                    // Update milestone status to Completed
                    await TryUpdateMilestoneStatusAsync(graphClient, milestoneNodeId, PlanNodeStatus.Completed,
                        $"Completed milestone {milestonesExecuted}/{totalMilestones} after {iterationCount} iterations", ct);

                    emitAssistantDelta($"\n\n**Milestone {milestonesExecuted}/{totalMilestones} completed.**\n\n---\n");
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "[MilestoneLoop] Milestone {Index} failed: {Message}", currentMilestoneNum, ex.Message);

                    session.Events.Publish(new CustomEvent
                    {
                        Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                        Name = "aevatar.vibe.milestone_error",
                        Value = new
                        {
                            sessionId = session.Id,
                            milestoneIndex = currentMilestoneNum,
                            roundIndex = milestone.RoundIndex,
                            error = ex.Message
                        }
                    });

                    emitAssistantDelta($"\n\n**Milestone {currentMilestoneNum}/{totalMilestones} encountered an error: {ex.Message}**\n\nContinuing to next milestone...\n\n---\n");

                    // Continue to next milestone even if this one failed
                    milestonesExecuted++;
                }

                // Publish milestone finished
                session.Events.Publish(new StepFinishedEvent
                {
                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    StepName = stepName
                });

                session.Events.Publish(new CustomEvent
                {
                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    Name = "aevatar.vibe.milestone_finished",
                    Value = new
                    {
                        sessionId = session.Id,
                        dagId,
                        milestoneNodeId,  // For UI to clear the active highlight
                        milestoneIndex = milestonesExecuted,  // Use actual executed count (already incremented)
                        totalMilestones,
                        roundIndex = milestone.RoundIndex,
                        milestonesExecuted,
                        remainingMilestones = totalMilestones - milestonesExecuted
                    }
                });
            }

            if (milestonesExecuted >= totalMilestones)
            {
                emitAssistantDelta($"\n\n## Research Complete!\n\nAll {totalMilestones} milestones have been completed.\n");
            }
            else
            {
                emitAssistantDelta($"\n\n## Research Paused\n\nCompleted {milestonesExecuted}/{totalMilestones} milestones. Reason: {stopReason}\n");
            }
        }
        catch (OperationCanceledException)
        {
            stopReason = "cancelled";
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[MilestoneLoop] Unexpected error: {Message}", ex.Message);
            stopReason = "error";
        }

        return new MilestoneLoopResult
        {
            Ok = stopReason == "completed",
            StopReason = stopReason,
            MilestonesExecuted = milestonesExecuted,
            TotalMilestones = totalMilestones,
            MaxTotalDurationMs = maxTotalMs
        };
    }

    private static string GetMilestoneNodeId(string sessionId, int roundIndex, int index)
    {
        var suffix = roundIndex > 0 ? $"r{roundIndex}" : $"i{index}";
        // IMPORTANT: Must match SanitizeId logic in VibeOrchestrator.cs exactly
        // - Replace ALL non-alphanumeric chars with '_'
        // - Convert to lowercase
        // - Max 64 chars
        return SanitizeId($"plan_{sessionId}_ms_{suffix}");
    }

    private static string SanitizeId(string s)
    {
        var t = (s ?? string.Empty).Trim();
        if (t.Length == 0) return string.Empty;
        var sb = new System.Text.StringBuilder(t.Length);
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

    // ============================================================
    //  Deep Research Helpers
    // ============================================================

    private static string BuildDeepResearchPrompt(
        string originalQuestion,
        string milestoneGoal,
        int milestoneIndex,
        int totalMilestones,
        int iterationCount,
        int maxIterations,
        string milestoneNodeId)
    {
        var iterationGuidance = iterationCount switch
        {
            1 => """
                This is your FIRST iteration. Focus on:
                1. Understanding the goal deeply
                2. Identifying key concepts and theories to research
                3. Using web search to find authoritative sources
                4. Building foundational knowledge
                """,
            2 => """
                This is your SECOND iteration. Focus on:
                1. Deepening your analysis based on initial findings
                2. Deriving mathematical formulas or logical proofs if applicable
                3. Cross-referencing multiple sources
                4. Identifying gaps in your understanding
                """,
            _ => $"""
                This is iteration {iterationCount}/{maxIterations}. Focus on:
                1. Filling remaining knowledge gaps
                2. Verifying your conclusions with additional sources
                3. Synthesizing findings into coherent knowledge
                4. Ensuring completeness of your research
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
            - **Verify**: Cross-check conclusions against multiple sources

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
            var (verifier, verifierId) = await _vibe.Runtime.GetVerifierAgentAsync(session.Id, providerOverride, ct);

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
                3. Findings are supported by credible sources
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

        // Fallback: check for keywords
        var isComplete = content.Contains("\"isComplete\": true", StringComparison.OrdinalIgnoreCase) ||
                         content.Contains("\"isComplete\":true", StringComparison.OrdinalIgnoreCase) ||
                         content.Contains("goal achieved", StringComparison.OrdinalIgnoreCase);

        return new MilestoneEvaluation
        {
            IsComplete = isComplete || iterationCount >= 3,
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

internal sealed class MilestoneLoopResult
{
    public bool Ok { get; init; }
    public string StopReason { get; init; } = "completed"; // completed | timeout | cancelled | error | no_milestones
    public int MilestonesExecuted { get; init; }
    public int TotalMilestones { get; init; }
    public int MaxTotalDurationMs { get; init; }
}
