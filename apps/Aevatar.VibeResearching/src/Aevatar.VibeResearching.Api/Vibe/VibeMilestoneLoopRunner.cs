using Aevatar.Agents.AGUI;
using Aevatar.Agents.Knowledge.Graph;
using Aevatar.Agents.Knowledge.Graph.Models;
using System.Text;
using VibeResearching.Api.Materials;
using VibeResearching.Api.Sessions;
using VibeResearching.Api.Vibe.Brief;
using VibeResearching.Api.Vibe.Dag;
using VibeResearching.Api.Vibe.Trace;
using VibeResearching.Contracts.Collab;
using VibeResearching.Vibe;

namespace VibeResearching.Api.Vibe;

// ============================================================
//  VibeMilestoneLoopRunner (milestone-driven research loop)
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

internal sealed class VibeMilestoneLoopRunner
{
    private const int AbsoluteMaxIterationsPerMilestone = 5; // Safety limit for quality-gate driven iteration
    private const int MaxMilestones = 20; // Maximum milestones including auto-extensions
    private const int MaxExtensionRounds = 3; // Maximum auto-extension rounds

    private readonly VibeOrchestrator _vibe;
    private readonly BriefStore _brief;
    private readonly DagStore _dag;
    private readonly TraceStore _trace;
    private readonly MaterialsService _materials;
    private readonly IKnowledgeGraphClientFactory _graphFactory;
    private readonly ILogger<VibeMilestoneLoopRunner> _logger;

