using System.Text;
using System.Text.Json;
using Aevatar.Agents.AGUI;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Options;
using ScientificResearchAssistant.Api.Facts;
using ScientificResearchAssistant.Api.Materials;
using ScientificResearchAssistant.Api.Paper;
using ScientificResearchAssistant.Api.Vibe.Dag;
using ScientificResearchAssistant.Api.Vibe.Goals;
using ScientificResearchAssistant.Api.Vibe.Trace;
using ScientificResearchAssistant.Api.Vibe.Uploads;
using ScientificResearchAssistant.Api.Workspace;
using ScientificResearchAssistant.Contracts.Collab;

namespace ScientificResearchAssistant.Api.Sessions;

// ============================================================
//  Scientific Research Assistant Sessions API (AG-UI)
//
//  Endpoints:
//  - GET  /health
//  - GET  /api/info
//  - POST /api/sessions
//  - GET  /api/sessions
//  - POST /api/sessions/{id}/input
//  - GET  /api/sessions/{id}/agui/events   (SSE, snapshot-first)
// ============================================================

internal static class ResearchSessionsApi
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static void MapResearchSessionsApi(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        MapCreate(app);
        MapList(app);
        MapTools(app);
        MapGoals(app);
        MapUploads(app);
        MapDag(app);
        MapInput(app);
        MapMcpReconnect(app);
        MapFacts(app);
        MapWorkspace(app);
        MapAgUiEvents(app);
    }

    private static void MapCreate(WebApplication app)
    {
        app.MapPost("/api/sessions", async (
            CreateSessionInDto? input,
            ResearchSessionManager sessions,
            WorkspaceService workspace,
            PaperService paper,
            CancellationToken ct) =>
        {
            var s = sessions.Create(input?.ProviderName);

            // File-SSoT: ensure workspace + paper scaffold exists at creation time.
            workspace.EnsureSessionWorkspace(s.Id);
            await paper.EnsurePaperFilesAsync(s.Id, ct);

            return Results.Json(new { ok = true, sessionId = s.Id });
        });
    }

    private static void MapList(WebApplication app)
    {
        app.MapGet("/api/sessions", (ResearchSessionManager sessions) =>
        {
            var list = sessions.ListSessions();
            return Results.Json(new { count = list.Count, sessions = list });
        });
    }

    private static void MapTools(WebApplication app)
    {
        app.MapGet("/api/sessions/{sessionId}/tools", async (
            string sessionId,
            ResearchSessionManager sessions,
            ResearchRuntime runtime,
            CancellationToken ct) =>
        {
            if (!sessions.TryGet(sessionId, out var session))
                return Results.NotFound(new { error = "session not found" });

            try
            {
                var (tools, _mcpNames) = await runtime.GetToolsSnapshotAsync(session.Id, session.ProviderName, ct);
                return Results.Json(new { ok = true, sessionId = session.Id, tools });
            }
            catch (Exception ex)
            {
                return Results.Problem(title: "tools snapshot failed", detail: ex.Message, statusCode: 500);
            }
        });
    }

    private static void MapGoals(WebApplication app)
    {
        app.MapGet("/api/sessions/{sessionId}/goals", async (
            string sessionId,
            ResearchSessionManager sessions,
            GoalsStore goals,
            CancellationToken ct) =>
        {
            if (!sessions.TryGet(sessionId, out var session))
                return Results.NotFound(new { error = "session not found" });

            var snap = await goals.LoadAsync(session.Id, ct);
            return Results.Json(new
            {
                ok = true,
                sessionId = session.Id,
                goals = new
                {
                    version = snap.Version,
                    updatedAt = snap.UpdatedAt?.ToDateTime().ToUniversalTime().ToString("O") ?? "",
                    items = snap.Goals.Select(g => new
                    {
                        goalId = g.GoalId,
                        text = g.Text,
                        priority = g.Priority,
                        updatedAt = g.UpdatedAt?.ToDateTime().ToUniversalTime().ToString("O") ?? ""
                    }).ToList()
                }
            });
        });

        app.MapPut("/api/sessions/{sessionId}/goals", async (
            string sessionId,
            GoalsPutInDto input,
            ResearchSessionManager sessions,
            GoalsStore goals,
            FileMailboxService mailbox,
            CancellationToken ct) =>
        {
            if (!sessions.TryGet(sessionId, out var session))
                return Results.NotFound(new { error = "session not found" });

            var existing = await goals.LoadAsync(session.Id, ct);
            var targetVersion = input.Version ?? 0;
            if (targetVersion <= existing.Version)
                targetVersion = existing.Version + 1;

            var snap = new SraGoalsSnapshot
            {
                SessionId = session.Id,
                Version = targetVersion
            };

            if (input.Items != null)
            {
                foreach (var it in input.Items)
                {
                    if (it == null) continue;
                    var text = (it.Text ?? string.Empty).Trim();
                    if (text.Length == 0) continue;

                    snap.Goals.Add(new SraGoalItem
                    {
                        GoalId = (it.GoalId ?? string.Empty).Trim(),
                        Text = text,
                        Priority = it.Priority ?? 0
                    });
                }
            }

            var saved = await goals.SaveAsync(session.Id, snap, ct);

            // UI: notify goals updated (best-effort)
            session.Events.Publish(new CustomEvent
            {
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                Name = "aevatar.vibe.goals_updated",
                Value = new { sessionId = session.Id, version = saved.Version, count = saved.Goals.Count }
            });

            // Mailbox broadcast (best-effort) - stable roster for MVP.
            try
            {
                var now = Timestamp.FromDateTime(DateTime.UtcNow);
                var evt = new SraGoalsUpdated
                {
                    SessionId = session.Id,
                    Snapshot = saved,
                    Reason = "user_edit",
                    UpdatedBy = "user",
                    CreatedAt = now
                };

                var toAgents = new[] { "research_assistant", "planner", "reasoner", "librarian", "verifier", "dag_builder" };
                foreach (var a in toAgents)
                {
                    var envelope = new SraMailboxMessage
                    {
                        SessionId = session.Id,
                        MessageId = $"goals_updated:{saved.Version}",
                        FromAgent = "user",
                        ToAgent = a,
                        Type = "goals.updated",
                        CorrelationId = $"goals:{saved.Version}",
                        CreatedAt = now,
                        Payload = Any.Pack(evt)
                    };

                    await mailbox.SendAsync(session.Id, a, envelope, ct);
                }
            }
            catch
            {
                // best-effort only
            }

            return Results.Json(new { ok = true, sessionId = session.Id, version = saved.Version, count = saved.Goals.Count });
        });
    }

    private static void MapUploads(WebApplication app)
    {
        app.MapPost("/api/sessions/{sessionId}/uploads", async (
            string sessionId,
            HttpRequest request,
            ResearchSessionManager sessions,
            UploadsStore uploads,
            CancellationToken ct) =>
        {
            if (!sessions.TryGet(sessionId, out var session))
                return Results.NotFound(new { error = "session not found" });

            if (!request.HasFormContentType)
                return Results.BadRequest(new { error = "multipart/form-data is required" });

            var form = await request.ReadFormAsync(ct);
            var files = form.Files;
            if (files == null || files.Count == 0)
                return Results.BadRequest(new { error = "no files uploaded" });

            var max = uploads.GetMaxFilesPerRequest();
            if (files.Count > max)
                return Results.BadRequest(new { error = $"too many files (max {max})" });

            var paths = new List<string>(capacity: files.Count);
            foreach (var f in files)
            {
                ct.ThrowIfCancellationRequested();
                var rel = await uploads.SaveAsync(session.Id, f, ct);
                paths.Add(rel);
            }

            return Results.Json(new { ok = true, sessionId = session.Id, attachmentPaths = paths });
        });
    }

    private sealed record GoalsPutInDto
    {
        public int? Version { get; init; }
        public List<GoalItemInDto>? Items { get; init; }
    }

    private sealed record GoalItemInDto
    {
        public string? GoalId { get; init; }
        public string? Text { get; init; }
        public int? Priority { get; init; }
    }

    private static void MapDag(WebApplication app)
    {
        app.MapGet("/api/sessions/{sessionId}/dag", async (
            string sessionId,
            ResearchSessionManager sessions,
            DagStore dag,
            CancellationToken ct) =>
        {
            if (!sessions.TryGet(sessionId, out var session))
                return Results.NotFound(new { error = "session not found" });

            var snap = await dag.GetSnapshotForListAsync(session.Id, ct);
            return Results.Json(new { ok = true, sessionId = session.Id, dag = snap });
        });

        app.MapGet("/api/sessions/{sessionId}/dag/{nodeId}/explain", async (
            string sessionId,
            string nodeId,
            ResearchSessionManager sessions,
            DagStore dag,
            CancellationToken ct) =>
        {
            if (!sessions.TryGet(sessionId, out var session))
                return Results.NotFound(new { error = "session not found" });

            var snap = await dag.LoadSnapshotAsync(session.Id, ct);
            var explain = DagExplain.Explain(snap, nodeId);

            // Return a JSON-friendly shape (avoid protobuf Timestamp JSON issues).
            return Results.Json(new
            {
                ok = true,
                sessionId = session.Id,
                nodeId = (nodeId ?? string.Empty).Trim(),
                explain = new
                {
                    hasCycle = explain.HasCycle,
                    provable = explain.Provable,
                    directDeps = explain.DirectDeps.ToList(),
                    topo = explain.Topo.ToList(),
                    missing = explain.Missing.Select(n => new { id = n.Id, type = n.Type.ToString(), label = n.Label }).ToList()
                }
            });
        });

        app.MapGet("/api/sessions/{sessionId}/dag/staged", async (
            string sessionId,
            ResearchSessionManager sessions,
            DagStore dag,
            CancellationToken ct) =>
        {
            if (!sessions.TryGet(sessionId, out var session))
                return Results.NotFound(new { error = "session not found" });

            var list = await dag.ListStagedAsync(session.Id, ct);
            return Results.Json(new { ok = true, sessionId = session.Id, staged = list });
        });
    }

    private static void MapInput(WebApplication app)
    {
        app.MapPost("/api/sessions/{sessionId}/input", async (
            string sessionId,
            SessionInputInDto input,
            ResearchSessionManager sessions,
            ResearchRunExecutor executor,
            CancellationToken ct) =>
        {
            if (!sessions.TryGet(sessionId, out var session))
                return Results.NotFound(new { error = "session not found" });

            if (string.IsNullOrWhiteSpace(input.Message))
                return Results.BadRequest(new { error = "message is required" });

            // Fire-and-forget run; clients receive progress via AG-UI SSE.
            var runSeq = session.NextRunSeq();
            var runId = $"{session.Id}:{runSeq}";

            _ = Task.Run(async () =>
            {
                await executor.ExecuteAsync(session, runId, input, CancellationToken.None);
            }, CancellationToken.None);

            return Results.Accepted($"/api/sessions/{session.Id}", new { ok = true, sessionId = session.Id, runId });
        });
    }

    private static void MapMcpReconnect(WebApplication app)
    {
        app.MapPost("/api/sessions/{sessionId}/mcp/reconnect", (
            string sessionId,
            ResearchSessionManager sessions,
            ResearchRuntime runtime) =>
        {
            if (!sessions.TryGet(sessionId, out var session))
                return Results.NotFound(new { error = "session not found" });

            // Fire-and-forget; result is streamed back via SSE (CUSTOM events + tools_snapshot refresh).
            _ = Task.Run(async () =>
            {
                long Ts() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

                session.Events.Publish(new CustomEvent
                {
                    Timestamp = Ts(),
                    Name = "aevatar.scientific.mcp_reconnect_started",
                    Value = new { sessionId = session.Id }
                });

                try
                {
                    var (agent, _) = await runtime.GetAgentAsync(session.Id, session.ProviderName, CancellationToken.None);
                    var (ok, error) = await agent.ReconnectMcpAsync(CancellationToken.None);

                    // Always refresh the tools snapshot so UI updates immediately (even if reconnect failed).
                    var (tools, _mcpNames) = await runtime.RefreshToolsSnapshotAsync(session.Id, session.ProviderName, CancellationToken.None);
                    session.Events.Publish(new CustomEvent
                    {
                        Timestamp = Ts(),
                        Name = "aevatar.scientific.tools_snapshot",
                        Value = new { sessionId = session.Id, tools }
                    });

                    if (ok)
                    {
                        session.Events.Publish(new CustomEvent
                        {
                            Timestamp = Ts(),
                            Name = "aevatar.scientific.mcp_reconnect_finished",
                            Value = new { sessionId = session.Id, ok = true }
                        });
                    }
                    else
                    {
                        session.Events.Publish(new CustomEvent
                        {
                            Timestamp = Ts(),
                            Name = "aevatar.scientific.mcp_reconnect_error",
                            Value = new { sessionId = session.Id, ok = false, error = error ?? "mcp reconnect failed" }
                        });
                    }
                }
                catch (Exception ex)
                {
                    session.Events.Publish(new CustomEvent
                    {
                        Timestamp = Ts(),
                        Name = "aevatar.scientific.mcp_reconnect_error",
                        Value = new { sessionId = session.Id, ok = false, error = ex.Message }
                    });
                }
            }, CancellationToken.None);

            return Results.Accepted($"/api/sessions/{session.Id}", new { ok = true });
        });
    }

    private static void MapFacts(WebApplication app)
    {
        // Facts workflow: create a proposal under workspace facts_proposed/ (NOT directly into facts/).
        app.MapPost("/api/sessions/{sessionId}/facts", async (
            string sessionId,
            SaveFactInDto input,
            ResearchSessionManager sessions,
            FactLifecycleService facts,
            IOptions<MaterialsOptions> materialsOptions,
            WorkspaceService workspace,
            CancellationToken ct) =>
        {
            if (!sessions.TryGet(sessionId, out var session))
                return Results.NotFound(new { error = "session not found" });

            // Safe-by-default: reuse existing write switch (same as previous facts write-back).
            if (!materialsOptions.Value.AllowWrite)
                return Results.Problem(title: "facts write is disabled", detail: "Materials:AllowWrite=false", statusCode: 403);

            var content = (input.Content ?? string.Empty).Trim();
            if (content.Length == 0)
                return Results.BadRequest(new { error = "content is required" });

            try
            {
                var title = (input.Title ?? string.Empty).Trim();
                var maxWrite = Math.Clamp(materialsOptions.Value.MaxWriteChars, 1, 500_000);
                if (content.Length > maxWrite)
                    content = content[..maxWrite];

                var proposal = await facts.CreateProposalAsync(
                    session.Id,
                    title,
                    content,
                    evidencePaths: input.EvidencePaths,
                    proposedBy: "api",
                    ct);

                // Refresh file-backed workspace snapshot for UI.
                ApplyKnowledgeToWorkspace(session, workspace.ScanWorkspace(session.Id));
                session.Events.Publish(new StateSnapshotEvent
                {
                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    Snapshot = session.Workspace
                });

                session.Events.Publish(new CustomEvent
                {
                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    Name = "aevatar.scientific.fact_proposed",
                    Value = new { sessionId = session.Id, factId = proposal.FactId, title = proposal.Title }
                });

                return Results.Json(new
                {
                    ok = true,
                    sessionId = session.Id,
                    proposal = new { factId = proposal.FactId, title = proposal.Title }
                });
            }
            catch (Exception ex)
            {
                return Results.Problem(title: "fact proposal failed", detail: ex.Message, statusCode: 500);
            }
        });
    }

    private static void MapWorkspace(WebApplication app)
    {
        // Bounded snapshot derived from file-backed workspace.
        app.MapGet("/api/sessions/{sessionId}/workspace", (
            string sessionId,
            ResearchSessionManager sessions,
            WorkspaceService workspace) =>
        {
            if (!sessions.TryGet(sessionId, out var session))
                return Results.NotFound(new { error = "session not found" });

            var scan = workspace.ScanWorkspace(session.Id);
            return Results.Json(new { ok = true, sessionId = session.Id, workspace = scan });
        });

        app.MapPost("/api/sessions/{sessionId}/facts/{factId}/votes", async (
            string sessionId,
            string factId,
            FactVoteInDto input,
            ResearchSessionManager sessions,
            FactLifecycleService facts,
            WorkspaceService workspace,
            CancellationToken ct) =>
        {
            if (!sessions.TryGet(sessionId, out var session))
                return Results.NotFound(new { error = "session not found" });

            var reviewerId = (input.ReviewerId ?? string.Empty).Trim();
            var voteRaw = (input.Vote ?? string.Empty).Trim().ToLowerInvariant();

            if (reviewerId.Length == 0)
                return Results.BadRequest(new { error = "reviewerId is required" });

            var vote = voteRaw switch
            {
                "approve" => FactVoteValue.Approve,
                "reject" => FactVoteValue.Reject,
                "needs_work" or "needswork" => FactVoteValue.NeedsWork,
                _ => FactVoteValue.Unspecified
            };

            if (vote == FactVoteValue.Unspecified)
                return Results.BadRequest(new { error = "vote must be approve|reject|needs_work" });

            var msg = new FactVote
            {
                FactId = factId,
                ReviewerId = reviewerId,
                Vote = vote,
                Comment = (input.Comment ?? string.Empty).Trim()
            };

            await facts.RecordVoteAsync(session.Id, msg, ct);

            ApplyKnowledgeToWorkspace(session, workspace.ScanWorkspace(session.Id));
            session.Events.Publish(new StateSnapshotEvent
            {
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                Snapshot = session.Workspace
            });

            return Results.Json(new { ok = true, sessionId = session.Id, factId });
        });

        app.MapPost("/api/sessions/{sessionId}/facts/{factId}/verifications", async (
            string sessionId,
            string factId,
            FactVerificationInDto input,
            ResearchSessionManager sessions,
            FactLifecycleService facts,
            WorkspaceService workspace,
            CancellationToken ct) =>
        {
            if (!sessions.TryGet(sessionId, out var session))
                return Results.NotFound(new { error = "session not found" });

            var verifierId = (input.VerifierId ?? string.Empty).Trim();
            if (verifierId.Length == 0)
                return Results.BadRequest(new { error = "verifierId is required" });

            if (input.Result is null)
                return Results.BadRequest(new { error = "result is required" });

            var msg = new FactVerification
            {
                FactId = factId,
                VerifierId = verifierId,
                Tool = (input.Tool ?? "unknown").Trim(),
                Result = input.Result.Value,
                LogExcerpt = (input.LogExcerpt ?? string.Empty).Trim()
            };

            if (input.ArtifactPaths != null)
            {
                foreach (var p in input.ArtifactPaths)
                {
                    var t = (p ?? string.Empty).Trim();
                    if (t.Length == 0) continue;
                    msg.ArtifactPaths.Add(t);
                }
            }

            await facts.RecordVerificationAsync(session.Id, msg, ct);

            ApplyKnowledgeToWorkspace(session, workspace.ScanWorkspace(session.Id));
            session.Events.Publish(new StateSnapshotEvent
            {
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                Snapshot = session.Workspace
            });

            return Results.Json(new { ok = true, sessionId = session.Id, factId });
        });

        app.MapPost("/api/sessions/{sessionId}/facts/{factId}/promote", async (
            string sessionId,
            string factId,
            PromoteFactInDto? input,
            ResearchSessionManager sessions,
            FactLifecycleService facts,
            WorkspaceService workspace,
            CancellationToken ct) =>
        {
            if (!sessions.TryGet(sessionId, out var session))
                return Results.NotFound(new { error = "session not found" });

            var finalizedBy = (input?.FinalizedBy ?? "api").Trim();
            var decision = await facts.EvaluateAndPromoteAsync(session.Id, factId, finalizedBy, ct);

            ApplyKnowledgeToWorkspace(session, workspace.ScanWorkspace(session.Id));
            session.Events.Publish(new StateSnapshotEvent
            {
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                Snapshot = session.Workspace
            });

            if (decision == null)
                return Results.Json(new { ok = true, sessionId = session.Id, factId, pending = true });

            return Results.Json(new
            {
                ok = true,
                sessionId = session.Id,
                factId,
                decision = decision.Decision.ToString(),
                basis = decision.Basis,
                rationale = decision.Rationale
            });
        });
    }

    private static void MapAgUiEvents(WebApplication app)
    {
        app.MapGet("/api/sessions/{sessionId}/agui/events", async (
            HttpContext http,
            string sessionId,
            ResearchSessionManager sessions,
            ResearchRuntime runtime,
            WorkspaceService workspace,
            GoalsStore goals,
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

            await using var writer = new StreamWriter(
                http.Response.Body,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                bufferSize: 16 * 1024,
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
                Messages = session.GetMessagesSnapshot(maxMessages: 60)
            }, ct);

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
            try
            {
                var snap = await goals.LoadAsync(session.Id, ct);
                await WriteSseAsync(new CustomEvent
                {
                    Timestamp = Ts(DateTimeOffset.UtcNow),
                    Name = "aevatar.vibe.goals_snapshot",
                    Value = new
                    {
                        sessionId = session.Id,
                        version = snap.Version,
                        updatedAt = snap.UpdatedAt?.ToDateTime().ToUniversalTime().ToString("O") ?? "",
                        items = snap.Goals.Select(g => new
                        {
                            goalId = g.GoalId,
                            text = g.Text,
                            priority = g.Priority,
                            updatedAt = g.UpdatedAt?.ToDateTime().ToUniversalTime().ToString("O") ?? ""
                        }).ToList()
                    }
                }, ct);
            }
            catch
            {
                // best-effort
            }

            try
            {
                var snap = await dag.GetSnapshotForListAsync(session.Id, ct);
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
                            new { agent = "dag_builder", agentId = $"{baseId}-dag_builder" }
                        }
                    }
                }, ct);
            }
            catch
            {
                // best-effort
            }

            // Optional visual state (workspace / materials / graph)
            ApplyKnowledgeToWorkspace(session, workspace.ScanWorkspace(session.Id));
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

    private static void ApplyMaterialsToWorkspace(ResearchSession session, MaterialsSnapshot snapshot)
    {
        var ws = session.Workspace;
        ws.Materials.RootDir = "";
        ws.Materials.LoadedAt = snapshot.LoadedAt.ToString("O");
        ws.Materials.Items = new List<MaterialMeta>(capacity: snapshot.Facts.Count + snapshot.Sources.Count);

        foreach (var x in snapshot.Facts)
        {
            ws.Materials.Items.Add(new MaterialMeta
            {
                Id = x.Id,
                Title = x.Title,
                RelativePath = x.RelativePath,
                Kind = x.Kind
            });
        }

        foreach (var x in snapshot.Sources)
        {
            ws.Materials.Items.Add(new MaterialMeta
            {
                Id = x.Id,
                Title = x.Title,
                RelativePath = x.RelativePath,
                Kind = x.Kind
            });
        }

        ws.Materials.ContextPreview = Trunc(snapshot.RenderedContext, 2000);
    }

    private static void ApplyKnowledgeToWorkspace(ResearchSession session, WorkspaceScanResult scan)
    {
        var k = session.Workspace.Knowledge;
        k.FactsCount = scan.FactsCount;
        k.FactsProposedCount = scan.FactsProposedCount;
        k.SourcesCount = scan.SourcesCount;
        k.FactsProposedRecent = scan.FactsProposedRecent;
    }

    private static string Trunc(string? s, int maxChars)
    {
        var t = (s ?? string.Empty).Replace("\r", "").Trim();
        if (t.Length <= maxChars) return t;
        return t[..maxChars];
    }
}


