using System.IO;
using Aevatar.Agents.AGUI;
using Aevatar.Agents.Sessions.Runtime;
using VibeResearching.Api.Vibe.Brief;
using VibeResearching.Api.Vibe.Dag;
using VibeResearching.Api.Vibe.Delivery;
using VibeResearching.Api.Vibe.Trace;
using VibeResearching.Api.Workspace;

namespace VibeResearching.Api.Sessions;

public sealed class VibeAgUiBootstrapper : ISessionAgUiBootstrapper
{
    private readonly SessionUiSnapshotStore _ui;
    private readonly WorkspaceService _workspace;
    private readonly BriefStore _brief;
    private readonly DeliveryCenterStore _delivery;
    private readonly AgentProvidersStore _agentProviders;
    private readonly DagStore _dag;
    private readonly TraceStore _trace;
    private readonly ResearchRuntime _runtime;
    private readonly ResearchSessionManager _sessions;

    public VibeAgUiBootstrapper(
        SessionUiSnapshotStore ui,
        WorkspaceService workspace,
        BriefStore brief,
        DeliveryCenterStore delivery,
        AgentProvidersStore agentProviders,
        DagStore dag,
        TraceStore trace,
        ResearchRuntime runtime,
        ResearchSessionManager sessions)
    {
        _ui = ui ?? throw new ArgumentNullException(nameof(ui));
        _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        _brief = brief ?? throw new ArgumentNullException(nameof(brief));
        _delivery = delivery ?? throw new ArgumentNullException(nameof(delivery));
        _agentProviders = agentProviders ?? throw new ArgumentNullException(nameof(agentProviders));
        _dag = dag ?? throw new ArgumentNullException(nameof(dag));
        _trace = trace ?? throw new ArgumentNullException(nameof(trace));
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
    }

