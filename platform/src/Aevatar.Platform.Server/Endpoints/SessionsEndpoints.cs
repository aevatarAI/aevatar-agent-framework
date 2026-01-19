using System.Linq;
using System.Text;
using System.Text.Json;
using Aevatar.Platform;
using Aevatar.Platform.Core.Config;
using Aevatar.Platform.Core.Sessions;
using Aevatar.Platform.Core.Workflow;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;

namespace Aevatar.Platform.Server.Endpoints;

// ============================================================
//  SessionsEndpoints
//
//  说明：
//  - 提供 serve/web/attach 的最小后端接口
//  - SSE: snapshot-first + 增量事件流
// ============================================================
public static class SessionsEndpoints
{
    private static readonly JsonFormatter ProtoJson = new JsonFormatter(
        JsonFormatter.Settings.Default
            .WithFormatDefaultValues(true)
            .WithPreserveProtoFieldNames(true));

    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/sessions", async (SessionService sessions, CancellationToken ct) =>
        {
            var list = await sessions.ListSessionsAsync(ct);
            return Results.Json(list);
        });

        app.MapGet("/api/sessions/{id}", async (string id, SessionService sessions, CancellationToken ct) =>
        {
            var state = await sessions.GetSessionStateAsync(id, ct);
            return state == null
                ? Results.NotFound()
                : JsonMessage(state);
        });

        app.MapPost("/api/sessions", async (
            CreateSessionRequest? request,
            SessionService sessions,
            AevatarEffectiveConfig effective,
            CancellationToken ct) =>
        {
            var state = new PlatformSessionState
            {
                SessionId = string.Empty,
                Profile = request?.Profile ?? effective.Config.Agents.DefaultProfile,
                ActiveWorkflow = request?.Workflow ?? effective.Config.Agents.DefaultWorkflow,
                WorkingDirectory = request?.WorkingDirectory ?? Directory.GetCurrentDirectory(),
                Provider = request?.Provider ?? effective.Config.Models.DefaultProvider ?? string.Empty,
                Model = request?.Model ?? effective.Config.Models.DefaultModel ?? string.Empty
            };

            await sessions.CreateSessionAsync(state, ct);
            return Results.Json(new
            {
                session_id = state.SessionId,
                profile = state.Profile,
                workflow = state.ActiveWorkflow
            });
        });

        app.MapGet("/api/sessions/{id}/events", async (string id, SessionService sessions, CancellationToken ct) =>
        {
            var events = await sessions.GetSessionEventsAsync(id, ct);
            return Results.Text(SerializeMessages(events), "application/json", Encoding.UTF8);
        });

        app.MapPost("/api/sessions/{id}/messages", async (
            string id,
            UserMessageRequest? request,
            SessionService sessions,
            AevatarEffectiveConfig effective,
            CancellationToken ct) =>
        {
            var state = await sessions.GetSessionStateAsync(id, ct);
            if (state == null)
                return Results.NotFound();

            var events = await sessions.GetSessionEventsAsync(id, ct);
            var seq = events.Count > 0 ? events.Max(e => e.Seq) + 1 : 1;

            var messageId = Guid.NewGuid().ToString("N");
            var evt = new PlatformSessionEvent
            {
                Seq = seq,
                Timestamp = Timestamp.FromDateTime(DateTime.UtcNow),
                UserMessage = new UserMessageEvent
                {
                    MessageId = messageId,
                    Text = request?.Text ?? string.Empty,
                    AttachedFiles = { request?.AttachedFiles ?? new List<string>() }
                }
            };

            await sessions.AppendEventAsync(id, evt, ct);

            var output = await WorkflowEngine.RunWorkflowAsync(
                workflow: state.ActiveWorkflow,
                configDir: effective.ConfigDirectory,
                configPath: effective.ConfigPath,
                secretsPath: effective.SecretsPath,
                workingDirectory: state.WorkingDirectory ?? Directory.GetCurrentDirectory(),
                defaultProvider: effective.Config.Models.DefaultProvider,
                defaultModel: effective.Config.Models.DefaultModel,
                prompt: request?.Text ?? string.Empty,
                attachedFiles: request?.AttachedFiles ?? new List<string>(),
                toolsConfig: effective.Config.Tools,
                ct: ct);
            var agentEvent = new PlatformSessionEvent
            {
                Seq = ++seq,
                Timestamp = Timestamp.FromDateTime(DateTime.UtcNow),
                AgentOutputDelta = new AgentOutputDeltaEvent
                {
                    Agent = state.Profile,
                    MessageId = messageId,
                    Delta = output,
                    IsFinal = true
                }
            };

            await sessions.AppendEventAsync(id, agentEvent, ct);
            return Results.Ok(new { message_id = messageId, output });
        });

        app.MapGet("/api/sessions/{id}/stream", async (HttpContext ctx, string id, SessionService sessions) =>
        {
            ctx.Response.Headers.ContentType = "text/event-stream";
            ctx.Response.Headers.CacheControl = "no-cache";
            ctx.Response.Headers.Connection = "keep-alive";
            ctx.Response.Headers.Append("X-Accel-Buffering", "no");

            var ct = ctx.RequestAborted;
            var state = await sessions.GetSessionStateAsync(id, ct);
            if (state == null)
            {
                await WriteSseAsync(ctx, "error", "{\"code\":\"session_not_found\"}", ct);
                return;
            }

            var snapshotJson = SerializeSnapshot(state, 0);
            await WriteSseAsync(ctx, "snapshot", snapshotJson, ct);

            var events = await sessions.GetSessionEventsAsync(id, ct);
            ulong lastSeq = 0;
            foreach (var evt in events.OrderBy(e => e.Seq))
            {
                lastSeq = Math.Max(lastSeq, evt.Seq);
                await WriteSseAsync(ctx, "event", ProtoJson.Format(evt), ct);
            }

            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(500, ct);

                var latest = await sessions.GetSessionEventsAsync(id, ct);
                foreach (var evt in latest.Where(e => e.Seq > lastSeq).OrderBy(e => e.Seq))
                {
                    lastSeq = evt.Seq;
                    await WriteSseAsync(ctx, "event", ProtoJson.Format(evt), ct);
                }
            }
        });
    }

    private static IResult JsonMessage(IMessage message)
        => Results.Text(ProtoJson.Format(message), "application/json", Encoding.UTF8);

    private static string SerializeMessages(IReadOnlyList<PlatformSessionEvent> events)
    {
        if (events.Count == 0)
            return "[]";

        var items = events
            .OrderBy(e => e.Seq)
            .Select(e => ProtoJson.Format(e));

        return "[" + string.Join(",", items) + "]";
    }

    private static string SerializeSnapshot(PlatformSessionState state, ulong lastSeq)
    {
        var stateJson = ProtoJson.Format(state);
        return $"{{\"state\":{stateJson},\"last_seq\":{lastSeq}}}";
    }


    private static async Task WriteSseAsync(HttpContext ctx, string eventName, string data, CancellationToken ct)
    {
        await ctx.Response.WriteAsync($"event: {eventName}\n", ct);
        await ctx.Response.WriteAsync($"data: {data}\n\n", ct);
        await ctx.Response.Body.FlushAsync(ct);
    }

    public sealed record CreateSessionRequest(
        string? Profile,
        string? Provider,
        string? Workflow,
        string? Model,
        string? WorkingDirectory);

    public sealed record UserMessageRequest(string? Text, List<string>? AttachedFiles);
}
