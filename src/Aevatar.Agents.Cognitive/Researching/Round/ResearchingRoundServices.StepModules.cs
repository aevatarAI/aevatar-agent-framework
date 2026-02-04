using Aevatar.Agents.AGUI;
using Aevatar.Agents.Cognitive.Execution.Run;
using Aevatar.Agents.Cognitive.Primitives;
using Aevatar.Agents.Cognitive.Researching.Materials;
using Aevatar.Agents.Cognitive.Researching.Sessions;
using Aevatar.Agents.Cognitive.Researching.Dag;
using Aevatar.Agents.Cognitive.Researching.Trace;
using Aevatar.Agents.Cognitive.Researching.Workflow;
using Aevatar.Agents.Cognitive.Researching.Workspace;

namespace Aevatar.Agents.Cognitive.Researching.Round;

public sealed partial class ResearchingRoundServices
{
    private const string ProviderResolverKey = "vibe.provider_resolver";

    internal ResearchingRoundContext BuildRoundContext(WorkflowRunContext context, MaterialsSnapshot materials)
        => new(
            context.GetSession(),
            context.RunId,
            context.GetInput(),
            context.GetQuestion(),
            materials,
            context.EmitAssistantDelta);

    internal async Task<Func<string, string?>> GetProviderResolverAsync(
        WorkflowRunContext context,
        string? providerOverride,
        CancellationToken ct)
    {
        if (context.Items.TryGetValue(ProviderResolverKey, out var cached) &&
            cached is Func<string, string?> resolver)
        {
            return resolver;
        }

        var session = context.GetSession();
        AgentProvidersStore.AgentProvidersSnapshot? agentProvidersSnap = null;
        try { agentProvidersSnap = await _core.AgentProviders.LoadAsync(session.Id, ct); } catch { /* best-effort */ }
        var agentProviderMap = agentProvidersSnap?.Map ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        string? Resolve(string agent)
        {
            if (!string.IsNullOrWhiteSpace(providerOverride))
                return providerOverride;

            var yamlProvider = _mesh.Roles.TryGetProvider(agent);
            if (!string.IsNullOrWhiteSpace(yamlProvider))
                return yamlProvider;

            if (agentProviderMap.TryGetValue(agent, out var p) && !string.IsNullOrWhiteSpace(p))
                return p;

            return session.ProviderName;
        }

        context.Items[ProviderResolverKey] = (Func<string, string?>)Resolve;
        return Resolve;
    }

    private static void ApplyWorkspaceSnapshot(
        ResearchSession session,
        string runId,
        string question,
        MaterialsSnapshot snapshot)
    {
        var ws = session.Workspace;
        WorkspaceProjection.ApplyMaterials(ws, snapshot);

        ws.Vibe.LastRunId = runId;
        ws.Vibe.LastGoal = question;
        ws.Vibe.Steps =
        [
            "vibe.materials",
            "vibe.pivot",
            "vibe.brief",
            "vibe.plan",
            "vibe.mesh",
            "vibe.dag_consensus",
            "vibe.delivery",
            "vibe.trace"
        ];
    }

    private static void PublishMessageMeta(
        WorkflowRunContext context,
        string agent,
        string stepName,
        string providerName)
    {
        if (!context.Items.TryGetValue(WorkflowRunContextKeys.AssistantMessageId, out var idObj) ||
            idObj is not string messageId ||
            string.IsNullOrWhiteSpace(messageId))
        {
            return;
        }

        context.Events.Publish(new CustomEvent
        {
            Timestamp = NowMs(),
            Name = "aevatar.vibe.message_meta",
            Value = new
            {
                messageId,
                agent,
                stepName,
                providerName = (providerName ?? string.Empty).Trim()
            }
        });
    }

    // ============================================================
    //  Step Modules (WorkflowRun)
    // ============================================================

    public sealed class ResearchingMaterialsStepModule : WorkflowRunStepModuleBase
    {
        private readonly ResearchingRoundServices _services;

        public ResearchingMaterialsStepModule(ResearchingRoundServices services)
        {
            _services = services ?? throw new ArgumentNullException(nameof(services));
        }

        public override string Name => "vibe_materials";
        public override string StepType => "vibe_materials";

