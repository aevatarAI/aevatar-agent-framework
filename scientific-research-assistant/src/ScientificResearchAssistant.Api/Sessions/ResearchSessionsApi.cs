using System.Text;
using System.Text.Json;
using Aevatar.Agents.AGUI;
using Aevatar.Agents.AI.Abstractions.Configuration;
using Aevatar.Agents.Knowledge.Graph;
using Aevatar.Agents.Knowledge.Graph.Exceptions;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Options;
using ScientificResearchAssistant.Api.Facts;
using ScientificResearchAssistant.Api.Materials;
using ScientificResearchAssistant.Api.Paper;
using ScientificResearchAssistant.Api.Vibe.Brief;
using ScientificResearchAssistant.Api.Vibe.Compute;
using ScientificResearchAssistant.Api.Vibe.Delivery;
using ScientificResearchAssistant.Api.Vibe.Dag;
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
        MapAgentProviders(app);
        MapDeliverables(app);
        MapCompute(app);
        MapUploads(app);
        MapDag(app);
        MapStatus(app);
        MapInput(app);
        MapMcpReconnect(app);
        MapFacts(app);
        MapWorkspace(app);
        MapFiles(app);
        MapAgUiEvents(app);
    }

    private static void MapStatus(WebApplication app)
    {
        app.MapGet("/api/sessions/{sessionId}/status", async (
            string sessionId,
            ResearchSessionManager sessions,
            SessionUiSnapshotStore ui,
            CancellationToken ct) =>
        {
            if (!sessions.TryGet(sessionId, out var session))
                return Results.NotFound(new { error = "session not found" });

            var uiSnap = await ui.LoadAsync(session.Id, ct);

            var runId = uiSnap.RunSteps?.RunId ?? "";
            var order = uiSnap.RunSteps?.Order ?? [];
            var map = uiSnap.RunSteps?.Map ?? new Dictionary<string, SessionUiSnapshotStore.UiRunStep>();

            var runningSteps = map
                .Where(kv => string.Equals(kv.Value?.Status, "running", StringComparison.OrdinalIgnoreCase))
                .Select(kv => kv.Key)
                .OrderBy(x => x, StringComparer.Ordinal)
                .ToList();

            var doneSteps = map
                .Where(kv => string.Equals(kv.Value?.Status, "done", StringComparison.OrdinalIgnoreCase))
                .Select(kv => kv.Key)
                .OrderBy(x => x, StringComparer.Ordinal)
                .ToList();

            // Aggregate per-agent activity from message_meta snapshot (best-effort).
            var agentLatest = new Dictionary<string, SessionUiSnapshotStore.UiMessageMeta>(StringComparer.OrdinalIgnoreCase);
            foreach (var m in uiSnap.MessageMeta ?? [])
            {
                if (m == null) continue;
                var agent = (m.Agent ?? string.Empty).Trim();
                if (agent.Length == 0) continue;
                agentLatest[agent] = m; // last writer wins (snapshot order is best-effort)
            }

            var agents = agentLatest
                .Values
                .Select(m =>
                {
                    var stepName = (m.StepName ?? string.Empty).Trim();
                    // Heuristic: if step is currently running, agent is "running", else "idle".
                    var status = runningSteps.Contains(stepName, StringComparer.Ordinal) ? "running" : "idle";
                    return new
                    {
                        agent = (m.Agent ?? string.Empty).Trim(),
                        stepName,
                        providerName = (m.ProviderName ?? string.Empty).Trim(),
                        status
                    };
                })
                .OrderBy(x => x.agent, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var runningTools = (uiSnap.Tools ?? [])
                .Where(t => t != null && string.Equals(t.Status, "running", StringComparison.OrdinalIgnoreCase))
                .Select(t => new
                {
                    messageId = t.MessageId,
                    toolCallId = t.ToolCallId,
                    toolName = t.ToolName
                })
                .Take(40)
                .ToList();

            return Results.Json(new
            {
                ok = true,
                sessionId = session.Id,
                updatedAt = uiSnap.UpdatedAt ?? "",
                runId,
                steps = new
                {
                    order,
                    map,
                    running = runningSteps,
                    done = doneSteps
                },
                agents,
                runningTools
            }, Json);
        });
    }

    private static void MapFiles(WebApplication app)
    {
        // File manager API (local-only; safe within workspace/sessions/{id})
        app.MapGet("/api/sessions/{sessionId}/files/tree", (
            string sessionId,
            string? dir,
            int? depth,
            ResearchSessionManager sessions,
            SessionFilesService files,
            HttpContext http) =>
        {
            if (!IsLocal(http))
                return Results.Forbid();

            if (!sessions.TryGet(sessionId, out var session))
                return Results.NotFound(new { error = "session not found" });

            try
            {
                var tree = files.ListTree(session.Id, dir, maxDepth: depth ?? 6);
                return Results.Json(new { ok = true, sessionId = session.Id, tree });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { ok = false, error = ex.Message });
            }
        });

        app.MapGet("/api/sessions/{sessionId}/files", async (
            string sessionId,
            string path,
            ResearchSessionManager sessions,
            SessionFilesService files,
            HttpContext http,
            CancellationToken ct) =>
        {
            if (!IsLocal(http))
                return Results.Forbid();

            if (!sessions.TryGet(sessionId, out var session))
                return Results.NotFound(new { error = "session not found" });

            try
            {
                var res = await files.ReadTextAsync(session.Id, path, ct);
                return Results.Json(new { ok = true, sessionId = session.Id, file = res });
            }
            catch (FileNotFoundException)
            {
                return Results.NotFound(new { ok = false, error = "file not found" });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { ok = false, error = ex.Message });
            }
        });

        app.MapPut("/api/sessions/{sessionId}/files", async (
            string sessionId,
            SaveFileInDto input,
            ResearchSessionManager sessions,
            SessionFilesService files,
            HttpContext http,
            CancellationToken ct) =>
        {
            if (!IsLocal(http))
                return Results.Forbid();

            if (!sessions.TryGet(sessionId, out var session))
                return Results.NotFound(new { error = "session not found" });

            var path = (input.Path ?? string.Empty).Trim();
            if (path.Length == 0)
                return Results.BadRequest(new { ok = false, error = "path is required" });

            try
            {
                var wr = await files.WriteTextAsync(session.Id, path, input.Content ?? string.Empty, ct);
                return Results.Json(new { ok = true, sessionId = session.Id, file = wr });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { ok = false, error = ex.Message });
            }
        });
    }

    private static bool IsLocal(HttpContext ctx)
    {
        var ip = ctx.Connection.RemoteIpAddress;
        return ip == null || System.Net.IPAddress.IsLoopback(ip);
    }

    private static void MapDeliverables(WebApplication app)
    {
        app.MapGet("/api/sessions/{sessionId}/deliverables", async (
            string sessionId,
            ResearchSessionManager sessions,
            BriefStore brief,
            DeliveryCenterStore delivery,
            CancellationToken ct) =>
        {
            if (!sessions.TryGet(sessionId, out _))
                return Results.NotFound(new { error = "session not found" });

            var b = await brief.LoadAsync(sessionId, ct);
            var d = await delivery.GetSnapshotForUiAsync(sessionId, ct);

            return Results.Json(new
            {
                ok = true,
                sessionId,
                brief = new
                {
                    version = b.Version,
                    updatedAt = b.UpdatedAt?.ToDateTime().ToUniversalTime().ToString("O") ?? "",
                    rewrittenQuestion = b.RewrittenQuestion ?? "",
                    scope = b.Scope ?? "",
                    successCriteria = b.SuccessCriteria ?? "",
                    terms = b.Terms.Select(t => new { term = t.Term, meaning = t.Meaning }).ToList(),
                    assumptions = b.Assumptions.ToList(),
                    risks = b.Risks.ToList(),
                    uncertainties = b.Uncertainties.ToList(),
                    milestones = b.Milestones.Select(m => new { roundIndex = m.RoundIndex, expectedOutput = m.ExpectedOutput }).ToList()
                },
                delivery = d
            });
        });
    }

    private static void MapCompute(WebApplication app)
    {
        app.MapPost("/api/sessions/{sessionId}/compute/decision", async (
            string sessionId,
            ComputeDecisionInDto? input,
            ResearchSessionManager sessions,
            ComputeDecisionStore store,
            CancellationToken ct) =>
        {
            if (!sessions.TryGet(sessionId, out var session))
                return Results.NotFound(new { error = "session not found" });

            var planId = (input?.PlanId ?? string.Empty).Trim();
            var action = (input?.Action ?? string.Empty).Trim().ToLowerInvariant();
            var comment = input?.Comment;

            var path = await store.SaveAsync(sessionId, planId, action, comment, ct);

            session.Events.Publish(new CustomEvent
            {
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                Name = "aevatar.vibe.compute_decision",
                Value = new
                {
                    sessionId,
                    planId,
                    action,
                    comment = (comment ?? string.Empty).Replace("\r", "").Trim(),
                    path,
                    createdAt = DateTimeOffset.UtcNow.ToString("O")
                }
            });

            return Results.Json(new { ok = true, sessionId, planId, action, path });
        });
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

    private static void MapAgentProviders(WebApplication app)
    {
        app.MapGet("/api/sessions/{sessionId}/agent-providers", async (
            string sessionId,
            ResearchSessionManager sessions,
            AgentProvidersStore store,
            CancellationToken ct) =>
        {
            if (!sessions.TryGet(sessionId, out var session))
                return Results.NotFound(new { error = "session not found" });

            var snap = await store.LoadAsync(session.Id, ct);
            return Results.Json(new
            {
                ok = true,
                sessionId = session.Id,
                version = snap.Version,
                updatedAt = snap.UpdatedAt,
                map = snap.Map
            });
        });

        app.MapPut("/api/sessions/{sessionId}/agent-providers", async (
            string sessionId,
            AgentProviderPutInDto input,
            ResearchSessionManager sessions,
            AgentProvidersStore store,
            IOptionsMonitor<LLMProvidersConfig> llm,
            CancellationToken ct) =>
        {
            if (!sessions.TryGet(sessionId, out var session))
                return Results.NotFound(new { error = "session not found" });

            var agent = (input.Agent ?? string.Empty).Trim();
            if (agent.Length == 0)
                return Results.BadRequest(new { ok = false, error = "agent is required" });

            var providerName = (input.ProviderName ?? string.Empty).Trim();
            if (providerName.Length > 0 && !string.Equals(providerName, "default", StringComparison.OrdinalIgnoreCase))
            {
                // Validate provider exists and is runnable (apiKey present).
                if (!llm.CurrentValue.Providers.TryGetValue(providerName, out var p) || p == null)
                    return Results.BadRequest(new { ok = false, error = $"unknown provider: {providerName}" });
                if (string.IsNullOrWhiteSpace(p.ApiKey))
                    return Results.BadRequest(new { ok = false, error = $"provider has no apiKey: {providerName}" });
            }

            var saved = await store.UpsertAsync(session.Id, agent, providerName, ct);

            // UI: push snapshot so the Agents panel updates without a page refresh.
            session.Events.Publish(new CustomEvent
            {
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                Name = "aevatar.vibe.agent_providers_snapshot",
                Value = new { sessionId = session.Id, version = saved.Version, updatedAt = saved.UpdatedAt, map = saved.Map }
            });

            return Results.Json(new { ok = true, sessionId = session.Id, version = saved.Version, updatedAt = saved.UpdatedAt, map = saved.Map });
        });
    }

    // Goals removed: executable intent lives in DAG plan nodes.

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

    private sealed record AgentProviderPutInDto
    {
        public string? Agent { get; init; }
        public string? ProviderName { get; init; } // empty/"default" => clear mapping
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

            var dagId = string.IsNullOrWhiteSpace(session.DagId) ? session.Id : session.DagId.Trim();
            var snap = await dag.GetSnapshotForListAsync(dagId, ct);
            return Results.Json(new { ok = true, sessionId = session.Id, dagId, dag = snap });
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

            var dagId = string.IsNullOrWhiteSpace(session.DagId) ? session.Id : session.DagId.Trim();
            var snap = await dag.LoadSnapshotAsync(dagId, ct);
            var explain = DagExplain.Explain(snap, nodeId);

            // Return a JSON-friendly shape (avoid protobuf Timestamp JSON issues).
            return Results.Json(new
            {
                ok = true,
                sessionId = session.Id,
                dagId,
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

            var dagId = string.IsNullOrWhiteSpace(session.DagId) ? session.Id : session.DagId.Trim();
            var list = await dag.ListStagedAsync(dagId, ct);
            return Results.Json(new { ok = true, sessionId = session.Id, dagId, staged = list });
        });

        // Bind/unbind a session to a shared dagId (in-memory; process-scoped).
        app.MapGet("/api/sessions/{sessionId}/dag/binding", (
            string sessionId,
            ResearchSessionManager sessions) =>
        {
            if (!sessions.TryGet(sessionId, out var session))
                return Results.NotFound(new { error = "session not found" });
            var dagId = string.IsNullOrWhiteSpace(session.DagId) ? session.Id : session.DagId.Trim();
            return Results.Json(new { ok = true, sessionId = session.Id, dagId });
        });

        app.MapPut("/api/sessions/{sessionId}/dag/binding", async (
            string sessionId,
            DagBindingPutInDto input,
            ResearchSessionManager sessions,
            WorkspaceService workspace,
            CancellationToken ct) =>
        {
            if (!sessions.TryGet(sessionId, out var session))
                return Results.NotFound(new { error = "session not found" });

            var dagId = (input?.DagId ?? string.Empty).Trim();
            if (dagId.Length == 0)
            {
                session.DagId = null;
                return Results.Json(new { ok = true, sessionId = session.Id, dagId = session.Id, mode = "per_session" });
            }

            // Validate + create dag workspace (throws on invalid format).
            _ = workspace.EnsureDagWorkspace(dagId);
            await Task.CompletedTask; // keep signature async

            session.DagId = dagId;
            return Results.Json(new { ok = true, sessionId = session.Id, dagId, mode = "shared" });
        });

        // ============================================================
        //  Knowledge Graph API (session-scoped, KnowledgeGraph-backed)
        //
        //  中文说明：
        //  - /dag 保持兼容（前端 DAG/Explain 逻辑不动）
        //  - /graph 提供更强的知识链与论文生成能力
        // ============================================================

        app.MapGet("/api/sessions/{sessionId}/graph", async (
            string sessionId,
            ResearchSessionManager sessions,
            DagStore dag,
            IKnowledgeGraphClientFactory graph,
            CancellationToken ct) =>
        {
            if (!sessions.TryGet(sessionId, out var session))
                return Results.NotFound(new { error = "session not found" });

            // Ensure hydration for in-memory backend (best-effort).
            var dagId = string.IsNullOrWhiteSpace(session.DagId) ? session.Id : session.DagId.Trim();
            _ = await dag.LoadSnapshotAsync(dagId, ct);

            var client = graph.CreateClient(dagId);
            var snapshot = await client.GetKnowledgeSnapshotAsync(ct);
            return Results.Json(new { ok = true, sessionId = session.Id, dagId, graph = snapshot });
        });

        app.MapGet("/api/sessions/{sessionId}/graph/{nodeId}/chain", async (
            string sessionId,
            string nodeId,
            ResearchSessionManager sessions,
            DagStore dag,
            IKnowledgeGraphClientFactory graph,
            CancellationToken ct) =>
        {
            if (!sessions.TryGet(sessionId, out var session))
                return Results.NotFound(new { error = "session not found" });

            nodeId = (nodeId ?? string.Empty).Trim();
            if (nodeId.Length == 0)
                return Results.BadRequest(new { error = "nodeId is required" });

            // Ensure hydration for in-memory backend (best-effort).
            var dagId = string.IsNullOrWhiteSpace(session.DagId) ? session.Id : session.DagId.Trim();
            _ = await dag.LoadSnapshotAsync(dagId, ct);

            var client = graph.CreateClient(dagId);
            try
            {
                var details = await client.GetKnowledgeChainDetailsAsync(nodeId, ct);
                return Results.Json(new
                {
                    ok = true,
                    sessionId = session.Id,
                    dagId,
                    nodeId,
                    chain = details.Chain,
                    markdown = details.Description
                });
            }
            catch (NodeNotFoundException ex)
            {
                return Results.NotFound(new { error = "node not found", nodeId = ex.NodeId });
            }
        });

        app.MapGet("/api/sessions/{sessionId}/graph/paper", async (
            string sessionId,
            ResearchSessionManager sessions,
            DagStore dag,
            IKnowledgeGraphClientFactory graph,
            CancellationToken ct) =>
        {
            if (!sessions.TryGet(sessionId, out var session))
                return Results.NotFound(new { error = "session not found" });

            // Ensure hydration for in-memory backend (best-effort).
            var dagId = string.IsNullOrWhiteSpace(session.DagId) ? session.Id : session.DagId.Trim();
            _ = await dag.LoadSnapshotAsync(dagId, ct);

            var client = graph.CreateClient(dagId);
            var markdown = await client.GenerateFullPaperAsync(ct);
            return Results.Json(new { ok = true, sessionId = session.Id, dagId, markdown });
        });
    }

    private sealed record DagBindingPutInDto
    {
        public string? DagId { get; init; } // empty => unbind (per-session)
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
                Messages = session.GetMessagesSnapshot(maxMessages: 60)
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



