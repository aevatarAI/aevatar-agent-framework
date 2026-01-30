using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Aevatar.Agents.AGUI;
using Aevatar.Agents.AI;
using Google.Protobuf.Collections;
using Google.Protobuf.WellKnownTypes;
using VibeResearching.Api.Materials;
using VibeResearching.Api.Paper;
using VibeResearching.Api.Sessions;
using VibeResearching.Api.Vibe.Brief;
using VibeResearching.Api.Vibe.Delivery;
using VibeResearching.Api.Vibe.Dag;
using VibeResearching.Api.Vibe.Trace;
using VibeResearching.Api.Workspace;
using VibeResearching.Contracts.Collab;
using VibeResearching.Vibe;

namespace VibeResearching.Api.Vibe;

internal sealed partial class VibeOrchestrator
{
    // ============================================================
    //  AG-UI per-agent message projection (like AxiomReasoning)
    // ============================================================

    private static void StartAgentMessage(ResearchSession session, string messageId, string agent, string stepName, string? providerName)
    {
        // Ensure message exists for snapshot-first reconnect.
        session.SetMessage(messageId, role: "assistant", content: string.Empty);

        session.Events.Publish(new TextMessageStartEvent
        {
            Timestamp = NowMs(),
            MessageId = messageId,
            Role = "assistant"
        });

        // Attach metadata so frontend can label/group per agent without parsing messageId.
        session.Events.Publish(new CustomEvent
        {
            Timestamp = NowMs(),
            Name = "aevatar.vibe.message_meta",
            Value = new { messageId, agent, stepName, providerName = (providerName ?? string.Empty).Trim() }
        });
    }

    private static void EmitAgentDelta(ResearchSession session, string messageId, string role, string delta)
    {
        if (string.IsNullOrEmpty(delta))
            return;

        session.AppendToMessage(messageId, role: role, delta);

        session.Events.Publish(new TextMessageContentEvent
        {
            Timestamp = NowMs(),
            MessageId = messageId,
            Delta = delta
        });
    }

    private static void EndAgentMessage(ResearchSession session, string messageId)
    {
        session.Events.Publish(new TextMessageEndEvent
        {
            Timestamp = NowMs(),
            MessageId = messageId
        });
    }

    // ============================================================
    //  Agent Status Reporting
    // ============================================================

    /// <summary>
    /// Emit an agent status report event for real-time UI updates.
    /// Status text should be a brief, human-readable description of current work.
    /// </summary>
    private static void EmitAgentStatusReport(
        ResearchSession session,
        string agentName,
        string statusText,
        double? progress = null)
    {
        session.Events.Publish(new CustomEvent
        {
            Timestamp = NowMs(),
            Name = VibeEventNames.AgentStatusReport,
            Value = new
            {
                agentId = agentName,
                agentName,
                statusText,
                progress,
                sessionId = session.Id
            }
        });
    }

    // Agent-specific status messages
    private static class AgentStatusMessages
    {
        public const string PlannerStart = "正在分析研究问题，制定研究计划...";
        public const string PlannerStreaming = "正在输出研究计划...";
        public const string ReasonerStart = "正在进行深度推理分析...";
        public const string ReasonerStreaming = "正在构建推理链...";
        public const string LibrarianStart = "正在搜索相关文献和参考资料...";
        public const string LibrarianStreaming = "正在整理文献摘要...";
        public const string VerifierStart = "正在验证推理步骤的正确性...";
        public const string VerifierStreaming = "正在检查边界条件...";
        public const string VerifierScoutStart = "🔍 Scout 阶段：快速检测反例和缺失前提 (2 workers)...";
        public const string VerifierProverStart = "📐 Prover 阶段：验证推理过程正确性 (5 workers, 需 ≥3 通过)...";
        public const string DagBuilderStart = "正在构建知识图谱节点...";
        public const string DagBuilderStreaming = "正在生成 DAG 结构...";
        public const string PaperEditorStart = "正在更新论文草稿...";
        public const string PaperEditorStreaming = "正在编辑论文内容...";
    }

    // ============================================================
    //  Workers
    // ============================================================

    private async Task<(string Output, AgentPromptRecord? PromptRecord)> RunPlannerAsync(
        VibeRoundContext ctx,
        SraDagSnapshot dag,
        string? providerName,
        CancellationToken ct)
    {
        var session = ctx.Session;
        session.Events.Publish(new StepStartedEvent { Timestamp = NowMs(), StepName = "vibe.planner" });
        EmitAgentStatusReport(session, "planner", AgentStatusMessages.PlannerStart);
        var messageId = $"msg:{session.Id}:planner:{ctx.RunId}";
        StartAgentMessage(session, messageId, agent: "planner", stepName: "vibe.planner", providerName: providerName);

        var userMessage = BuildWorkerMessage("planner", ctx.Question, dag, attachments: ctx.Input.AttachmentPaths);
        var baseSystemPrompt = VibePlannerAgent.GetSystemPrompt();
        var materialsContext = ctx.Materials.RenderedContext;
        
        // Build final system prompt (with Materials Context appended)
        var finalSystemPrompt = string.IsNullOrWhiteSpace(materialsContext)
            ? baseSystemPrompt
            : $"{baseSystemPrompt}\n\nMaterials context:\n{materialsContext.Trim()}\n";

        try
        {
            var (planner, plannerId) = await _core.Runtime.GetPlannerAgentAsync(session.Id, providerName, ct);
            var req = new ChatRequest
            {
                Message = userMessage,
                RequestId = ctx.Input.RequestId ?? Guid.NewGuid().ToString("N"),
                StageHint = "session:vibe:planner"
            };
            req.Context["agent_id"] = plannerId;
            req.Context["materials_context"] = materialsContext;

            var sb = new StringBuilder(1024);
            await foreach (var chunk in planner.ChatStreamAsync(req, ct))
            {
                if (string.IsNullOrEmpty(chunk)) continue;
                sb.Append(chunk);
                EmitAgentDelta(session, messageId, "assistant", chunk);
            }

            EmitAgentDelta(session, messageId, "assistant", "\n\n");
            session.Events.Publish(new StepFinishedEvent { Timestamp = NowMs(), StepName = "vibe.planner" });
            
            var output = Bound(sb.ToString(), 20_000);
            var promptRecord = new AgentPromptRecord(
                AgentName: "planner",
                SystemPrompt: finalSystemPrompt,
                UserPrompt: userMessage,
                MaterialsContext: materialsContext,
                RawOutput: output,
                Timestamp: DateTimeOffset.UtcNow
            );
            return (output, promptRecord);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            var msg = $"[planner error] {ex.Message}\n\n";
            EmitAgentDelta(session, messageId, "assistant", msg);
            session.Events.Publish(new StepFinishedEvent { Timestamp = NowMs(), StepName = "vibe.planner" });
            
            var promptRecord = new AgentPromptRecord(
                AgentName: "planner",
                SystemPrompt: finalSystemPrompt,
                UserPrompt: userMessage,
                MaterialsContext: materialsContext,
                RawOutput: msg,
                Timestamp: DateTimeOffset.UtcNow
            );
            return (msg, promptRecord);
        }
        finally
        {
            EndAgentMessage(session, messageId);
        }
    }