        public override async Task<PrimitiveResult> ExecuteAsync(
            WorkflowRunContext context,
            StepDefinition step,
            string? preRenderedPrompt,
            string? preRenderedSystem,
            CancellationToken ct)
        {
            var session = context.GetSession();
            var question = context.GetQuestion();
            var input = context.GetInput();

            var materials = context.TryGetMaterials();
            if (materials == null)
            {
                materials = await _services._core.Materials.LoadAsync(
                    session.Id,
                    session.EffectiveDagId,
                    query: question,
                    ct);
            }

            var dagSnap = await _services._core.Dag.LoadSnapshotAsync(session.EffectiveDagId, ct);
            var recentTrace = await _services._core.Trace.LoadLatestAsync(session.Id, max: 5, ct);

            context.SetMaterials(materials);
            context.SetDagSnapshot(dagSnap);
            context.SetRecentTrace(recentTrace);

            ApplyWorkspaceSnapshot(session, context.RunId, question, materials);
            WorkspaceProjection.ApplyKnowledge(
                session.Workspace,
                _services._core.Workspace.ScanWorkspace(session.Id),
                materials);

            context.Events.Publish(new StateSnapshotEvent
            {
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                Snapshot = session.Workspace
            });

            context.Variables["question"] = question;
            context.Variables["attachments"] = input.AttachmentPaths ?? new List<string>();
            context.Variables["materials_context"] = materials.RenderedContext ?? string.Empty;
            context.Variables["dag_nodes"] = dagSnap.Nodes.Count;
            context.Variables["dag_edges"] = dagSnap.Edges.Count;
            context.Variables["plan_context"] = BuildPlanContextFromDag(dagSnap);

            return PrimitiveResult.Ok(materials);
        }
    }

    public sealed class ResearchingPivotStepModule : WorkflowRunStepModuleBase
    {
        private readonly ResearchingRoundServices _services;

        public ResearchingPivotStepModule(ResearchingRoundServices services)
        {
            _services = services ?? throw new ArgumentNullException(nameof(services));
        }

        public override string Name => "vibe_pivot";
        public override string StepType => "vibe_pivot";

        public override async Task<PrimitiveResult> ExecuteAsync(
            WorkflowRunContext context,
            StepDefinition step,
            string? preRenderedPrompt,
            string? preRenderedSystem,
            CancellationToken ct)
        {
            var materials = context.TryGetMaterials() ?? new MaterialsSnapshot();
            var ctx = _services.BuildRoundContext(context, materials);
            await _services.RunPivotDetectionAsync(ctx, ct);
            return PrimitiveResult.Ok();
        }
    }

    public sealed class ResearchingBriefStepModule : WorkflowRunStepModuleBase
    {
        private readonly ResearchingRoundServices _services;
        private readonly LlmProviderGate _llmGate;

        public ResearchingBriefStepModule(ResearchingRoundServices services, LlmProviderGate llmGate)
        {
            _services = services ?? throw new ArgumentNullException(nameof(services));
            _llmGate = llmGate ?? throw new ArgumentNullException(nameof(llmGate));
        }

        public override string Name => "vibe_brief";
        public override string StepType => "vibe_brief";

        public override async Task<PrimitiveResult> ExecuteAsync(
            WorkflowRunContext context,
            StepDefinition step,
            string? preRenderedPrompt,
            string? preRenderedSystem,
            CancellationToken ct)
        {
            var session = context.GetSession();
            var input = context.GetInput();
            var question = context.GetQuestion();
            var materials = context.TryGetMaterials() ?? new MaterialsSnapshot();
            var dagSnap = context.TryGetDagSnapshot() ?? await _services._core.Dag.LoadSnapshotAsync(session.EffectiveDagId, ct);
            var recentTrace = context.GetRecentTrace();
            var providerOverride = context.GetProviderOverride();

            var existing = await _services._core.Brief.LoadAsync(session.Id, ct);
            if (existing.Version > 0)
                return PrimitiveResult.Ok(existing);

            var resolver = await _services.GetProviderResolverAsync(context, providerOverride, ct);
            var raProvider = await _llmGate.EnsureProviderRunnableOrPauseAsync(
                context,
                agent: "research_assistant",
                stepName: "vibe.brief",
                resolveProvider: () => resolver("research_assistant"),
                ct: ct);
            PublishMessageMeta(context, "research_assistant", "vibe.brief", raProvider);

            var brief = await _services.TryGetBriefAsync(
                session.Id,
                input,
                question,
                materials,
                dagSnap,
                recentTrace,
                raProvider,
                ct);

            if (brief == null)
                return PrimitiveResult.Ok();

            brief.Version = 1;
            var saved = await _services._core.Brief.SaveAsync(session.Id, brief, ct);

            context.Events.Publish(new CustomEvent
            {
                Timestamp = NowMs(),
                Name = "aevatar.vibe.brief_updated",
                Value = new { sessionId = session.Id, version = saved.Version }
            });

            EmitSection(context.EmitAssistantDelta, "### Research Brief (1 page)\n");
            context.EmitAssistantDelta(RenderBriefMarkdown(saved));
            context.EmitAssistantDelta("\n\n");

            try
            {
                var mm = BuildMilestonesPlanDagMutation(session.Id, context.RunId, question, saved);
                if (mm != null)
                {
                    dagSnap = await _services._core.Dag.ApplyMutationAsync(session.EffectiveDagId, mm, ct);
                    context.SetDagSnapshot(dagSnap);

                    context.Events.Publish(new CustomEvent
                    {
                        Timestamp = NowMs(),
                        Name = "aevatar.vibe.milestones_plan_dag_written",
                        Value = new
                        {
                            sessionId = session.Id,
                            dagId = session.EffectiveDagId,
                            runId = context.RunId,
                            mutationId = mm.MutationId,
                            milestones = saved.Milestones.Count
                        }
                    });
                }
            }
            catch
            {
                // best-effort only
            }

            return PrimitiveResult.Ok(saved);
        }
    }

