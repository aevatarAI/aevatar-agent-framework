using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Builder;
using VibeResearching.Api.Vibe.Brief;
using VibeResearching.Api.Vibe.Delivery;

namespace VibeResearching.Api.Sessions;

internal static partial class ResearchSessionsApi
{
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
                    toolName = t.ToolName,
                    status = t.Status,
                    startedAt = t.StartedAt,
                    providerName = t.ProviderName ?? "",
                    targetAgent = t.TargetAgent ?? ""
                })
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
}