    private async Task<(string Output, AgentPromptRecord? PromptRecord)> RunReasonerAsync(
        VibeRoundContext ctx,
        SraDagSnapshot dag,
        string? plannerOutput,
        string? providerName,
        CancellationToken ct)
    {
        var session = ctx.Session;
        session.Events.Publish(new StepStartedEvent { Timestamp = NowMs(), StepName = "vibe.reasoner" });
        EmitAgentStatusReport(session, "reasoner", AgentStatusMessages.ReasonerStart);
        var messageId = $"msg:{session.Id}:reasoner:{ctx.RunId}";
        StartAgentMessage(session, messageId, agent: "reasoner", stepName: "vibe.reasoner", providerName: providerName);

        var userMessage = BuildWorkerMessage("reasoner", ctx.Question, dag, attachments: ctx.Input.AttachmentPaths,
            extra: string.IsNullOrWhiteSpace(plannerOutput) ? null : $"Planner output (excerpt):\n{Bound(plannerOutput!, 12000)}");
        var baseSystemPrompt = VibeReasonerAgent.GetSystemPrompt();
        var materialsContext = ctx.Materials.RenderedContext;
        
        // Build final system prompt (with Materials Context appended)
        var finalSystemPrompt = string.IsNullOrWhiteSpace(materialsContext)
            ? baseSystemPrompt
            : $"{baseSystemPrompt}\n\nMaterials context:\n{materialsContext.Trim()}\n";

        try
        {
            var (reasoner, reasonerId) = await _core.Runtime.GetReasonerAgentAsync(session.Id, providerName, ct);

            // Best-effort: include python tool if enabled.
            _ = await _core.Runtime.RefreshToolsSnapshotAsync(session.Id, providerName, ct);

            var req = new ChatRequest
            {
                Message = userMessage,
                RequestId = ctx.Input.RequestId ?? Guid.NewGuid().ToString("N"),
                StageHint = "session:vibe:reasoner"
            };
            req.Context["agent_id"] = reasonerId;
            req.Context["materials_context"] = materialsContext;

            var sb = new StringBuilder(2048);
            var supportsStreaming = await reasoner.SupportsStreamingAsync(ct);
            if (!supportsStreaming)
            {
                var resp = await reasoner.ChatAsync(req, ct);
                var text = resp.Content ?? string.Empty;
                sb.Append(text);
                if (text.Length > 0) EmitAgentDelta(session, messageId, "assistant", text);
            }
            else
            {
                await foreach (var chunk in reasoner.ChatStreamAsync(req, ct))
                {
                    if (string.IsNullOrEmpty(chunk)) continue;
                    sb.Append(chunk);
                    EmitAgentDelta(session, messageId, "assistant", chunk);
                }
            }

            EmitAgentDelta(session, messageId, "assistant", "\n\n");
            session.Events.Publish(new StepFinishedEvent { Timestamp = NowMs(), StepName = "vibe.reasoner" });
            
            var output = Bound(sb.ToString(), 40_000);
            var promptRecord = new AgentPromptRecord(
                AgentName: "reasoner",
                SystemPrompt: finalSystemPrompt,
                UserPrompt: userMessage,
                MaterialsContext: materialsContext,
                RawOutput: output,
                Timestamp: DateTimeOffset.UtcNow
            );
            return (output, promptRecord);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            var msg = $"[reasoner error] {ex.Message}\n\n";
            EmitAgentDelta(session, messageId, "assistant", msg);
            session.Events.Publish(new StepFinishedEvent { Timestamp = NowMs(), StepName = "vibe.reasoner" });
            
            var promptRecord = new AgentPromptRecord(
                AgentName: "reasoner",
                SystemPrompt: finalSystemPrompt,
                UserPrompt: userMessage,
                MaterialsContext: materialsContext,
                RawOutput: msg,
                Timestamp: DateTimeOffset.UtcNow
            );
            return (msg, promptRecord);
        }
        finally
        {
            EndAgentMessage(session, messageId);
        }
    }

    private async Task<(string Output, AgentPromptRecord? PromptRecord)> RunLibrarianAsync(
        VibeRoundContext ctx,
        SraDagSnapshot dag,
        string? providerName,
        CancellationToken ct)
    {
        var session = ctx.Session;
        session.Events.Publish(new StepStartedEvent { Timestamp = NowMs(), StepName = "vibe.librarian" });
        EmitAgentStatusReport(session, "librarian", AgentStatusMessages.LibrarianStart);
        var messageId = $"msg:{session.Id}:librarian:{ctx.RunId}";
        StartAgentMessage(session, messageId, agent: "librarian", stepName: "vibe.librarian", providerName: providerName);

        var userMessage = BuildWorkerMessage("librarian", ctx.Question, dag, attachments: ctx.Input.AttachmentPaths);
        var systemPrompt = VibeLibrarianAgent.GetSystemPrompt();

        try
        {
            var (lib, libId) = await _core.Runtime.GetLibrarianAgentAsync(session.Id, providerName, ct);
            var req = new ChatRequest
            {
                Message = userMessage,
                RequestId = ctx.Input.RequestId ?? Guid.NewGuid().ToString("N"),
                StageHint = "session:vibe:librarian"
            };
            req.Context["agent_id"] = libId;
            req.Context["materials_context"] = ctx.Materials.RenderedContext;

            var sb = new StringBuilder(1024);
            var supportsStreaming = await lib.SupportsStreamingAsync(ct);
            if (!supportsStreaming)
            {
                var resp = await lib.ChatAsync(req, ct);
                var text = resp.Content ?? string.Empty;
                sb.Append(text);
                if (text.Length > 0) EmitAgentDelta(session, messageId, "assistant", text);
            }
            else
            {
                await foreach (var chunk in lib.ChatStreamAsync(req, ct))
                {
                    if (string.IsNullOrEmpty(chunk)) continue;
                    sb.Append(chunk);
                    EmitAgentDelta(session, messageId, "assistant", chunk);
                }
            }
            EmitAgentDelta(session, messageId, "assistant", "\n\n");

            session.Events.Publish(new StepFinishedEvent { Timestamp = NowMs(), StepName = "vibe.librarian" });
            
            var output = Bound(sb.ToString(), 20_000);
            var promptRecord = new AgentPromptRecord(
                AgentName: "librarian",
                SystemPrompt: systemPrompt,
                UserPrompt: userMessage,
                MaterialsContext: ctx.Materials.RenderedContext,
                RawOutput: output,
                Timestamp: DateTimeOffset.UtcNow
            );
            return (output, promptRecord);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            var msg = $"[librarian error] {ex.Message}\n\n";
            EmitAgentDelta(session, messageId, "assistant", msg);
            session.Events.Publish(new StepFinishedEvent { Timestamp = NowMs(), StepName = "vibe.librarian" });
            
            var promptRecord = new AgentPromptRecord(
                AgentName: "librarian",
                SystemPrompt: systemPrompt,
                UserPrompt: userMessage,
                MaterialsContext: ctx.Materials.RenderedContext,
                RawOutput: msg,
                Timestamp: DateTimeOffset.UtcNow
            );
            return (msg, promptRecord);
        }
        finally
        {
            EndAgentMessage(session, messageId);
        }
    }

    private async Task<(string Output, AgentPromptRecord? PromptRecord)> RunVerifierAsync(
        VibeRoundContext ctx,
        SraDagSnapshot dag,
        string? reasonerOutput,
        string? providerName,
        CancellationToken ct)
    {
        var session = ctx.Session;
        session.Events.Publish(new StepStartedEvent { Timestamp = NowMs(), StepName = "vibe.verifier" });
        EmitAgentStatusReport(session, "verifier", AgentStatusMessages.VerifierStart);
        var messageId = $"msg:{session.Id}:verifier:{ctx.RunId}";
        StartAgentMessage(session, messageId, agent: "verifier", stepName: "vibe.verifier", providerName: providerName);

        var userMessage = BuildWorkerMessage("verifier", ctx.Question, dag, attachments: ctx.Input.AttachmentPaths,
            extra: string.IsNullOrWhiteSpace(reasonerOutput) ? null : $"Reasoner output (excerpt):\n{Bound(reasonerOutput!, 3500)}");
        var baseSystemPrompt = VibeVerifierAgent.GetSystemPrompt();
        var materialsContext = ctx.Materials.RenderedContext;
        
        // Build final system prompt (with Materials Context appended)
        var finalSystemPrompt = string.IsNullOrWhiteSpace(materialsContext)
            ? baseSystemPrompt
            : $"{baseSystemPrompt}\n\nMaterials context:\n{materialsContext.Trim()}\n";

        try
        {
            var (ver, verId) = await _core.Runtime.GetVerifierAgentAsync(session.Id, providerName, ct);
            var req = new ChatRequest
            {
                Message = userMessage,
                RequestId = ctx.Input.RequestId ?? Guid.NewGuid().ToString("N"),
                StageHint = "session:vibe:verifier"
            };
            req.Context["agent_id"] = verId;
            req.Context["materials_context"] = materialsContext;

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

            session.Events.Publish(new StepFinishedEvent { Timestamp = NowMs(), StepName = "vibe.verifier" });
            
            var output = Bound(sb.ToString(), 20_000);
            var promptRecord = new AgentPromptRecord(
                AgentName: "verifier",
                SystemPrompt: finalSystemPrompt,
                UserPrompt: userMessage,
                MaterialsContext: materialsContext,
                RawOutput: output,
                Timestamp: DateTimeOffset.UtcNow
            );
            return (output, promptRecord);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            var msg = $"[verifier error] {ex.Message}\n\n";
            EmitAgentDelta(session, messageId, "assistant", msg);
            session.Events.Publish(new StepFinishedEvent { Timestamp = NowMs(), StepName = "vibe.verifier" });
            
            var errorPromptRecord = new AgentPromptRecord(
                AgentName: "verifier",
                SystemPrompt: finalSystemPrompt,
                UserPrompt: userMessage,
                MaterialsContext: materialsContext,
                RawOutput: msg,
                Timestamp: DateTimeOffset.UtcNow
            );
            return (msg, errorPromptRecord);
        }
        finally
        {
            EndAgentMessage(session, messageId);
        }
    }

