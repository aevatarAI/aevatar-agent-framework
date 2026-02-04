using Aevatar.Agents.AGUI;
using Aevatar.Agents.Knowledge.Graph;
using Aevatar.Agents.Knowledge.Graph.Models;
using Aevatar.Agents.Cognitive.Researching.Runtime;
using Aevatar.Agents.Cognitive.Researching.Materials;
using Aevatar.Agents.Cognitive.Researching.Sessions;
using Aevatar.Agents.Cognitive.Researching.Brief;
using Aevatar.Agents.Cognitive.Researching.Dag;
using Aevatar.Agents.Cognitive.Researching.Workflow;
using VibeResearching.Contracts.Collab;

namespace Aevatar.Agents.Cognitive.Researching.Round;

// ============================================================
//  ResearchingMilestoneLoopRunner (milestone-driven research loop)
//
//  Purpose:
//  - Execute research by iterating through milestones in order
//  - Each milestone is a PlanNode in the DAG
//  - Update milestone status as research progresses:
//      Pending -> Active -> Completed
//  - Quality gate driven: iterate until goal achieved or safety limit (50)
//
//  Flow:
//  1. Load milestones from Brief
//  2. For each milestone (ordered by roundIndex):
//     a. Set milestone status to Active
//     b. Execute research iterations until quality gate passes
//     c. Set milestone status to Completed
//  3. Publish completion event
// ============================================================

public sealed partial class ResearchingMilestoneLoopRunner
{
    private const int AbsoluteMaxIterationsPerMilestone = 50; // Safety limit for quality-gate driven iteration
    private const int MaxMilestones = 20; // Maximum milestones including auto-extensions
    private const int MaxExtensionRounds = 3; // Maximum auto-extension rounds

    private readonly ResearchingWorkflowRunner _runner;
    private readonly IResearchingRuntime _runtime;
    private readonly BriefStore _brief;
    private readonly DagStore _dag;
    private readonly IKnowledgeGraphClientFactory _graphFactory;
    private readonly ILogger<ResearchingMilestoneLoopRunner> _logger;

