using System.Linq;
using Aevatar.Agents.AGUI;
using Aevatar.Agents.Knowledge.Graph;
using Aevatar.Agents.Knowledge.Graph.Exceptions;
using Aevatar.Agents.Knowledge.Graph.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using VibeResearching.Api.Vibe;
using VibeResearching.Api.Vibe.Compute;
using VibeResearching.Api.Vibe.Dag;
using VibeResearching.Api.Vibe.Uploads;
using VibeResearching.Api.Workspace;

namespace VibeResearching.Api.Sessions;

internal static partial class ResearchSessionsApi
{
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

            if (planId.Length == 0)
                return Results.BadRequest(new { error = "planId is required" });

            if (action is not ("execute" or "degrade" or "skip"))
                return Results.BadRequest(new { error = "action must be execute|degrade|skip" });

            var path = await store.WriteDecisionAsync(session.Id, planId, action, comment, ct);

            session.Events.Publish(new CustomEvent
            {
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                Name = "aevatar.vibe.compute_decision",
                Value = new
                {
                    sessionId = session.Id,
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

    private static void MapDag(WebApplication app)
    {
        // ============================================================
        //  Global DAG API - No sessionId required
        //  Returns ALL nodes across ALL sessions
        // ============================================================
        app.MapGet("/api/dag/global", async (
            DagStore dag,
            CancellationToken ct) =>
        {
            var snap = await dag.GetSnapshotForListAsync(ResearchSession.GlobalDagId, ct);
            return Results.Json(new { ok = true, dagId = ResearchSession.GlobalDagId, dag = snap });
        });

        app.MapGet("/api/sessions/{sessionId}/dag", async (
            string sessionId,
            ResearchSessionManager sessions,
            DagStore dag,
            CancellationToken ct) =>
        {
            if (!sessions.TryGet(sessionId, out var session))
                return Results.NotFound(new { error = "session not found" });

            var dagId = session.EffectiveDagId;
            var snap = await dag.GetSnapshotForListAsync(dagId, ct, currentSessionId: session.Id);
            return Results.Json(new { ok = true, sessionId = session.Id, dagId, dag = snap });
        });

        app.MapPost("/api/sessions/{sessionId}/dag/plan/edit", async (
            string sessionId,
            PlanEditInDto input,
            ResearchSessionManager sessions,
            VibeOrchestrator vibe,
            DagStore dag,
            CancellationToken ct) =>
        {
            if (!sessions.TryGet(sessionId, out var session))
                return Results.NotFound(new { error = "session not found" });

            var dagId = session.EffectiveDagId;
            var instruction = (input.Instruction ?? string.Empty).Replace("\r", "").Trim();
            if (instruction.Length == 0)
                return Results.BadRequest(new { ok = false, error = "instruction is required" });

            // Use session default provider (or client can override via normal Agents panel mapping later).
            var (applied, note) = await vibe.EditDagPlanMilestonesAsync(session, dagId, instruction, providerOverride: null, ct);
            if (applied == null)
                return Results.BadRequest(new { ok = false, sessionId = session.Id, dagId, error = note });

            // Return the same "list" shape used by DagPanel.
            var snap = await dag.GetSnapshotForListAsync(dagId, ct, currentSessionId: session.Id);
            return Results.Json(new { ok = true, sessionId = session.Id, dagId, note, dag = snap });
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

            var dagId = session.EffectiveDagId;
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
            }, Json);
        });

        app.MapGet("/api/sessions/{sessionId}/dag/staged", async (
            string sessionId,
            ResearchSessionManager sessions,
            DagStore dag,
            CancellationToken ct) =>
        {
            if (!sessions.TryGet(sessionId, out var session))
                return Results.NotFound(new { error = "session not found" });

            var dagId = session.EffectiveDagId;
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
            var dagId = session.EffectiveDagId;
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
            var dagId = session.EffectiveDagId;
            _ = await dag.LoadSnapshotAsync(dagId, ct);

            // 使用 session.Id 查询知识图谱（节点以真实 sessionId 存储）。
            var client = graph.CreateClient(session.Id);
            var snapshot = await client.GetGraphSnapshotAsync(ct);
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
            var dagId = session.EffectiveDagId;
            _ = await dag.LoadSnapshotAsync(dagId, ct);

            // 使用 session.Id 查询知识图谱（节点以真实 sessionId 存储）。
            var client = graph.CreateClient(session.Id);
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

        // Node Explanation API (US4) - called by frontend workflow-topology.tsx
        // 该 API 需要跨 session 全局搜索，因为前端 DAG 是全局视图。
        app.MapGet("/api/sessions/{sessionId}/graph/{nodeId}/explain", async (
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
            var dagId = session.EffectiveDagId;
            _ = await dag.LoadSnapshotAsync(dagId, ct);

            // 使用 session.Id 查询知识图谱（节点以真实 sessionId 存储）。
            var client = graph.CreateClient(session.Id);

            // 快路径：先查当前 session
            try
            {
                var explanation = await client.ExplainNodeAsync(nodeId, ct);
                return BuildExplainResponse(explanation, session.Id, dagId);
            }
            catch (NodeNotFoundException)
            {
                // 当前 session 未找到，进入全局搜索
            }

            // 全局搜索 KnowledgeNodes
            var allKnowledge = await client.GetAllKnowledgeNodesGlobalAsync(ct);
            var knowledgeNode = allKnowledge.FirstOrDefault(n =>
                string.Equals(n.Id, nodeId, StringComparison.Ordinal));

            if (knowledgeNode != null)
            {
                // 命中后用节点实际 sessionId
                var nodeClient = graph.CreateClient(knowledgeNode.SessionId);
                try
                {
                    var explanation = await nodeClient.ExplainNodeAsync(nodeId, ct);
                    return BuildExplainResponse(explanation, knowledgeNode.SessionId, dagId);
                }
                catch (NodeNotFoundException ex)
                {
                    return Results.NotFound(new { error = "node found but explain failed", nodeId = ex.NodeId });
                }
            }

            // 全局搜索 PlanNodes
            var allPlans = await client.GetAllPlanNodesGlobalAsync(ct);
            var planNode = allPlans.FirstOrDefault(n =>
                string.Equals(n.Id, nodeId, StringComparison.Ordinal));

            if (planNode != null)
            {
                // 命中后用节点实际 sessionId
                var nodeClient = graph.CreateClient(planNode.SessionId);
                try
                {
                    var explanation = await nodeClient.ExplainNodeAsync(nodeId, ct);
                    return BuildExplainResponse(explanation, planNode.SessionId, dagId);
                }
                catch (NodeNotFoundException ex)
                {
                    return Results.NotFound(new { error = "node found but explain failed", nodeId = ex.NodeId });
                }
            }

            // 全局未找到
            return Results.NotFound(new { error = "node not found", nodeId });
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
            var dagId = session.EffectiveDagId;
            _ = await dag.LoadSnapshotAsync(dagId, ct);

            // 使用 session.Id 查询知识图谱（节点以真实 sessionId 存储）。
            var client = graph.CreateClient(session.Id);
            var markdown = await client.GenerateFullPaperAsync(ct);
            return Results.Json(new { ok = true, sessionId = session.Id, dagId, markdown });
        });
    }

    /// <summary>
    /// 构建节点解释的 JSON 响应。
    /// </summary>
    private static IResult BuildExplainResponse(NodeExplanation explanation, string nodeSessionId, string dagId)
    {
        return Results.Json(new
        {
            ok = true,
            sessionId = nodeSessionId, // The actual session where the node resides
            dagId,
            explanation = new
            {
                nodeId = explanation.NodeId,
                title = explanation.Title,
                kind = explanation.NodeType, // Frontend expects "kind", not "nodeType"
                markdownContent = explanation.MarkdownContent,
                directDependencies = explanation.DirectDependencies,
                fullChainNodeIds = Array.Empty<string>(), // Not in backend model; keep empty for compatibility
                dependents = explanation.Dependents,
                createdAt = explanation.CreatedAt.ToString("O"),
                updatedAt = explanation.UpdatedAt.ToString("O")
            }
        }, Json);
    }

    private sealed record PlanEditInDto
    {
        public string? Instruction { get; init; }
    }

    private sealed record DagBindingPutInDto
    {
        public string? DagId { get; init; } // empty => unbind (per-session)
    }
}