    // Helper functions for hypothesis extraction and parsing
    private static List<string> ExtractHypothesesFromReasonerOutput(string? reasonerOutput)
    {
        var hypotheses = new List<string>();
        if (string.IsNullOrWhiteSpace(reasonerOutput))
            return hypotheses;

        // Look for patterns like:
        // - "Hypotheses to Verify:"
        // - "Hypotheses:"
        // - "H1:", "H2:", etc.
        // - "- **H1**: ..."
        // - "- H1: ..."
        
        var lines = reasonerOutput.Split('\n');
        bool inHypothesisSection = false;
        
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            
            // Check if we're entering a hypothesis section
            if (Regex.IsMatch(trimmed, @"(?i)(hypotheses?\s*(to\s+verify)?|assumptions?\s*(to\s+verify)?)\s*:?", RegexOptions.IgnoreCase))
            {
                inHypothesisSection = true;
                continue;
            }
            
            // Check for hypothesis markers
            var hMatch = Regex.Match(trimmed, @"(?i)^[-*]?\s*(?:H|Hypothesis|Assumption)\s*(\d+|[A-Z])\s*[:：]\s*(.+)$");
            if (hMatch.Success)
            {
                var hypothesisText = hMatch.Groups[2].Value.Trim();
                if (!string.IsNullOrWhiteSpace(hypothesisText))
                {
                    hypotheses.Add(hypothesisText);
                }
                inHypothesisSection = true;
                continue;
            }
            
            // If we're in a hypothesis section and see a list item, try to extract
            if (inHypothesisSection && (trimmed.StartsWith("-") || trimmed.StartsWith("*")))
            {
                var content = trimmed.Substring(1).Trim();
                if (!string.IsNullOrWhiteSpace(content) && content.Length > 10)
                {
                    // Remove markdown formatting
                    content = Regex.Replace(content, @"\*\*([^*]+)\*\*", "$1");
                    content = Regex.Replace(content, @"`([^`]+)`", "$1");
                    if (!string.IsNullOrWhiteSpace(content))
                    {
                        hypotheses.Add(content);
                    }
                }
            }
            
            // Exit hypothesis section if we hit a new major section
            if (inHypothesisSection && Regex.IsMatch(trimmed, @"^#{1,3}\s+[A-Z]"))
            {
                inHypothesisSection = false;
            }
        }
        