    public VibeMilestoneLoopRunner(
        VibeOrchestrator vibe,
        BriefStore brief,
        DagStore dag,
        TraceStore trace,
        MaterialsService materials,
        IKnowledgeGraphClientFactory graphFactory,
        ILogger<VibeMilestoneLoopRunner> logger)
    {
        _vibe = vibe ?? throw new ArgumentNullException(nameof(vibe));
        _brief = brief ?? throw new ArgumentNullException(nameof(brief));
        _dag = dag ?? throw new ArgumentNullException(nameof(dag));
        _trace = trace ?? throw new ArgumentNullException(nameof(trace));
        _materials = materials ?? throw new ArgumentNullException(nameof(materials));
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

        var startedAt = DateTimeOffset.UtcNow;
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
                await _vibe.ExecuteOneRoundAsync(
                    session, runId, input, question, materials,
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
                            question,
                            materials,
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
        string question,
        MaterialsSnapshot materials,
        string? providerOverride,
        CancellationToken ct)
    {
        try
        {
            // Determine milestone type based on goal keywords (used in both prompt building and auto-completion logic)
            var isIdentificationMilestone = milestoneGoal.Contains("识别", StringComparison.OrdinalIgnoreCase) ||
                                           milestoneGoal.Contains("identify", StringComparison.OrdinalIgnoreCase);
            var isVerificationMilestone = milestoneGoal.Contains("验证", StringComparison.OrdinalIgnoreCase) ||
                                        milestoneGoal.Contains("verify", StringComparison.OrdinalIgnoreCase);
            
            // Use verifier agent to evaluate completion
            var (verifier, verifierId) = await _vibe.Runtime.GetVerifierAgentAsync(session.Id, providerOverride, ct);

            // Load current research context for evaluation
            var dagId = session.EffectiveDagId;
            var dagSnap = await _dag.LoadSnapshotAsync(dagId, ct);
            var recentTrace = await _trace.LoadLatestAsync(session.Id, max: 5, ct); // Increased from 2 to 5 for better context

            // Build context sections for the evaluation prompt
            var sb = new StringBuilder(4096);
            sb.AppendLine("# Milestone Completion Evaluation");
            sb.AppendLine();
            sb.AppendLine($"**Milestone Goal**: {milestoneGoal}");
            sb.AppendLine($"**Research Question**: {question}");
            sb.AppendLine($"**Iterations Completed**: {iterationCount}");
            sb.AppendLine();

            // Add DAG snapshot summary
            sb.AppendLine("## Current Knowledge State (DAG)");
            sb.AppendLine($"- Total nodes: {dagSnap.Nodes.Count}");
            sb.AppendLine($"- Total edges: {dagSnap.Edges.Count}");
            if (dagSnap.Nodes.Count > 0)
            {
                var knowledgeNodes = dagSnap.Nodes.Where(n => n.Kind == SraDagNodeKind.Knowledge).ToList();
                if (knowledgeNodes.Count > 0)
                {
                    sb.AppendLine($"- All knowledge nodes ({knowledgeNodes.Count} total):");
                    // Show all nodes, but bound each label to avoid excessive length
                    foreach (var node in knowledgeNodes)
                    {
                        var label = Bound((node.Label ?? string.Empty).Trim(), 200);
                        var nodeType = node.Type.ToString();
                        sb.AppendLine($"  - [{nodeType}] {node.Id}: {label}");
                    }
                }
            }
            sb.AppendLine();

            // Add recent research trace
            if (recentTrace.Count > 0)
            {
                sb.AppendLine("## Recent Research History");
                foreach (var round in recentTrace.Take(3))
                {
                    sb.AppendLine($"- Round {round.RoundIndex} (run: {round.RunId}):");
                    foreach (var agent in round.PerAgent.Take(5))
                    {
                        sb.AppendLine($"  - {agent.Agent}: {string.Join(", ", agent.Highlights.Take(2))}");
                    }
                    if (round.DagChanges.Count > 0)
                    {
                        sb.AppendLine($"  - DAG changes: {round.DagChanges.Count} nodes");
                    }
                }
                sb.AppendLine();
            }

            // Extract verifier output from recent trace (most recent round)
            var verifierOutput = string.Empty;
            if (recentTrace.Count > 0)
            {
                var latestRound = recentTrace[0]; // Most recent round
                var verifierAgent = latestRound.PerAgent.FirstOrDefault(a => 
                    string.Equals(a.Agent, "verifier", StringComparison.OrdinalIgnoreCase));
                if (verifierAgent != null && verifierAgent.Highlights.Count > 0)
                {
                    verifierOutput = string.Join("\n", verifierAgent.Highlights);
                }
            }

            // Add verifier output (if available)
            if (!string.IsNullOrWhiteSpace(verifierOutput))
            {
                sb.AppendLine("## Verifier Output (from most recent research round)");
                sb.AppendLine(Bound(verifierOutput, 2000));
                sb.AppendLine();
            }

            // Add materials context (if available)
            if (!string.IsNullOrWhiteSpace(materials.RenderedContext))
            {
                sb.AppendLine("## Materials Context");
                sb.AppendLine(Bound(materials.RenderedContext, 2000));
                sb.AppendLine();
            }

            // Add evaluation instructions
            sb.AppendLine("## Evaluation Instructions");
            sb.AppendLine();
            sb.AppendLine("Based on the research conducted in this session (including DAG state, research history, materials, and verifier output), evaluate whether the milestone goal has been achieved.");
            sb.AppendLine();
            sb.AppendLine("Respond in this exact JSON format:");
            sb.AppendLine("```json");
            sb.AppendLine("{");
            sb.AppendLine("    \"isComplete\": true/false,");
            sb.AppendLine("    \"completionPercentage\": 0-100,");
            sb.AppendLine("    \"summary\": \"Brief assessment of current progress\",");
            sb.AppendLine("    \"achievedAspects\": [\"list\", \"of\", \"achieved\", \"items\"],");
            sb.AppendLine("    \"missingAspects\": [\"list\", \"of\", \"missing\", \"items\"],");
            sb.AppendLine("    \"nextSteps\": \"What needs to be done next if not complete\"");
            sb.AppendLine("}");
            sb.AppendLine("```");
            sb.AppendLine();
            
            // Use milestone type determined at the beginning of the method
            if (isIdentificationMilestone)
            {
                // For identification milestones: focus on completeness of identification
                sb.AppendLine("CRITICAL: This is an IDENTIFICATION milestone. Focus on whether all required items have been IDENTIFIED and added to the DAG.");
                sb.AppendLine();
                sb.AppendLine("Mark isComplete=true if ALL of the following are satisfied:");
                sb.AppendLine("1. All required items (definitions, theorems, lemmas, corollaries, propositions) have been IDENTIFIED from the specified scope");
                sb.AppendLine("2. Knowledge nodes have been created in the DAG for the identified items");
                sb.AppendLine("3. The identification is comprehensive (no major items are missing)");
                sb.AppendLine("4. The knowledge graph structure is established");
                sb.AppendLine();
                sb.AppendLine("NOTE: For identification milestones, you do NOT need to verify proofs or derivations. " +
                             "Verification will be done in subsequent milestones. Focus on COMPLETENESS of identification.");
            }
            else if (isVerificationMilestone)
            {
                // For verification milestones: use strict verification criteria
                sb.AppendLine("CRITICAL: This is a VERIFICATION milestone. Focus on whether all items have been VERIFIED.");
                sb.AppendLine();
                sb.AppendLine("Mark isComplete=true if ALL of the following are satisfied:");
                sb.AppendLine("1. The core question/goal has been thoroughly addressed");
                sb.AppendLine("2. Key derivations or proofs have been completed (if applicable)");
                sb.AppendLine("3. Findings are supported by credible evidence");
                sb.AppendLine("4. Knowledge has been properly synthesized");
            }
            else
            {
                // Default: use general criteria
                sb.AppendLine("Be strict in your evaluation. Only mark isComplete=true if ALL of the following are satisfied:");
                sb.AppendLine("1. The core question/goal has been thoroughly addressed");
                sb.AppendLine("2. Key derivations or proofs have been completed (if applicable)");
                sb.AppendLine("3. Findings are supported by credible evidence");
                sb.AppendLine("4. Knowledge has been properly synthesized");
            }
            
            sb.AppendLine();
            sb.AppendLine("When evaluating, consider:");
            sb.AppendLine("- What new knowledge nodes were added to the DAG?");
            sb.AppendLine("- What progress was made in recent research rounds?");
            sb.AppendLine("- Are the findings grounded in the materials context?");
            sb.AppendLine("- What did the verifier report? Were claims VERIFIED, NOT VERIFIED, or INCONCLUSIVE?");
            sb.AppendLine("- Is the milestone goal fully achieved or only partially?");
            sb.AppendLine($"- Total knowledge nodes in DAG: {dagSnap.Nodes.Count(n => n.Kind == SraDagNodeKind.Knowledge)}");

            var evaluationPrompt = sb.ToString();

            var req = new Aevatar.Agents.AI.ChatRequest
            {
                Message = evaluationPrompt,
                RequestId = Guid.NewGuid().ToString("N"),
                StageHint = "session:vibe:milestone_evaluation"
            };
            req.Context["agent_id"] = verifierId;
            req.Context["materials_context"] = materials.RenderedContext; // Include materials context

            // Get verifier system prompt
            var verifierSystemPrompt = VibeVerifierAgent.GetSystemPrompt();
            var finalSystemPrompt = string.IsNullOrWhiteSpace(materials.RenderedContext)
                ? verifierSystemPrompt
                : $"{verifierSystemPrompt}\n\nMaterials context:\n{materials.RenderedContext.Trim()}\n";

            var resp = await verifier.ChatAsync(req, ct);
            var content = resp.Content ?? string.Empty;

            // Save verifier prompt record for milestone evaluation
            try
            {
                var promptRecord = new VibeOrchestrator.AgentPromptRecord(
                    AgentName: "verifier_milestone_evaluation",
                    SystemPrompt: finalSystemPrompt,
                    UserPrompt: evaluationPrompt,
                    MaterialsContext: materials.RenderedContext,
                    RawOutput: content,
                    Timestamp: DateTimeOffset.UtcNow
                );

                var promptRecords = new Dictionary<string, VibeOrchestrator.AgentPromptRecord>(StringComparer.OrdinalIgnoreCase)
                {
                    ["verifier_milestone_evaluation"] = promptRecord
                };

                // Save asynchronously (fire-and-forget)
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await _vibe.SaveAgentPromptsToFileAsync(
                            session.Id,
                            $"milestone_eval_{iterationCount}",
                            $"Milestone Evaluation: {milestoneGoal}",
                            promptRecords,
                            CancellationToken.None);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "[MilestoneLoop] Failed to save milestone evaluation prompts (best-effort).");
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "[MilestoneLoop] Failed to create prompt record for milestone evaluation (best-effort).");
            }

            // Parse JSON response
            var evaluation = ParseEvaluationResponse(content, iterationCount);
            
            // Add automatic completion logic for identification milestones
            // If milestone goal contains "识别" (identify) and we have created substantial knowledge nodes,
            // and iteration count is reasonable, consider auto-completing
            // (isIdentificationMilestone was already determined at the beginning of the method)
            if (isIdentificationMilestone && !evaluation.IsComplete)
            {
                var knowledgeNodeCount = dagSnap.Nodes.Count(n => n.Kind == SraDagNodeKind.Knowledge);
                var hasSubstantialProgress = knowledgeNodeCount >= 8 && iterationCount >= 2;
                
                // If we have substantial progress but LLM says not complete, check if it's a false negative
                if (hasSubstantialProgress && evaluation.CompletionPercentage >= 70)
                {
                    _logger.LogInformation(
                        "[MilestoneLoop] Identification milestone has substantial progress ({NodeCount} nodes, {Completion}% complete, {Iterations} iterations). " +
                        "Considering auto-completion.",
                        knowledgeNodeCount, evaluation.CompletionPercentage, iterationCount);
                    
                    // If completion percentage is high (>=70%) and we have enough nodes, auto-complete
                    if (evaluation.CompletionPercentage >= 70 && knowledgeNodeCount >= 8)
                    {
                        evaluation = new MilestoneEvaluation
                        {
                            IsComplete = true,
                            CompletionPercentage = Math.Min(100, evaluation.CompletionPercentage + 10),
                            Summary = $"{evaluation.Summary} (Auto-completed: {knowledgeNodeCount} knowledge nodes created, {evaluation.CompletionPercentage}% completion)",
                            NextSteps = "Milestone goal achieved"
                        };
                    }
                }
            }
            
            return evaluation;
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

    private static string Bound(string text, int maxLength)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        return text.Length <= maxLength ? text : text[..maxLength] + "...";
    }

