using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Aevatar.Agents.AGUI;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Aevatar.Agents.Cognitive.Researching.Brief;
using Aevatar.Agents.Cognitive.Researching.Dag;
using Aevatar.Agents.Cognitive.Researching.Delivery;
using Aevatar.Agents.Cognitive.Researching.Sessions;
using Aevatar.Agents.Cognitive.Researching.Trace;
using Aevatar.Agents.Cognitive.Researching.Workspace;
using VibeResearching.Api;

namespace VibeResearching.Api.Sessions;

internal static partial class ResearchSessionsApi
{
    private static void MapAgUiEvents(WebApplication app)
    {
        app.MapGet("/api/sessions/{sessionId}/agui/events", async (
            HttpContext http,
            string sessionId,
            ResearchSessionManager sessions,
            ResearchRuntime runtime,
            WorkspaceService workspace,
            BriefStore brief,
            DeliveryCenterStore delivery,
            SessionUiSnapshotStore ui,
            AgentProvidersStore agentProviders,
            DagStore dag,
            TraceStore trace,
            CancellationToken ct) =>
        {
            if (!sessions.TryGet(sessionId, out var session))
            {
                http.Response.StatusCode = StatusCodes.Status404NotFound;
                await http.Response.WriteAsJsonAsync(new { error = "session not found" }, cancellationToken: ct);
                return;
            }

            http.Response.StatusCode = StatusCodes.Status200OK;
            http.Response.Headers.ContentType = "text/event-stream; charset=utf-8";
            http.Response.Headers.CacheControl = "no-store";
            http.Response.Headers.Pragma = "no-cache";
            http.Response.Headers["X-Accel-Buffering"] = "no";

            await http.Response.StartAsync(ct);

            // Use minimal buffer for real-time token streaming.
            // Keep AutoFlush=false (default) to avoid sync Flush() which Kestrel disallows.
            // WriteSseAsync calls FlushAsync explicitly after each event.
            await using var writer = new StreamWriter(
                http.Response.Body,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                bufferSize: 256,
                leaveOpen: true);

            async Task WriteSseAsync(AgUiEvent evt, CancellationToken token)
            {
                // IMPORTANT:
                // System.Text.Json does not polymorphically serialize derived members when using the generic overload.
                // Here `evt` is typed as AgUiEvent, so Serialize(evt, options) would only output base fields
                // (type/timestamp/rawEvent) and drop derived fields like messageId/delta/name/value.
                // Use runtime type to keep AG-UI protocol payloads intact.
                var line = JsonSerializer.Serialize((object)evt, evt.GetType(), Json);
                await writer.WriteAsync("data: ");
                await writer.WriteLineAsync(line);
                await writer.WriteLineAsync();
                await writer.FlushAsync();
            }

            long Ts(DateTimeOffset? ts) => (ts ?? DateTimeOffset.UtcNow).ToUnixTimeMilliseconds();

            // Best-effort: hydrate server-side message snapshot from file-backed UI snapshot
            // so refresh can recover client-only projections (step/tool cards).
            SessionUiSnapshotStore.UiSnapshot? uiSnap = null;
            try
            {
                uiSnap = await ui.LoadAsync(session.Id, ct);
                foreach (var m in uiSnap.Messages)
                    session.SetMessage(m.Id, m.Role, m.Content);
            }
            catch
            {
                // best-effort
            }

            // ------------------------------------------------------------
            //  0) Fast bootstrap (NEVER block SSE on agent/tool init)
            //
            //  WHY:
            //  - Agent initialization may involve MCP/tool bootstrap and can be slow or flaky.
            //  - If we await it here, frontend sees an "empty" chat for a long time.
            //  - So we send a minimal snapshot immediately, then hydrate in background.
            // ------------------------------------------------------------
            await WriteSseAsync(new MessagesSnapshotEvent
            {
                Timestamp = Ts(DateTimeOffset.UtcNow),
                Messages = session.GetMessagesSnapshot(maxMessages: 60).ToList()
            }, ct);

            // Extra bootstrap: UI meta + tool cards + run steps (file-backed)
            try
            {
                if (uiSnap == null)
                    uiSnap = await ui.LoadAsync(session.Id, ct);

                if (uiSnap.MessageMeta.Count > 0)
                {
                    await WriteSseAsync(new CustomEvent
                    {
                        Timestamp = Ts(DateTimeOffset.UtcNow),
                        Name = "aevatar.vibe.message_meta_snapshot",
                        Value = new
                        {
                            sessionId = session.Id,
                            items = uiSnap.MessageMeta.Select(x => new
                            {
                                messageId = x.MessageId,
                                agent = x.Agent,
                                stepName = x.StepName,
                                providerName = x.ProviderName ?? ""
                            }).ToList()
                        }
                    }, ct);
                }

                if (uiSnap.Tools.Count > 0)
                {
                    await WriteSseAsync(new CustomEvent
                    {
                        Timestamp = Ts(DateTimeOffset.UtcNow),
                        Name = "aevatar.ui.tools_snapshot",
                        Value = new
                        {
                            sessionId = session.Id,
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
                    }, ct);
                }

                if (uiSnap.RunSteps != null && uiSnap.RunSteps.Order is { Count: > 0 })
                {
                    await WriteSseAsync(new CustomEvent
                    {
                        Timestamp = Ts(DateTimeOffset.UtcNow),
                        Name = "aevatar.ui.run_steps_snapshot",
                        Value = new
                        {
                            sessionId = session.Id,
                            runId = uiSnap.RunSteps.RunId ?? "",
                            order = uiSnap.RunSteps.Order ?? new List<string>(),
                            map = uiSnap.RunSteps.Map ?? new Dictionary<string, SessionUiSnapshotStore.UiRunStep>()
                        }
                    }, ct);
                }
            }
            catch
            {
                // best-effort
            }

            await WriteSseAsync(new CustomEvent
            {
                Timestamp = Ts(DateTimeOffset.UtcNow),
                Name = "aevatar.scientific.session",
                Value = new
                {
                    sessionId = session.Id,
                    createdAt = session.CreatedAt.ToString("O"),
                    providerName = session.ProviderName ?? ""
                }
            }, ct);

            // ------------------------------------------------------------
            //  Vibe bootstrap snapshots (File-SSoT projections)
            // ------------------------------------------------------------
            // Goals removed: executable intent lives in DAG plan nodes.

            try
            {
                var snap = await brief.LoadAsync(session.Id, ct);
                await WriteSseAsync(new CustomEvent
                {
                    Timestamp = Ts(DateTimeOffset.UtcNow),
                    Name = "aevatar.vibe.brief_snapshot",
                    Value = new
                    {
                        sessionId = session.Id,
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
                }, ct);
            }
            catch
            {
                // best-effort
            }

            try
            {
                var snap = await delivery.GetSnapshotForUiAsync(session.Id, ct);
                await WriteSseAsync(new CustomEvent
                {
                    Timestamp = Ts(DateTimeOffset.UtcNow),
                    Name = "aevatar.vibe.delivery_snapshot",
                    Value = new { sessionId = session.Id, delivery = snap }
                }, ct);
            }
            catch
            {
                // best-effort
            }

            try
            {
                var snap = await dag.GetSnapshotForListAsync(session.EffectiveDagId, ct, currentSessionId: session.Id);
                await WriteSseAsync(new CustomEvent
                {
                    Timestamp = Ts(DateTimeOffset.UtcNow),
                    Name = "aevatar.vibe.dag_snapshot",
                    Value = new { sessionId = session.Id, dag = snap }
                }, ct);
            }
            catch
            {
                // best-effort
            }

            try
            {
                var list = await trace.LoadLatestAsync(session.Id, max: 20, ct);
                var ws = workspace.EnsureSessionWorkspace(session.Id);
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

                await WriteSseAsync(new CustomEvent
                {
                    Timestamp = Ts(DateTimeOffset.UtcNow),
                    Name = "aevatar.vibe.trace_snapshot",
                    Value = new { sessionId = session.Id, items }
                }, ct);
            }
            catch
            {
                // best-effort
            }

            // Agents snapshot (best-effort; deterministic ids only, no init).
            try
            {
                var baseId = $"sra-{session.Id}";
                await WriteSseAsync(new CustomEvent
                {
                    Timestamp = Ts(DateTimeOffset.UtcNow),
                    Name = "aevatar.vibe.agents_snapshot",
                    Value = new
                    {
                        sessionId = session.Id,
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
                }, ct);
            }
            catch
            {
                // best-effort
            }

            // Agent provider mapping snapshot (File-SSoT)
            try
            {
                var snap = await agentProviders.LoadAsync(session.Id, ct);
                await WriteSseAsync(new CustomEvent
                {
                    Timestamp = Ts(DateTimeOffset.UtcNow),
                    Name = "aevatar.vibe.agent_providers_snapshot",
                    Value = new { sessionId = session.Id, version = snap.Version, updatedAt = snap.UpdatedAt, map = snap.Map }
                }, ct);
            }
            catch
            {
                // best-effort
            }

            // Optional visual state (workspace / materials / graph)
            WorkspaceProjection.ApplyKnowledge(session.Workspace, workspace.ScanWorkspace(session.Id), null);
            await WriteSseAsync(new StateSnapshotEvent
            {
                Timestamp = Ts(DateTimeOffset.UtcNow),
                Snapshot = session.Workspace
            }, ct);

            // 1) Background hydration (tools catalog) -> publish into the same hub.
            _ = Task.Run(async () =>
            {
                try
                {
                    var (tools, _mcpNames) = await runtime.GetToolsSnapshotAsync(session.Id, session.ProviderName, ct);
                    session.Events.Publish(new CustomEvent
                    {
                        Timestamp = Ts(DateTimeOffset.UtcNow),
                        Name = "aevatar.scientific.tools_snapshot",
                        Value = new { sessionId = session.Id, tools }
                    });
                }
                catch
                {
                    // best-effort
                }
            }, ct);

            // 2) Live stream (no replay; snapshot already hydrated UI)
            await foreach (var evt in session.Events.SubscribeAsync(replay: false, ct: ct))
            {
                ct.ThrowIfCancellationRequested();
                await WriteSseAsync(evt, ct);
            }
        });
    }
}