    public sealed class ResearchingPlanStepModule : WorkflowRunStepModuleBase
    {
        private readonly ResearchingRoundServices _services;
        private readonly LlmProviderGate _llmGate;

        public ResearchingPlanStepModule(ResearchingRoundServices services, LlmProviderGate llmGate)
        {
            _services = services ?? throw new ArgumentNullException(nameof(services));
            _llmGate = llmGate ?? throw new ArgumentNullException(nameof(llmGate));
        }

        public override string Name => "vibe_plan";
        public override string StepType => "vibe_plan";

        public override async Task<PrimitiveResult> ExecuteAsync(
            WorkflowRunContext context,
            StepDefinition step,
            string? preRenderedPrompt,
            string? preRenderedSystem,
            CancellationToken ct)
        {
            var session = context.GetSession();
            var input = context.GetInput();
            var question = context.GetQuestion();
            var materials = context.TryGetMaterials() ?? new MaterialsSnapshot();
            var dagSnap = context.TryGetDagSnapshot() ?? await _services._core.Dag.LoadSnapshotAsync(session.EffectiveDagId, ct);
            var recentTrace = context.GetRecentTrace();
            var providerOverride = context.GetProviderOverride();

            var resolver = await _services.GetProviderResolverAsync(context, providerOverride, ct);
            var raProvider = await _llmGate.EnsureProviderRunnableOrPauseAsync(
                context,
                agent: "research_assistant",
                stepName: "vibe.plan",
                resolveProvider: () => resolver("research_assistant"),
                ct: ct);
            PublishMessageMeta(context, "research_assistant", "vibe.plan", raProvider);

            var plan = await _services.TryGetPlanAsync(
                session.Id,
                input,
                question,
                materials,
                dagSnap,
                recentTrace,
                raProvider,
                ct);

            if (!string.IsNullOrWhiteSpace(plan.RawJson))
            {
                EmitSection(context.EmitAssistantDelta, "### Plan (research_assistant)\n");
                context.EmitAssistantDelta("```json\n");
                context.EmitAssistantDelta(plan.RawJson!.Trim() + "\n");
                context.EmitAssistantDelta("```\n\n");
            }

            return PrimitiveResult.Ok(plan);
        }
    }

    public sealed class ResearchingDagConsensusStepModule : WorkflowRunStepModuleBase
    {
        private readonly ResearchingRoundServices _services;

        public ResearchingDagConsensusStepModule(ResearchingRoundServices services)
        {
            _services = services ?? throw new ArgumentNullException(nameof(services));
        }

        public override string Name => "vibe_dag_consensus";
        public override string StepType => "vibe_dag_consensus";