    // ============================================================
    //  User Intent Analysis (Dynamic User Input)
    // ============================================================

    /// <summary>
    /// Analyzes the user's intent from their interruption message.
    /// Returns the intent type and any relevant information.
    /// </summary>
    private async Task<UserIntentAnalysis> AnalyzeUserIntentAsync(
        ResearchSession session,
        InterruptionContext interruptionContext,
        string newMessage,
        string? providerOverride,
        CancellationToken ct)
    {
        try
        {
            var (verifier, verifierId) = await _vibe.Runtime.GetVerifierAgentAsync(session.Id, providerOverride, ct);

            var analysisPrompt = $$"""
                # User Intent Analysis

                The user has interrupted an ongoing research session with a new message.
                Analyze the user's intent and categorize it into one of three types.

                **User's New Message**: {{newMessage}}

                **Research Progress Context**:
                - Interrupted at milestone: {{interruptionContext.InterruptedAtMilestoneIndex}}/{{interruptionContext.TotalMilestones}}
                - Completed milestones: {{interruptionContext.CompletedMilestones}}

                Respond in this exact JSON format:
                ```json
                {
                    "intentType": "direction_change" | "progress_inquiry" | "other",
                    "summary": "Brief summary of what the user wants",
                    "directionChangeDescription": "Only if intentType is direction_change: describe the new direction",
                    "suggestedNewMilestones": ["Only if direction_change: list of 1-3 new milestone descriptions"],
                    "systemReplyContent": "A friendly response to show to the user (1-2 sentences)"
                }
                ```

                **Intent Type Definitions**:
                - **direction_change**: User wants to change research focus, add new topics, modify goals, or pivot the research direction
                - **progress_inquiry**: User asks about current progress, status, what's been done, or time remaining
                - **other**: Any other message (general comments, encouragement, unrelated questions)

                Be strict in categorization. Only use "direction_change" if the user clearly wants to modify the research plan.
                """;

            var req = new Aevatar.Agents.AI.ChatRequest
            {
                Message = analysisPrompt,
                RequestId = Guid.NewGuid().ToString("N"),
                StageHint = "session:vibe:intent_analysis"
            };
            req.Context["agent_id"] = verifierId;

            var resp = await verifier.ChatAsync(req, ct);
            var content = resp.Content ?? string.Empty;

            return ParseIntentAnalysisResponse(content);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[MilestoneLoop] Intent analysis failed, defaulting to 'other'");
            return new UserIntentAnalysis
            {
                IntentType = UserIntentType.Other,
                Summary = "Unable to analyze intent",
                SystemReplyContent = "I've received your message. Let me continue with the research."
            };
        }
    }

