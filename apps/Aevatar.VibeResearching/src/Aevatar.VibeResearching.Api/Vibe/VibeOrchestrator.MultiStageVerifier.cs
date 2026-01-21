using System.Text;
using System.Text.Json;
using Aevatar.Agents.AGUI;
using Aevatar.Agents.AI;
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
        //  Phase 1: Scout (2 workers, both must accept)
        // ============================================================
        EmitAgentStatusReport(session, "verifier", AgentStatusMessages.VerifierScoutStart);

        var scoutResults = await RunVerificationPhaseAsync(
            ctx, dag, reasonerOutput, providerName,
            ScoutWorkers.All,
            phaseName: "scout",
            ct);

        var scoutPhase = new VerificationPhaseResult(
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
                ScoutPhase: scoutPhase,
                ProverPhase: null,
                OverallPass: false,
                Summary: BuildVerificationSummary(scoutPhase, null, overallPass: false)
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
            ctx, dag, reasonerOutput, providerName,
            ProverWorkers.All,
            phaseName: "prover",
            ct);

        var proverPhase = new VerificationPhaseResult(
            PhaseName: "prover",
            Results: proverResults,
            AcceptCount: proverResults.Count(r => r.Accept),
            RejectCount: proverResults.Count(r => !r.Accept),
            PhasePass: proverResults.Count(r => r.Accept) >= ProverWorkers.MinAcceptCount
        );

        // Emit prover phase summary
        EmitVerificationPhaseSummary(session, proverPhase);

        session.Events.Publish(new StepFinishedEvent { Timestamp = NowMs(), StepName = "vibe.verifier.multi_stage" });

        var finalResult = new MultiStageVerificationResult(
            ScoutPhase: scoutPhase,
            ProverPhase: proverPhase,
            OverallPass: proverPhase.PhasePass,
            Summary: BuildVerificationSummary(scoutPhase, proverPhase, proverPhase.PhasePass)
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
                } : null
            }, new JsonSerializerOptions { WriteIndented = true });

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
        string? reasonerOutput,
        string? providerName,
        IReadOnlyList<VerificationWorker> workers,
        string phaseName,
        CancellationToken ct)
    {
        var session = ctx.Session;
        var results = new List<VerificationWorkerResult>();

        // Run workers in parallel for efficiency
        var tasks = workers.Select(worker => RunSingleVerificationWorkerAsync(
            ctx, dag, reasonerOutput, providerName, worker, phaseName, ct
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
        string? reasonerOutput,
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
                worker, ctx.Question, dag, reasonerOutput, ctx.Input.AttachmentPaths);

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
            var errorUserPrompt = BuildVerificationWorkerMessage(worker, ctx.Question, dag, reasonerOutput, ctx.Input.AttachmentPaths);

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
        string? reasonerOutput,
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

        if (!string.IsNullOrWhiteSpace(reasonerOutput))
        {
            sb.AppendLine();
            sb.AppendLine("=== REASONER OUTPUT TO VERIFY ===");
            sb.AppendLine(Bound(reasonerOutput!, 8000));
            sb.AppendLine("=== END REASONER OUTPUT ===");
        }

        sb.AppendLine();
        sb.AppendLine("Your task: Evaluate the reasoning above and output JSON with your verdict.");
        sb.AppendLine($"Remember: You are {worker.Role}. Focus on: {worker.Angle}");

        return sb.ToString();
    }

    // ============================================================
    //  Parse verification worker response
    // ============================================================

    private static (bool Accept, string Reason) ParseVerificationWorkerResponse(string rawOutput, string workerId)
    {
        try
        {
            // Try to extract JSON from the response
            var jsonStart = rawOutput.IndexOf('{');
            var jsonEnd = rawOutput.LastIndexOf('}');

            if (jsonStart >= 0 && jsonEnd > jsonStart)
            {
                var jsonStr = rawOutput.Substring(jsonStart, jsonEnd - jsonStart + 1);

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
        catch (Exception)
        {
            // Fall through to default parsing
        }

        // Fallback: look for keywords
        var lowerOutput = rawOutput.ToLowerInvariant();
        var fallbackAccept = lowerOutput.Contains("\"accept\": true") ||
                             lowerOutput.Contains("\"accept\":true") ||
                             lowerOutput.Contains("accept=true") ||
                             (lowerOutput.Contains("verified") && !lowerOutput.Contains("not verified"));

        return (fallbackAccept, $"Parsed from raw output (worker: {workerId})");
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
        VerificationPhaseResult scoutPhase,
        VerificationPhaseResult? proverPhase,
        bool overallPass)
    {
        var sb = new StringBuilder();

        sb.AppendLine("## Multi-Stage Verification Summary");
        sb.AppendLine();

        // Scout phase
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
}