    public async Task<IReadOnlyList<AgUiEvent>> BuildAsync(SessionAgUiBootstrapContext context, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(context);

        var sessionId = context.Session.SessionId;
        var events = new List<AgUiEvent>();

        _sessions.TryGet(sessionId, out var session);

        SessionUiSnapshotStore.UiSnapshot? uiSnap = null;
        try
        {
            uiSnap = await _ui.LoadAsync(sessionId, ct);
        }
        catch
        {
            // best-effort
        }

        var messages = uiSnap?.Messages?.Count > 0
            ? uiSnap.Messages.Select(m => new AgUiMessage
            {
                Id = m.Id,
                Role = m.Role,
                Content = m.Content
            }).ToList()
            : session?.GetMessagesSnapshot(context.Options.MaxSnapshotMessages);

        if (messages is { Count: > 0 })
        {
            events.Add(new MessagesSnapshotEvent
            {
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                Messages = messages
            });
        }

        if (uiSnap?.MessageMeta is { Count: > 0 })
        {
            events.Add(new CustomEvent
            {
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                Name = "aevatar.vibe.message_meta_snapshot",
                Value = new
                {
                    sessionId,
                    items = uiSnap.MessageMeta.Select(x => new
                    {
                        messageId = x.MessageId,
                        agent = x.Agent,
                        stepName = x.StepName,
                        providerName = x.ProviderName ?? ""
                    }).ToList()
                }
            });
        }

        if (uiSnap?.Tools is { Count: > 0 })
        {
            events.Add(new CustomEvent
            {
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                Name = "aevatar.ui.tools_snapshot",
                Value = new
                {
                    sessionId,
                    tools = uiSnap.Tools.Select(t => new
                    {
                        messageId = t.MessageId,
                        toolCallId = t.ToolCallId,
                        toolName = t.ToolName,
                        status = t.Status,
                        resultPreview = t.ResultPreview ?? "",
                        error = t.Error ?? ""
                    }).ToList()
                }
            });
        }

        if (uiSnap?.RunSteps is { Order.Count: > 0 })
        {
            events.Add(new CustomEvent
            {
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                Name = "aevatar.ui.run_steps_snapshot",
                Value = new
                {
                    sessionId,
                    runId = uiSnap.RunSteps.RunId ?? "",
                    order = uiSnap.RunSteps.Order ?? new List<string>(),
                    map = uiSnap.RunSteps.Map ?? new Dictionary<string, SessionUiSnapshotStore.UiRunStep>()
                }
            });
        }

        events.Add(new CustomEvent
        {
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Name = "aevatar.scientific.session",
            Value = new
            {
                sessionId,
                createdAt = context.Session.CreatedAt.ToDateTime().ToUniversalTime().ToString("O"),
                providerName = session?.ProviderName ?? ""
            }
        });

        try
        {
            var snap = await _brief.LoadAsync(sessionId, ct);
            events.Add(new CustomEvent
            {
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                Name = "aevatar.vibe.brief_snapshot",
                Value = new
                {
                    sessionId,
                    version = snap.Version,
                    updatedAt = snap.UpdatedAt?.ToDateTime().ToUniversalTime().ToString("O") ?? "",
                    rewrittenQuestion = snap.RewrittenQuestion ?? "",
                    scope = snap.Scope ?? "",
                    successCriteria = snap.SuccessCriteria ?? "",
                    terms = snap.Terms.Select(t => new { term = t.Term, meaning = t.Meaning }).ToList(),
                    assumptions = snap.Assumptions.ToList(),
                    risks = snap.Risks.ToList(),
                    uncertainties = snap.Uncertainties.ToList(),
                    milestones = snap.Milestones.Select(m => new { roundIndex = m.RoundIndex, expectedOutput = m.ExpectedOutput }).ToList()
                }
            });
        }
        catch
        {
            // best-effort
        }

        try
        {
            var snap = await _delivery.GetSnapshotForUiAsync(sessionId, ct);
            events.Add(new CustomEvent
            {
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                Name = "aevatar.vibe.delivery_snapshot",
                Value = new { sessionId, delivery = snap }
            });
        }
        catch
        {
            // best-effort
        }

        try
        {
            var dagId = session?.EffectiveDagId ?? ResearchSession.GlobalDagId;
            var snap = await _dag.GetSnapshotForListAsync(dagId, ct, currentSessionId: sessionId);
            events.Add(new CustomEvent
            {
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                Name = "aevatar.vibe.dag_snapshot",
                Value = new { sessionId, dag = snap }
            });
        }
        catch
        {
            // best-effort
        }

        try
        {
            var list = await _trace.LoadLatestAsync(sessionId, max: 20, ct);
            var ws = _workspace.EnsureSessionWorkspace(sessionId);
            var items = list.Select(s => new
            {
                runId = s.RunId,
                roundIndex = s.RoundIndex,
                triggerKind = s.TriggerKind,
                triggerRef = s.TriggerRef,
                updatedAt = s.UpdatedAt?.ToDateTime().ToUniversalTime().ToString("O") ?? "",
                agents = s.PerAgent.Select(a => a.Agent).ToList(),
                dagChangesCount = s.DagChanges.Count,
                summaryPath = string.IsNullOrWhiteSpace(s.RunId)
                    ? ""
                    : Path.GetRelativePath(ws.SessionRoot, Path.Combine(ws.RunsDir, s.RunId, "summary.md"))
                        .Replace('\\', '/')
                        .Trim('/')
            }).ToList();

            events.Add(new CustomEvent
            {
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                Name = "aevatar.vibe.trace_snapshot",
                Value = new { sessionId, items }
            });
        }
        catch
        {
            // best-effort
        }

        try
        {
            var baseId = $"sra-{sessionId}";
            events.Add(new CustomEvent
            {
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                Name = "aevatar.vibe.agents_snapshot",
                Value = new
                {
                    sessionId,
                    roster = new[]
                    {
                        new { agent = "research_assistant", agentId = $"{baseId}-research_assistant" },
                        new { agent = "planner", agentId = $"{baseId}-planner" },
                        new { agent = "reasoner", agentId = $"{baseId}-reasoner" },
                        new { agent = "librarian", agentId = $"{baseId}-librarian" },
                        new { agent = "verifier", agentId = $"{baseId}-verifier" },
                        new { agent = "dag_builder", agentId = $"{baseId}-dag_builder" },
                        new { agent = "paper_editor", agentId = $"{baseId}-paper_editor" }
                    }
                }
            });
        }
        catch
        {
            // best-effort
        }

        try
        {
            var snap = await _agentProviders.LoadAsync(sessionId, ct);
            events.Add(new CustomEvent
            {
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                Name = "aevatar.vibe.agent_providers_snapshot",
                Value = new { sessionId, version = snap.Version, updatedAt = snap.UpdatedAt, map = snap.Map }
            });
        }
        catch
        {
            // best-effort
        }

        try
        {
            var workspace = new ResearchWorkspaceState { SessionId = sessionId };
            WorkspaceProjection.ApplyKnowledge(workspace, _workspace.ScanWorkspace(sessionId), null);
            events.Add(new StateSnapshotEvent
            {
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                Snapshot = workspace
            });
        }
        catch
        {
            // best-effort
        }

        // Background: tools snapshot (best-effort).
        _ = Task.Run(async () =>
        {
            try
            {
                var (tools, _mcpNames) = await _runtime.GetToolsSnapshotAsync(sessionId, session?.ProviderName, CancellationToken.None);
                context.Stream.Publish(new CustomEvent
                {
                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    Name = "aevatar.scientific.tools_snapshot",
                    Value = new { sessionId, tools }
                });
            }
            catch
            {
                // best-effort
            }
        }, CancellationToken.None);

        return events;
    }
}
