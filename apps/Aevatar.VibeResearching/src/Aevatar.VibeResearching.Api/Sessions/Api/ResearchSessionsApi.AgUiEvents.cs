using Aevatar.Agents.Abstractions;
using Aevatar.Agents.AGUI;
using Aevatar.Agents.Sessions;
using Aevatar.Agents.Sessions.Runtime;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace VibeResearching.Api.Sessions;

internal static partial class ResearchSessionsApi
{
    private static void MapAgUiEvents(WebApplication app)
    {
        app.MapGet("/api/sessions/{sessionId}/agui/events", async (
            HttpContext http,
            string sessionId,
            SessionRuntime runtime,
            CognitiveSessionService sessionStore,
            IGAgentActorManager actorManager,
            IEnumerable<ISessionAgUiBootstrapper> bootstrappers,
            IOptions<SessionRuntimeOptions> options,
            CancellationToken ct) =>
        {
            var state = await sessionStore.GetSessionStateAsync(sessionId, ct);
            if (state == null)
            {
                http.Response.StatusCode = StatusCodes.Status404NotFound;
                await http.Response.WriteAsJsonAsync(new { error = "session not found" }, cancellationToken: ct);
                return;
            }

            var primary = await runtime.ResolvePrimaryAgentAsync(sessionId, ct);
            if (primary == null)
            {
                http.Response.StatusCode = StatusCodes.Status404NotFound;
                await http.Response.WriteAsJsonAsync(new { error = "session not found" }, cancellationToken: ct);
                return;
            }

            var stream = await runtime.GetSessionStreamAsync(sessionId, ct);
            if (stream == null)
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
            await using var writer = new AgUiSseWriter(http.Response, Json);

            var bootstrapContext = new SessionAgUiBootstrapContext(
                state,
                primary,
                stream,
                actorManager,
                options.Value);

            foreach (var bootstrapper in bootstrappers)
            {
                var events = await bootstrapper.BuildAsync(bootstrapContext, ct);
                foreach (var evt in events)
                    await writer.WriteAsync(evt, ct);
            }

            await writer.WriteAsync(new CustomEvent
            {
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                Name = "SSE_CONNECTED",
                Value = new { sessionId }
            }, ct);

            await foreach (var evt in stream.SubscribeAsync(ct))
            {
                ct.ThrowIfCancellationRequested();
                await writer.WriteAsync(evt, ct);
            }
        });
    }
}
