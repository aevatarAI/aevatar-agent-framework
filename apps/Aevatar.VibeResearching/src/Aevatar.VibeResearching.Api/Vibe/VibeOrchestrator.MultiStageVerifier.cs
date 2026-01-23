using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Aevatar.Agents.AGUI;
using Aevatar.Agents.AI;
using Aevatar.Agents.AI.Core.Utils;
using VibeResearching.Api.Sessions;
using VibeResearching.Api.Vibe.Dag;
using VibeResearching.Contracts.Collab;
using VibeResearching.Vibe;

namespace VibeResearching.Api.Vibe;

// ============================================================
//  Multi-Stage Verifier
//
//  Two-phase verification inspired by hypothesis_promotion_loop_hpa.yaml:
//  - Scout phase: 2 workers for quick refutation detection
//  - Prover phase: 5 workers for proof verification (only if scout passes)
// ============================================================

internal sealed partial class VibeOrchestrator
{
    // ============================================================
    //  Verification Results Log Directory
    // ============================================================
    private static readonly string VerificationLogDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".aevatar",
        "verification_logs"
    );

    // ============================================================
    //  Multi-Stage Verification Entry Point
    // ============================================================

    /// <summary>
    /// Run multi-stage verification on reasoner output.
    /// Pre-Processing: Extract, select, and decompose hypotheses
    /// Phase 1 (Scout): 2 workers must both accept to proceed.
    /// Phase 2 (Prover): At least 3 of 5 workers must accept to pass.
    /// </summary>
    private async Task<MultiStageVerificationResult> RunMultiStageVerifierAsync(
        VibeRoundContext ctx,
        SraDagSnapshot dag,
        string? reasonerOutput,
        string? providerName,
        CancellationToken ct)
    {
        var session = ctx.Session;
        session.Events.Publish(new StepStartedEvent { Timestamp = NowMs(), StepName = "vibe.verifier.multi_stage" });

        // ============================================================
        //  Pre-Processing Phase: Extract, Select, Decompose
        // ============================================================
        PreProcessingResult? preProcessingResult = null;
        ExtractedHypothesis? selectedHypothesis = null;
        HypothesisDecompositionResult? decompositionResult = null;
        HypothesisExtractionResult? extractionResult = null;
        HypothesisSelectionResult? selectionResult = null;
        var verifiedHypotheses = new List<ExtractedHypothesis>();
        
        // Collect verification results from the loop (for saving to file)
        var loopScoutResults = new List<VerificationWorkerResult>();
        var loopProverResults = new List<VerificationWorkerResult>();

        try
        {
            EmitAgentStatusReport(session, "verifier", "🔍 Pre-Processing: Extracting hypotheses from reasoner output...");

            // Step 1: Extract all hypotheses from reasoner output
            extractionResult = await ExtractHypothesesAsync(ctx, reasonerOutput, providerName, ct);
            if (extractionResult == null || extractionResult.Hypotheses.Count == 0)
            {
                // If no hypotheses found, create placeholder results for Step 2 and Step 3
                EmitAgentStatusReport(session, "verifier", "⚠️ No hypotheses extracted. Proceeding with full reasoner output.");
                
                // Create placeholder for Step 2
                var step2SystemPrompt = """
                    You are a hypothesis selector. Your task is to choose the hypothesis that is easiest to verify.

                    NOTE: Step 2 was skipped because no hypotheses were extracted in Step 1.
                    """;
                var step2UserMessage = """
                    No hypotheses were extracted from the reasoner output, so the selection step was skipped.
                    """;
                selectionResult = new HypothesisSelectionResult(
                    SelectedHypothesis: new ExtractedHypothesis("N/A", "No hypotheses extracted", "Step 1 did not extract any hypotheses", null),
                    SelectionReason: "Step 2 skipped: No hypotheses extracted in Step 1",
                    RawOutput: "Step 2 skipped: No hypotheses extracted",
                    SystemPrompt: step2SystemPrompt,
                    UserPrompt: step2UserMessage
                );
                
                // Create placeholder for Step 3
                var step3SystemPrompt = """
                    You are a hypothesis decomposer. Your task is to break down a hypothesis into its logical dependencies.

                    NOTE: Step 3 was skipped because no hypothesis was selected in Step 2.
                    """;
                var step3UserMessage = """
                    No hypothesis was selected, so the decomposition step was skipped.
                    """;
                decompositionResult = new HypothesisDecompositionResult(
                    Hypothesis: new ExtractedHypothesis("N/A", "No hypothesis selected", "Step 2 did not select any hypothesis", null),
                    Dependencies: new List<DependencyTheorem>(),
                    DerivationPath: new List<string>(),
                    DecompositionReason: "Step 3 skipped: No hypothesis selected in Step 2",
                    RawOutput: "Step 3 skipped: No hypothesis selected",
                    SystemPrompt: step3SystemPrompt,
                    UserPrompt: step3UserMessage
                );
            }
            else
            {
                EmitAgentStatusReport(session, "verifier", $"✅ Extracted {extractionResult.Hypotheses.Count} hypotheses.");

                // ============================================================
                //  Verification Loop: Step 2 → Step 3 → Phase 1 → Phase 2
                //  For each iteration, select the easiest hypothesis from remaining ones,
                //  then verify it. Continue until all hypotheses are processed.
                // ============================================================
                EmitAgentStatusReport(session, "verifier", $"🔄 Starting verification loop for {extractionResult.Hypotheses.Count} hypotheses...");
                
                // Keep track of remaining hypotheses (not yet verified or failed)
                var remainingHypotheses = new List<ExtractedHypothesis>(extractionResult.Hypotheses);
                var iterationCount = 0;
                
                while (remainingHypotheses.Count > 0)
                {
                    iterationCount++;
                    EmitAgentStatusReport(session, "verifier", $"🔄 [Iteration {iterationCount}] {remainingHypotheses.Count} hypotheses remaining.");
                    
                    // Step 2: Select the easiest hypothesis from remaining ones
                    ExtractedHypothesis? selectedHypothesisInLoop = null;
                    HypothesisSelectionResult? selectionResultInLoop = null;
                    
                    if (remainingHypotheses.Count == 1)
                    {
                        // Only one hypothesis left, select it automatically
                        selectedHypothesisInLoop = remainingHypotheses[0];
                        selectionResultInLoop = new HypothesisSelectionResult(
                            SelectedHypothesis: selectedHypothesisInLoop,
                            SelectionReason: "Only one hypothesis remaining",
                            RawOutput: "Single hypothesis - selection step skipped",
                            SystemPrompt: "Hypothesis selector - single remaining hypothesis",
                            UserPrompt: $"Only one hypothesis remaining: {selectedHypothesisInLoop.Id}"
                        );
                    }
                    else
                    {
                        // Step 2: Select the easiest hypothesis from remaining ones
                        EmitAgentStatusReport(session, "verifier", $"🔍 Step 2: Selecting easiest hypothesis from {remainingHypotheses.Count} remaining...");
                        selectionResultInLoop = await SelectEasiestHypothesisAsync(ctx, remainingHypotheses, providerName, ct);
                        
                        if (selectionResultInLoop != null)
                        {
                            selectedHypothesisInLoop = selectionResultInLoop.SelectedHypothesis;
                            EmitAgentStatusReport(session, "verifier", $"✅ Selected hypothesis {selectedHypothesisInLoop.Id}: {Bound(selectedHypothesisInLoop.Statement, 100)}");
                        }
                        else
                        {
                            // Selection failed, use first remaining hypothesis as fallback
                            selectedHypothesisInLoop = remainingHypotheses[0];
                            selectionResultInLoop = new HypothesisSelectionResult(
                                SelectedHypothesis: selectedHypothesisInLoop,
                                SelectionReason: "Selection failed, using first remaining as fallback",
                                RawOutput: "Selection failed - using first remaining",
                                SystemPrompt: "Hypothesis selector - fallback",
                                UserPrompt: "Selection failed, using first remaining hypothesis"
                            );
                            EmitAgentStatusReport(session, "verifier", $"⚠️ Selection failed, using first remaining: {selectedHypothesisInLoop.Id}");
                        }
                    }
                    
                    // Remove selected hypothesis from remaining list
                    remainingHypotheses.Remove(selectedHypothesisInLoop);
                    
                    // Verify this hypothesis (Step 3 → Phase 1 → Phase 2)
                    EmitAgentStatusReport(session, "verifier", $"🔍 Verifying hypothesis {selectedHypothesisInLoop.Id}: {Bound(selectedHypothesisInLoop.Statement, 100)}");
                    var (verificationPassed, scoutResults, proverResults) = await VerifySingleHypothesisAsync(
                        ctx, dag, selectedHypothesisInLoop, reasonerOutput, providerName, ct);
                    
                    // Collect results from loop verification (for saving to file)
                    // Save results from the first verified hypothesis (or first hypothesis if none verified)
                    if (verifiedHypotheses.Count == 0)
                    {
                        loopScoutResults.AddRange(scoutResults);
                        loopProverResults.AddRange(proverResults);
                    }
                    
                    if (verificationPassed)
                    {
                        verifiedHypotheses.Add(selectedHypothesisInLoop);
                        EmitAgentStatusReport(session, "verifier", $"✅ Hypothesis {selectedHypothesisInLoop.Id} verified and added to verified list.");
                        
                        // If this is the first verified hypothesis, update loop results for saving
                        if (verifiedHypotheses.Count == 1)
                        {
                            loopScoutResults.Clear();
                            loopProverResults.Clear();
                            loopScoutResults.AddRange(scoutResults);
                            loopProverResults.AddRange(proverResults);
                            
                            // Save selection result for first verified hypothesis
                            selectionResult = selectionResultInLoop;
                            selectedHypothesis = selectedHypothesisInLoop;
                        }
                    }
                    else
                    {
                        EmitAgentStatusReport(session, "verifier", $"❌ Hypothesis {selectedHypothesisInLoop.Id} verification failed.");
                        
                        // If no hypotheses verified yet, save selection result for fallback
                        if (verifiedHypotheses.Count == 0 && selectionResult == null)
                        {
                            selectionResult = selectionResultInLoop;
                            selectedHypothesis = selectedHypothesisInLoop;
                        }
                    }
                }
                
                EmitAgentStatusReport(session, "verifier", $"📊 Verification loop complete: {verifiedHypotheses.Count}/{extractionResult.Hypotheses.Count} hypotheses verified after {iterationCount} iterations.");
                
                // Use the first verified hypothesis (or first selected hypothesis if none verified) for legacy compatibility
                if (verifiedHypotheses.Count > 0)
                {
                    // Already set above when first hypothesis was verified
                    if (selectedHypothesis == null)
                    {
                        selectedHypothesis = verifiedHypotheses[0];
                        selectionResult = new HypothesisSelectionResult(
                            SelectedHypothesis: selectedHypothesis,
                            SelectionReason: $"Selected from {verifiedHypotheses.Count} verified hypotheses",
                            RawOutput: $"Selected first verified hypothesis: {selectedHypothesis.Id}",
                            SystemPrompt: "Hypothesis selected from verified list",
                            UserPrompt: $"Selected hypothesis {selectedHypothesis.Id} from {verifiedHypotheses.Count} verified hypotheses"
                        );
                    }
                }
                else if (selectedHypothesis == null && extractionResult.Hypotheses.Count > 0)
                {
                    // Fallback: use first hypothesis even if not verified
                    selectedHypothesis = extractionResult.Hypotheses[0];
                    selectionResult = new HypothesisSelectionResult(
                        SelectedHypothesis: selectedHypothesis,
                        SelectionReason: "No hypotheses verified, using first hypothesis as fallback",
                        RawOutput: "No verified hypotheses, using first as fallback",
                        SystemPrompt: "Hypothesis selection fallback",
                        UserPrompt: "No hypotheses passed verification, using first hypothesis"
                    );
                }
                
                // Step 3: Decompose the selected hypothesis (for legacy compatibility and final summary)
                if (selectedHypothesis != null)
                {
                    EmitAgentStatusReport(session, "verifier", "🔧 Decomposing selected hypothesis for final summary...");
                    decompositionResult = await DecomposeHypothesisAsync(ctx, dag, selectedHypothesis, providerName, ct);
                    if (decompositionResult != null)
                    {
                        EmitAgentStatusReport(session, "verifier", $"✅ Found {decompositionResult.Dependencies.Count} dependencies.");
                    }
                }
            }

            preProcessingResult = new PreProcessingResult(
                ExtractionResult: extractionResult,
                SelectionResult: selectionResult,
                DecompositionResult: decompositionResult,
                Success: true,
                ErrorMessage: null
            );
        }
        catch (Exception ex)
        {
            // If pre-processing fails, continue with full reasoner output
            EmitAgentStatusReport(session, "verifier", $"⚠️ Pre-processing failed: {ex.Message}. Proceeding with full reasoner output.");
            preProcessingResult = new PreProcessingResult(
                ExtractionResult: null,
                SelectionResult: null,
                DecompositionResult: null,
                Success: false,
                ErrorMessage: ex.Message
            );
        }

        // ============================================================
        //  Final Summary Phase (only if no hypotheses were verified in loop)
        //  If verified hypotheses exist, skip final verification and use summary
        // ============================================================
        VerificationPhaseResult? scoutPhase = null;
        VerificationPhaseResult? proverPhase = null;
        
        if (verifiedHypotheses.Count == 0 && selectedHypothesis != null)
        {
            // No hypotheses were verified in the loop, perform final verification for summary
            // Build verification context (use selected hypothesis if available, otherwise use full reasoner output)
            var verificationContext = BuildVerificationContext(reasonerOutput, selectedHypothesis, decompositionResult);

            // ============================================================
            //  Phase 1: Scout (2 workers, both must accept)
            // ============================================================
            EmitAgentStatusReport(session, "verifier", AgentStatusMessages.VerifierScoutStart);

            var scoutResults = await RunVerificationPhaseAsync(
                ctx, dag, verificationContext, providerName,
                ScoutWorkers.All,
                phaseName: "scout",
                ct);

            scoutPhase = new VerificationPhaseResult(
                PhaseName: "scout",
                Results: scoutResults,
                AcceptCount: scoutResults.Count(r => r.Accept),
                RejectCount: scoutResults.Count(r => !r.Accept),
                PhasePass: scoutResults.All(r => r.Accept) // Both must accept
            );

            // Emit scout phase summary
            EmitVerificationPhaseSummary(session, scoutPhase);

            // If scout phase fails, stop early
            if (!scoutPhase.PhasePass)
            {
                session.Events.Publish(new StepFinishedEvent { Timestamp = NowMs(), StepName = "vibe.verifier.multi_stage" });

                var earlyResult = new MultiStageVerificationResult(
                    PreProcessing: preProcessingResult,
                    ScoutPhase: scoutPhase,
                    ProverPhase: null,
                    OverallPass: false,
                    Summary: BuildVerificationSummary(preProcessingResult, scoutPhase, null, overallPass: false, verifiedHypotheses),
                    VerifiedHypotheses: verifiedHypotheses
                );

                // Save results to file
                await SaveVerificationResultsToFileAsync(session.Id, ctx.RunId, earlyResult, reasonerOutput, ct);

                return earlyResult;
            }

            // ============================================================
            //  Phase 2: Prover (5 workers, at least 3 must accept)
            // ============================================================
            EmitAgentStatusReport(session, "verifier", AgentStatusMessages.VerifierProverStart);

            var proverResults = await RunVerificationPhaseAsync(
                ctx, dag, verificationContext, providerName,
                ProverWorkers.All,
                phaseName: "prover",
                ct);

            proverPhase = new VerificationPhaseResult(
                PhaseName: "prover",
                Results: proverResults,
                AcceptCount: proverResults.Count(r => r.Accept),
                RejectCount: proverResults.Count(r => !r.Accept),
                PhasePass: proverResults.Count(r => r.Accept) >= ProverWorkers.MinAcceptCount
            );

            // Emit prover phase summary
            EmitVerificationPhaseSummary(session, proverPhase);
        }
        else if (verifiedHypotheses.Count > 0)
        {
            // Hypotheses were verified in the loop, use loop results for summary
            EmitAgentStatusReport(session, "verifier", $"✅ Skipping final verification phase - {verifiedHypotheses.Count} hypotheses already verified in loop.");
            
            // Create phase results from loop verification (use results from first verified hypothesis)
            if (loopScoutResults.Count > 0)
            {
                scoutPhase = new VerificationPhaseResult(
                    PhaseName: "scout",
                    Results: loopScoutResults,
                    AcceptCount: loopScoutResults.Count(r => r.Accept),
                    RejectCount: loopScoutResults.Count(r => !r.Accept),
                    PhasePass: loopScoutResults.All(r => r.Accept)
                );
            }
            
            if (loopProverResults.Count > 0)
            {
                proverPhase = new VerificationPhaseResult(
                    PhaseName: "prover",
                    Results: loopProverResults,
                    AcceptCount: loopProverResults.Count(r => r.Accept),
                    RejectCount: loopProverResults.Count(r => !r.Accept),
                    PhasePass: loopProverResults.Count(r => r.Accept) >= ProverWorkers.MinAcceptCount
                );
            }
        }

        session.Events.Publish(new StepFinishedEvent { Timestamp = NowMs(), StepName = "vibe.verifier.multi_stage" });

        // Overall pass: true if at least one hypothesis was verified, or if final prover phase passed
        var overallPass = verifiedHypotheses.Count > 0 || (proverPhase?.PhasePass ?? false);

        var finalResult = new MultiStageVerificationResult(
            PreProcessing: preProcessingResult,
            ScoutPhase: scoutPhase,
            ProverPhase: proverPhase,
            OverallPass: overallPass,
            Summary: BuildVerificationSummary(preProcessingResult, scoutPhase, proverPhase, overallPass, verifiedHypotheses),
            VerifiedHypotheses: verifiedHypotheses
        );

        // Save results to file
        await SaveVerificationResultsToFileAsync(session.Id, ctx.RunId, finalResult, reasonerOutput, ct);

        return finalResult;
    }

    // ============================================================
    //  Save verification results to file
    // ============================================================

    private static async Task SaveVerificationResultsToFileAsync(
        string sessionId,
        string runId,
        MultiStageVerificationResult result,
        string? reasonerOutput,
        CancellationToken ct)
    {
        try
        {
            // Ensure directory exists
            Directory.CreateDirectory(VerificationLogDir);

            // Generate filename with timestamp
            var timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd_HH-mm-ss");
            var filename = $"verification_{timestamp}_{sessionId[..8]}_{runId[..8]}.md";
            var filepath = Path.Combine(VerificationLogDir, filename);

            var sb = new StringBuilder();

            // Header
            sb.AppendLine("# Multi-Stage Verification Results");
            sb.AppendLine();
            sb.AppendLine($"- **Session ID**: {sessionId}");
            sb.AppendLine($"- **Run ID**: {runId}");
            sb.AppendLine($"- **Timestamp**: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
            sb.AppendLine($"- **Overall Result**: {(result.OverallPass ? "✅ PASSED" : "❌ FAILED")}");
            sb.AppendLine();
            sb.AppendLine("---");
            sb.AppendLine();

            // Pre-Processing Phase
            if (result.PreProcessing != null)
            {
                sb.AppendLine("## Pre-Processing Phase");
                sb.AppendLine();
                sb.AppendLine($"- **Success**: {(result.PreProcessing.Success ? "✅ YES" : "❌ NO")}");
                if (!string.IsNullOrWhiteSpace(result.PreProcessing.ErrorMessage))
                {
                    sb.AppendLine($"- **Error**: {result.PreProcessing.ErrorMessage}");
                }
                sb.AppendLine();

                if (result.PreProcessing.ExtractionResult != null)
                {
                    sb.AppendLine("### Step 1: Hypothesis Extraction");
                    sb.AppendLine();
                    sb.AppendLine($"- **Extracted**: {result.PreProcessing.ExtractionResult.Hypotheses.Count} hypotheses");
                    sb.AppendLine();
                    foreach (var h in result.PreProcessing.ExtractionResult.Hypotheses)
                    {
                        sb.AppendLine($"#### Hypothesis {h.Id}");
                        sb.AppendLine($"- **Statement**: {h.Statement}");
                        if (!string.IsNullOrWhiteSpace(h.Context))
                        {
                            sb.AppendLine($"- **Context**: {h.Context}");
                        }
                        if (h.Confidence.HasValue)
                        {
                            sb.AppendLine($"- **Confidence**: {h.Confidence.Value:F2}");
                        }
                        sb.AppendLine();
                    }
                    sb.AppendLine("<details>");
                    sb.AppendLine("<summary>System Prompt</summary>");
                    sb.AppendLine();
                    sb.AppendLine("```");
                    sb.AppendLine(result.PreProcessing.ExtractionResult.SystemPrompt ?? "(not captured)");
                    sb.AppendLine("```");
                    sb.AppendLine();
                    sb.AppendLine("</details>");
                    sb.AppendLine();
                    sb.AppendLine("<details>");
                    sb.AppendLine("<summary>User Prompt</summary>");
                    sb.AppendLine();
                    sb.AppendLine("```");
                    sb.AppendLine(result.PreProcessing.ExtractionResult.UserPrompt ?? "(not captured)");
                    sb.AppendLine("```");
                    sb.AppendLine();
                    sb.AppendLine("</details>");
                    sb.AppendLine();
                    sb.AppendLine("<details>");
                    sb.AppendLine("<summary>Raw Extraction Output</summary>");
                    sb.AppendLine();
                    sb.AppendLine("```");
                    sb.AppendLine(result.PreProcessing.ExtractionResult.RawOutput);
                    sb.AppendLine("```");
                    sb.AppendLine();
                    sb.AppendLine("</details>");
                    sb.AppendLine();
                }

                if (result.PreProcessing.SelectionResult != null)
                {
                    sb.AppendLine("### Step 2: Hypothesis Selection");
                    sb.AppendLine();
                    sb.AppendLine($"- **Selected ID**: {result.PreProcessing.SelectionResult.SelectedHypothesis.Id}");
                    sb.AppendLine($"- **Statement**: {result.PreProcessing.SelectionResult.SelectedHypothesis.Statement}");
                    sb.AppendLine($"- **Reason**: {result.PreProcessing.SelectionResult.SelectionReason}");
                    sb.AppendLine();
                    sb.AppendLine("<details>");
                    sb.AppendLine("<summary>System Prompt</summary>");
                    sb.AppendLine();
                    sb.AppendLine("```");
                    sb.AppendLine(result.PreProcessing.SelectionResult.SystemPrompt ?? "(not captured)");
                    sb.AppendLine("```");
                    sb.AppendLine();
                    sb.AppendLine("</details>");
                    sb.AppendLine();
                    sb.AppendLine("<details>");
                    sb.AppendLine("<summary>User Prompt</summary>");
                    sb.AppendLine();
                    sb.AppendLine("```");
                    sb.AppendLine(result.PreProcessing.SelectionResult.UserPrompt ?? "(not captured)");
                    sb.AppendLine("```");
                    sb.AppendLine();
                    sb.AppendLine("</details>");
                    sb.AppendLine();
                    sb.AppendLine("<details>");
                    sb.AppendLine("<summary>Raw Selection Output</summary>");
                    sb.AppendLine();
                    sb.AppendLine("```");
                    sb.AppendLine(result.PreProcessing.SelectionResult.RawOutput);
                    sb.AppendLine("```");
                    sb.AppendLine();
                    sb.AppendLine("</details>");
                    sb.AppendLine();
                }

                if (result.PreProcessing.DecompositionResult != null)
                {
                    sb.AppendLine("### Step 3: Hypothesis Decomposition");
                    sb.AppendLine();
                    sb.AppendLine($"- **Dependencies Found**: {result.PreProcessing.DecompositionResult.Dependencies.Count}");
                    sb.AppendLine($"- **Derivation Path Length**: {result.PreProcessing.DecompositionResult.DerivationPath.Count}");
                    sb.AppendLine();
                    sb.AppendLine("#### Dependencies (in derivation order):");
                    foreach (var dep in result.PreProcessing.DecompositionResult.Dependencies.OrderBy(d => d.DerivationOrder))
                    {
                        sb.AppendLine($"{dep.DerivationOrder}. **[{dep.Id}]** {dep.Statement}");
                    }
                    sb.AppendLine();
                    if (result.PreProcessing.DecompositionResult.DerivationPath.Count > 0)
                    {
                        sb.AppendLine("#### Derivation Path:");
                        sb.AppendLine(string.Join(" → ", result.PreProcessing.DecompositionResult.DerivationPath));
                        sb.AppendLine();
                    }
                    sb.AppendLine($"**Decomposition Reason**: {result.PreProcessing.DecompositionResult.DecompositionReason}");
                    sb.AppendLine();
                    sb.AppendLine("<details>");
                    sb.AppendLine("<summary>System Prompt</summary>");
                    sb.AppendLine();
                    sb.AppendLine("```");
                    sb.AppendLine(result.PreProcessing.DecompositionResult.SystemPrompt ?? "(not captured)");
                    sb.AppendLine("```");
                    sb.AppendLine();
                    sb.AppendLine("</details>");
                    sb.AppendLine();
                    sb.AppendLine("<details>");
                    sb.AppendLine("<summary>User Prompt</summary>");
                    sb.AppendLine();
                    sb.AppendLine("```");
                    sb.AppendLine(result.PreProcessing.DecompositionResult.UserPrompt ?? "(not captured)");
                    sb.AppendLine("```");
                    sb.AppendLine();
                    sb.AppendLine("</details>");
                    sb.AppendLine();
                    sb.AppendLine("<details>");
                    sb.AppendLine("<summary>Raw Decomposition Output</summary>");
                    sb.AppendLine();
                    sb.AppendLine("```");
                    sb.AppendLine(result.PreProcessing.DecompositionResult.RawOutput);
                    sb.AppendLine("```");
                    sb.AppendLine();
                    sb.AppendLine("</details>");
                    sb.AppendLine();
                }

                sb.AppendLine("---");
                sb.AppendLine();
            }

            // Verified Hypotheses Summary
            if (result.VerifiedHypotheses.Count > 0)
            {
                sb.AppendLine("## Verified Hypotheses Summary");
                sb.AppendLine();
                sb.AppendLine($"- **Total Verified**: {result.VerifiedHypotheses.Count} hypotheses passed verification");
                sb.AppendLine();
                foreach (var h in result.VerifiedHypotheses)
                {
                    sb.AppendLine($"### ✅ Verified Hypothesis: {h.Id}");
                    sb.AppendLine();
                    sb.AppendLine($"- **Statement**: {h.Statement}");
                    if (!string.IsNullOrWhiteSpace(h.Context))
                    {
                        sb.AppendLine($"- **Context**: {h.Context}");
                    }
                    if (h.Confidence.HasValue)
                    {
                        sb.AppendLine($"- **Confidence**: {h.Confidence.Value:F2}");
                    }
                    sb.AppendLine();
                }
                sb.AppendLine("---");
                sb.AppendLine();
            }

            // Phase 1: Scout
            sb.AppendLine("## Phase 1: Scout (Quick Refutation Detection)");
            sb.AppendLine();
            if (result.ScoutPhase != null)
            {
                sb.AppendLine($"- **Workers**: {result.ScoutPhase.Results.Count}");
                sb.AppendLine($"- **Accept**: {result.ScoutPhase.AcceptCount}");
                sb.AppendLine($"- **Reject**: {result.ScoutPhase.RejectCount}");
                sb.AppendLine($"- **Requirement**: All workers must accept");
                sb.AppendLine($"- **Result**: {(result.ScoutPhase.PhasePass ? "✅ PASS" : "❌ FAIL")}");
                sb.AppendLine();

                foreach (var r in result.ScoutPhase.Results)
                {
                    var icon = r.Accept ? "✅" : "❌";
                    sb.AppendLine($"### {icon} {r.WorkerId}");
                    sb.AppendLine();
                    sb.AppendLine($"**Accept**: {r.Accept}");
                    sb.AppendLine();
                    sb.AppendLine($"**Reason**: {r.Reason}");
                    sb.AppendLine();
                    sb.AppendLine("<details>");
                    sb.AppendLine("<summary>System Prompt</summary>");
                    sb.AppendLine();
                    sb.AppendLine("```");
                    sb.AppendLine(r.SystemPrompt ?? "(not captured)");
                    sb.AppendLine("```");
                    sb.AppendLine();
                    sb.AppendLine("</details>");
                    sb.AppendLine();
                    sb.AppendLine("<details>");
                    sb.AppendLine("<summary>User Prompt</summary>");
                    sb.AppendLine();
                    sb.AppendLine("```");
                    sb.AppendLine(r.UserPrompt ?? "(not captured)");
                    sb.AppendLine("```");
                    sb.AppendLine();
                    sb.AppendLine("</details>");
                    sb.AppendLine();
                    sb.AppendLine("<details>");
                    sb.AppendLine("<summary>Raw Output</summary>");
                    sb.AppendLine();
                    sb.AppendLine("```");
                    sb.AppendLine(r.RawOutput);
                    sb.AppendLine("```");
                    sb.AppendLine();
                    sb.AppendLine("</details>");
                    sb.AppendLine();
                }
            }
            sb.AppendLine("---");
            sb.AppendLine();

            // Phase 2: Prover
            sb.AppendLine("## Phase 2: Prover (Proof Verification)");
            sb.AppendLine();
            if (result.ProverPhase != null)
            {
                sb.AppendLine($"- **Workers**: {result.ProverPhase.Results.Count}");
                sb.AppendLine($"- **Accept**: {result.ProverPhase.AcceptCount}");
                sb.AppendLine($"- **Reject**: {result.ProverPhase.RejectCount}");
                sb.AppendLine($"- **Requirement**: At least {ProverWorkers.MinAcceptCount} workers must accept");
                sb.AppendLine($"- **Result**: {(result.ProverPhase.PhasePass ? "✅ PASS" : "❌ FAIL")}");
                sb.AppendLine();

                foreach (var r in result.ProverPhase.Results)
                {
                    var icon = r.Accept ? "✅" : "❌";
                    sb.AppendLine($"### {icon} {r.WorkerId}");
                    sb.AppendLine();
                    sb.AppendLine($"**Accept**: {r.Accept}");
                    sb.AppendLine();
                    sb.AppendLine($"**Reason**: {r.Reason}");
                    sb.AppendLine();
                    sb.AppendLine("<details>");
                    sb.AppendLine("<summary>System Prompt</summary>");
                    sb.AppendLine();
                    sb.AppendLine("```");
                    sb.AppendLine(r.SystemPrompt ?? "(not captured)");
                    sb.AppendLine("```");
                    sb.AppendLine();
                    sb.AppendLine("</details>");
                    sb.AppendLine();
                    sb.AppendLine("<details>");
                    sb.AppendLine("<summary>User Prompt</summary>");
                    sb.AppendLine();
                    sb.AppendLine("```");
                    sb.AppendLine(r.UserPrompt ?? "(not captured)");
                    sb.AppendLine("```");
                    sb.AppendLine();
                    sb.AppendLine("</details>");
                    sb.AppendLine();
                    sb.AppendLine("<details>");
                    sb.AppendLine("<summary>Raw Output</summary>");
                    sb.AppendLine();
                    sb.AppendLine("```");
                    sb.AppendLine(r.RawOutput);
                    sb.AppendLine("```");
                    sb.AppendLine();
                    sb.AppendLine("</details>");
                    sb.AppendLine();
                }
            }
            else
            {
                sb.AppendLine("*Skipped - Scout phase failed*");
                sb.AppendLine();
            }
            sb.AppendLine("---");
            sb.AppendLine();

            // Reasoner Output (truncated)
            sb.AppendLine("## Reasoner Output (Input to Verification)");
            sb.AppendLine();
            sb.AppendLine("<details>");
            sb.AppendLine("<summary>Click to expand</summary>");
            sb.AppendLine();
            sb.AppendLine("```markdown");
            sb.AppendLine(reasonerOutput ?? "(No reasoner output)");
            sb.AppendLine("```");
            sb.AppendLine();
            sb.AppendLine("</details>");
            sb.AppendLine();

            // Write to file
            await File.WriteAllTextAsync(filepath, sb.ToString(), ct);

            // Also write a JSON version for programmatic access
            var jsonFilepath = Path.ChangeExtension(filepath, ".json");
            var jsonContent = JsonSerializer.Serialize(new
            {
                sessionId,
                runId,
                timestamp = DateTime.UtcNow,
                overallPass = result.OverallPass,
                preProcessing = result.PreProcessing != null ? new
                {
                    success = result.PreProcessing.Success,
                    errorMessage = result.PreProcessing.ErrorMessage,
                    extractionResult = result.PreProcessing.ExtractionResult != null ? new
                    {
                        hypotheses = result.PreProcessing.ExtractionResult.Hypotheses.Select(h => new
                        {
                            id = h.Id,
                            statement = h.Statement,
                            context = h.Context,
                            confidence = h.Confidence
                        }).ToList(),
                        rawOutput = result.PreProcessing.ExtractionResult.RawOutput,
                        systemPrompt = result.PreProcessing.ExtractionResult.SystemPrompt,
                        userPrompt = result.PreProcessing.ExtractionResult.UserPrompt
                    } : null,
                    selectionResult = result.PreProcessing.SelectionResult != null ? new
                    {
                        selectedHypothesis = new
                        {
                            id = result.PreProcessing.SelectionResult.SelectedHypothesis.Id,
                            statement = result.PreProcessing.SelectionResult.SelectedHypothesis.Statement,
                            context = result.PreProcessing.SelectionResult.SelectedHypothesis.Context,
                            confidence = result.PreProcessing.SelectionResult.SelectedHypothesis.Confidence
                        },
                        selectionReason = result.PreProcessing.SelectionResult.SelectionReason,
                        rawOutput = result.PreProcessing.SelectionResult.RawOutput,
                        systemPrompt = result.PreProcessing.SelectionResult.SystemPrompt,
                        userPrompt = result.PreProcessing.SelectionResult.UserPrompt
                    } : null,
                    decompositionResult = result.PreProcessing.DecompositionResult != null ? new
                    {
                        hypothesis = new
                        {
                            id = result.PreProcessing.DecompositionResult.Hypothesis.Id,
                            statement = result.PreProcessing.DecompositionResult.Hypothesis.Statement,
                            context = result.PreProcessing.DecompositionResult.Hypothesis.Context,
                            confidence = result.PreProcessing.DecompositionResult.Hypothesis.Confidence
                        },
                        dependencies = result.PreProcessing.DecompositionResult.Dependencies.Select(d => new
                        {
                            id = d.Id,
                            statement = d.Statement,
                            derivationOrder = d.DerivationOrder
                        }).ToList(),
                        derivationPath = result.PreProcessing.DecompositionResult.DerivationPath,
                        decompositionReason = result.PreProcessing.DecompositionResult.DecompositionReason,
                        rawOutput = result.PreProcessing.DecompositionResult.RawOutput,
                        systemPrompt = result.PreProcessing.DecompositionResult.SystemPrompt,
                        userPrompt = result.PreProcessing.DecompositionResult.UserPrompt
                    } : null
                } : null,
                scoutPhase = result.ScoutPhase != null ? new
                {
                    acceptCount = result.ScoutPhase.AcceptCount,
                    rejectCount = result.ScoutPhase.RejectCount,
                    phasePass = result.ScoutPhase.PhasePass,
                    results = result.ScoutPhase.Results.Select(r => new
                    {
                        workerId = r.WorkerId,
                        accept = r.Accept,
                        reason = r.Reason,
                        systemPrompt = r.SystemPrompt,
                        userPrompt = r.UserPrompt,
                        rawOutput = r.RawOutput
                    }).ToList()
                } : null,
                proverPhase = result.ProverPhase != null ? new
                {
                    acceptCount = result.ProverPhase.AcceptCount,
                    rejectCount = result.ProverPhase.RejectCount,
                    phasePass = result.ProverPhase.PhasePass,
                    results = result.ProverPhase.Results.Select(r => new
                    {
                        workerId = r.WorkerId,
                        accept = r.Accept,
                        reason = r.Reason,
                        systemPrompt = r.SystemPrompt,
                        userPrompt = r.UserPrompt,
                        rawOutput = r.RawOutput
                    }).ToList()
                } : null,
                verifiedHypotheses = result.VerifiedHypotheses.Select(h => new
                {
                    id = h.Id,
                    statement = h.Statement,
                    context = h.Context,
                    confidence = h.Confidence
                }).ToList()
            }, new JsonSerializerOptions 
            { 
                WriteIndented = true,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping  // Allow Chinese characters without escaping
            });

            await File.WriteAllTextAsync(jsonFilepath, jsonContent, ct);

            Console.WriteLine($"[Verification] Results saved to: {filepath}");
            Console.WriteLine($"[Verification] JSON saved to: {jsonFilepath}");
        }
        catch (Exception ex)
        {
            // Don't fail the verification if file saving fails
            Console.WriteLine($"[Verification] Warning: Failed to save results to file: {ex.Message}");
        }
    }

    // ============================================================
    //  Run a single verification phase with multiple workers
    // ============================================================

    private async Task<IReadOnlyList<VerificationWorkerResult>> RunVerificationPhaseAsync(
        VibeRoundContext ctx,
        SraDagSnapshot dag,
        string? verificationContext,
        string? providerName,
        IReadOnlyList<VerificationWorker> workers,
        string phaseName,
        CancellationToken ct)
    {
        var session = ctx.Session;
        var results = new List<VerificationWorkerResult>();

        // Run workers in parallel for efficiency
        var tasks = workers.Select(worker => RunSingleVerificationWorkerAsync(
            ctx, dag, verificationContext, providerName, worker, phaseName, ct
        )).ToList();

        var workerResults = await Task.WhenAll(tasks);
        results.AddRange(workerResults);

        return results;
    }

    // ============================================================
    //  Run a single verification worker
    // ============================================================

    private async Task<VerificationWorkerResult> RunSingleVerificationWorkerAsync(
        VibeRoundContext ctx,
        SraDagSnapshot dag,
        string? verificationContext,
        string? providerName,
        VerificationWorker worker,
        string phaseName,
        CancellationToken ct)
    {
        var session = ctx.Session;
        var messageId = $"msg:{session.Id}:verifier:{phaseName}:{worker.Id}:{ctx.RunId}";
        StartAgentMessage(session, messageId, agent: $"verifier-{worker.Id}", stepName: $"vibe.verifier.{phaseName}", providerName: providerName);

        try
        {
            // Get a fresh verifier instance
            var (ver, verId) = await _core.Runtime.GetVerifierAgentAsync(session.Id, providerName, ct);

            // Build worker-specific system prompt
            var workerSystemPrompt = VibeVerifierAgent.BuildWorkerSystemPrompt(worker.Id, worker.Role, worker.Angle);

            // Build the user message
            var userMessage = BuildVerificationWorkerMessage(
                worker, ctx.Question, dag, verificationContext, ctx.Input.AttachmentPaths);

            var req = new ChatRequest
            {
                Message = userMessage,
                RequestId = ctx.Input.RequestId ?? Guid.NewGuid().ToString("N"),
                StageHint = $"session:vibe:verifier:{phaseName}:{worker.Id}"
            };
            req.Context["agent_id"] = verId;
            req.Context["materials_context"] = ctx.Materials.RenderedContext;
            req.Context["system_prompt_override"] = workerSystemPrompt;

            var sb = new StringBuilder(1024);
            var supportsStreaming = await ver.SupportsStreamingAsync(ct);

            if (!supportsStreaming)
            {
                var resp = await ver.ChatAsync(req, ct);
                var text = resp.Content ?? string.Empty;
                sb.Append(text);
                if (text.Length > 0) EmitAgentDelta(session, messageId, "assistant", text);
            }
            else
            {
                await foreach (var chunk in ver.ChatStreamAsync(req, ct))
                {
                    if (string.IsNullOrEmpty(chunk)) continue;
                    sb.Append(chunk);
                    EmitAgentDelta(session, messageId, "assistant", chunk);
                }
            }
            EmitAgentDelta(session, messageId, "assistant", "\n\n");

            // Parse the JSON response
            var rawOutput = sb.ToString();
            var (accept, reason) = ParseVerificationWorkerResponse(rawOutput, worker.Id);

            return new VerificationWorkerResult(
                WorkerId: worker.Id,
                Accept: accept,
                Reason: reason,
                RawOutput: Bound(rawOutput, 5000),
                SystemPrompt: workerSystemPrompt,
                UserPrompt: userMessage
            );
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            var msg = $"[{worker.Id} error] {ex.Message}";
            EmitAgentDelta(session, messageId, "assistant", msg + "\n\n");

            // Build prompts for error case too
            var errorSystemPrompt = VibeVerifierAgent.BuildWorkerSystemPrompt(worker.Id, worker.Role, worker.Angle);
            var errorUserPrompt = BuildVerificationWorkerMessage(worker, ctx.Question, dag, verificationContext, ctx.Input.AttachmentPaths);

            return new VerificationWorkerResult(
                WorkerId: worker.Id,
                Accept: false,
                Reason: $"Error: {ex.Message}",
                RawOutput: msg,
                SystemPrompt: errorSystemPrompt,
                UserPrompt: errorUserPrompt
            );
        }
        finally
        {
            EndAgentMessage(session, messageId);
        }
    }

    // ============================================================
    //  Build verification worker message
    // ============================================================

    private static string BuildVerificationWorkerMessage(
        VerificationWorker worker,
        string question,
        SraDagSnapshot dag,
        string? verificationContext,
        List<string>? attachments)
    {
        var sb = new StringBuilder(4096);

        sb.AppendLine($"Worker: {worker.Id} ({worker.Role})");
        sb.AppendLine($"Angle: {worker.Angle}");
        sb.AppendLine();
        sb.AppendLine($"Question: {question}");

        if (attachments is { Count: > 0 })
        {
            sb.AppendLine();
            sb.AppendLine("AttachmentPaths:");
            foreach (var p in attachments.Take(12))
                sb.AppendLine($"- {p}");
        }

        sb.AppendLine();
        sb.AppendLine("Plan:");
        sb.AppendLine(BuildPlanContextFromDag(dag));

        sb.AppendLine();
        sb.AppendLine("DAG stats:");
        sb.AppendLine($"- nodes={dag.Nodes.Count}, edges={dag.Edges.Count}");

        if (!string.IsNullOrWhiteSpace(verificationContext))
        {
            sb.AppendLine();
            sb.AppendLine("=== CONTENT TO VERIFY ===");
            sb.AppendLine(Bound(verificationContext!, 8000));
            sb.AppendLine("=== END CONTENT ===");
        }

        sb.AppendLine();
        sb.AppendLine("Your task: Evaluate the reasoning above and output JSON with your verdict.");
        sb.AppendLine($"Remember: You are {worker.Role}. Focus on: {worker.Angle}");
        sb.AppendLine();
        
        // Add detailed evaluation rules for scout-1 (Missing-premise hunter)
        if (worker.Id == "scout-1")
        {
            sb.AppendLine("SIMPLE RULE: accept=true if the hypothesis is CONSISTENT with given axioms/facts and represents a reasonable mathematical claim");
            sb.AppendLine();
            sb.AppendLine("What constitutes a valid derivation (accept=true):");
            sb.AppendLine("1. Direct application of axioms/theorems");
            sb.AppendLine("2. Logical inferences (modus ponens, transitivity, etc.)");
            sb.AppendLine("3. Mathematical operations based on definitions");
            sb.AppendLine("4. Reasonable implicit steps that follow obviously, physically plausible or logically correct");
            sb.AppendLine("5. Properties that can be DERIVED from known definitions, even if not explicitly stated in axioms");
            sb.AppendLine("6. Reasonable mathematical claims that are CONSISTENT with the axioms/facts, even if they require additional");
            sb.AppendLine("   mathematical knowledge (e.g., theta series, mass formulas, combinatorial theorems) to prove rigorously.");
            sb.AppendLine("   The key is CONSISTENCY, not complete derivability from the given axioms alone.");
            sb.AppendLine("7. Combinatorial counting formulas that are mathematically plausible given the structure defined in axioms,");
            sb.AppendLine("   even if proving it requires theta series knowledge");
            sb.AppendLine();
            sb.AppendLine("What constitutes gap δ (accept=false):");
            sb.AppendLine("1. Explicit contradiction with axioms/facts");
            sb.AppendLine("2. Counterexample exists that can be constructed from the given axioms/facts");
            sb.AppendLine("3. Crucial logical step is physically implausible or contradictory");
            sb.AppendLine("4. Missing assumption that is INCORRECT or contradicts known facts");
            sb.AppendLine();
        }
        
        sb.AppendLine("CRITICAL OUTPUT REQUIREMENTS:");
        sb.AppendLine("- Return ONLY valid JSON (no markdown, no code blocks, no commentary, no ```json tags, no ``` markers).");
        sb.AppendLine("- Output the JSON object EXACTLY ONCE. Do NOT repeat any fields, values, or fragments.");
        sb.AppendLine("- Do NOT append anything after the closing brace } of the JSON object.");
        sb.AppendLine("- Do NOT include duplicate closing braces like }}.");
        sb.AppendLine("- Do NOT include any text, numbers, or characters after the JSON object ends.");
        sb.AppendLine("- Do NOT output multiple JSON objects. Extract and output ONLY the first complete JSON object.");
        sb.AppendLine("- After outputting the closing brace }, STOP immediately. Do NOT continue with any text.");
        sb.AppendLine("- Validate your JSON structure before outputting. Count opening and closing braces to ensure balance.");
        sb.AppendLine("- If your output contains any non-JSON text (including Markdown), it will be REJECTED.");
        sb.AppendLine();
        sb.AppendLine("MANDATORY Output JSON schema (you MUST follow this exact structure):");
        sb.AppendLine("{");
        sb.AppendLine($"  \"worker_id\": \"{worker.Id}\",");
        sb.AppendLine("  \"accept\": bool,");
        sb.AppendLine("  \"reason\": string");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine("Requirements:");
        sb.AppendLine("- Output ONLY the JSON object (no markdown, no code blocks, no commentary).");
        sb.AppendLine($"- Use the exact field names: \"worker_id\" (must be \"{worker.Id}\"), \"accept\", \"reason\".");
        sb.AppendLine("- The \"accept\" field MUST be a boolean (true or false).");
        sb.AppendLine("- The \"reason\" field MUST be a string.");
        sb.AppendLine("- Do NOT output any text before or after the JSON object.");
        sb.AppendLine("- Do NOT wrap the JSON in markdown code blocks (no ```json or ```).");
        sb.AppendLine("- Do NOT add explanations or commentary outside the JSON object.");

        return sb.ToString();
    }

    // ============================================================
    //  Parse verification worker response
    // ============================================================

    private static (bool Accept, string Reason) ParseVerificationWorkerResponse(string rawOutput, string workerId)
    {
        try
        {
            // Use robust JSON extraction (handles markdown code blocks, nested brackets, etc.)
            var jsonStr = LLMResponseParser.ExtractJson(rawOutput);
            
            if (!string.IsNullOrWhiteSpace(jsonStr) && jsonStr != "{}")
            {
                using var doc = JsonDocument.Parse(jsonStr);
                var root = doc.RootElement;

                var accept = false;
                var reason = "No reason provided";

                if (root.TryGetProperty("accept", out var acceptProp))
                {
                    accept = acceptProp.GetBoolean();
                }

                if (root.TryGetProperty("reason", out var reasonProp))
                {
                    reason = reasonProp.GetString() ?? "No reason provided";
                }

                return (accept, reason);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Verification] Failed to parse worker {workerId} response: {ex.Message}");
            Console.WriteLine($"[Verification] Raw output preview: {Bound(rawOutput, 500)}");
        }

        // Fallback: look for keywords (but log a warning)
        Console.WriteLine($"[Verification] WARNING: Worker {workerId} did not return valid JSON. Using fallback parsing.");
        Console.WriteLine($"[Verification] Raw output preview: {Bound(rawOutput, 500)}");
        
        var lowerOutput = rawOutput.ToLowerInvariant();
        var fallbackAccept = lowerOutput.Contains("\"accept\": true") ||
                             lowerOutput.Contains("\"accept\":true") ||
                             lowerOutput.Contains("accept=true") ||
                             (lowerOutput.Contains("verified") && !lowerOutput.Contains("not verified"));

        return (fallbackAccept, $"Parsed from raw output (worker: {workerId}) - WARNING: Output was not valid JSON");
    }

    // ============================================================
    //  Emit verification phase summary
    // ============================================================

    private static void EmitVerificationPhaseSummary(ResearchSession session, VerificationPhaseResult phase)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"\n### Verification Phase: {phase.PhaseName.ToUpperInvariant()}");
        sb.AppendLine($"- Accept: {phase.AcceptCount} / {phase.Results.Count}");
        sb.AppendLine($"- Reject: {phase.RejectCount} / {phase.Results.Count}");
        sb.AppendLine($"- Phase Pass: {(phase.PhasePass ? "✅ YES" : "❌ NO")}");
        sb.AppendLine();

        foreach (var result in phase.Results)
        {
            var icon = result.Accept ? "✅" : "❌";
            sb.AppendLine($"  {icon} **{result.WorkerId}**: {result.Reason}");
        }

        // Emit as a custom event for frontend
        session.Events.Publish(new CustomEvent
        {
            Timestamp = NowMs(),
            Name = "aevatar.vibe.verification_phase_summary",
            Value = new
            {
                phaseName = phase.PhaseName,
                acceptCount = phase.AcceptCount,
                rejectCount = phase.RejectCount,
                totalCount = phase.Results.Count,
                phasePass = phase.PhasePass,
                results = phase.Results.Select(r => new
                {
                    workerId = r.WorkerId,
                    accept = r.Accept,
                    reason = r.Reason
                }).ToList()
            }
        });
    }

    // ============================================================
    //  Build verification summary
    // ============================================================

    private static string BuildVerificationSummary(
        PreProcessingResult? preProcessing,
        VerificationPhaseResult? scoutPhase,
        VerificationPhaseResult? proverPhase,
        bool overallPass,
        IReadOnlyList<ExtractedHypothesis> verifiedHypotheses)
    {
        var sb = new StringBuilder();

        sb.AppendLine("## Multi-Stage Verification Summary");
        sb.AppendLine();

        // Pre-processing phase
        if (preProcessing != null && preProcessing.Success)
        {
            sb.AppendLine("### Pre-Processing Phase");
            if (preProcessing.ExtractionResult != null)
            {
                sb.AppendLine($"- Extracted {preProcessing.ExtractionResult.Hypotheses.Count} hypotheses");
            }
            if (preProcessing.SelectionResult != null)
            {
                sb.AppendLine($"- Selected hypothesis: {Bound(preProcessing.SelectionResult.SelectedHypothesis.Statement, 150)}");
            }
            if (preProcessing.DecompositionResult != null)
            {
                sb.AppendLine($"- Found {preProcessing.DecompositionResult.Dependencies.Count} dependencies");
            }
            sb.AppendLine();
        }
        
        // Verified hypotheses summary
        if (verifiedHypotheses.Count > 0)
        {
            sb.AppendLine("### Verified Hypotheses");
            sb.AppendLine($"- **Total Verified**: {verifiedHypotheses.Count} hypotheses passed verification");
            sb.AppendLine();
            foreach (var h in verifiedHypotheses)
            {
                sb.AppendLine($"  ✅ **{h.Id}**: {Bound(h.Statement, 150)}");
            }
            sb.AppendLine();
        }

        // Scout phase
        if (scoutPhase != null)
        {
            sb.AppendLine("### Phase 1: Scout (Quick Refutation Detection)");
            sb.AppendLine($"- Workers: {scoutPhase.Results.Count}");
            sb.AppendLine($"- Accept: {scoutPhase.AcceptCount}, Reject: {scoutPhase.RejectCount}");
            sb.AppendLine($"- Requirement: All workers must accept");
            sb.AppendLine($"- Result: {(scoutPhase.PhasePass ? "✅ PASS" : "❌ FAIL")}");
            sb.AppendLine();

            foreach (var r in scoutPhase.Results)
            {
                var icon = r.Accept ? "✅" : "❌";
                sb.AppendLine($"  {icon} {r.WorkerId}: {r.Reason}");
            }
            sb.AppendLine();
        }
        else
        {
            sb.AppendLine("### Phase 1: Scout (Skipped - Hypotheses verified in loop)");
            sb.AppendLine();
        }

        // Prover phase (if executed)
        if (proverPhase != null)
        {
            sb.AppendLine("### Phase 2: Prover (Proof Verification)");
            sb.AppendLine($"- Workers: {proverPhase.Results.Count}");
            sb.AppendLine($"- Accept: {proverPhase.AcceptCount}, Reject: {proverPhase.RejectCount}");
            sb.AppendLine($"- Requirement: At least {ProverWorkers.MinAcceptCount} workers must accept");
            sb.AppendLine($"- Result: {(proverPhase.PhasePass ? "✅ PASS" : "❌ FAIL")}");
            sb.AppendLine();

            foreach (var r in proverPhase.Results)
            {
                var icon = r.Accept ? "✅" : "❌";
                sb.AppendLine($"  {icon} {r.WorkerId}: {r.Reason}");
            }
            sb.AppendLine();
        }
        else
        {
            sb.AppendLine("### Phase 2: Prover (Skipped - Scout phase failed)");
            sb.AppendLine();
        }

        // Overall result
        sb.AppendLine("### Overall Result");
        sb.AppendLine($"**{(overallPass ? "✅ VERIFICATION PASSED" : "❌ VERIFICATION FAILED")}**");

        return sb.ToString();
    }

    // ============================================================
    //  Pre-Processing Methods
    // ============================================================

    /// <summary>
    /// Step 1: Extract all hypotheses from reasoner output.
    /// </summary>
    private async Task<HypothesisExtractionResult?> ExtractHypothesesAsync(
        VibeRoundContext ctx,
        string? reasonerOutput,
        string? providerName,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(reasonerOutput))
            return null;

        try
        {
            var (ver, verId) = await _core.Runtime.GetVerifierAgentAsync(ctx.Session.Id, providerName, ct);

            var systemPrompt = """
                You are a hypothesis extractor. Your task is to identify all hypotheses, claims, or assertions 
                that need verification from the reasoner output.

                CRITICAL OUTPUT REQUIREMENTS:
                - Return ONLY valid JSON (no markdown, no code blocks, no commentary, no ```json tags, no ``` markers).
                - Output the JSON object EXACTLY ONCE. Do NOT repeat any fields or fragments.
                - Do NOT append anything after the closing brace }.
                - Do NOT include any text before or after the JSON object.
                - The JSON MUST start with { and end with }.
                - Do NOT output Markdown lists, numbered lists, bullet points, or any text formatting.
                - Do NOT output explanations or commentary outside the JSON object.
                - Validate your JSON structure before outputting.
                - If your output contains any non-JSON text (including Markdown), it will be REJECTED.

                MANDATORY Output JSON schema (you MUST follow this exact structure):
                {
                  "hypotheses": [
                    {
                      "id": "string (unique identifier, e.g., H1, H2, H3)",
                      "statement": "string (the hypothesis statement)",
                      "context": "string (optional: surrounding context)",
                      "confidence": number (optional: 0.0-1.0)
                    }
                  ]
                }

                IMPORTANT:
                - The root object MUST have a field named "hypotheses" (plural, lowercase).
                - The "hypotheses" field MUST be an array.
                - Each array element MUST be an object with "id" and "statement" fields.
                - Do NOT use alternative field names like "hypothesis", "items", "results", etc.
                - Do NOT output a simple array without the "hypotheses" wrapper.
                - Do NOT output Markdown lists or numbered lists.

                Rules:
                - Extract only verifiable claims (not definitions or facts).
                - Each hypothesis should be a testable statement.
                - Include context if it helps understand the hypothesis.
                - Assign unique IDs to each hypothesis (H1, H2, H3, ...).
                """;

            var userMessage = $$"""
                Extract all hypotheses from the following reasoner output:

                === REASONER OUTPUT ===
                {{Bound(reasonerOutput, 10000)}}
                === END REASONER OUTPUT ===

                CRITICAL: You MUST output ONLY valid JSON following the exact schema specified in the system prompt:
                {
                  "hypotheses": [
                    {
                      "id": "string (unique identifier, e.g., H1, H2, H3)",
                      "statement": "string (the hypothesis statement)",
                      "context": "string (optional: surrounding context)",
                      "confidence": number (optional: 0.0-1.0)
                    }
                  ]
                }

                Requirements:
                - Output ONLY the JSON object (no markdown, no code blocks, no commentary).
                - The root object MUST have a field named "hypotheses" (plural, lowercase).
                - Each hypothesis MUST have "id" and "statement" fields.
                - Assign unique IDs (H1, H2, H3, ...) to each hypothesis.
                - Do NOT output any text before or after the JSON object.
                """;

            var req = new ChatRequest
            {
                Message = userMessage,
                RequestId = Guid.NewGuid().ToString("N"),
                StageHint = "session:vibe:verifier:extract_hypotheses"
            };
            req.Context["agent_id"] = verId;
            req.Context["system_prompt_override"] = systemPrompt;

            var resp = await ver.ChatAsync(req, ct);
            var rawOutput = resp.Content ?? string.Empty;

            // Parse JSON response using robust JSON extraction
            var hypotheses = new List<ExtractedHypothesis>();
            var jsonParseSuccess = false;
            var jsonParseError = "";
            
            try
            {
                // Use LLMResponseParser to extract JSON robustly (handles markdown code blocks, nested brackets, etc.)
                var jsonStr = LLMResponseParser.ExtractJson(rawOutput);
                
                Console.WriteLine($"[Verification] Extracted JSON string length: {jsonStr?.Length ?? 0}");
                if (!string.IsNullOrWhiteSpace(jsonStr) && jsonStr.Length > 100)
                {
                    Console.WriteLine($"[Verification] Extracted JSON preview (first 500 chars): {Bound(jsonStr, 500)}");
                }
                
                if (!string.IsNullOrWhiteSpace(jsonStr) && jsonStr != "{}")
                {
                    using var doc = JsonDocument.Parse(jsonStr);
                    var root = doc.RootElement;
                    
                    Console.WriteLine($"[Verification] JSON root element type: {root.ValueKind}");

                    // First, try the required format: "hypotheses" field
                    if (root.TryGetProperty("hypotheses", out var hypothesesProp) && hypothesesProp.ValueKind == JsonValueKind.Array)
                    {
                        jsonParseSuccess = true;
                        var extractedCount = 0;
                        var arrayLength = hypothesesProp.GetArrayLength();
                        Console.WriteLine($"[Verification] Found 'hypotheses' array with {arrayLength} elements");
                        
                        foreach (var h in hypothesesProp.EnumerateArray())
                        {
                            // Log raw JSON element for debugging
                            var rawElement = h.GetRawText();
                            Console.WriteLine($"[Verification] Processing JSON element: {Bound(rawElement, 200)}");
                            
                            // Handle both object and string formats
                            string id;
                            string statement;
                            string? context = null;
                            double? confidence = null;

                            if (h.ValueKind == JsonValueKind.Object)
                            {
                                // Standard object format
                                id = h.TryGetProperty("id", out var idProp) ? idProp.GetString() ?? $"H{hypotheses.Count + 1}" : $"H{hypotheses.Count + 1}";
                                
                                // Try to get statement property
                                if (h.TryGetProperty("statement", out var stmtProp))
                                {
                                    // Check the value kind first
                                    Console.WriteLine($"[Verification]   Found 'statement' property, ValueKind: {stmtProp.ValueKind}");
                                    
                                    if (stmtProp.ValueKind == JsonValueKind.String)
                                    {
                                        statement = stmtProp.GetString() ?? "";
                                        Console.WriteLine($"[Verification]   Statement value length: {statement.Length}");
                                        if (statement.Length > 0)
                                        {
                                            Console.WriteLine($"[Verification]   Statement preview: {Bound(statement, 100)}");
                                        }
                                        else
                                        {
                                            Console.WriteLine($"[Verification]   WARNING: Statement is empty string");
                                            // Try to get raw text as fallback
                                            var rawStatement = stmtProp.GetRawText();
                                            Console.WriteLine($"[Verification]   Raw statement text: {Bound(rawStatement, 200)}");
                                            if (!string.IsNullOrWhiteSpace(rawStatement) && rawStatement.Length > 2)
                                            {
                                                // Remove quotes if present
                                                var unquoted = rawStatement.Trim('"');
                                                if (unquoted != rawStatement)
                                                {
                                                    statement = unquoted;
                                                    Console.WriteLine($"[Verification]   Extracted statement from raw text: {Bound(statement, 100)}");
                                                }
                                            }
                                        }
                                    }
                                    else
                                    {
                                        Console.WriteLine($"[Verification]   WARNING: 'statement' property is not a string, ValueKind: {stmtProp.ValueKind}");
                                        statement = "";
                                    }
                                }
                                else
                                {
                                    statement = "";
                                    Console.WriteLine($"[Verification]   WARNING: 'statement' property not found in JSON element");
                                }
                                
                                // Try alternative field names for statement (fallback)
                                if (string.IsNullOrWhiteSpace(statement))
                                {
                                    Console.WriteLine($"[Verification]   Statement is empty, trying alternative field names...");
                                    if (h.TryGetProperty("text", out var textProp))
                                    {
                                        statement = textProp.GetString() ?? "";
                                        Console.WriteLine($"[Verification]   Found 'text' property, value length: {statement.Length}");
                                    }
                                    else if (h.TryGetProperty("content", out var contentProp))
                                    {
                                        statement = contentProp.GetString() ?? "";
                                        Console.WriteLine($"[Verification]   Found 'content' property, value length: {statement.Length}");
                                    }
                                    else if (h.TryGetProperty("claim", out var claimProp))
                                    {
                                        statement = claimProp.GetString() ?? "";
                                        Console.WriteLine($"[Verification]   Found 'claim' property, value length: {statement.Length}");
                                    }
                                }
                                
                                context = h.TryGetProperty("context", out var ctxProp) ? ctxProp.GetString() : null;
                                if (h.TryGetProperty("confidence", out var confProp) && confProp.ValueKind == JsonValueKind.Number)
                                    confidence = confProp.GetDouble();
                            }
                            else if (h.ValueKind == JsonValueKind.String)
                            {
                                // Simple string format - use as statement
                                statement = h.GetString() ?? "";
                                id = $"H{hypotheses.Count + 1}";
                                Console.WriteLine($"[Verification]   Element is string, value length: {statement.Length}");
                            }
                            else
                            {
                                Console.WriteLine($"[Verification]   WARNING: Element is not Object or String, ValueKind: {h.ValueKind}");
                                continue; // Skip invalid entries
                            }

                            if (!string.IsNullOrWhiteSpace(statement))
                            {
                                hypotheses.Add(new ExtractedHypothesis(id, statement, context, confidence));
                                extractedCount++;
                                Console.WriteLine($"[Verification] ✅ Added hypothesis {id}: {Bound(statement, 100)}");
                            }
                            else
                            {
                                Console.WriteLine($"[Verification] ❌ WARNING: Skipped hypothesis entry - statement is empty or whitespace");
                                Console.WriteLine($"[Verification]   ID: '{id}'");
                                Console.WriteLine($"[Verification]   Statement (raw): '{statement}'");
                                Console.WriteLine($"[Verification]   Statement length: {statement?.Length ?? 0}");
                                Console.WriteLine($"[Verification]   Raw JSON element: {rawElement}");
                            }
                        }
                        
                        Console.WriteLine($"[Verification] Successfully parsed JSON: extracted {extractedCount} hypotheses from 'hypotheses' array (total processed: {arrayLength})");
                    }
                    // Fallback: Try alternative field names (but log a warning)
                    else
                    {
                        JsonElement? hypothesesArray = null;
                        string? usedFieldName = null;
                        
                        // Try "hypothesis" (singular)
                        if (root.TryGetProperty("hypothesis", out var hypothesisProp) && hypothesisProp.ValueKind == JsonValueKind.Array)
                        {
                            hypothesesArray = hypothesisProp;
                            usedFieldName = "hypothesis";
                        }
                        // Try "items"
                        else if (root.TryGetProperty("items", out var itemsProp) && itemsProp.ValueKind == JsonValueKind.Array)
                        {
                            hypothesesArray = itemsProp;
                            usedFieldName = "items";
                        }
                        // Try "results"
                        else if (root.TryGetProperty("results", out var resultsProp) && resultsProp.ValueKind == JsonValueKind.Array)
                        {
                            hypothesesArray = resultsProp;
                            usedFieldName = "results";
                        }
                        // Try "data"
                        else if (root.TryGetProperty("data", out var dataProp) && dataProp.ValueKind == JsonValueKind.Array)
                        {
                            hypothesesArray = dataProp;
                            usedFieldName = "data";
                        }
                        // If root itself is an array
                        else if (root.ValueKind == JsonValueKind.Array)
                        {
                            hypothesesArray = root;
                            usedFieldName = "root array";
                        }

                        if (hypothesesArray.HasValue)
                        {
                            jsonParseSuccess = true;
                            Console.WriteLine($"[Verification] WARNING: JSON does not use 'hypotheses' field. Using '{usedFieldName}' instead.");
                            
                            foreach (var h in hypothesesArray.Value.EnumerateArray())
                            {
                                string id;
                                string statement;
                                string? context = null;
                                double? confidence = null;

                                if (h.ValueKind == JsonValueKind.Object)
                                {
                                    id = h.TryGetProperty("id", out var idProp) ? idProp.GetString() ?? $"H{hypotheses.Count + 1}" : $"H{hypotheses.Count + 1}";
                                    statement = h.TryGetProperty("statement", out var stmtProp) ? stmtProp.GetString() ?? "" : "";
                                    
                                    if (string.IsNullOrWhiteSpace(statement))
                                    {
                                        if (h.TryGetProperty("text", out var textProp))
                                            statement = textProp.GetString() ?? "";
                                        else if (h.TryGetProperty("content", out var contentProp))
                                            statement = contentProp.GetString() ?? "";
                                        else if (h.TryGetProperty("claim", out var claimProp))
                                            statement = claimProp.GetString() ?? "";
                                    }
                                    
                                    context = h.TryGetProperty("context", out var ctxProp) ? ctxProp.GetString() : null;
                                    if (h.TryGetProperty("confidence", out var confProp) && confProp.ValueKind == JsonValueKind.Number)
                                        confidence = confProp.GetDouble();
                                }
                                else if (h.ValueKind == JsonValueKind.String)
                                {
                                    statement = h.GetString() ?? "";
                                    id = $"H{hypotheses.Count + 1}";
                                }
                                else
                                {
                                    continue;
                                }

                                if (!string.IsNullOrWhiteSpace(statement))
                                {
                                    hypotheses.Add(new ExtractedHypothesis(id, statement, context, confidence));
                                }
                            }
                            
                            Console.WriteLine($"[Verification] Extracted {hypotheses.Count} hypotheses from '{usedFieldName}' field");
                        }
                    }
                }
                else
                {
                    jsonParseError = "Extracted JSON is empty or invalid";
                }
            }
            catch (Exception ex)
            {
                jsonParseError = ex.Message;
                Console.WriteLine($"[Verification] Failed to parse hypothesis extraction result: {ex.Message}");
                Console.WriteLine($"[Verification] Raw output preview: {Bound(rawOutput, 500)}");
            }
            
            // Log parsing result
            if (jsonParseSuccess)
            {
                Console.WriteLine($"[Verification] JSON parsing successful: extracted {hypotheses.Count} hypotheses");
            }
            else if (!string.IsNullOrWhiteSpace(jsonParseError))
            {
                Console.WriteLine($"[Verification] JSON parsing failed: {jsonParseError}");
            }

            // Reject non-JSON output (Markdown, text, etc.)
            if (!jsonParseSuccess && !string.IsNullOrWhiteSpace(rawOutput))
            {
                // Check if output contains Markdown markers
                var hasMarkdownMarkers = rawOutput.Contains("```") || rawOutput.Contains("##") || rawOutput.Contains("###") || 
                                         rawOutput.Contains("- ") || rawOutput.Contains("* ") || rawOutput.Contains("1. ");
                
                if (hasMarkdownMarkers)
                {
                    Console.WriteLine($"[Verification] ERROR: LLM returned Markdown instead of JSON. Output REJECTED.");
                    Console.WriteLine($"[Verification] Raw output preview: {Bound(rawOutput, 500)}");
                    // Do NOT use text fallback - reject the output
                    return new HypothesisExtractionResult(
                        new List<ExtractedHypothesis>(), 
                        rawOutput, 
                        systemPrompt, 
                        userMessage + "\n\nERROR: Output was rejected because it contained Markdown instead of JSON.");
                }
            }
            
            // Fallback: Try to extract hypotheses from text if JSON parsing failed or returned no results
            // BUT only if output doesn't contain Markdown (already rejected above)
            if (hypotheses.Count == 0 && !string.IsNullOrWhiteSpace(rawOutput) && jsonParseSuccess)
            {
                Console.WriteLine($"[Verification] WARNING: JSON parsing succeeded but no hypotheses found. Attempting text extraction fallback...");
                Console.WriteLine($"[Verification] Raw output length: {rawOutput.Length} characters");
                
                // Try to find numbered list patterns like "1. ...", "H1: ...", etc.
                // Use simple pattern with English colon only to avoid regex compilation issues
                var numberedPattern = new System.Text.RegularExpressions.Regex(
                    @"(?:^\s*(?:\d+\.|H\d+[:])\s*(.+?)(?=\n\s*(?:\d+\.|H\d+[:])|$))",
                    System.Text.RegularExpressions.RegexOptions.Multiline | System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                
                var matches = numberedPattern.Matches(rawOutput);
                foreach (System.Text.RegularExpressions.Match match in matches)
                {
                    var statement = match.Groups[1].Value.Trim();
                    if (!string.IsNullOrWhiteSpace(statement) && statement.Length > 10) // Filter out very short matches
                    {
                        var id = $"H{hypotheses.Count + 1}";
                        hypotheses.Add(new ExtractedHypothesis(id, statement, null, null));
                    }
                }
                
                // If still no results, try to find quoted statements or statements after colons
                if (hypotheses.Count == 0)
                {
                    // Try to find quoted statements or statements after colons
                    // Use simple pattern - only match English colon to avoid regex compilation issues
                    var quotedPattern = new System.Text.RegularExpressions.Regex(
                        @"[""“](.+?)[""]",
                        System.Text.RegularExpressions.RegexOptions.Multiline);
                    var colonPattern = new System.Text.RegularExpressions.Regex(
                        @":\s*(.+?)(?=\n|$)",
                        System.Text.RegularExpressions.RegexOptions.Multiline);
                    
                    // Try quoted statements first
                    var quotedMatches = quotedPattern.Matches(rawOutput);
                    foreach (System.Text.RegularExpressions.Match match in quotedMatches)
                    {
                        var statement = match.Groups[1].Value.Trim();
                        if (!string.IsNullOrWhiteSpace(statement) && statement.Length > 10)
                        {
                            var id = $"H{hypotheses.Count + 1}";
                            hypotheses.Add(new ExtractedHypothesis(id, statement, null, null));
                        }
                    }
                    
                    // If no quoted statements found, try colon pattern
                    if (hypotheses.Count == 0)
                    {
                        var colonMatches = colonPattern.Matches(rawOutput);
                        foreach (System.Text.RegularExpressions.Match match in colonMatches)
                        {
                            var statement = match.Groups[1].Value.Trim();
                            if (!string.IsNullOrWhiteSpace(statement) && statement.Length > 10)
                            {
                                var id = $"H{hypotheses.Count + 1}";
                                hypotheses.Add(new ExtractedHypothesis(id, statement, null, null));
                            }
                        }
                    }
                }
                
                if (hypotheses.Count > 0)
                {
                    Console.WriteLine($"[Verification] Extracted {hypotheses.Count} hypotheses from text fallback");
                }
            }
            
            // Validation: Try to count hypotheses in raw output to detect parsing issues
            if (hypotheses.Count > 0 && !string.IsNullOrWhiteSpace(rawOutput))
            {
                // Count potential hypothesis IDs in raw output (e.g., "H1", "H2", "id": "H3")
                var idPattern = new System.Text.RegularExpressions.Regex(
                    @"""id""\s*:\s*""([^""]+)""|""id""\s*:\s*'([^']+)'|H\d+",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                var idMatches = idPattern.Matches(rawOutput);
                var uniqueIds = new System.Collections.Generic.HashSet<string>();
                foreach (System.Text.RegularExpressions.Match match in idMatches)
                {
                    var id = match.Groups[1].Success ? match.Groups[1].Value : 
                             match.Groups[2].Success ? match.Groups[2].Value : 
                             match.Value;
                    if (!string.IsNullOrWhiteSpace(id))
                    {
                        uniqueIds.Add(id.ToUpperInvariant());
                    }
                }
                
                // Count "statement" fields in raw output
                var statementPattern = new System.Text.RegularExpressions.Regex(
                    @"""statement""\s*:\s*""([^""]+)""|""statement""\s*:\s*'([^']+)'",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                var statementMatches = statementPattern.Matches(rawOutput);
                
                // Count array elements (rough estimate)
                var arrayElementPattern = new System.Text.RegularExpressions.Regex(
                    @"\{[^{}]*""(?:id|statement)""[^{}]*\}|""[^""]+""",
                    System.Text.RegularExpressions.RegexOptions.Multiline);
                var arrayMatches = arrayElementPattern.Matches(rawOutput);
                
                var estimatedCount = Math.Max(Math.Max(uniqueIds.Count, statementMatches.Count), arrayMatches.Count / 2);
                
                if (estimatedCount > hypotheses.Count)
                {
                    Console.WriteLine($"[Verification] WARNING: Raw output appears to contain {estimatedCount} hypotheses, but only {hypotheses.Count} were extracted. Possible parsing issue!");
                    Console.WriteLine($"[Verification] Found {uniqueIds.Count} unique IDs, {statementMatches.Count} statement fields in raw output");
                }
                else if (hypotheses.Count > estimatedCount * 2)
                {
                    Console.WriteLine($"[Verification] WARNING: Extracted {hypotheses.Count} hypotheses, but raw output only appears to contain ~{estimatedCount}. Possible over-extraction!");
                }
                else
                {
                    Console.WriteLine($"[Verification] Validation: Extracted {hypotheses.Count} hypotheses, raw output contains ~{estimatedCount} (consistent)");
                }
            }

            // Log final extraction result for debugging
            if (hypotheses.Count == 0 && !string.IsNullOrWhiteSpace(rawOutput))
            {
                Console.WriteLine($"[Verification] WARNING: No hypotheses extracted despite non-empty raw output!");
                Console.WriteLine($"[Verification] Raw output length: {rawOutput.Length} characters");
                Console.WriteLine($"[Verification] Raw output preview: {Bound(rawOutput, 1000)}");
                Console.WriteLine($"[Verification] JSON parse success: {jsonParseSuccess}");
                Console.WriteLine($"[Verification] JSON parse error: {jsonParseError}");
            }
            
            return new HypothesisExtractionResult(hypotheses, rawOutput, systemPrompt, userMessage);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Verification] Hypothesis extraction failed: {ex.Message}");
            Console.WriteLine($"[Verification] Exception stack trace: {ex.StackTrace}");
            return null;
        }
    }

    /// <summary>
    /// Step 2: Select the easiest hypothesis to verify.
    /// </summary>
    private async Task<HypothesisSelectionResult?> SelectEasiestHypothesisAsync(
        VibeRoundContext ctx,
        IReadOnlyList<ExtractedHypothesis> hypotheses,
        string? providerName,
        CancellationToken ct)
    {
        if (hypotheses.Count == 0)
            return null;

        if (hypotheses.Count == 1)
        {
            // Even with a single hypothesis, save prompts for consistency
            var systemPrompt = """
                You are a hypothesis selector. Your task is to choose the hypothesis that is easiest that is NOT already proven to verify 
                (i.e., most likely to be proven correct with available evidence).

                NOTE: Only one hypothesis was found, so selection step was skipped.
                """;
            
            var userMessage = $"""
                Only one hypothesis was extracted, so it was automatically selected:

                Hypothesis:
                ID: {hypotheses[0].Id}
                Statement: {hypotheses[0].Statement}
                Context: {hypotheses[0].Context ?? "N/A"}
                Confidence: {hypotheses[0].Confidence?.ToString("F2") ?? "N/A"}
                """;
            
            return new HypothesisSelectionResult(
                SelectedHypothesis: hypotheses[0],
                SelectionReason: "Only one hypothesis found.",
                RawOutput: "Single hypothesis - selection step skipped",
                SystemPrompt: systemPrompt,
                UserPrompt: userMessage
            );
        }

        try
        {
            var (ver, verId) = await _core.Runtime.GetVerifierAgentAsync(ctx.Session.Id, providerName, ct);

            var systemPrompt = """
                You are a hypothesis selector. Your task is to choose the hypothesis that is easiest to verify 
                (i.e., most likely to be proven correct with available evidence).

                CRITICAL OUTPUT REQUIREMENTS:
                - Return ONLY valid JSON (no markdown, no code blocks, no commentary, no ```json tags, no ``` markers).
                - Output the JSON object EXACTLY ONCE. Do NOT repeat any fields or fragments.
                - Do NOT append anything after the closing brace }.
                - Do NOT include any text before or after the JSON object.
                - The JSON MUST start with { and end with }.
                - Do NOT output Markdown lists, numbered lists, bullet points, or any text formatting.
                - Do NOT output explanations or commentary outside the JSON object.
                - If your output contains any non-JSON text (including Markdown), it will be REJECTED.

                MANDATORY Output JSON schema (you MUST follow this exact structure):
                {
                  "selected_id": "string (id of the selected hypothesis, e.g., H1, H2, H3)",
                  "reason": "string (why this hypothesis is easiest to verify)"
                }

                Selection criteria:
                - Prefer hypotheses with clear, testable statements.
                - Prefer hypotheses that can be verified with available DAG facts.
                - Prefer hypotheses with higher confidence scores.
                - Avoid hypotheses that require external knowledge not in the context.
                """;

            var hypothesesList = string.Join("\n", hypotheses.Select((h, i) => 
                $"{i + 1}. ID: {h.Id}\n   Statement: {h.Statement}\n   Context: {h.Context ?? "N/A"}\n   Confidence: {h.Confidence?.ToString("F2") ?? "N/A"}"));

            var userMessage = $$"""
                Select the easiest hypothesis to verify from the following list:

                {{hypothesesList}}

                CRITICAL: You MUST output ONLY valid JSON following the exact schema specified in the system prompt:
                {
                  "selected_id": "string (id of the selected hypothesis, e.g., H1, H2, H3)",
                  "reason": "string (why this hypothesis is easiest to verify)"
                }

                Requirements:
                - Output ONLY the JSON object (no markdown, no code blocks, no commentary).
                - Use the exact field names: "selected_id" and "reason".
                - The "selected_id" MUST match one of the hypothesis IDs from the list above.
                - Do NOT output any text before or after the JSON object.
                """;

            var req = new ChatRequest
            {
                Message = userMessage,
                RequestId = Guid.NewGuid().ToString("N"),
                StageHint = "session:vibe:verifier:select_hypothesis"
            };
            req.Context["agent_id"] = verId;
            req.Context["system_prompt_override"] = systemPrompt;

            var resp = await ver.ChatAsync(req, ct);
            var rawOutput = resp.Content ?? string.Empty;

            // FIRST: Check for Markdown markers BEFORE attempting JSON parsing
            // This ensures we reject Markdown output immediately
            var hasMarkdownMarkers = rawOutput.Contains("```") || rawOutput.Contains("##") || rawOutput.Contains("###") || 
                                     rawOutput.Contains("- ") || rawOutput.Contains("* ") || rawOutput.Contains("1. ") ||
                                     rawOutput.TrimStart().StartsWith("#") || rawOutput.Contains("**") || rawOutput.Contains("__");
            
            // If Markdown markers are detected, reject immediately (unless it's a code block containing JSON)
            // Check if it's a code block with JSON inside (which is acceptable)
            var isJsonCodeBlock = rawOutput.Contains("```json") || 
                                  (rawOutput.Contains("```") && (rawOutput.Contains("\"selected_id\"") || rawOutput.Contains("\"selectedId\"")));
            
            if (hasMarkdownMarkers && !isJsonCodeBlock)
            {
                Console.WriteLine($"[Verification] ERROR: Step 2 LLM returned Markdown instead of JSON. Output REJECTED.");
                Console.WriteLine($"[Verification] Raw output preview: {Bound(rawOutput, 500)}");
                // Return error result immediately
                return new HypothesisSelectionResult(
                    SelectedHypothesis: hypotheses[0],
                    SelectionReason: $"ERROR: Output was rejected (Markdown instead of JSON). Using first hypothesis as fallback.",
                    RawOutput: rawOutput,
                    SystemPrompt: systemPrompt,
                    UserPrompt: userMessage
                );
            }

            // Parse JSON response using robust JSON extraction
            ExtractedHypothesis? selected = null;
            string reason = "Failed to parse selection";
            try
            {
                // Use LLMResponseParser to extract JSON robustly (handles markdown code blocks, nested brackets, etc.)
                var jsonStr = LLMResponseParser.ExtractJson(rawOutput);
                
                if (!string.IsNullOrWhiteSpace(jsonStr) && jsonStr != "{}")
                {
                    using var doc = JsonDocument.Parse(jsonStr);
                    var root = doc.RootElement;

                    // Try multiple possible field names
                    string? selectedId = null;
                    if (root.TryGetProperty("selected_id", out var idProp1))
                        selectedId = idProp1.GetString();
                    else if (root.TryGetProperty("selectedId", out var idProp2))
                        selectedId = idProp2.GetString();
                    else if (root.TryGetProperty("selected_hypothesis_id", out var idProp3))
                        selectedId = idProp3.GetString();
                    else if (root.TryGetProperty("hypothesis_id", out var idProp4))
                        selectedId = idProp4.GetString();

                    if (root.TryGetProperty("reason", out var reasonProp))
                        reason = reasonProp.GetString() ?? "No reason provided";
                    else if (root.TryGetProperty("selection_reason", out var reasonProp2))
                        reason = reasonProp2.GetString() ?? "No reason provided";

                    if (!string.IsNullOrWhiteSpace(selectedId))
                    {
                        selected = hypotheses.FirstOrDefault(h => h.Id.Equals(selectedId, StringComparison.OrdinalIgnoreCase));
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Verification] Failed to parse hypothesis selection result: {ex.Message}");
                Console.WriteLine($"[Verification] Raw output preview: {Bound(rawOutput, 500)}");
            }

            // Fallback: Try to extract hypothesis ID from text if JSON parsing failed (but only if not Markdown)
            if (selected == null && !hasMarkdownMarkers)
            {
                // Try to find pattern like "H1", "H2", etc. in the raw output
                var hypothesisIdPattern = new System.Text.RegularExpressions.Regex(@"\b(H\d+)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                var matches = hypothesisIdPattern.Matches(rawOutput);
                
                // Look for the most mentioned hypothesis ID or the one mentioned in "Easiest to verify" context
                var easiestMatch = System.Text.RegularExpressions.Regex.Match(rawOutput, @"(?:Easiest|easiest|selected|choose|recommend).*?\b(H\d+)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (easiestMatch.Success)
                {
                    var extractedId = easiestMatch.Groups[1].Value;
                    selected = hypotheses.FirstOrDefault(h => h.Id.Equals(extractedId, StringComparison.OrdinalIgnoreCase));
                    if (selected != null)
                    {
                        reason = $"Extracted from text: {extractedId}";
                    }
                }
                
                // If still not found, try any mentioned hypothesis ID
                if (selected == null && matches.Count > 0)
                {
                    foreach (System.Text.RegularExpressions.Match match in matches)
                    {
                        var extractedId = match.Groups[1].Value;
                        selected = hypotheses.FirstOrDefault(h => h.Id.Equals(extractedId, StringComparison.OrdinalIgnoreCase));
                        if (selected != null)
                        {
                            reason = $"Extracted from text (fallback): {extractedId}";
                            break;
                        }
                    }
                }
                
                // Final fallback: select first hypothesis
                if (selected == null)
                {
                    selected = hypotheses[0];
                    reason = "Fallback: selected first hypothesis (JSON parsing failed and no ID found in text)";
                }
            }

            // Ensure selected is not null (should not happen, but safety check)
            if (selected == null)
            {
                selected = hypotheses[0];
                reason = "Fallback: selected first hypothesis (unexpected null)";
            }
            
            return new HypothesisSelectionResult(selected, reason, rawOutput, systemPrompt, userMessage);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Verification] Hypothesis selection failed: {ex.Message}");
            // Fallback: return first hypothesis with error prompts
            var errorSystemPrompt = """
                You are a hypothesis selector. Your task is to choose the hypothesis that is easiest to verify.

                NOTE: Selection step failed due to an error.
                """;
            
            var errorUserMessage = $"""
                Selection failed with error: {ex.Message}
                
                Available hypotheses:
                {string.Join("\n", hypotheses.Select((h, i) => $"{i + 1}. ID: {h.Id}\n   Statement: {h.Statement}"))}
                
                Using first hypothesis as fallback.
                """;
            
            return new HypothesisSelectionResult(
                SelectedHypothesis: hypotheses[0],
                SelectionReason: $"Selection failed: {ex.Message}. Using first hypothesis.",
                RawOutput: $"Selection failed: {ex.Message}",
                SystemPrompt: errorSystemPrompt,
                UserPrompt: errorUserMessage
            );
        }
    }

    /// <summary>
    /// Step 3: Decompose hypothesis and find dependencies from DAG.
    /// </summary>
    private async Task<HypothesisDecompositionResult?> DecomposeHypothesisAsync(
        VibeRoundContext ctx,
        SraDagSnapshot dag,
        ExtractedHypothesis hypothesis,
        string? providerName,
        CancellationToken ct)
    {
        try
        {
            var (ver, verId) = await _core.Runtime.GetVerifierAgentAsync(ctx.Session.Id, providerName, ct);

            // Build DAG facts context
            var dagFacts = dag.Nodes
                .Where(n => n.Kind == SraDagNodeKind.Knowledge)
                .Select(n => $"[{n.Id}] {n.Label}")
                .Take(50)
                .ToList();

            var systemPrompt = """
                You are a hypothesis decomposer. Your task is to break down a hypothesis into its logical dependencies 
                and identify what theorems/facts would support it.

                CRITICAL OUTPUT REQUIREMENTS:
                - Return ONLY valid JSON (no markdown, no code blocks, no commentary, no ```json tags, no ``` markers).
                - Output the JSON object EXACTLY ONCE. Do NOT repeat any fields or fragments.
                - Do NOT append anything after the closing brace }.
                - Do NOT include any text before or after the JSON object.
                - The JSON MUST start with { and end with }.
                - Do NOT output Markdown lists, numbered lists, bullet points, or any text formatting.
                - Do NOT output explanations or commentary outside the JSON object.
                - ALWAYS output valid JSON, even if DAG information is incomplete or missing.
                - If your output contains any non-JSON text (including Markdown), it will be REJECTED.

                MANDATORY Output JSON schema (you MUST follow this exact structure):
                {
                  "dependencies": [
                    {
                      "id": "string (DAG node ID if found, or inferred ID like 'DEP1', 'DEP2', etc.)",
                      "statement": "string (what this dependency states)",
                      "derivation_order": number (order in derivation: 1, 2, 3, ...)
                    }
                  ],
                  "derivation_path": ["string (ordered list of dependency IDs)"],
                  "reason": "string (explanation of the decomposition)"
                }

                Decomposition Strategy:
                1. **If DAG facts are provided and match the hypothesis:**
                   - Use actual DAG node IDs from the provided facts.
                   - Map hypothesis components to DAG nodes.
                   - Build derivation path using DAG node IDs.

                2. **If DAG facts are missing or don't match:**
                   - Analyze the hypothesis statement itself to identify logical components.
                   - Extract key concepts, principles, or claims mentioned in the hypothesis.
                   - Create inferred dependencies based on what the hypothesis logically depends on.
                   - Use inferred IDs like "DEP1", "DEP2", "DEP3", etc. for dependencies not found in DAG.
                   - Still build a logical derivation path showing how the hypothesis would be derived.

                3. **Always provide:**
                   - At least 1-3 dependencies (even if inferred from hypothesis content).
                   - A logical derivation order (prerequisites first).
                   - A clear explanation of the decomposition reasoning.

                IMPORTANT:
                - Do NOT return "NOT VERIFIED" or similar error messages.
                - Do NOT skip decomposition if DAG information is incomplete.
                - Base dependencies on the hypothesis statement itself if DAG facts don't match.
                - Focus on logical structure: what would need to be established before this hypothesis can hold?
                """;

            var dagFactsText = dagFacts.Count > 0 
                ? string.Join("\n", dagFacts) 
                : "(No DAG facts available - decompose based on hypothesis content)";

            var userMessage = $$"""
                Decompose the following hypothesis and identify its logical dependencies:

                Hypothesis:
                {{hypothesis.Statement}}

                Context:
                {{hypothesis.Context ?? "N/A"}}

                Available DAG Facts (if any):
                {{dagFactsText}}

                Instructions:
                - If DAG facts match the hypothesis, use those DAG node IDs.
                - If DAG facts are missing or don't match, analyze the hypothesis statement itself:
                  * Extract key concepts, principles, or claims from the hypothesis.
                  * Identify what logical components the hypothesis depends on.
                  * Create inferred dependencies (use IDs like "DEP1", "DEP2", etc.).
                - Always provide at least 1-3 dependencies with logical derivation order.
                - Explain your decomposition reasoning in the "reason" field.

                CRITICAL: You MUST output ONLY valid JSON following the exact schema specified in the system prompt:
                {
                  "dependencies": [
                    {
                      "id": "string (DAG node ID if found, or inferred ID like 'DEP1', 'DEP2', etc.)",
                      "statement": "string (what this dependency states)",
                      "derivation_order": number (order in derivation: 1, 2, 3, ...)
                    }
                  ],
                  "derivation_path": ["string (ordered list of dependency IDs)"],
                  "reason": "string (explanation of the decomposition)"
                }

                Requirements:
                - Output ONLY the JSON object (no markdown, no code blocks, no commentary).
                - Use the exact field names: "dependencies", "derivation_path", "reason".
                - Each dependency MUST have "id", "statement", and "derivation_order" fields.
                - Do NOT output any text before or after the JSON object.
                """;

            var req = new ChatRequest
            {
                Message = userMessage,
                RequestId = Guid.NewGuid().ToString("N"),
                StageHint = "session:vibe:verifier:decompose_hypothesis"
            };
            req.Context["agent_id"] = verId;
            req.Context["system_prompt_override"] = systemPrompt;

            var resp = await ver.ChatAsync(req, ct);
            var rawOutput = resp.Content ?? string.Empty;

            // Reject non-JSON output (Markdown, text, etc.)
            var hasMarkdownMarkers = rawOutput.Contains("```") || rawOutput.Contains("##") || rawOutput.Contains("###") || 
                                     rawOutput.Contains("- ") || rawOutput.Contains("* ") || rawOutput.Contains("1. ");
            
            if (hasMarkdownMarkers)
            {
                Console.WriteLine($"[Verification] ERROR: Step 3 LLM returned Markdown instead of JSON. Output REJECTED.");
                Console.WriteLine($"[Verification] Raw output preview: {Bound(rawOutput, 500)}");
                // Return error result
                return new HypothesisDecompositionResult(
                    Hypothesis: hypothesis,
                    Dependencies: new List<DependencyTheorem>(),
                    DerivationPath: new List<string>(),
                    DecompositionReason: "ERROR: Output was rejected because it contained Markdown instead of JSON.",
                    RawOutput: rawOutput,
                    SystemPrompt: systemPrompt,
                    UserPrompt: userMessage
                );
            }
            
            // Parse JSON response using robust JSON extraction
            var dependencies = new List<DependencyTheorem>();
            var derivationPath = new List<string>();
            string reason = "Failed to parse decomposition";
            try
            {
                // Use LLMResponseParser to extract JSON robustly (handles markdown code blocks, nested brackets, etc.)
                var jsonStr = LLMResponseParser.ExtractJson(rawOutput);
                
                if (!string.IsNullOrWhiteSpace(jsonStr) && jsonStr != "{}")
                {
                    using var doc = JsonDocument.Parse(jsonStr);
                    var root = doc.RootElement;

                    if (root.TryGetProperty("dependencies", out var depsProp) && depsProp.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var dep in depsProp.EnumerateArray())
                        {
                            var id = dep.TryGetProperty("id", out var idProp) ? idProp.GetString() ?? "" : "";
                            var statement = dep.TryGetProperty("statement", out var stmtProp) ? stmtProp.GetString() ?? "" : "";
                            var order = dep.TryGetProperty("derivation_order", out var orderProp) && orderProp.ValueKind == JsonValueKind.Number ? orderProp.GetInt32() : 0;

                            if (!string.IsNullOrWhiteSpace(id) && !string.IsNullOrWhiteSpace(statement))
                            {
                                dependencies.Add(new DependencyTheorem(id, statement, order));
                            }
                        }
                    }

                    if (root.TryGetProperty("derivation_path", out var pathProp) && pathProp.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in pathProp.EnumerateArray())
                        {
                            if (item.ValueKind == JsonValueKind.String)
                            {
                                derivationPath.Add(item.GetString() ?? "");
                            }
                        }
                    }

                    reason = root.TryGetProperty("reason", out var reasonProp) ? reasonProp.GetString() ?? "No reason provided" : "No reason provided";
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Verification] Failed to parse hypothesis decomposition result: {ex.Message}");
            }

            // Sort dependencies by derivation order
            dependencies = dependencies.OrderBy(d => d.DerivationOrder).ToList();

            return new HypothesisDecompositionResult(
                Hypothesis: hypothesis,
                Dependencies: dependencies,
                DerivationPath: derivationPath,
                DecompositionReason: reason,
                RawOutput: rawOutput,
                SystemPrompt: systemPrompt,
                UserPrompt: userMessage
            );
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Verification] Hypothesis decomposition failed: {ex.Message}");
            
            // Return a result with error information instead of null, so prompts are still saved
            var errorSystemPrompt = """
                You are a hypothesis decomposer. Your task is to break down a hypothesis into its logical dependencies.

                NOTE: Decomposition step failed due to an error.
                """;
            
            var errorUserMessage = $"""
                Decomposition failed with error: {ex.Message}
                
                Hypothesis:
                {hypothesis.Statement}
                
                Context:
                {hypothesis.Context ?? "N/A"}
                
                Unable to decompose due to error.
                """;
            
            return new HypothesisDecompositionResult(
                Hypothesis: hypothesis,
                Dependencies: new List<DependencyTheorem>(),
                DerivationPath: new List<string>(),
                DecompositionReason: $"Decomposition failed: {ex.Message}",
                RawOutput: $"Decomposition failed: {ex.Message}",
                SystemPrompt: errorSystemPrompt,
                UserPrompt: errorUserMessage
            );
        }
    }

    /// <summary>
    /// Verify a single hypothesis: Step 3 → Phase 1 → Phase 2
    /// Returns (verificationPassed, scoutResults, proverResults) where:
    /// - verificationPassed: true if Phase 2 passes (at least 3 prover workers accept)
    /// - scoutResults: Phase 1 results (for saving to file)
    /// - proverResults: Phase 2 results (for saving to file)
    /// </summary>
    private async Task<(bool VerificationPassed, IReadOnlyList<VerificationWorkerResult> ScoutResults, IReadOnlyList<VerificationWorkerResult> ProverResults)> VerifySingleHypothesisAsync(
        VibeRoundContext ctx,
        SraDagSnapshot dag,
        ExtractedHypothesis hypothesis,
        string? reasonerOutput,
        string? providerName,
        CancellationToken ct)
    {
        var session = ctx.Session;
        
        try
        {
            // Step 3: Decompose hypothesis and find dependencies
            var decompositionResult = await DecomposeHypothesisAsync(ctx, dag, hypothesis, providerName, ct);
            
            // Build verification context for this hypothesis
            var verificationContext = BuildVerificationContext(reasonerOutput, hypothesis, decompositionResult);
            
            // Phase 1: Scout (2 workers, both must accept)
            var scoutResults = await RunVerificationPhaseAsync(
                ctx, dag, verificationContext, providerName,
                ScoutWorkers.All,
                phaseName: "scout",
                ct);
            
            var scoutPhasePass = scoutResults.All(r => r.Accept);
            
            // Phase 2: Prover (5 workers, at least 3 must accept)
            var proverResults = await RunVerificationPhaseAsync(
                ctx, dag, verificationContext, providerName,
                ProverWorkers.All,
                phaseName: "prover",
                ct);
            
            var proverPhasePass = proverResults.Count(r => r.Accept) >= ProverWorkers.MinAcceptCount;
            
            // Return both verification result and phase results for saving
            return (proverPhasePass && scoutPhasePass, scoutResults, proverResults);
        }
        catch (Exception ex)
        {
            // If verification fails due to error, return false with empty results
            Console.WriteLine($"[Verification] Error verifying hypothesis {hypothesis.Id}: {ex.Message}");
            return (false, new List<VerificationWorkerResult>(), new List<VerificationWorkerResult>());
        }
    }

    /// <summary>
    /// Step 4: Build verification context from selected hypothesis and dependencies.
    /// </summary>
    private static string BuildVerificationContext(
        string? reasonerOutput,
        ExtractedHypothesis? selectedHypothesis,
        HypothesisDecompositionResult? decompositionResult)
    {
        var sb = new StringBuilder();

        if (selectedHypothesis != null)
        {
            sb.AppendLine("=== SELECTED HYPOTHESIS TO VERIFY ===");
            sb.AppendLine($"ID: {selectedHypothesis.Id}");
            sb.AppendLine($"Statement: {selectedHypothesis.Statement}");
            if (!string.IsNullOrWhiteSpace(selectedHypothesis.Context))
            {
                sb.AppendLine($"Context: {selectedHypothesis.Context}");
            }
            sb.AppendLine();

            if (decompositionResult != null && decompositionResult.Dependencies.Count > 0)
            {
                sb.AppendLine("=== DEPENDENCIES (IN DERIVATION ORDER) ===");
                foreach (var dep in decompositionResult.Dependencies.OrderBy(d => d.DerivationOrder))
                {
                    sb.AppendLine($"{dep.DerivationOrder}. [{dep.Id}] {dep.Statement}");
                }
                sb.AppendLine();

                if (decompositionResult.DerivationPath.Count > 0)
                {
                    sb.AppendLine("=== DERIVATION PATH ===");
                    sb.AppendLine(string.Join(" → ", decompositionResult.DerivationPath));
                    sb.AppendLine();
                }

                sb.AppendLine($"=== DECOMPOSITION REASON ===");
                sb.AppendLine(decompositionResult.DecompositionReason);
                sb.AppendLine();
            }
        }

        // ORIGINAL REASONER OUTPUT disabled - only use selected hypothesis and dependencies for verification
        // if (!string.IsNullOrWhiteSpace(reasonerOutput))
        // {
        //     sb.AppendLine("=== ORIGINAL REASONER OUTPUT (FOR REFERENCE) ===");
        //     sb.AppendLine(Bound(reasonerOutput, 5000));
        // }

        return sb.ToString();
    }
}
