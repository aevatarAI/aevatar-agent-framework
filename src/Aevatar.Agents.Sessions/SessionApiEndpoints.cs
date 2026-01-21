using Aevatar.Agents.Abstractions.Memory;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Aevatar.Agents.Sessions;

public static class SessionApiEndpoints
{
    public static IEndpointRouteBuilder MapAevatarSessionApi(this IEndpointRouteBuilder app)
    {
        var sessions = app.MapGroup("/api/sessions");

        sessions.MapPost("/", async (HttpRequest request, CognitiveSessionService service, CancellationToken ct) =>
        {
            var payload = await ProtoHttp.ReadAsync<StartSessionRequest>(request, ct);
            var state = await service.StartSessionAsync(payload, ct);
            return ProtoHttp.Ok(new StartSessionResponse { State = state });
        });

        sessions.MapGet("/", async (HttpRequest request, CognitiveSessionService service, CancellationToken ct) =>
        {
            var limit = ReadInt(request, "limit", 200, 1, 2000);
            var response = await service.ListSessionsAsync(limit, ct);
            return ProtoOrNotFound(response);
        });

        sessions.MapGet("/{sessionId}", async (string sessionId, CognitiveSessionService service, CancellationToken ct) =>
        {
            var state = await service.GetSessionStateAsync(sessionId, ct);
            return ProtoOrNotFound(state);
        });

        sessions.MapGet("/{sessionId}/agents", async (string sessionId, CognitiveSessionService service, CancellationToken ct) =>
        {
            var response = await service.GetSessionAgentsAsync(sessionId, ct);
            return ProtoOrNotFound(response);
        });

        sessions.MapGet("/{sessionId}/agents/states",
            async (string sessionId, HttpRequest request, CognitiveSessionService service, CancellationToken ct) =>
            {
                var includeHistory = ReadBool(request, "include_history", true);
                var historyLimit = ReadInt(request, "history_limit", 50, 1, 500);
                var response = await service.GetSessionAgentStatesAsync(
                    sessionId,
                    includeHistory,
                    historyLimit,
                    ct);
                return ProtoOrNotFound(response);
            });

        sessions.MapGet("/{sessionId}/agents/histories",
            async (string sessionId, HttpRequest request, CognitiveSessionService service, CancellationToken ct) =>
            {
                var historyLimit = ReadInt(request, "history_limit", 50, 1, 500);
                var response = await service.GetSessionAgentHistoriesAsync(sessionId, historyLimit, ct);
                return ProtoOrNotFound(response);
            });

        sessions.MapGet("/{sessionId}/memory/session",
            async (string sessionId, HttpRequest request, CognitiveSessionService service, CancellationToken ct) =>
            {
                var limit = ReadInt(request, "limit", 200, 1, 2000);
                var response = await service.GetSessionMemoryEntriesAsync(sessionId, limit, ct);
                return ProtoOrNotFound(response);
            });

        sessions.MapGet("/{sessionId}/memory/agents/{agentId}",
            async (string sessionId, string agentId, HttpRequest request, CognitiveSessionService service, CancellationToken ct) =>
            {
                var limit = ReadInt(request, "limit", 200, 1, 2000);
                var response = await service.GetAgentMemoryEntriesAsync(sessionId, agentId, limit, ct);
                return ProtoOrNotFound(response);
            });

        sessions.MapGet("/{sessionId}/memory/resources",
            async (string sessionId, HttpRequest request, CognitiveSessionService service, CancellationToken ct) =>
            {
                var scopeType = ReadScopeType(request);
                var limit = ReadInt(request, "limit", 200, 1, 2000);
                var response = await service.ListSessionMemoryResourcesAsync(sessionId, scopeType, limit, ct);
                return ProtoOrNotFound(response);
            });

        sessions.MapGet("/{sessionId}/trace",
            async (string sessionId, CognitiveSessionService service, CancellationToken ct) =>
            {
                var response = await service.GetSessionTraceAsync(sessionId, ct);
                return ProtoOrNotFound(response);
            });

        app.MapGet("/api/workflows", (CognitiveSessionService service) =>
        {
            var response = service.ListWorkflows();
            return ProtoHttp.Ok(response);
        });

        return app;
    }

    private static IResult ProtoOrNotFound<T>(T? message) where T : class, Google.Protobuf.IMessage
        => message == null ? Results.NotFound() : ProtoHttp.Ok(message);

    private static int ReadInt(HttpRequest request, string key, int fallback, int min, int max)
    {
        var raw = request.Query.TryGetValue(key, out var values) ? values.ToString() : string.Empty;
        if (!int.TryParse(raw, out var value))
            return fallback;
        return Math.Clamp(value, min, max);
    }

    private static bool ReadBool(HttpRequest request, string key, bool fallback)
    {
        var raw = request.Query.TryGetValue(key, out var values) ? values.ToString() : string.Empty;
        if (string.IsNullOrWhiteSpace(raw))
            return fallback;
        return bool.TryParse(raw, out var value) ? value : fallback;
    }

    private static MemoryScopeType ReadScopeType(HttpRequest request)
    {
        var raw = request.Query.TryGetValue("scope_type", out var values) ? values.ToString() : string.Empty;
        if (string.IsNullOrWhiteSpace(raw))
            return MemoryScopeType.Session;

        return Enum.TryParse<MemoryScopeType>(raw, ignoreCase: true, out var parsed)
            ? parsed
            : MemoryScopeType.Session;
    }
}
