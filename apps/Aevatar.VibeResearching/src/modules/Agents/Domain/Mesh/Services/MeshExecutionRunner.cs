using Microsoft.Extensions.Logging;
using System.Text;
using System.Text.Json;
using Aevatar.Agents.AGUI;
using Aevatar.Agents.AI;
using Aevatar.Agents.AI.Core;
using Google.Protobuf.WellKnownTypes;
using Aevatar.VibeResearching.Infrastructure;
using Aevatar.VibeResearching.Sessions;
using Aevatar.VibeResearching.Sessions.Services;
using Aevatar.VibeResearching.Agents.Contracts.Collab;
using Aevatar.VibeResearching.Agents.Orchestration;
using Aevatar.VibeResearching.Agents.Mesh.ValueObjects;

namespace Aevatar.VibeResearching.Agents.Mesh.Services;

// ============================================================
//  MeshExecutionRunner
//
//  Purpose:
//  - Execute MeshExecutionPlan nodes in topo order using existing SRA agents.
//  - Route bounded outputs according to channel semantics.
//  - Emit per-node AG-UI events + per-agent messages (snapshot-first).
//
//  Notes:
//  - The runner is best-effort by default: it captures errors per node and continues.
// ============================================================

internal sealed class MeshExecutionRunner : IMeshExecutionRunner
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    private readonly ResearchRuntime _runtime;
    private readonly ILogger<MeshExecutionRunner> _logger;

    public MeshExecutionRunner(ResearchRuntime runtime, ILogger<MeshExecutionRunner> logger)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<MeshRunResult> ExecuteAsync(
        ResearchSession session,
        SessionInputInDto input,
        MaterialsSnapshot materials,
        SraDagSnapshot dag,
        MeshExecutionPlan plan,
        string question,
        Func<string, string?> resolveProviderName,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(materials);
        ArgumentNullException.ThrowIfNull(dag);
        ArgumentNullException.ThrowIfNull(plan);
        resolveProviderName ??= _ => null;

        var outputs = new Dictionary<string, string>(StringComparer.Ordinal);
        var errors = new List<string>();

        // Notify mesh start (best-effort).
        session.Events.Publish(new CustomEvent
        {
            Timestamp = NowMs(),
            Name = "aevatar.vibe.mesh_started",
            Value = new
            {
                sessionId = session.Id,
                runId = plan.RunId,
                dslVersion = plan.DslVersion,
                nodes = plan.Nodes.Count,
                edges = plan.Nodes.Sum(n => n.Inbound.Count)
            }
        });

        // Create aggregated research_assistant message for main chat display
        var raMessageId = $"msg:{session.Id}:research_assistant:{plan.RunId}";
        var raProviderName = resolveProviderName("research_assistant");
        StartAgentMessage(session, raMessageId, agent: "research_assistant", stepName: "vibe.mesh", providerName: raProviderName);

        var plannerNodeId = plan.Nodes.FirstOrDefault(n => string.Equals((n.Type ?? "").Trim(), "planner", StringComparison.OrdinalIgnoreCase))?.Id;

        foreach (var nodeId in plan.TopoOrder)
        {
            ct.ThrowIfCancellationRequested();

            var node = plan.Nodes.FirstOrDefault(n => string.Equals(n.Id, nodeId, StringComparison.Ordinal));
            if (node == null)
            {
                errors.Add($"missing node in plan: {nodeId}");
                continue;
            }

            var role = (node.Type ?? string.Empty).Trim().ToLowerInvariant();
            var providerName = resolveProviderName(role);

            // Use vibe.{role} format to match VibeOrchestrator.Workers pattern for frontend recognition
            var stepName = $"vibe.{role}";
            var messageId = $"msg:{session.Id}:{role}:{plan.RunId}";

            // Step + message start
            session.Events.Publish(new StepStartedEvent { Timestamp = NowMs(), StepName = stepName });
            EmitAgentStatusReport(session, role, GetAgentStartMessage(role));
            StartAgentMessage(session, messageId, agent: role, stepName: stepName, providerName: providerName);
            session.Events.Publish(new CustomEvent
            {
                Timestamp = NowMs(),
                Name = "aevatar.vibe.mesh_node_started",
                Value = new { sessionId = session.Id, runId = plan.RunId, nodeId = node.Id, nodeType = role }
            });

            try
            {
                // Best-effort: refresh tools snapshot before nodes that may need tool calling.
                if (role is "reasoner" or "verifier" or "dag_builder")
                    _ = await _runtime.RefreshToolsSnapshotAsync(session.Id, providerName, ct);

                var inputText = BuildNodeInputText(node, question, materials, dag, outputs, plannerNodeId);
                var req = new ChatRequest
                {
                    Message = BuildNodeMessage(role, question, inputText, input.AttachmentPaths),
                    RequestId = input.RequestId ?? Guid.NewGuid().ToString("N"),
                    StageHint = $"session:vibe:mesh:{role}"
                };

                // Route agent_id + materials into context (materials injection happens in VibeAgentBase.BuildLLMRequest).
                req.Context["materials_context"] = materials.RenderedContext;

                string text;

                switch (role)
                {
                    case "planner":
                    {
                        var (agent, agentId) = await _runtime.GetPlannerAgentAsync(session.Id, providerName, ct);
                        req.Context["agent_id"] = agentId;
                        text = await RunAgentAsync(agent, req, session, messageId, raMessageId, role, maxChars: 20_000, fencedJson: false, ct);
                        break;
                    }
                    case "reasoner":
                    {
                        var (agent, agentId) = await _runtime.GetReasonerAgentAsync(session.Id, providerName, ct);
                        req.Context["agent_id"] = agentId;
                        text = await RunAgentAsync(agent, req, session, messageId, raMessageId, role, maxChars: 40_000, fencedJson: false, ct);
                        break;
                    }
                    case "librarian":
                    {
                        var (agent, agentId) = await _runtime.GetLibrarianAgentAsync(session.Id, providerName, ct);
                        req.Context["agent_id"] = agentId;
                        text = await RunAgentAsync(agent, req, session, messageId, raMessageId, role, maxChars: 20_000, fencedJson: false, ct);
                        break;
                    }
                    case "verifier":
                    {
                        var (agent, agentId) = await _runtime.GetVerifierAgentAsync(session.Id, providerName, ct);
                        req.Context["agent_id"] = agentId;
                        text = await RunAgentAsync(agent, req, session, messageId, raMessageId, role, maxChars: 20_000, fencedJson: false, ct);
                        break;
                    }
                    case "dag_builder":
                    {
                        var (agent, agentId) = await _runtime.GetDagBuilderAgentAsync(session.Id, providerName, ct);
                        req.Context["agent_id"] = agentId;
                        text = await RunAgentAsync(agent, req, session, messageId, raMessageId, role, maxChars: 30_000, fencedJson: true, ct);
                        break;
                    }
                    default:
                    {
                        // Dynamic role path (role defined by ~/.aevatar/agents/{role}.yaml).
                        var (agent, agentId) = await _runtime.GetRoleAgentAsync(session.Id, providerName, role, ct);
                        req.Context["agent_id"] = agentId;
                        text = await RunAgentAsync(agent, req, session, messageId, raMessageId, role, maxChars: 20_000, fencedJson: false, ct);
                        break;
                    }
                }

                outputs[node.Id] = text;

                session.Events.Publish(new CustomEvent
                {
                    Timestamp = NowMs(),
                    Name = "aevatar.vibe.mesh_node_finished",
                    Value = new { sessionId = session.Id, runId = plan.RunId, nodeId = node.Id, ok = true }
                });
            }
            catch (Exception ex)
            {
                var msg = $"[mesh node error] node={node.Id} type={role} error={ex.Message}\n\n";
                errors.Add(msg.Trim());
                EmitAgentDelta(session, messageId, "assistant", msg);

                session.Events.Publish(new CustomEvent
                {
                    Timestamp = NowMs(),
                    Name = "aevatar.vibe.mesh_node_finished",
                    Value = new { sessionId = session.Id, runId = plan.RunId, nodeId = node.Id, ok = false, error = ex.Message }
                });
            }
            finally
            {
                session.Events.Publish(new StepFinishedEvent { Timestamp = NowMs(), StepName = stepName });
                EndAgentMessage(session, messageId);
            }
        }

        session.Events.Publish(new CustomEvent
        {
            Timestamp = NowMs(),
            Name = "aevatar.vibe.mesh_finished",
            Value = new { sessionId = session.Id, runId = plan.RunId, ok = errors.Count == 0, nodes = plan.Nodes.Count }
        });

        // End the aggregated research_assistant message
        EndAgentMessage(session, raMessageId);

        return new MeshRunResult(errors.Count == 0, outputs, errors);
    }

    private static async Task<string> RunAgentAsync(
        AIGAgentBase agent,
        ChatRequest req,
        ResearchSession session,
        string messageId,
        string raMessageId,
        string role,
        int maxChars,
        bool fencedJson,
        CancellationToken ct)
    {
        var sb = new StringBuilder(capacity: Math.Min(maxChars, 4096));
        var supportsStreaming = await agent.SupportsStreamingAsync(ct);

        // Emit header to research_assistant for main chat display
        var roleHeader = $"\n### [{role.ToUpperInvariant()}]\n";
        EmitAgentDelta(session, raMessageId, "assistant", roleHeader);

        if (fencedJson)
        {
            EmitAgentDelta(session, messageId, "assistant", "```json\n");
            EmitAgentDelta(session, raMessageId, "assistant", "```json\n");
        }

        if (!supportsStreaming)
        {
            Console.WriteLine($"[MeshRunner] NON-STREAMING mode for {messageId}");
            var resp = await agent.ChatAsync(req, ct);
            var text = (resp.Content ?? string.Empty);
            if (fencedJson) text = text.Trim();
            sb.Append(text);
            if (text.Length > 0)
            {
                var bounded = Bound(text, maxChars) + (fencedJson ? "\n" : "");
                EmitAgentDelta(session, messageId, "assistant", bounded);
                EmitAgentDelta(session, raMessageId, "assistant", bounded);
            }
            Console.WriteLine($"[MeshRunner] Non-streaming response: {text.Length} chars");
        }
        else
        {
            var chunkCount = 0;
            await foreach (var chunk in agent.ChatStreamAsync(req, ct))
            {
                if (string.IsNullOrEmpty(chunk)) continue;
                chunkCount++;
                sb.Append(chunk);
                // Send to both agent-specific message and aggregated research_assistant
                EmitAgentDelta(session, messageId, "assistant", chunk);
                EmitAgentDelta(session, raMessageId, "assistant", chunk);
                if (sb.Length >= maxChars)
                    break;
            }
            // Log streaming stats for debugging
            Console.WriteLine($"[MeshRunner] Streamed {chunkCount} chunks, total {sb.Length} chars for {messageId}");
        }

        if (fencedJson)
        {
            EmitAgentDelta(session, messageId, "assistant", "\n```\n\n");
            EmitAgentDelta(session, raMessageId, "assistant", "\n```\n\n");
        }
        else
        {
            EmitAgentDelta(session, messageId, "assistant", "\n\n");
            EmitAgentDelta(session, raMessageId, "assistant", "\n\n");
        }

        var result = sb.ToString();
        result = fencedJson ? result.Trim() : result;
        return Bound(result, maxChars);
    }

    private static string BuildNodeInputText(
        MeshPlanNode node,
        string question,
        MaterialsSnapshot materials,
        SraDagSnapshot dag,
        IReadOnlyDictionary<string, string> outputs,
        string? plannerNodeId)
    {
        // Build a bounded "Inputs" section based on inbound bindings.
        var sb = new StringBuilder(2048);

        foreach (var b in node.Inbound)
        {
            var ch = (b.Channel ?? string.Empty).Trim().ToLowerInvariant();
            switch (ch)
            {
                case var _ when string.Equals(ch, SraMeshMappings.ChannelQuestion, StringComparison.OrdinalIgnoreCase):
                    sb.AppendLine("### Input: question");
                    sb.AppendLine(Bound(question, 2500));
                    sb.AppendLine();
                    break;

                case var _ when string.Equals(ch, SraMeshMappings.ChannelMaterials, StringComparison.OrdinalIgnoreCase):
                    // Materials are already injected via system prompt; keep this as a hint only.
                    sb.AppendLine("### Input: materials");
                    sb.AppendLine("(Provided via system prompt: materials_context)");
                    sb.AppendLine();
                    break;

                case var _ when string.Equals(ch, SraMeshMappings.ChannelDagSnapshot, StringComparison.OrdinalIgnoreCase):
                    sb.AppendLine("### Input: dag_snapshot");
                    sb.AppendLine(RenderDagSummary(dag));
                    sb.AppendLine();
                    break;

                case var _ when string.Equals(ch, SraMeshMappings.ChannelUpstreamOutput, StringComparison.OrdinalIgnoreCase):
                    sb.AppendLine($"### Input: upstream_output (from {b.FromNodeId})");
                    if (outputs.TryGetValue(b.FromNodeId, out var up))
                        sb.AppendLine(Bound(up, 3500));
                    else
                        sb.AppendLine("(missing)");
                    sb.AppendLine();
                    break;

                case var _ when string.Equals(ch, SraMeshMappings.ChannelPlannerOutput, StringComparison.OrdinalIgnoreCase):
                    sb.AppendLine("### Input: planner_output");
                    if (!string.IsNullOrWhiteSpace(plannerNodeId) && outputs.TryGetValue(plannerNodeId!, out var po))
                        sb.AppendLine(Bound(po, 3500));
                    else
                        sb.AppendLine("(missing)");
                    sb.AppendLine();
                    break;
            }
        }

        return Bound(sb.ToString().Trim(), 12_000);
    }

    private static string BuildNodeMessage(string role, string question, string inputText, IReadOnlyList<string>? attachments)
    {
        role = (role ?? string.Empty).Trim();
        question = (question ?? string.Empty).Replace("\r", "").Trim();
        inputText = (inputText ?? string.Empty).Replace("\r", "").Trim();

        var sb = new StringBuilder(4096);
        sb.AppendLine($"You are the '{role}' role in a mesh-driven SRA workflow.");
        sb.AppendLine();
        sb.AppendLine("User question:");
        sb.AppendLine(question.Length == 0 ? "(empty)" : question);
        sb.AppendLine();

        if (inputText.Length > 0)
        {
            sb.AppendLine("Inputs:");
            sb.AppendLine(inputText);
            sb.AppendLine();
        }

        if (attachments is { Count: > 0 })
        {
            sb.AppendLine("Attachments (paths):");
            foreach (var p in attachments.Take(12))
                sb.AppendLine($"- {p}");
            sb.AppendLine();
        }

        sb.AppendLine("Rules:");
        sb.AppendLine("- Keep output bounded and actionable.");
        sb.AppendLine("- If you rely on materials, cite [material:...] ids when available in the system prompt context.");

        return sb.ToString().Trim();
    }

    private static string RenderDagSummary(SraDagSnapshot dag)
    {
        try
        {
            var payload = new
            {
                nodeCount = dag.Nodes.Count,
                edgeCount = dag.Edges.Count,
                nodes = dag.Nodes
                    .Where(n => n != null)
                    .Take(12)
                    .Select(n => new
                    {
                        id = n.Id,
                        type = n.Type.ToString(),
                        kind = n.Kind.ToString(),
                        label = Bound(n.Label ?? "", 160)
                    })
                    .ToList()
            };
            return Bound(JsonSerializer.Serialize(payload, Json), 4000);
        }
        catch
        {
            return $"nodeCount={dag.Nodes.Count} edgeCount={dag.Edges.Count}";
        }
    }

    // ------------------------------------------------------------
    //  AG-UI message helpers (copied from VibeOrchestrator.Workers patterns)
    // ------------------------------------------------------------

    private static void StartAgentMessage(ResearchSession session, string messageId, string agent, string stepName, string? providerName)
    {
        session.SetMessage(messageId, role: "assistant", content: string.Empty);

        session.Events.Publish(new TextMessageStartEvent
        {
            Timestamp = NowMs(),
            MessageId = messageId,
            Role = "assistant"
        });

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

    private static long NowMs() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    private static string Bound(string s, int max)
    {
        s = (s ?? string.Empty).Replace("\r", "").Trim();
        if (s.Length <= max) return s;
        return s[..max];
    }

    // ------------------------------------------------------------
    //  Agent Status Reporting (matches VibeOrchestrator.Workers pattern)
    // ------------------------------------------------------------

    private static void EmitAgentStatusReport(
        ResearchSession session,
        string agentName,
        string statusText,
        double? progress = null)
    {
        session.Events.Publish(new CustomEvent
        {
            Timestamp = NowMs(),
            Name = "aevatar.vibe.agent_status_report",
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

    private static string GetAgentStartMessage(string role) => role switch
    {
        "planner" => "正在分析研究问题，制定研究计划...",
        "reasoner" => "正在进行深度推理分析...",
        "librarian" => "正在搜索相关文献和参考资料...",
        "verifier" => "正在验证推理步骤的正确性...",
        "dag_builder" => "正在构建知识图谱节点...",
        "paper_editor" => "正在更新论文草稿...",
        _ => $"正在执行 {role} 任务..."
    };
}


