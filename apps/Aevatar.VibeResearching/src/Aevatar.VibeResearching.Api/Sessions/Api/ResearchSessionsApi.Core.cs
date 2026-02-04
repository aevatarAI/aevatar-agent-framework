using Aevatar.Agents.AGUI;
using Aevatar.Agents.AI.Abstractions.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Options;
using Aevatar.Agents.Cognitive.Researching.Paper;
using Aevatar.Agents.Cognitive.Researching.Sessions;
using Aevatar.Agents.Cognitive.Researching.Workspace;
using VibeResearching.Api;

namespace VibeResearching.Api.Sessions;

internal static partial class ResearchSessionsApi
{
    private static void MapCreate(WebApplication app)
    {
        app.MapPost("/api/sessions", async (
            CreateSessionInDto? input,
            ResearchSessionManager sessions,
            WorkspaceService workspace,
            PaperService paper,
            CancellationToken ct) =>
        {
            var s = await sessions.CreateAsync(input?.ProviderName, ct);

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

    private sealed record AgentProviderPutInDto
    {
        public string? Agent { get; init; }
        public string? ProviderName { get; init; } // empty/"default" => clear mapping
    }
}