    private UserIntentAnalysis ParseIntentAnalysisResponse(string content)
    {
        try
        {
            var jsonStart = content.IndexOf('{');
            var jsonEnd = content.LastIndexOf('}');
            if (jsonStart >= 0 && jsonEnd > jsonStart)
            {
                var json = content[jsonStart..(jsonEnd + 1)];
                var parsed = System.Text.Json.JsonSerializer.Deserialize<IntentAnalysisJson>(json,
                    new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (parsed != null)
                {
                    var intentType = (parsed.IntentType ?? "other").ToLowerInvariant() switch
                    {
                        "direction_change" => UserIntentType.DirectionChange,
                        "progress_inquiry" => UserIntentType.ProgressInquiry,
                        _ => UserIntentType.Other
                    };

                    return new UserIntentAnalysis
                    {
                        IntentType = intentType,
                        Summary = parsed.Summary ?? "Intent analyzed",
                        DirectionChangeDescription = parsed.DirectionChangeDescription,
                        SuggestedNewMilestones = parsed.SuggestedNewMilestones,
                        SystemReplyContent = parsed.SystemReplyContent
                    };
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "[MilestoneLoop] Failed to parse intent analysis JSON");
        }

        return new UserIntentAnalysis
        {
            IntentType = UserIntentType.Other,
            Summary = "Unable to parse intent",
            SystemReplyContent = "I've received your message and will continue with the research."
        };
    }

    /// <summary>
    /// Handles the analyzed user intent by emitting appropriate system replies
    /// and taking action based on intent type.
    /// </summary>
    private async Task HandleUserIntentAsync(
        ResearchSession session,
        InterruptionContext interruptionContext,
        UserIntentAnalysis intentAnalysis,
        string? providerOverride,
        Action<string> emitAssistantDelta,
        CancellationToken ct)
    {
        long Ts() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        switch (intentAnalysis.IntentType)
        {
            case UserIntentType.DirectionChange:
                _logger.LogInformation("[MilestoneLoop] User requested direction change: {Desc}",
                    intentAnalysis.DirectionChangeDescription);

                // Emit system reply acknowledging direction change
                session.Events.Publish(new CustomEvent
                {
                    Timestamp = Ts(),
                    Name = "aevatar.scientific.system_reply",
                    Value = new
                    {
                        sessionId = session.Id,
                        messageType = "direction_change",
                        content = intentAnalysis.SystemReplyContent ??
                            "Got it! I'll adjust the research direction based on your feedback."
                    }
                });

                // Modify pending milestones based on user's new direction
                await ModifyPendingMilestonesAsync(
                    session,
                    interruptionContext,
                    intentAnalysis,
                    providerOverride,
                    emitAssistantDelta,
                    ct);

                break;

            case UserIntentType.ProgressInquiry:
                _logger.LogInformation("[MilestoneLoop] User requested progress inquiry");

                // Generate progress summary
                var progressContent = GenerateProgressSummary(interruptionContext);

                session.Events.Publish(new CustomEvent
                {
                    Timestamp = Ts(),
                    Name = "aevatar.scientific.system_reply",
                    Value = new
                    {
                        sessionId = session.Id,
                        messageType = "progress_inquiry",
                        content = progressContent
                    }
                });

                emitAssistantDelta($"\n## Progress Update\n\n{progressContent}\n\n");
                break;

            case UserIntentType.Other:
            default:
                _logger.LogInformation("[MilestoneLoop] User message categorized as 'other', continuing research");

                session.Events.Publish(new CustomEvent
                {
                    Timestamp = Ts(),
                    Name = "aevatar.scientific.system_reply",
                    Value = new
                    {
                        sessionId = session.Id,
                        messageType = "other",
                        content = intentAnalysis.SystemReplyContent ??
                            "Thanks for your message! I haven't detected a specific research direction change, so I'll continue with the current plan."
                    }
                });
                break;
        }
    }

    private static string GenerateProgressSummary(InterruptionContext ctx)
    {
        var completed = ctx.CompletedMilestones;
        var total = ctx.TotalMilestones;
        var percent = total > 0 ? (completed * 100 / total) : 0;

        return $"**Research Progress**: {completed}/{total} milestones completed ({percent}% done)\n" +
               $"Currently working on milestone {ctx.InterruptedAtMilestoneIndex}.\n" +
               "The research will continue from where it left off.";
    }

    private sealed class IntentAnalysisJson
    {
        public string? IntentType { get; init; }
        public string? Summary { get; init; }
        public string? DirectionChangeDescription { get; init; }
        public List<string>? SuggestedNewMilestones { get; init; }
        public string? SystemReplyContent { get; init; }
    }

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
            var (verifier, verifierId) = await _vibe.Runtime.GetVerifierAgentAsync(session.Id, providerOverride, ct);

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
            var (verifier, verifierId) = await _vibe.Runtime.GetVerifierAgentAsync(session.Id, providerOverride, ct);

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
        SessionInputInDto input,
        string question,
        Materials.MaterialsSnapshot materials,
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
                question,
                materials,
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

/// <summary>
/// User intent type detected from interruption message.
/// </summary>
internal enum UserIntentType
{
    /// <summary>User wants to change research direction, modify or add milestones.</summary>
    DirectionChange,

    /// <summary>User wants to know about current progress.</summary>
    ProgressInquiry,

    /// <summary>Other intent that doesn't affect research execution.</summary>
    Other
}

/// <summary>
/// Result of analyzing user intent from an interruption message.
/// </summary>
internal sealed class UserIntentAnalysis
{
    public UserIntentType IntentType { get; init; } = UserIntentType.Other;
    public string Summary { get; init; } = string.Empty;
    public string? DirectionChangeDescription { get; init; }
    public List<string>? SuggestedNewMilestones { get; init; }
    public string? SystemReplyContent { get; init; }
}

internal sealed class MilestoneLoopResult
{
    public bool Ok { get; init; }
    public string StopReason { get; init; } = "completed"; // completed | cancelled | error | no_milestones | interrupted | auto_extended
    public int MilestonesExecuted { get; init; }
    public int TotalMilestones { get; init; }
    public int MilestonesSkipped { get; init; } // Number of milestones skipped due to already completed
    public int ExtensionRound { get; init; } // Current auto-extension round (0 = original plan)
}

/// <summary>
/// Context information about an interrupted milestone loop execution.
/// Used to provide context when analyzing user intent and handling interruptions.
/// </summary>
internal sealed class InterruptionContext
{
    /// <summary>The run ID that was interrupted.</summary>
    public string InterruptedRunId { get; init; } = string.Empty;

    /// <summary>The new user message that triggered the interruption.</summary>
    public string NewUserMessage { get; init; } = string.Empty;

    /// <summary>When the interruption occurred.</summary>
    public DateTimeOffset InterruptedAt { get; init; }

    /// <summary>Reason for the interruption (e.g., "new_input").</summary>
    public string Reason { get; init; } = string.Empty;

    /// <summary>The milestone index (1-based) where the interruption occurred.</summary>
    public int InterruptedAtMilestoneIndex { get; set; }

    /// <summary>The milestone node ID where the interruption occurred.</summary>
    public string? InterruptedAtMilestoneNodeId { get; set; }

    /// <summary>Total number of milestones in the current plan.</summary>
    public int TotalMilestones { get; set; }

    /// <summary>Number of milestones that have been completed.</summary>
    public int CompletedMilestones { get; set; }
}