    public ResearchingMilestoneLoopRunner(
        ResearchingWorkflowRunner runner,
        IResearchingRuntime runtime,
        BriefStore brief,
        DagStore dag,
        IKnowledgeGraphClientFactory graphFactory,
        ILogger<ResearchingMilestoneLoopRunner> logger)
    {
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _brief = brief ?? throw new ArgumentNullException(nameof(brief));
        _dag = dag ?? throw new ArgumentNullException(nameof(dag));
        _graphFactory = graphFactory ?? throw new ArgumentNullException(nameof(graphFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<MilestoneLoopResult> ExecuteByMilestonesAsync(
        ResearchSession session,
        string runId,
        ResearchingInput input,
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

        var stopReason = "completed";
        var milestonesExecuted = 0;
        var totalMilestones = 0;
        var milestonesSkipped = 0;
        var extensionRound = 0;

        try
        {
            // ============================================================
            // Dynamic User Input: Check for interruption and analyze intent
            // ============================================================
            var interruptionContext = session.ConsumeLastInterruption();
            if (interruptionContext != null)
            {
                _logger.LogInformation(
                    "[MilestoneLoop] Detected interruption from run {OldRunId}, analyzing user intent...",
                    interruptionContext.InterruptedRunId);

                var intentAnalysis = await AnalyzeUserIntentAsync(
                    session,
                    interruptionContext,
                    question,
                    providerOverride,
                    ct);

                // Emit system reply event based on intent
                await HandleUserIntentAsync(
                    session,
                    interruptionContext,
                    intentAnalysis,
                    providerOverride,
                    emitAssistantDelta,
                    ct);

                // If intent was handled and no further research needed, return early
                if (intentAnalysis.IntentType == UserIntentType.ProgressInquiry)
                {
                    return new MilestoneLoopResult
                    {
                        Ok = true,
                        StopReason = "progress_inquiry_handled",
                        MilestonesExecuted = interruptionContext.CompletedMilestones,
                        TotalMilestones = interruptionContext.TotalMilestones
                    };
                }
            }

            // Load milestones from Brief
            var briefSnapshot = await _brief.LoadAsync(session.Id, ct);
            var milestones = briefSnapshot.Milestones
                .Where(m => !string.IsNullOrWhiteSpace(m?.ExpectedOutput))
                .OrderBy(m => m.RoundIndex)
                .ToList();

            totalMilestones = milestones.Count;

            if (totalMilestones == 0)
            {
                // ============================================================
                // PLANNING PHASE: Generate Brief with milestones
                // This is NOT research execution - just planning.
                // All milestones will be executed in the loop below.
                // ============================================================
                _logger.LogInformation("[MilestoneLoop] No milestones found. Running planning round to generate Brief with milestones.");

                emitAssistantDelta("\n## Generating Research Plan...\n\n");
                var runInput = input;
                await _runner.ExecuteRoundAsync(
                    session, runId, runInput, question, materials,
                    providerOverride, emitAssistantDelta, ct);

                // Reload Brief after planning round - now it should have milestones
                briefSnapshot = await _brief.LoadAsync(session.Id, ct);
                milestones = briefSnapshot.Milestones
                    .Where(m => !string.IsNullOrWhiteSpace(m?.ExpectedOutput))
                    .OrderBy(m => m.RoundIndex)
                    .ToList();

                totalMilestones = milestones.Count;

                if (totalMilestones == 0)
                {
                    _logger.LogWarning(
                        "[MilestoneLoop] Still no milestones after planning round. " +
                        "Possible causes: LLM did not generate milestones in Brief, Brief generation failed, or question is too simple. " +
                        "Research complete.");
                    
                    emitAssistantDelta(
                        "\n\n## ⚠️ Research Plan Not Generated\n\n" +
                        "The system was unable to generate a research plan with milestones. " +
                        "This may happen if:\n" +
                        "- The question is too simple and doesn't require multi-step research\n" +
                        "- The LLM provider is not configured correctly\n" +
                        "- The Brief generation failed\n\n" +
                        "You can try:\n" +
                        "1. Rephrasing your question to be more specific\n" +
                        "2. Using a different LLM provider\n" +
                        "3. Using 'chat' mode instead of 'vibe' mode for simpler questions\n\n");
                    
                    return new MilestoneLoopResult
                    {
                        Ok = true,
                        StopReason = "no_milestones",
                        MilestonesExecuted = 0,
                        TotalMilestones = 0
                    };
                }

                _logger.LogInformation("[MilestoneLoop] Brief generated with {Count} milestones. Starting research from milestone 1.", totalMilestones);
                emitAssistantDelta($"\n\n## Research Plan Generated: {totalMilestones} Milestones\n\n");
                for (var i = 0; i < milestones.Count; i++)
                {
                    var ms = milestones[i];
                    emitAssistantDelta($"{i + 1}. **Round {ms.RoundIndex}**: {ms.ExpectedOutput}\n");
                }
                emitAssistantDelta("\n---\n\n");

                // Link any orphaned knowledge nodes from planning round to first milestone
                // IMPORTANT: Use session.Id (not EffectiveDagId) because PlanNodes use session.Id
                var firstMilestone = milestones[0];
                var firstMilestoneNodeId = GetMilestoneNodeId(session.Id, firstMilestone.RoundIndex, 1);
                await TryLinkOrphanedKnowledgeNodesToMilestoneAsync(
                    session.Id, firstMilestoneNodeId, ct);

                // DO NOT skip any milestone - all milestones will be executed in the loop below
                // milestonesExecuted remains 0
            }

            var dagId = session.EffectiveDagId;
            // IMPORTANT: Use session.Id (not EffectiveDagId) for graph operations on PlanNodes.
            // PlanNodes are stored with session.Id as their sessionId, not EffectiveDagId.
            // EffectiveDagId may be "global" for cross-session DAG, but PlanNodes use actual session.Id.
            var graphClient = _graphFactory.CreateClient(session.Id);

            // ============================================================
            // Resume Logic: Get completed milestone status from graph
            // Skip milestones that are already marked as Completed.
            // ============================================================
            var completedMilestoneIds = await GetCompletedMilestoneIdsAsync(graphClient, session.Id, milestones, ct);
            if (completedMilestoneIds.Count > 0)
            {
                _logger.LogInformation("[MilestoneLoop] Found {Count} completed milestones, will skip them",
                    completedMilestoneIds.Count);
            }

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

                var milestone = milestones[i];
                var milestoneNodeId = GetMilestoneNodeId(session.Id, milestone.RoundIndex, i + 1);
                var stepName = $"vibe.milestone.round_{milestone.RoundIndex}";

                // ============================================================
                // Resume Logic: Skip already completed milestones
                // ============================================================
                if (completedMilestoneIds.Contains(milestoneNodeId))
                {
                    _logger.LogInformation("[MilestoneLoop] Skipping completed milestone {NodeId} (round {Round})",
                        milestoneNodeId, milestone.RoundIndex);

                    emitAssistantDelta($"\n**Skipping completed milestone {i + 1}/{totalMilestones} (Round {milestone.RoundIndex})**\n");
                    milestonesSkipped++;
                    milestonesExecuted++;
                    continue;
                }

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

                // Update session context for potential interruption tracking
                UpdateInterruptionTrackingContext(session, milestoneNodeId, currentMilestoneNum, totalMilestones, milestonesExecuted);

                emitAssistantDelta($"\n## Milestone {currentMilestoneNum}/{totalMilestones} (Round {milestone.RoundIndex})\n\n");
                emitAssistantDelta($"**Goal**: {milestone.ExpectedOutput}\n\n");

                try
                {
                    // ============================================================
                    // Deep Research Loop for this milestone
                    // - Execute multiple iterations until goal is achieved
                    // - Each iteration: research -> evaluate -> decide continue/done
                    // - No fixed iteration limit - driven entirely by quality gate
                    // ============================================================
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
                            currentMilestoneNum,
                            totalMilestones,
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

                    milestonesExecuted++;

                    // ============================================================
                    // CRITICAL: Ensure there is ALWAYS an Active milestone during research
                    // Before marking current milestone as Completed, mark next milestone as Active.
                    // This ensures no gap where there's no Active milestone.
                    // ============================================================
                    if (i + 1 < milestones.Count)
                    {
                        // Mark NEXT milestone as Active BEFORE marking current as Completed
                        var nextMilestone = milestones[i + 1];
                        var nextMilestoneNodeId = GetMilestoneNodeId(session.Id, nextMilestone.RoundIndex, i + 2);
                        await TryUpdateMilestoneStatusAsync(graphClient, nextMilestoneNodeId, PlanNodeStatus.Active,
                            $"Starting milestone {milestonesExecuted + 1}/{totalMilestones}", ct);
                    }

                    // NOW mark current milestone as Completed
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

            // ============================================================
            // Auto-Extension Loop: Continue extending until done or limits reached
            // ============================================================
            while (milestonesExecuted >= totalMilestones && stopReason == "completed")
            {
                // Check if we can extend
                var canExtend = totalMilestones < MaxMilestones && extensionRound < MaxExtensionRounds;

                if (!canExtend)
                {
                    _logger.LogInformation(
                        "[MilestoneLoop] Cannot extend: totalMilestones={Total}, maxMilestones={Max}, extensionRound={Round}, maxRounds={MaxRounds}",
                        totalMilestones, MaxMilestones, extensionRound, MaxExtensionRounds);
                    break;
                }

                ct.ThrowIfCancellationRequested();

                _logger.LogInformation(
                    "[MilestoneLoop] All {Count} milestones completed. Attempting auto-extension (round {Round})",
                    totalMilestones, extensionRound + 1);

                emitAssistantDelta("\n\n## Exploring Further...\n\nAnalyzing research progress for potential extensions...\n\n");

                var autoExtensionResult = await TryAutoExtendAsync(
                    session,
                    briefSnapshot,
                    milestones,
                    providerOverride,
                    emitAssistantDelta,
                    ct);

                if (autoExtensionResult.ExtendedMilestones.Count == 0)
                {
                    emitAssistantDelta($"**Decision**: No further extensions needed. {autoExtensionResult.Reason}\n\n");
                    break;
                }

                extensionRound++;
                stopReason = "auto_extended";

                // Add extended milestones to the list
                var newMilestones = autoExtensionResult.ExtendedMilestones;
                milestones.AddRange(newMilestones);
                totalMilestones = milestones.Count;

                // Save extended milestones to Brief
                await _brief.UpdateMilestonesAsync(session.Id, milestones, ct);

                // Create Plan Nodes for new milestones
                await CreatePlanNodesForExtendedMilestonesAsync(
                    graphClient,
                    session.Id,
                    milestonesExecuted,
                    newMilestones,
                    ct);

                emitAssistantDelta($"\n### Auto-Extension Round {extensionRound}\n");
                emitAssistantDelta($"Added {newMilestones.Count} new milestones to explore:\n\n");
                for (var i = 0; i < newMilestones.Count; i++)
                {
                    emitAssistantDelta($"{milestonesExecuted + i + 1}. {newMilestones[i].ExpectedOutput}\n");
                }
                emitAssistantDelta("\n---\n");

                // Update completed milestone IDs for new milestones
                completedMilestoneIds = await GetCompletedMilestoneIdsAsync(graphClient, session.Id, milestones, ct);

                // Execute the new milestones
                for (var i = milestonesExecuted; i < milestones.Count; i++)
                {
                    ct.ThrowIfCancellationRequested();

                    var milestone = milestones[i];
                    var milestoneNodeId = GetMilestoneNodeId(session.Id, milestone.RoundIndex, i + 1);
                    var stepName = $"vibe.milestone.round_{milestone.RoundIndex}";
                    var currentMilestoneNum = i + 1;

                    // Skip if already completed
                    if (completedMilestoneIds.Contains(milestoneNodeId))
                    {
                        milestonesSkipped++;
                        milestonesExecuted++;
                        continue;
                    }

                    // Publish milestone started
                    session.Events.Publish(new StepStartedEvent
                    {
                        Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                        StepName = stepName
                    });

                    session.Events.Publish(new CustomEvent
                    {
                        Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                        Name = "aevatar.vibe.milestone_started",
                        Value = new
                        {
                            sessionId = session.Id,
                            dagId,
                            milestoneNodeId,
                            milestoneIndex = currentMilestoneNum,
                            totalMilestones,
                            roundIndex = milestone.RoundIndex,
                            expectedOutput = milestone.ExpectedOutput,
                            isExtension = true
                        }
                    });

                    await TryUpdateMilestoneStatusAsync(graphClient, milestoneNodeId, PlanNodeStatus.Active,
                        $"Starting extended research for milestone {currentMilestoneNum}/{totalMilestones}", ct);

                    UpdateInterruptionTrackingContext(session, milestoneNodeId, currentMilestoneNum, totalMilestones, milestonesExecuted);

                    emitAssistantDelta($"\n## Extended Milestone {currentMilestoneNum}/{totalMilestones} (Round {milestone.RoundIndex})\n\n");
                    emitAssistantDelta($"**Goal**: {milestone.ExpectedOutput}\n\n");

                    try
                    {
                        await ExecuteMilestoneWithDeepResearchAsync(
                            session, runId, input, question, materials, milestone,
                            milestoneNodeId, dagId, graphClient,
                            providerOverride, emitAssistantDelta, ct);

                        milestonesExecuted++;
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "[MilestoneLoop] Extended milestone {NodeId} failed: {Msg}", milestoneNodeId, ex.Message);
                        await TryUpdateMilestoneStatusAsync(graphClient, milestoneNodeId, PlanNodeStatus.Completed,
                            $"Completed with error: {ex.Message}", ct);
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
                            milestoneNodeId,
                            milestoneIndex = milestonesExecuted,
                            totalMilestones,
                            roundIndex = milestone.RoundIndex,
                            milestonesExecuted,
                            remainingMilestones = totalMilestones - milestonesExecuted
                        }
                    });
                }

                // Reset stopReason to completed if we finished all extended milestones
                if (milestonesExecuted >= totalMilestones && stopReason == "auto_extended")
                {
                    stopReason = "completed";
                }
            }

            if (milestonesExecuted >= totalMilestones)
            {
                emitAssistantDelta($"\n\n## Research Complete!\n\nAll {totalMilestones} milestones have been completed");
                if (extensionRound > 0)
                {
                    emitAssistantDelta($" (including {extensionRound} auto-extension round{(extensionRound > 1 ? "s" : "")})");
                }
                emitAssistantDelta(".\n");
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
            Ok = stopReason == "completed" || stopReason == "auto_extended",
            StopReason = stopReason,
            MilestonesExecuted = milestonesExecuted,
            TotalMilestones = totalMilestones,
            MilestonesSkipped = milestonesSkipped,
            ExtensionRound = extensionRound
        };
    }

}