        return hypotheses.Distinct().ToList();
    }

    private static List<string> ExtractDependencies(string text, List<string> hypotheses)
    {
        var dependencies = new List<string>();
        if (string.IsNullOrWhiteSpace(text) || hypotheses.Count == 0)
            return dependencies;

        // Look for patterns like:
        // - "depends on H1", "depends on H2"
        // - "requires H1", "requires H2"
        // - "uses H1", "uses H2"
        // - "if H1 then", "if H2 then"
        // - "H1 and H2", "H1, H2"
        
        foreach (var hypothesis in hypotheses)
        {
            var patterns = new[]
            {
                $@"(?i)(?:depends?\s+on|requires?|uses?|based\s+on|assumes?)\s+{Regex.Escape(hypothesis)}",
                $@"(?i){Regex.Escape(hypothesis)}\s+(?:and|,|then)",
                $@"(?i)if\s+{Regex.Escape(hypothesis)}\s+then"
            };
            
            foreach (var pattern in patterns)
            {
                if (Regex.IsMatch(text, pattern))
                {
                    dependencies.Add(hypothesis);
                    break;
                }
            }
        }
        
        return dependencies.Distinct().ToList();
    }

    private static List<HypothesisJudgment> ApplyDependencyPropagation(
        List<HypothesisJudgment> judgments,
        bool isScoutPhase)
    {
        // Create a map for quick lookup
        var judgmentMap = judgments.ToDictionary(j => j.HypothesisId, j => j);
        var updatedJudgments = new List<HypothesisJudgment>();
        
        // Process judgments in dependency order (dependencies first)
        var processed = new HashSet<string>();
        
        // Helper function to process a judgment and its dependencies recursively
        void ProcessJudgment(HypothesisJudgment judgment)
        {
            if (processed.Contains(judgment.HypothesisId))
                return;
            
            // Process dependencies first
            if (judgment.Dependencies != null)
            {
                foreach (var depId in judgment.Dependencies)
                {
                    if (judgmentMap.TryGetValue(depId, out var depJudgment))
                    {
                        ProcessJudgment(depJudgment);
                        
                        // If dependency is blocked/not verified, block/not verify this hypothesis
                        var depBlocked = isScoutPhase
                            ? depJudgment.Recommendation == "BLOCK"
                            : depJudgment.Recommendation == "NOT VERIFIED";
                        
                        if (depBlocked)
                        {
                            var newRecommendation = isScoutPhase ? "BLOCK" : "NOT VERIFIED";
                            var newReason = $"Blocked due to dependency on {depId} which is {depJudgment.Recommendation}";
                            judgment = judgment with
                            {
                                Recommendation = newRecommendation,
                                Reason = string.IsNullOrWhiteSpace(judgment.Reason)
                                    ? newReason
                                    : $"{judgment.Reason}. {newReason}"
                            };
                        }
                    }
                }
            }
            
            processed.Add(judgment.HypothesisId);
            updatedJudgments.Add(judgment);
        }
        
        // Process all judgments
        foreach (var judgment in judgments)
        {
            ProcessJudgment(judgment);
        }
        
        return updatedJudgments;
    }

    private static List<HypothesisJudgment> ParseScoutHypothesisJudgments(string output, List<string>? hypotheses)
    {
        var judgments = new List<HypothesisJudgment>();
        
        if (string.IsNullOrWhiteSpace(output) || hypotheses == null || hypotheses.Count == 0)
            return judgments;

        // Try to parse structured output
        // Look for patterns like:
        // - "Hypothesis: H1 / [text]"
        // - "Dependencies: H2, H3"
        // - "Recommendation: PROCEED / BLOCK"
        // - "Brief Reason: ..."
        
        var lines = output.Split('\n');
        HypothesisJudgment? currentJudgment = null;
        
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            
            // Match hypothesis identifier
            var hMatch = Regex.Match(trimmed, @"(?i)^Hypothesis\s*[:：]\s*(.+)$");
            if (hMatch.Success)
            {
                if (currentJudgment != null)
                {
                    judgments.Add(currentJudgment);
                }
                
                var hypothesisText = hMatch.Groups[1].Value.Trim();
                var hypothesisId = hypotheses.FirstOrDefault(h => hypothesisText.Contains(h) || h.Contains(hypothesisText)) ?? hypothesisText;
                var dependencies = ExtractDependencies(hypothesisText + " " + output, hypotheses);
                
                currentJudgment = new HypothesisJudgment(
                    HypothesisId: hypothesisId,
                    HypothesisText: hypothesisText,
                    Recommendation: "PROCEED", // default
                    Dependencies: dependencies.Count > 0 ? dependencies : null,
                    Reason: null
                );
                continue;
            }
            
            // Match dependencies
            var depMatch = Regex.Match(trimmed, @"(?i)^Dependencies?\s*[:：]\s*(.+)$");
            if (depMatch.Success && currentJudgment != null)
            {
                var depText = depMatch.Groups[1].Value.Trim();
                var dependencies = ExtractDependencies(depText, hypotheses);
                currentJudgment = currentJudgment with { Dependencies = dependencies.Count > 0 ? dependencies : null };
                continue;
            }
            
            // Match recommendation
            var recMatch = Regex.Match(trimmed, @"(?i)^Recommendation\s*[:：]\s*(PROCEED|BLOCK)$");
            if (recMatch.Success && currentJudgment != null)
            {
                currentJudgment = currentJudgment with { Recommendation = recMatch.Groups[1].Value.ToUpper() };
                continue;
            }
            
            // Match reason
            var reasonMatch = Regex.Match(trimmed, @"(?i)^(?:Brief\s+)?Reason\s*[:：]\s*(.+)$");
            if (reasonMatch.Success && currentJudgment != null)
            {
                currentJudgment = currentJudgment with { Reason = reasonMatch.Groups[1].Value.Trim() };
                continue;
            }
        }
        
        if (currentJudgment != null)
        {
            judgments.Add(currentJudgment);
        }
        
        // If we couldn't parse structured output, try to match hypotheses to overall recommendation
        if (judgments.Count == 0)
        {
            var overallRecommendation = output.Contains("BLOCK", StringComparison.OrdinalIgnoreCase) ? "BLOCK" : "PROCEED";
            foreach (var hypothesis in hypotheses)
            {
                var dependencies = ExtractDependencies(hypothesis + " " + output, hypotheses);
                judgments.Add(new HypothesisJudgment(
                    HypothesisId: hypothesis,
                    HypothesisText: hypothesis,
                    Recommendation: overallRecommendation,
                    Dependencies: dependencies.Count > 0 ? dependencies : null,
                    Reason: "Overall assessment"
                ));
            }
        }
        
        // Apply dependency propagation
        return ApplyDependencyPropagation(judgments, isScoutPhase: true);
    }

    private static List<HypothesisJudgment> ParseProverHypothesisJudgments(string output, List<string>? hypotheses)
    {
        var judgments = new List<HypothesisJudgment>();
        
        if (string.IsNullOrWhiteSpace(output) || hypotheses == null || hypotheses.Count == 0)
            return judgments;

        // Try to parse structured output
        // Look for patterns like:
        // - "Hypothesis: H1 / [text]"
        // - "Dependencies: H2, H3"
        // - "Result: VERIFIED / NOT VERIFIED / INCONCLUSIVE"
        // - "Notes: ..."
        
        var lines = output.Split('\n');
        HypothesisJudgment? currentJudgment = null;
        
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            
            // Match hypothesis identifier
            var hMatch = Regex.Match(trimmed, @"(?i)^Hypothesis\s*[:：]\s*(.+)$");
            if (hMatch.Success)
            {
                if (currentJudgment != null)
                {
                    judgments.Add(currentJudgment);
                }
                
                var hypothesisText = hMatch.Groups[1].Value.Trim();
                var hypothesisId = hypotheses.FirstOrDefault(h => hypothesisText.Contains(h) || h.Contains(hypothesisText)) ?? hypothesisText;
                var dependencies = ExtractDependencies(hypothesisText + " " + output, hypotheses);
                
                currentJudgment = new HypothesisJudgment(
                    HypothesisId: hypothesisId,
                    HypothesisText: hypothesisText,
                    Recommendation: "INCONCLUSIVE", // default
                    Dependencies: dependencies.Count > 0 ? dependencies : null,
                    Reason: null
                );
                continue;
            }
            
            // Match dependencies
            var depMatch = Regex.Match(trimmed, @"(?i)^Dependencies?\s*[:：]\s*(.+)$");
            if (depMatch.Success && currentJudgment != null)
            {
                var depText = depMatch.Groups[1].Value.Trim();
                var dependencies = ExtractDependencies(depText, hypotheses);
                currentJudgment = currentJudgment with { Dependencies = dependencies.Count > 0 ? dependencies : null };
                continue;
            }
            
            // Match result
            var resultMatch = Regex.Match(trimmed, @"(?i)^Result\s*[:：]\s*(VERIFIED|NOT\s+VERIFIED|INCONCLUSIVE)$");
            if (resultMatch.Success && currentJudgment != null)
            {
                var result = resultMatch.Groups[1].Value.ToUpper().Replace(" ", "_");
                if (result == "NOT_VERIFIED") result = "NOT VERIFIED";
                currentJudgment = currentJudgment with { Recommendation = result };
                continue;
            }
            
            // Match notes as reason
            var notesMatch = Regex.Match(trimmed, @"(?i)^Notes\s*[:：]\s*(.+)$");
            if (notesMatch.Success && currentJudgment != null)
            {
                currentJudgment = currentJudgment with { Reason = notesMatch.Groups[1].Value.Trim() };
                continue;
            }
        }
        
        if (currentJudgment != null)
        {
            judgments.Add(currentJudgment);
        }
        
        // If we couldn't parse structured output, try to match hypotheses to overall result
        if (judgments.Count == 0)
        {
            var overallResult = output.Contains("VERIFIED", StringComparison.OrdinalIgnoreCase) &&
                               !output.Contains("NOT VERIFIED", StringComparison.OrdinalIgnoreCase) &&
                               !output.Contains("INCONCLUSIVE", StringComparison.OrdinalIgnoreCase)
                ? "VERIFIED"
                : output.Contains("NOT VERIFIED", StringComparison.OrdinalIgnoreCase)
                    ? "NOT VERIFIED"
                    : "INCONCLUSIVE";
            
            foreach (var hypothesis in hypotheses)
            {
                var dependencies = ExtractDependencies(hypothesis + " " + output, hypotheses);
                judgments.Add(new HypothesisJudgment(
                    HypothesisId: hypothesis,
                    HypothesisText: hypothesis,
                    Recommendation: overallResult,
                    Dependencies: dependencies.Count > 0 ? dependencies : null,
                    Reason: "Overall assessment"
                ));
            }
        }
        
        // Apply dependency propagation
        return ApplyDependencyPropagation(judgments, isScoutPhase: false);
    }

    private sealed record MultiStageVerificationResult(
        string Summary,
        bool OverallPass,
        string? Proof,
        AgentPromptRecord? PromptRecord
    );

    private sealed record HypothesisJudgment(
        string HypothesisId,
        string HypothesisText,
        string Recommendation, // PROCEED / BLOCK for Scout, VERIFIED / NOT VERIFIED / INCONCLUSIVE for Prover
        List<string>? Dependencies = null, // List of hypothesis IDs this hypothesis depends on
        string? Reason = null
    );

    private sealed record ScoutWorkerResult(
        string Output,
        bool RecommendProceed,
        List<HypothesisJudgment>? HypothesisJudgments,
        AgentPromptRecord? PromptRecord
    );

    private sealed record ScoutPhaseResult(
        List<ScoutWorkerResult> WorkerResults,
        bool OverallProceed,
        string Summary,
        List<string>? IdentifiedHypotheses = null
    );

    private sealed record ProverWorkerResult(
        string Output,
        bool Verified,
        List<HypothesisJudgment>? HypothesisJudgments,
        AgentPromptRecord? PromptRecord
    );

    private sealed record ProverPhaseResult(
        List<ProverWorkerResult> WorkerResults,
        int PassCount,
        bool OverallPass,
        string Summary,
        Dictionary<string, int>? HypothesisVotes = null // hypothesis -> verified count
    );

    private async Task<MultiStageVerificationResult> RunMultiStageVerifierAsync(
        VibeRoundContext ctx,
        SraDagSnapshot dag,
        string? reasonerOutput,
        string? providerName,
        CancellationToken ct)
    {
        var session = ctx.Session;
        var allPromptRecords = new List<AgentPromptRecord>();

        // Phase 1: Scout Phase (2 workers)
        session.Events.Publish(new StepStartedEvent { Timestamp = NowMs(), StepName = "vibe.verifier.scout" });
        EmitAgentStatusReport(session, "verifier", AgentStatusMessages.VerifierScoutStart);
        
        var scoutResult = await RunScoutPhaseAsync(ctx, dag, reasonerOutput, providerName, ct);
        allPromptRecords.AddRange(scoutResult.WorkerResults.Where(r => r.PromptRecord != null).Select(r => r.PromptRecord!));
        
        session.Events.Publish(new StepFinishedEvent { Timestamp = NowMs(), StepName = "vibe.verifier.scout" });

        // If Scout phase recommends BLOCK, return early
        if (!scoutResult.OverallProceed)
        {
            var summary = $"Scout Phase: BLOCKED\n\n{scoutResult.Summary}";
            return new MultiStageVerificationResult(
                Summary: summary,
                OverallPass: false,
                Proof: null, // No proof if blocked
                PromptRecord: allPromptRecords.FirstOrDefault()
            );
        }

        // Phase 2: Prover Phase (5 workers)
        // Pass identified hypotheses from Scout phase to Prover phase
        session.Events.Publish(new StepStartedEvent { Timestamp = NowMs(), StepName = "vibe.verifier.prover" });
        EmitAgentStatusReport(session, "verifier", AgentStatusMessages.VerifierProverStart);
        
        var proverResult = await RunProverPhaseAsync(ctx, dag, reasonerOutput, providerName, ct);
        allPromptRecords.AddRange(proverResult.WorkerResults.Where(r => r.PromptRecord != null).Select(r => r.PromptRecord!));
        
        session.Events.Publish(new StepFinishedEvent { Timestamp = NowMs(), StepName = "vibe.verifier.prover" });

        // Phase 3: Final Proof Extraction (if verification passed)
        string? proof = null;
        if (proverResult.OverallPass)
        {
            session.Events.Publish(new StepStartedEvent { Timestamp = NowMs(), StepName = "vibe.verifier.proof_extraction" });
            EmitAgentStatusReport(session, "verifier", "正在提取验证证明...");
            
            proof = await RunProofExtractionAsync(ctx, dag, reasonerOutput, scoutResult, proverResult, providerName, ct);
            
            session.Events.Publish(new StepFinishedEvent { Timestamp = NowMs(), StepName = "vibe.verifier.proof_extraction" });
        }

        // Build final summary
        var finalSummary = new StringBuilder();
        finalSummary.AppendLine("## Multi-Stage Verification Summary");
        finalSummary.AppendLine();
        finalSummary.AppendLine("### Scout Phase:");
        finalSummary.AppendLine(scoutResult.Summary);
        finalSummary.AppendLine();
        finalSummary.AppendLine("### Prover Phase:");
        finalSummary.AppendLine(proverResult.Summary);
        finalSummary.AppendLine();
        finalSummary.AppendLine($"### Overall Result: {(proverResult.OverallPass ? "PASSED" : "FAILED")} (Prover: {proverResult.PassCount}/5)");
        if (!string.IsNullOrWhiteSpace(proof))
        {
            finalSummary.AppendLine();
            finalSummary.AppendLine("### Proof:");
            finalSummary.AppendLine(proof);
        }

        return new MultiStageVerificationResult(
            Summary: finalSummary.ToString(),
            OverallPass: proverResult.OverallPass,
            Proof: proof,
            PromptRecord: allPromptRecords.FirstOrDefault()
        );
    }

    private async Task<ScoutPhaseResult> RunScoutPhaseAsync(
        VibeRoundContext ctx,
        SraDagSnapshot dag,
        string? reasonerOutput,
        string? providerName,
        CancellationToken ct)
    {
        var session = ctx.Session;
        var materialsContext = ctx.Materials.RenderedContext;
        var workerResults = new List<ScoutWorkerResult>();

        // Extract hypotheses from Reasoner output
        var identifiedHypotheses = ExtractHypothesesFromReasonerOutput(reasonerOutput);

        // Build base user message
        var baseUserMessage = BuildWorkerMessage("verification_scout", ctx.Question, dag, attachments: ctx.Input.AttachmentPaths,
            extra: string.IsNullOrWhiteSpace(reasonerOutput) ? null : $"Reasoner output (excerpt):\n{Bound(reasonerOutput!, 3500)}");

        // Run 2 workers in parallel
        var tasks = new[]
        {
            RunScoutWorkerAsync(ctx, dag, reasonerOutput, providerName, "counterexample", baseUserMessage, materialsContext, identifiedHypotheses, ct),
            RunScoutWorkerAsync(ctx, dag, reasonerOutput, providerName, "premise", baseUserMessage, materialsContext, identifiedHypotheses, ct)
        };

        var results = await Task.WhenAll(tasks);
        workerResults.AddRange(results);

        // Analyze results: if any worker recommends BLOCK, overall is BLOCK
        var overallProceed = workerResults.All(r => r.RecommendProceed);

        // Build summary with hypothesis-level judgments
        var summary = new StringBuilder();
        summary.AppendLine($"### Worker 1 (Counterexample Scout):");
        summary.AppendLine($"- Overall Recommendation: {(workerResults[0].RecommendProceed ? "PROCEED" : "BLOCK")}");
        
        if (workerResults[0].HypothesisJudgments != null && workerResults[0].HypothesisJudgments.Count > 0)
        {
            summary.AppendLine($"- Hypothesis Judgments:");
            foreach (var judgment in workerResults[0].HypothesisJudgments)
            {
                summary.AppendLine($"  - {judgment.HypothesisId}: {judgment.Recommendation}");
                if (judgment.Dependencies != null && judgment.Dependencies.Count > 0)
                {
                    summary.AppendLine($"    Dependencies: {string.Join(", ", judgment.Dependencies)}");
                }
                if (!string.IsNullOrWhiteSpace(judgment.Reason))
                {
                    summary.AppendLine($"    Reason: {judgment.Reason}");
                }
            }
        }
        summary.AppendLine($"- Output: {Bound(workerResults[0].Output, 500)}");
        summary.AppendLine();
        
        summary.AppendLine($"### Worker 2 (Missing Premise Scout):");
        summary.AppendLine($"- Overall Recommendation: {(workerResults[1].RecommendProceed ? "PROCEED" : "BLOCK")}");
        
        if (workerResults[1].HypothesisJudgments != null && workerResults[1].HypothesisJudgments.Count > 0)
        {
            summary.AppendLine($"- Hypothesis Judgments:");
            foreach (var judgment in workerResults[1].HypothesisJudgments)
            {
                summary.AppendLine($"  - {judgment.HypothesisId}: {judgment.Recommendation}");
                if (judgment.Dependencies != null && judgment.Dependencies.Count > 0)
                {
                    summary.AppendLine($"    Dependencies: {string.Join(", ", judgment.Dependencies)}");
                }
                if (!string.IsNullOrWhiteSpace(judgment.Reason))
                {
                    summary.AppendLine($"    Reason: {judgment.Reason}");
                }
            }
        }
        summary.AppendLine($"- Output: {Bound(workerResults[1].Output, 500)}");
        summary.AppendLine();
        
        summary.AppendLine($"### Overall Scout Decision: {(overallProceed ? "PROCEED" : "BLOCK")}");
        
        if (identifiedHypotheses.Count > 0)
        {
            summary.AppendLine();
            summary.AppendLine($"### Identified Hypotheses ({identifiedHypotheses.Count}):");
            for (int i = 0; i < identifiedHypotheses.Count; i++)
            {
                summary.AppendLine($"- H{i + 1}: {Bound(identifiedHypotheses[i], 200)}");
            }
        }

        return new ScoutPhaseResult(
            WorkerResults: workerResults,
            OverallProceed: overallProceed,
            Summary: summary.ToString(),
            IdentifiedHypotheses: identifiedHypotheses.Count > 0 ? identifiedHypotheses : null
        );
    }

    private async Task<ScoutWorkerResult> RunScoutWorkerAsync(
        VibeRoundContext ctx,
        SraDagSnapshot dag,
        string? reasonerOutput,
        string? providerName,
        string focus,
        string baseUserMessage,
        string materialsContext,
        List<string> identifiedHypotheses,
        CancellationToken ct)
    {
        var session = ctx.Session;
        var messageId = $"msg:{session.Id}:verifier:scout:{focus}:{ctx.RunId}";

        // Build focus-specific user message
        var userMessage = new StringBuilder(baseUserMessage);
        userMessage.AppendLine();
        userMessage.AppendLine($"Focus: {focus}");
        userMessage.AppendLine();
        if (focus == "counterexample")
        {
            userMessage.AppendLine("Task:");
            userMessage.AppendLine("- Scan the reasoning chain for obvious counterexamples.");
            userMessage.AppendLine("- Test edge cases and boundary conditions.");
            userMessage.AppendLine("- Use computational checks if applicable.");
        }
        else
        {
            userMessage.AppendLine("Task:");
            userMessage.AppendLine("- Scan the reasoning chain for missing premises.");
            userMessage.AppendLine("- Identify unstated assumptions.");
            userMessage.AppendLine("- Check if all dependencies are properly cited.");
        }

        var baseSystemPrompt = VibeVerifierAgent.GetScoutSystemPrompt(focus);
        var finalSystemPrompt = string.IsNullOrWhiteSpace(materialsContext)
            ? baseSystemPrompt
            : $"{baseSystemPrompt}\n\nMaterials context:\n{materialsContext.Trim()}\n";

        try
        {
            var (ver, verId) = await _core.Runtime.GetVerifierAgentAsync(session.Id, providerName, ct);
            var req = new ChatRequest
            {
                Message = userMessage.ToString(),
                RequestId = ctx.Input.RequestId ?? Guid.NewGuid().ToString("N"),
                StageHint = $"session:vibe:verifier:scout:{focus}"
            };
            req.Context["agent_id"] = verId;
            req.Context["materials_context"] = materialsContext;

            var sb = new StringBuilder(512);
            var supportsStreaming = await ver.SupportsStreamingAsync(ct);
            if (!supportsStreaming)
            {
                var resp = await ver.ChatAsync(req, ct);
                var text = resp.Content ?? string.Empty;
                sb.Append(text);
            }
            else
            {
                await foreach (var chunk in ver.ChatStreamAsync(req, ct))
                {
                    if (string.IsNullOrEmpty(chunk)) continue;
                    sb.Append(chunk);
                }
            }

            var output = Bound(sb.ToString(), 5_000);
            
            // Parse recommendation: look for "PROCEED" or "BLOCK" in output
            var recommendProceed = output.Contains("PROCEED", StringComparison.OrdinalIgnoreCase) &&
                                  !output.Contains("BLOCK", StringComparison.OrdinalIgnoreCase);

            // Parse hypothesis-level judgments
            var hypothesisJudgments = ParseScoutHypothesisJudgments(output, identifiedHypotheses.Count > 0 ? identifiedHypotheses : null);

            var promptRecord = new AgentPromptRecord(
                AgentName: $"verifier_scout_{focus}",
                SystemPrompt: finalSystemPrompt,
                UserPrompt: userMessage.ToString(),
                MaterialsContext: materialsContext,
                RawOutput: output,
                Timestamp: DateTimeOffset.UtcNow
            );

            return new ScoutWorkerResult(
                Output: output,
                RecommendProceed: recommendProceed,
                HypothesisJudgments: hypothesisJudgments.Count > 0 ? hypothesisJudgments : null,
                PromptRecord: promptRecord
            );
        }
        catch (Exception ex)
        {
            var msg = $"[scout {focus} error] {ex.Message}\n\n";
            var promptRecord = new AgentPromptRecord(
                AgentName: $"verifier_scout_{focus}",
                SystemPrompt: finalSystemPrompt,
                UserPrompt: userMessage.ToString(),
                MaterialsContext: materialsContext,
                RawOutput: msg,
                Timestamp: DateTimeOffset.UtcNow
            );
            
            // On error, default to BLOCK (fail-safe)
            return new ScoutWorkerResult(
                Output: msg,
                RecommendProceed: false,
                HypothesisJudgments: null,
                PromptRecord: promptRecord
            );
        }
    }

    private async Task<ProverPhaseResult> RunProverPhaseAsync(
        VibeRoundContext ctx,
        SraDagSnapshot dag,
        string? reasonerOutput,
        string? providerName,
        CancellationToken ct)
    {
        var session = ctx.Session;
        var materialsContext = ctx.Materials.RenderedContext;
        var workerResults = new List<ProverWorkerResult>();

        // Extract hypotheses from Reasoner output
        var identifiedHypotheses = ExtractHypothesesFromReasonerOutput(reasonerOutput);

        // Build base user message
        var baseUserMessage = BuildWorkerMessage("verification_prover", ctx.Question, dag, attachments: ctx.Input.AttachmentPaths,
            extra: string.IsNullOrWhiteSpace(reasonerOutput) ? null : $"Reasoner output (excerpt):\n{Bound(reasonerOutput!, 3500)}");

        // Run 5 workers in parallel with different roles
        var proverRoles = new[]
        {
            "Direct prover",
            "Algebraic manipulator",
            "Dependency minimalist",
            "Case-split specialist",
            "Proof auditor"
        };
        
        var tasks = new[]
        {
            RunProverWorkerAsync(ctx, dag, reasonerOutput, providerName, 1, proverRoles[0], baseUserMessage, materialsContext, identifiedHypotheses, ct),
            RunProverWorkerAsync(ctx, dag, reasonerOutput, providerName, 2, proverRoles[1], baseUserMessage, materialsContext, identifiedHypotheses, ct),
            RunProverWorkerAsync(ctx, dag, reasonerOutput, providerName, 3, proverRoles[2], baseUserMessage, materialsContext, identifiedHypotheses, ct),
            RunProverWorkerAsync(ctx, dag, reasonerOutput, providerName, 4, proverRoles[3], baseUserMessage, materialsContext, identifiedHypotheses, ct),
            RunProverWorkerAsync(ctx, dag, reasonerOutput, providerName, 5, proverRoles[4], baseUserMessage, materialsContext, identifiedHypotheses, ct)
        };

        var results = await Task.WhenAll(tasks);
        workerResults.AddRange(results);

        // Count passes: need at least 3 to pass
        var passCount = workerResults.Count(r => r.Verified);
        var overallPass = passCount >= 3;

        // Aggregate hypothesis votes
        var hypothesisVotes = new Dictionary<string, int>();
        if (identifiedHypotheses.Count > 0)
        {
            foreach (var hypothesis in identifiedHypotheses)
            {
                hypothesisVotes[hypothesis] = 0;
            }
            
            foreach (var workerResult in workerResults)
            {
                if (workerResult.HypothesisJudgments != null)
                {
                    foreach (var judgment in workerResult.HypothesisJudgments)
                    {
                        if (judgment.Recommendation == "VERIFIED")
                        {
                            var key = identifiedHypotheses.FirstOrDefault(h => 
                                judgment.HypothesisId.Contains(h) || h.Contains(judgment.HypothesisId)) ?? judgment.HypothesisId;
                            if (hypothesisVotes.ContainsKey(key))
                            {
                                hypothesisVotes[key]++;
                            }
                        }
                    }
                }
            }
        }

        // Build summary with hypothesis-level judgments
        var summary = new StringBuilder();
        for (int i = 0; i < workerResults.Count; i++)
        {
            summary.AppendLine($"### Worker {i + 1} ({proverRoles[i]}):");
            summary.AppendLine($"- Overall Result: {(workerResults[i].Verified ? "VERIFIED" : "NOT VERIFIED")}");
            
            if (workerResults[i].HypothesisJudgments != null && workerResults[i].HypothesisJudgments.Count > 0)
            {
                summary.AppendLine($"- Hypothesis Judgments:");
                foreach (var judgment in workerResults[i].HypothesisJudgments)
                {
                    summary.AppendLine($"  - {Bound(judgment.HypothesisId, 150)}: {judgment.Recommendation}");
                    if (judgment.Dependencies != null && judgment.Dependencies.Count > 0)
                    {
                        summary.AppendLine($"    Dependencies: {string.Join(", ", judgment.Dependencies.Select(d => Bound(d, 100)))}");
                    }
                    if (!string.IsNullOrWhiteSpace(judgment.Reason))
                    {
                        summary.AppendLine($"    Notes: {Bound(judgment.Reason, 200)}");
                    }
                }
            }
            summary.AppendLine($"- Output: {Bound(workerResults[i].Output, 300)}");
            summary.AppendLine();
        }
        summary.AppendLine($"### Overall: {passCount}/5 workers verified (need ≥3 to pass)");
        
        if (hypothesisVotes.Count > 0)
        {
            summary.AppendLine();
            summary.AppendLine($"### Hypothesis-Level Verification Summary:");
            foreach (var kvp in hypothesisVotes.OrderByDescending(x => x.Value))
            {
                var verifiedCount = kvp.Value;
                var status = verifiedCount >= 3 ? "VERIFIED" : verifiedCount >= 1 ? "PARTIALLY VERIFIED" : "NOT VERIFIED";
                summary.AppendLine($"- {Bound(kvp.Key, 200)}: {verifiedCount}/5 workers verified ({status})");
            }
        }

        return new ProverPhaseResult(
            WorkerResults: workerResults,
            PassCount: passCount,
            OverallPass: overallPass,
            Summary: summary.ToString(),
            HypothesisVotes: hypothesisVotes.Count > 0 ? hypothesisVotes : null
        );
    }

    private async Task<ProverWorkerResult> RunProverWorkerAsync(
        VibeRoundContext ctx,
        SraDagSnapshot dag,
        string? reasonerOutput,
        string? providerName,
        int workerIndex,
        string role,
        string baseUserMessage,
        string materialsContext,
        List<string> identifiedHypotheses,
        CancellationToken ct)
    {
        var session = ctx.Session;
        var messageId = $"msg:{session.Id}:verifier:prover:{workerIndex}:{ctx.RunId}";

        var baseSystemPrompt = VibeVerifierAgent.GetProverSystemPrompt(role);
        var finalSystemPrompt = string.IsNullOrWhiteSpace(materialsContext)
            ? baseSystemPrompt
            : $"{baseSystemPrompt}\n\nMaterials context:\n{materialsContext.Trim()}\n";
        
        // Add role-specific context to user message
        var userMessage = new StringBuilder(baseUserMessage);
        userMessage.AppendLine();
        userMessage.AppendLine($"Role: {role}");
        userMessage.AppendLine();
        userMessage.AppendLine("Apply your specific verification approach based on your role.");

        try
        {
            var (ver, verId) = await _core.Runtime.GetVerifierAgentAsync(session.Id, providerName, ct);
            var req = new ChatRequest
            {
                Message = userMessage.ToString(),
                RequestId = ctx.Input.RequestId ?? Guid.NewGuid().ToString("N"),
                StageHint = $"session:vibe:verifier:prover:{workerIndex}"
            };
            req.Context["agent_id"] = verId;
            req.Context["materials_context"] = materialsContext;

            var sb = new StringBuilder(1024);
            var supportsStreaming = await ver.SupportsStreamingAsync(ct);
            if (!supportsStreaming)
            {
                var resp = await ver.ChatAsync(req, ct);
                var text = resp.Content ?? string.Empty;
                sb.Append(text);
            }
            else
            {
                await foreach (var chunk in ver.ChatStreamAsync(req, ct))
                {
                    if (string.IsNullOrEmpty(chunk)) continue;
                    sb.Append(chunk);
                }
            }

            var output = Bound(sb.ToString(), 10_000);
            
            // Parse verification result: look for "VERIFIED" in output
            var verified = output.Contains("VERIFIED", StringComparison.OrdinalIgnoreCase) &&
                          !output.Contains("NOT VERIFIED", StringComparison.OrdinalIgnoreCase) &&
                          !output.Contains("INCONCLUSIVE", StringComparison.OrdinalIgnoreCase);

            // Parse hypothesis-level judgments
            var hypothesisJudgments = ParseProverHypothesisJudgments(output, identifiedHypotheses.Count > 0 ? identifiedHypotheses : null);

            var promptRecord = new AgentPromptRecord(
                AgentName: $"verifier_prover_{workerIndex}_{role.Replace(" ", "_").ToLowerInvariant()}",
                SystemPrompt: finalSystemPrompt,
                UserPrompt: userMessage.ToString(),
                MaterialsContext: materialsContext,
                RawOutput: output,
                Timestamp: DateTimeOffset.UtcNow
            );

            return new ProverWorkerResult(
                Output: output,
                Verified: verified,
                HypothesisJudgments: hypothesisJudgments.Count > 0 ? hypothesisJudgments : null,
                PromptRecord: promptRecord
            );
        }
        catch (Exception ex)
        {
            var msg = $"[prover worker {workerIndex} ({role}) error] {ex.Message}\n\n";
            var promptRecord = new AgentPromptRecord(
                AgentName: $"verifier_prover_{workerIndex}_{role.Replace(" ", "_").ToLowerInvariant()}",
                SystemPrompt: finalSystemPrompt,
                UserPrompt: userMessage.ToString(),
                MaterialsContext: materialsContext,
                RawOutput: msg,
                Timestamp: DateTimeOffset.UtcNow
            );
            
            // On error, default to NOT VERIFIED (fail-safe)
            return new ProverWorkerResult(
                Output: msg,
                Verified: false,
                HypothesisJudgments: null,
                PromptRecord: promptRecord
            );
        }
    }

    private async Task<string?> RunProofExtractionAsync(
        VibeRoundContext ctx,
        SraDagSnapshot dag,
        string? reasonerOutput,
        ScoutPhaseResult scoutResult,
        ProverPhaseResult proverResult,
        string? providerName,
        CancellationToken ct)
    {
        var session = ctx.Session;
        var materialsContext = ctx.Materials.RenderedContext;
        var messageId = $"msg:{session.Id}:verifier:proof_extraction:{ctx.RunId}";

        // Build user message with verification results
        var userMessage = new StringBuilder();
        userMessage.AppendLine("Role: proof_extractor");
        userMessage.AppendLine($"Question: {ctx.Question}");
        userMessage.AppendLine();
        userMessage.AppendLine("## Verification Results Summary");
        userMessage.AppendLine();
        userMessage.AppendLine("### Scout Phase:");
        userMessage.AppendLine(scoutResult.Summary);
        userMessage.AppendLine();
        userMessage.AppendLine("### Prover Phase Summary:");
        userMessage.AppendLine(proverResult.Summary);
        userMessage.AppendLine();
        userMessage.AppendLine($"### Overall Result: PASSED (Prover: {proverResult.PassCount}/5)");
        userMessage.AppendLine();
        
        // Include FULL outputs from Prover workers (not just summary)
        userMessage.AppendLine("## Prover Workers' Full Outputs:");
        userMessage.AppendLine();
        var proverRoles = new[] { "Direct prover", "Algebraic manipulator", "Dependency minimalist", "Case-split specialist", "Proof auditor" };
        for (int i = 0; i < proverResult.WorkerResults.Count && i < proverRoles.Length; i++)
        {
            var worker = proverResult.WorkerResults[i];
            if (worker.Verified) // Only include verified workers' outputs
            {
                userMessage.AppendLine($"### Worker {i + 1} ({proverRoles[i]}) - Full Output:");
                userMessage.AppendLine(Bound(worker.Output, 4000)); // Include full output (up to 4000 chars per worker)
                userMessage.AppendLine();
            }
        }
        
        userMessage.AppendLine("## Task:");
        userMessage.AppendLine("Extract and synthesize proof information from the verification results above.");
        userMessage.AppendLine();
        userMessage.AppendLine("CRITICAL REQUIREMENTS:");
        userMessage.AppendLine("1. The proof MUST match the detail level of Prover workers' outputs - DO NOT oversimplify.");
        userMessage.AppendLine("2. Include ALL key verification methods, checks, and reasoning from Prover workers.");
        userMessage.AppendLine("3. Preserve technical depth: include mathematical statements, computational results, logical steps.");
        userMessage.AppendLine("4. If multiple workers verified, synthesize their approaches but preserve the detail level.");
        userMessage.AppendLine("5. The proof should be COMPREHENSIVE (1500-3000 characters), not concise.");
        userMessage.AppendLine("6. Structure the proof to show HOW verification was performed, not just THAT it passed.");
        userMessage.AppendLine("7. Include references to DAG facts, axioms, or theorems used in verification.");
        userMessage.AppendLine();
        userMessage.AppendLine("Output format:");
        userMessage.AppendLine("- Start with 'Proof:' marker");
        userMessage.AppendLine("- Provide detailed proof matching Prover workers' detail level");
        userMessage.AppendLine("- Include verification methods, checks, reasoning, and conclusions");

        if (!string.IsNullOrWhiteSpace(reasonerOutput))
        {
            userMessage.AppendLine();
            userMessage.AppendLine("## Original Reasoning:");
            userMessage.AppendLine(Bound(reasonerOutput, 2000));
        }

        var baseSystemPrompt = VibeVerifierAgent.GetProofExtractionSystemPrompt();
        var finalSystemPrompt = string.IsNullOrWhiteSpace(materialsContext)
            ? baseSystemPrompt
            : $"{baseSystemPrompt}\n\nMaterials context:\n{materialsContext.Trim()}\n";

        try
        {
            var (ver, verId) = await _core.Runtime.GetVerifierAgentAsync(session.Id, providerName, ct);
            var req = new ChatRequest
            {
                Message = userMessage.ToString(),
                RequestId = ctx.Input.RequestId ?? Guid.NewGuid().ToString("N"),
                StageHint = "session:vibe:verifier:proof_extraction"
            };
            req.Context["agent_id"] = verId;
            req.Context["materials_context"] = materialsContext;

            var sb = new StringBuilder(512);
            var supportsStreaming = await ver.SupportsStreamingAsync(ct);
            if (!supportsStreaming)
            {
                var resp = await ver.ChatAsync(req, ct);
                var text = resp.Content ?? string.Empty;
                sb.Append(text);
            }
            else
            {
                await foreach (var chunk in ver.ChatStreamAsync(req, ct))
                {
                    if (string.IsNullOrEmpty(chunk)) continue;
                    sb.Append(chunk);
                }
            }

            // Increased limit to accommodate detailed proof (was 1500, now 3500)
            var output = Bound(sb.ToString(), 3_500);
            
            // Extract proof from output (look for "Proof:" or similar markers)
            var proof = ExtractProofFromOutput(output);
            
            // If extraction found proof, ensure it's not too short (should match Prover detail level)
            if (!string.IsNullOrWhiteSpace(proof) && proof.Length < 200)
            {
                // If extracted proof is too short, use the full output (may contain proof without explicit marker)
                _host.Logger.LogDebug("[ProofExtraction] Extracted proof too short ({Length} chars), using full output", proof.Length);
                proof = output.Trim();
            }
            
            return string.IsNullOrWhiteSpace(proof) ? null : proof;
        }
        catch (Exception)
        {
            // On error, return null (no proof)
            return null;
        }
    }

    private static string? ExtractProofFromOutput(string output)
    {
        if (string.IsNullOrWhiteSpace(output))
            return null;

        // Try to extract proof section
        var proofMarkers = new[] { "Proof:", "proof:", "Proof Summary:", "proof summary:" };
        foreach (var marker in proofMarkers)
        {
            var index = output.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (index >= 0)
            {
                var proofStart = index + marker.Length;
                var proofText = output.Substring(proofStart).Trim();
                // Remove any trailing markers or empty sections
                var endMarkers = new[] { "\n\n##", "\n\n###", "\n---" };
                foreach (var endMarker in endMarkers)
                {
                    var endIndex = proofText.IndexOf(endMarker, StringComparison.OrdinalIgnoreCase);
                    if (endIndex > 0)
                    {
                        proofText = proofText.Substring(0, endIndex).Trim();
                    }
                }
                if (!string.IsNullOrWhiteSpace(proofText))
                    return proofText.Trim();
            }
        }

        // If no explicit proof marker, return the output itself (trimmed)
        var trimmed = output.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }

    private async Task<string> RunDagBuilderAsync(
        VibeRoundContext ctx,
        SraDagSnapshot dag,
        IReadOnlyDictionary<string, string> outputs,
        IReadOnlyList<LibrarianAxiomCandidate> librarianAxioms,
        string? providerName,
        CancellationToken ct)
    {
        var session = ctx.Session;
        session.Events.Publish(new StepStartedEvent { Timestamp = NowMs(), StepName = "vibe.dag_builder" });
        EmitAgentStatusReport(session, "dag_builder", AgentStatusMessages.DagBuilderStart);
        var messageId = $"msg:{session.Id}:dag_builder:{ctx.RunId}";
        StartAgentMessage(session, messageId, agent: "dag_builder", stepName: "vibe.dag_builder", providerName: providerName);

        try
        {
            var (db, dbId) = await _core.Runtime.GetDagBuilderAgentAsync(session.Id, providerName, ct);
            var req = new ChatRequest
            {
                Message = BuildDagBuilderMessage(ctx.Question, dag, outputs, librarianAxioms, ctx.Input.AttachmentPaths),
                RequestId = ctx.Input.RequestId ?? Guid.NewGuid().ToString("N"),
                StageHint = "session:vibe:dag_builder"
            };
            req.Context["agent_id"] = dbId;
            req.Context["materials_context"] = ctx.Materials.RenderedContext;

            var sb = new StringBuilder(4096);
            var supportsStreaming = await db.SupportsStreamingAsync(ct);
            if (!supportsStreaming)
            {
                var resp = await db.ChatAsync(req, ct);
                var text = (resp.Content ?? string.Empty).Trim();
                sb.Append(text);

                // Render as fenced json to UI (even if it's invalid; user can see it).
                if (text.Length > 0)
                {
                    EmitAgentDelta(session, messageId, "assistant", "```json\n");
                    EmitAgentDelta(session, messageId, "assistant", Bound(text, 20_000) + "\n");
                    EmitAgentDelta(session, messageId, "assistant", "```\n\n");
                }
            }
            else
            {
                // Stream inside fenced block for a better "always streaming" UX.
                EmitAgentDelta(session, messageId, "assistant", "```json\n");

                await foreach (var chunk in db.ChatStreamAsync(req, ct))
                {
                    if (string.IsNullOrEmpty(chunk)) continue;
                    sb.Append(chunk);
                    EmitAgentDelta(session, messageId, "assistant", chunk);

                    // Keep bounded (same as return contract).
                    if (sb.Length > 30_000)
                        break;
                }

                EmitAgentDelta(session, messageId, "assistant", "\n```\n\n");
            }

            session.Events.Publish(new StepFinishedEvent { Timestamp = NowMs(), StepName = "vibe.dag_builder" });
            return Bound(sb.ToString().Trim(), 30_000);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            var msg = $"[dag_builder error] {ex.Message}\n\n";
            EmitAgentDelta(session, messageId, "assistant", msg);
            session.Events.Publish(new StepFinishedEvent { Timestamp = NowMs(), StepName = "vibe.dag_builder" });
            return msg;
        }
        finally
        {
            EndAgentMessage(session, messageId);
        }
    }

    private async Task<string> RunPaperEditorAsync(
        VibeRoundContext ctx,
        DagRoundResult dagResult,
        IReadOnlyDictionary<string, string> outputs,
        string? providerName,
        CancellationToken ct)
    {
        var session = ctx.Session;
        session.Events.Publish(new StepStartedEvent { Timestamp = NowMs(), StepName = "vibe.paper_editor" });
        EmitAgentStatusReport(session, "paper_editor", AgentStatusMessages.PaperEditorStart);
        var messageId = $"msg:{session.Id}:paper_editor:{ctx.RunId}";
        StartAgentMessage(session, messageId, agent: "paper_editor", stepName: "vibe.paper_editor", providerName: providerName);

        try
        {
            // Ensure paper scaffold exists; we will include bounded excerpts for context.
            var ws = await _core.Paper.EnsurePaperFilesAsync(session.Id, ct);
            var outline = await SafeReadTextAsync(ws.PaperOutlinePath, maxChars: 8000, ct);
            var draft = await SafeReadTextAsync(ws.PaperDraftPath, maxChars: 12_000, ct);

            var (pe, peId) = await _core.Runtime.GetPaperEditorAgentAsync(session.Id, providerName, ct);
            var req = new ChatRequest
            {
                Message = BuildPaperEditorMessage(ctx.Question, dagResult, outputs, outline, draft, ctx.Input.AttachmentPaths),
                RequestId = ctx.Input.RequestId ?? Guid.NewGuid().ToString("N"),
                StageHint = "session:vibe:paper_editor"
            };
            req.Context["agent_id"] = peId;
            req.Context["materials_context"] = ctx.Materials.RenderedContext;

            var sb = new StringBuilder(4096);
            var supportsStreaming = await pe.SupportsStreamingAsync(ct);
            if (!supportsStreaming)
            {
                var resp = await pe.ChatAsync(req, ct);
                var text = (resp.Content ?? string.Empty).Trim();
                sb.Append(text);
                if (text.Length > 0)
                    EmitAgentDelta(session, messageId, "assistant", Bound(text, 30_000) + "\n\n");
            }
            else
            {
                await foreach (var chunk in pe.ChatStreamAsync(req, ct))
                {
                    if (string.IsNullOrEmpty(chunk)) continue;
                    sb.Append(chunk);
                    EmitAgentDelta(session, messageId, "assistant", chunk);
                    if (sb.Length > 40_000) break;
                }
                EmitAgentDelta(session, messageId, "assistant", "\n\n");
            }

            session.Events.Publish(new StepFinishedEvent { Timestamp = NowMs(), StepName = "vibe.paper_editor" });
            return Bound(sb.ToString().Trim(), 40_000);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            var msg = $"[paper_editor error] {ex.Message}\n\n";
            EmitAgentDelta(session, messageId, "assistant", msg);
            session.Events.Publish(new StepFinishedEvent { Timestamp = NowMs(), StepName = "vibe.paper_editor" });
            return msg;
        }
        finally
        {
            EndAgentMessage(session, messageId);
        }
    }

}
