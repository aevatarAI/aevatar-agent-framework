using System.Text;
using System.Text.Json;
using Aevatar.Agents.AGUI;
using Aevatar.Agents.AI;
using Aevatar.Agents.Core.Secrets;
using Google.Protobuf.Collections;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Aevatar.VibeResearching.Agents;
using Aevatar.VibeResearching.Infrastructure;
using Aevatar.VibeResearching.Sessions;
using Aevatar.VibeResearching.Sessions.Services;
using Aevatar.VibeResearching.Sessions.ValueObjects;
using Aevatar.VibeResearching.Agents.Mesh;
using Aevatar.VibeResearching.Agents.Contracts.Collab;
using Aevatar.VibeResearching.Agents.Pivot;
using Aevatar.VibeResearching.Agents.Orchestration;

namespace Aevatar.VibeResearching.Agents;

// ============================================================
//  VibeOrchestrator (MVP)
//
//  Goal:
//  - Run one vibe round end-to-end:
//      research_assistant plan → workers → maker consensus → DAG + Trace
//
//  Notes:
//  - Best-effort: never crash server due to orchestration/projection.
//  - Streaming: caller provides emitAssistantDelta() that projects to AG-UI.
// ============================================================

public sealed partial class VibeOrchestrator
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    private readonly VibeCore _core;
    private readonly VibePivot _pivot;
    private readonly VibeMesh _mesh;
    private readonly VibeHost _host;

    internal ResearchRuntime Runtime => _core.Runtime;

    internal VibeOrchestrator(VibeCore core, VibePivot pivot, VibeMesh mesh, VibeHost host)
    {
        _core = core ?? throw new ArgumentNullException(nameof(core));
        _pivot = pivot ?? throw new ArgumentNullException(nameof(pivot));
        _mesh = mesh ?? throw new ArgumentNullException(nameof(mesh));
        _host = host ?? throw new ArgumentNullException(nameof(host));
    }

    public async Task ExecuteOneRoundAsync(
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

        var ctx = new VibeRoundContext(
            session,
            runId,
            input,
            question,
            materials,
            emitAssistantDelta);

        // ------------------------------------------------------------
        // Shared DAG binding (cross-session)
        // ------------------------------------------------------------
        var dagId = session.EffectiveDagId;

        // Load File-SSoT context (best-effort).
        var dagSnap = await _core.Dag.LoadSnapshotAsync(dagId, ct);
        var recentTrace = await _core.Trace.LoadLatestAsync(session.Id, max: 5, ct);

        // ------------------------------------------------------------
        // Direction change detection (US-1 pivot intent detection)
        //
        // 中文说明：
        // - 在每轮开始时检测用户是否想要改变研究方向
        // - 检测是非阻塞的，不会延迟响应
        // - 如果检测到高置信度的方向变更，会在后续阶段触发 pivot 工作流
        // ------------------------------------------------------------
        await RunPivotDetectionAsync(ctx, ct);

        // ------------------------------------------------------------
        // Per-agent LLM provider mapping (File-SSoT)
        //
        // Rules:
        // - providerOverride (request-level) wins for all agents
        // - else use agent_providers.json mapping
        // - else fallback to session.ProviderName
        // - runtime resolves a final default if still empty/invalid
        // ------------------------------------------------------------
        AgentProvidersSnapshot? agentProvidersSnap = null;
        try { agentProvidersSnap = await _core.AgentProviders.LoadAsync(session.Id, ct); } catch { /* best-effort */ }
        var agentProviderMap = agentProvidersSnap?.Map ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        string? ResolveProvider(string agent)
        {
            if (!string.IsNullOrWhiteSpace(providerOverride))
                return providerOverride;

            // Global role config (shared across Aevatar apps):
            // ~/.aevatar/agents/{role}.yaml can specify provider per role.
            // This gives Mesh DSL a stable "role -> provider" mapping without per-app wiring.
            var yamlProvider = _mesh.Roles.TryGetProvider(agent);
            if (!string.IsNullOrWhiteSpace(yamlProvider))
                return yamlProvider;

            if (agentProviderMap.TryGetValue(agent, out var p) && !string.IsNullOrWhiteSpace(p))
                return p;
            return session.ProviderName;
        }

        var raProvider = ResolveProvider("research_assistant");

        // ------------------------------------------------------------
        // Pause gate: if provider is missing apiKey, wait for user to configure then continue.
        //
        // 中文说明：
        // - 只在真正要调用 LLM 之前做 gate（materials/dag/trace 都不需要 key）。
        // - 这里先 gate research_assistant（BRIEF/PLAN/SUMMARY 都依赖它）。
        // ------------------------------------------------------------
        raProvider = await EnsureProviderRunnableOrPauseAsync(
            session,
            runId,
            agent: "research_assistant",
            stepName: "vibe",
            resolveProvider: () => ResolveProvider("research_assistant"),
            emitAssistantDelta: emitAssistantDelta,
            ct: ct);

        // Attach metadata to the main assistant message so UI can show provider per agent card.
        // (Message id scheme matches ResearchRunExecutor: msg:{sessionId}:assistant:{runId})
        try
        {
            session.Events.Publish(new CustomEvent
            {
                Timestamp = NowMs(),
                Name = "aevatar.vibe.message_meta",
                Value = new
                {
                    messageId = $"msg:{session.Id}:assistant:{runId}",
                    agent = "research_assistant",
                    stepName = "vibe",
                    providerName = (raProvider ?? string.Empty).Trim()
                }
            });
        }
        catch
        {
            // best-effort only
        }

        // Librarian side-effects (DAG facts/axioms) collected during this round.
        var librarianAxioms = new List<LibrarianAxiomCandidate>();
        var factsWritten = new List<string>();

        // ------------------------------------------------------------
        // Step: Research Brief (1 page) - generate once if missing
        // ------------------------------------------------------------
        await RunStepBestEffortAsync(session, "vibe.brief", async innerCt =>
        {
            var existing = await _core.Brief.LoadAsync(session.Id, innerCt);
            if (existing.Version > 0)
                return;

            var brief = await TryGetBriefAsync(session.Id, input, question, materials, dagSnap, recentTrace, raProvider, innerCt);
            if (brief == null)
                return;

            // First brief for a session: start at version=1.
            brief.Version = 1;
            var saved = await _core.Brief.SaveAsync(session.Id, brief, innerCt);

            // UI: notify brief updated (best-effort)
            session.Events.Publish(new CustomEvent
            {
                Timestamp = NowMs(),
                Name = "aevatar.vibe.brief_updated",
                Value = new { sessionId = session.Id, version = saved.Version }
            });

            // Also surface a human-friendly excerpt into the chat stream.
            EmitSection(ctx.EmitAssistantDelta, "### Research Brief (1 page)\n");
            ctx.EmitAssistantDelta(RenderBriefMarkdown(saved));
            ctx.EmitAssistantDelta("\n\n");

            // ------------------------------------------------------------
            // Persist brief milestones into DAG (as multiple "plan" nodes)
            //
            // 中文说明：
            // - 以前 goals 是一次产出多个条目；现在用 brief.milestones 作为“多条 plan”
            // - 这些 plan 节点用于全局 roadmap（并不会污染 knowledge grounding）
            // ------------------------------------------------------------
            try
            {
                        var mm = BuildMilestonesPlanDagMutation(session.Id, runId, question, saved);
                if (mm != null)
                {
                    dagSnap = await _core.Dag.ApplyMutationAsync(dagId, mm, innerCt);
                    session.Events.Publish(new CustomEvent
                    {
                        Timestamp = NowMs(),
                        Name = "aevatar.vibe.milestones_plan_dag_written",
                        Value = new { sessionId = session.Id, dagId, runId, mutationId = mm.MutationId, milestones = saved.Milestones.Count }
                    });
                }
            }
            catch
            {
                // best-effort only
            }
        }, ct);

        var (plan, dagAfterPlan) = await RunPlanPhaseAsync(
            ctx,
            dagId,
            dagSnap,
            recentTrace,
            raProvider,
            ct);
        dagSnap = dagAfterPlan;

        var (outputs, _) = await RunWorkerPhaseAsync(
            ctx,
            dagId,
            dagSnap,
            plan,
            ResolveProvider,
            librarianAxioms,
            factsWritten,
            ct);

        // ------------------------------------------------------------
        // Step: DAG apply (no consensus)
        // ------------------------------------------------------------
        session.Events.Publish(new StepStartedEvent
        {
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            StepName = "vibe.dag_consensus"
        });

        // Refresh again before apply (shared DAG).
        dagSnap = await _core.Dag.LoadSnapshotAsync(dagId, ct);
        var consensusProvider = ResolveProvider("dag_consensus");
        var dagResult = await RunDagApplyAsync(
            session,
            runId,
            materials,
            dagSnap,
            outputs,
            emitAssistantDelta,
            consensusProvider,
            ct);

        session.Events.Publish(new StepFinishedEvent
        {
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            StepName = "vibe.dag_consensus"
        });

        // ------------------------------------------------------------
        // Step: Delivery center update (paper + lists) via paper_editor
        // ------------------------------------------------------------
        await RunStepBestEffortAsync(session, "vibe.delivery", async innerCt =>
        {
            // Only run paper_editor when DAG consensus accepted something (MVP).
            // Future: also update lists even when no DAG change occurred.
            if (!dagResult.Accepted || dagResult.AcceptedMutation == null)
                return;

            var paperEditorProvider = await EnsureProviderRunnableOrPauseAsync(
                session,
                runId,
                agent: "paper_editor",
                stepName: "vibe.paper_editor",
                resolveProvider: () => ResolveProvider("paper_editor"),
                emitAssistantDelta: ctx.EmitAssistantDelta,
                ct: innerCt);

            var paperEditorOut = await RunPaperEditorAsync(
                ctx,
                dagResult,
                outputs,
                paperEditorProvider,
                innerCt);

            outputs["paper_editor"] = paperEditorOut;

            var deliveryUpdate = await TryApplyPaperEditorOutputAsync(session, runId, paperEditorOut, innerCt);
            if (deliveryUpdate == null)
                return;

            EmitSection(ctx.EmitAssistantDelta, "### Delivery Center Updated\n");
            ctx.EmitAssistantDelta($"- paperPatchesApplied={deliveryUpdate.PatchesApplied}, listsWritten={deliveryUpdate.ListsWritten}\n");
            if (!string.IsNullOrWhiteSpace(deliveryUpdate.ChangedSummary))
                ctx.EmitAssistantDelta($"- summary: {Bound(deliveryUpdate.ChangedSummary!, 500)}\n");
            ctx.EmitAssistantDelta("\n");
        }, ct);

        // ------------------------------------------------------------
        // Step: research_assistant summary → TraceStore
        // ------------------------------------------------------------
        session.Events.Publish(new StepStartedEvent
        {
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            StepName = "vibe.summary"
        });

        var summaryMd = await TryGetSummaryAsync(session.Id, input, question, dagResult, outputs, factsWritten, raProvider, ct);
        if (!string.IsNullOrWhiteSpace(summaryMd))
        {
            EmitSection(ctx.EmitAssistantDelta, "### Round Summary\n");
            ctx.EmitAssistantDelta(summaryMd!.Trim() + "\n\n");
        }

        await PersistTraceAsync(session, runId, input, question, outputs, dagResult, summaryMd, ct);

        session.Events.Publish(new StepFinishedEvent
        {
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            StepName = "vibe.summary"
        });
    }

}
