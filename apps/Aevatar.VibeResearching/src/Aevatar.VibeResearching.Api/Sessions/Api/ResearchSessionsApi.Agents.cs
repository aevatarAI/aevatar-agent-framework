using Aevatar.Agents.Abstractions.Persistence;
using Aevatar.Agents.AI;
using Google.Protobuf.WellKnownTypes;
using Microsoft.AspNetCore.Builder;
using VibeResearching.Contracts.Sessions;

namespace VibeResearching.Api.Sessions;

internal static partial class ResearchSessionsApi
{
    private static void MapAgents(WebApplication app)
    {
        app.MapGet("/api/sessions/{sessionId}/agents", async (
            string sessionId,
            IVibeSessionStore? store,
            CancellationToken ct) =>
        {
            if (store == null)
                return Results.Problem(title: "session store not configured", statusCode: 500);

            var record = await store.GetAsync(sessionId, ct);
            if (record == null)
                return Results.NotFound(new { error = "session not found" });

            return Results.Json(new
            {
                ok = true,
                sessionId = record.SessionId,
                providerName = record.ProviderName,
                dagId = record.DagId,
                coordinatorId = record.CoordinatorId,
                workerIds = record.WorkerIds,
                agentIds = record.AgentIds,
                createdAt = ToIso(record.CreatedAt),
                updatedAt = ToIso(record.UpdatedAt)
            });
        });

        app.MapGet("/api/sessions/{sessionId}/agents/states", async (
            string sessionId,
            int? historyLimit,
            bool? includeHistory,
            IVibeSessionStore? store,
            IStateStore<AevatarAIAgentState>? stateStore,
            CancellationToken ct) =>
        {
            if (store == null)
                return Results.Problem(title: "session store not configured", statusCode: 500);
            if (stateStore == null)
                return Results.Problem(title: "state store not configured", statusCode: 500);

            var record = await store.GetAsync(sessionId, ct);
            if (record == null)
                return Results.NotFound(new { error = "session not found" });

            var limit = Math.Clamp(historyLimit ?? 50, 1, 500);
            var include = includeHistory ?? false;

            var agents = new List<object>();
            var missing = new List<string>();

            foreach (var agentId in record.AgentIds)
            {
                var state = await stateStore.LoadAsync(agentId, ct);
                if (state == null)
                {
                    missing.Add(agentId);
                    continue;
                }

                agents.Add(new
                {
                    agentId,
                    state = TrimHistory(state, include, limit)
                });
            }

            return Results.Json(new
            {
                ok = true,
                sessionId = record.SessionId,
                agents,
                missing
            });
        });

        app.MapGet("/api/sessions/{sessionId}/agents/{agentId}/history", async (
            string sessionId,
            string agentId,
            int? limit,
            IVibeSessionStore? store,
            IStateStore<AevatarAIAgentState>? stateStore,
            CancellationToken ct) =>
        {
            if (store == null)
                return Results.Problem(title: "session store not configured", statusCode: 500);
            if (stateStore == null)
                return Results.Problem(title: "state store not configured", statusCode: 500);

            var record = await store.GetAsync(sessionId, ct);
            if (record == null)
                return Results.NotFound(new { error = "session not found" });

            if (!record.AgentIds.Contains(agentId))
                return Results.NotFound(new { error = "agent not found in session" });

            var state = await stateStore.LoadAsync(agentId, ct);
            if (state == null)
                return Results.NotFound(new { error = "agent state not found" });

            var max = Math.Clamp(limit ?? 50, 1, 500);
            var history = TrimHistory(state, includeHistory: true, max).History;

            return Results.Json(new
            {
                ok = true,
                sessionId = record.SessionId,
                agentId,
                history
            });
        });
    }

    private static string ToIso(Timestamp? ts)
    {
        if (ts == null || (ts.Seconds == 0 && ts.Nanos == 0))
            return string.Empty;

        return ts.ToDateTime().ToUniversalTime().ToString("O");
    }

    private static AevatarAIAgentState TrimHistory(
        AevatarAIAgentState state,
        bool includeHistory,
        int limit)
    {
        var clone = state.Clone();
        if (!includeHistory)
        {
            clone.History.Clear();
            return clone;
        }

        if (clone.History.Count <= limit)
            return clone;

        var trimmed = clone.History
            .Skip(Math.Max(0, clone.History.Count - limit))
            .ToList();
        clone.History.Clear();
        clone.History.AddRange(trimmed);
        return clone;
    }
}