        public override async Task<PrimitiveResult> ExecuteAsync(
            WorkflowRunContext context,
            StepDefinition step,
            string? preRenderedPrompt,
            string? preRenderedSystem,
            CancellationToken ct)
        {
            var session = context.GetSession();
            var materials = context.TryGetMaterials() ?? new MaterialsSnapshot();
            var dagSnap = context.TryGetDagSnapshot() ?? await _services._core.Dag.LoadSnapshotAsync(session.EffectiveDagId, ct);

            var outputs = context.GetMeshOutputs();
            if (outputs.Count == 0 &&
                context.Variables.TryGetValue("mesh_outputs", out var obj) &&
                obj is IReadOnlyDictionary<string, string> map)
            {
                outputs = map;
            }

            context.SetMeshOutputs(outputs);

            var resolver = await _services.GetProviderResolverAsync(context, context.GetProviderOverride(), ct);
            var providerName = resolver("verifier") ?? session.ProviderName;

            var dagResult = await _services.RunDagApplyAsync(
                session,
                context.RunId,
                materials,
                dagSnap,
                outputs,
                context.EmitAssistantDelta,
                providerName,
                context.Events,
                ct);

            context.SetDagResult(dagResult);
            context.Variables["dag_result"] = dagResult;

            return PrimitiveResult.Ok(dagResult);
        }
    }

    public sealed class ResearchingDeliveryStepModule : WorkflowRunStepModuleBase
    {
        private readonly ResearchingRoundServices _services;
        private readonly LlmProviderGate _llmGate;

        public ResearchingDeliveryStepModule(ResearchingRoundServices services, LlmProviderGate llmGate)
        {
            _services = services ?? throw new ArgumentNullException(nameof(services));
            _llmGate = llmGate ?? throw new ArgumentNullException(nameof(llmGate));
        }

        public override string Name => "vibe_delivery";
        public override string StepType => "vibe_delivery";

        public override async Task<PrimitiveResult> ExecuteAsync(
            WorkflowRunContext context,
            StepDefinition step,
            string? preRenderedPrompt,
            string? preRenderedSystem,
            CancellationToken ct)
        {
            var session = context.GetSession();
            var materials = context.TryGetMaterials() ?? new MaterialsSnapshot();
            var outputs = context.GetMeshOutputs();
            var dagResult = context.TryGetDagResult() ?? new DagRoundResult(false, false, null, null, null, [], null);

            var resolver = await _services.GetProviderResolverAsync(context, context.GetProviderOverride(), ct);
            var providerName = await _llmGate.EnsureProviderRunnableOrPauseAsync(
                context,
                agent: "paper_editor",
                stepName: "vibe.delivery",
                resolveProvider: () => resolver("paper_editor"),
                ct: ct);

            var ctx = _services.BuildRoundContext(context, materials);
            var paperEditorOut = await _services.RunPaperEditorAsync(
                ctx,
                dagResult,
                outputs,
                providerName,
                ct);

            var deliveryUpdate = await _services.TryApplyPaperEditorOutputAsync(session, context.RunId, paperEditorOut, ct);
            context.Variables["delivery_update"] = deliveryUpdate;

            return PrimitiveResult.Ok(deliveryUpdate);
        }
    }

    public sealed class ResearchingTraceStepModule : WorkflowRunStepModuleBase
    {
        private readonly ResearchingRoundServices _services;
        private readonly LlmProviderGate _llmGate;

        public ResearchingTraceStepModule(ResearchingRoundServices services, LlmProviderGate llmGate)
        {
            _services = services ?? throw new ArgumentNullException(nameof(services));
            _llmGate = llmGate ?? throw new ArgumentNullException(nameof(llmGate));
        }

        public override string Name => "vibe_trace";
        public override string StepType => "vibe_trace";

        public override async Task<PrimitiveResult> ExecuteAsync(
            WorkflowRunContext context,
            StepDefinition step,
            string? preRenderedPrompt,
            string? preRenderedSystem,
            CancellationToken ct)
        {
            var session = context.GetSession();
            var input = context.GetInput();
            var question = context.GetQuestion();
            var outputs = context.GetMeshOutputs();
            var dagResult = context.TryGetDagResult() ?? new DagRoundResult(false, false, null, null, null, [], null);

            var resolver = await _services.GetProviderResolverAsync(context, context.GetProviderOverride(), ct);
            var raProvider = await _llmGate.EnsureProviderRunnableOrPauseAsync(
                context,
                agent: "research_assistant",
                stepName: "vibe.trace",
                resolveProvider: () => resolver("research_assistant"),
                ct: ct);

            var summaryMd = await _services.TryGetSummaryAsync(
                session.Id,
                input,
                question,
                dagResult,
                outputs,
                Array.Empty<string>(),
                raProvider,
                ct);

            await _services.PersistTraceAsync(
                session,
                context.RunId,
                input,
                question,
                outputs,
                dagResult,
                summaryMd,
                ct);

            context.Variables["summary_markdown"] = summaryMd ?? string.Empty;

            return PrimitiveResult.Ok(summaryMd);
        }
    }

}
