using System.Text;
using System.Text.Json;
using Aevatar.Agents.AGUI;
using Aevatar.Trade;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Aevatar.Trade.Api.AgUi;

internal static class TradeAgUiEndpoints
{
    public static void MapTradeAgUiEvents(this WebApplication app)
    {
        app.MapGet("/api/agui/messages", (TradeAgUiHub hub) =>
        {
            return Results.Ok(hub.GetMessagesSnapshot(maxMessages: 60));
        });

        app.MapPost("/api/agui/chat", async (AgUiChatRequest request, TradingSystem tradingSystem, CancellationToken ct) =>
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Message))
                return Results.BadRequest(new { error = "message is required" });

            try
            {
                var evt = await tradingSystem.SubmitUserChatAsync(
                    request.Message,
                    request.UserId,
                    "UI",
                    ct);
                return Results.Ok(evt);
            }
            catch (Exception ex)
            {
                return Results.Json(new { error = ex.Message }, statusCode: StatusCodes.Status500InternalServerError);
            }
        });

        app.MapGet("/api/agui/events", async (HttpContext http, TradeAgUiHub hub, CancellationToken ct) =>
        {
            http.Response.StatusCode = StatusCodes.Status200OK;
            http.Response.Headers.ContentType = "text/event-stream; charset=utf-8";
            http.Response.Headers.CacheControl = "no-store";
            http.Response.Headers.Pragma = "no-cache";
            http.Response.Headers["X-Accel-Buffering"] = "no";

            await http.Response.StartAsync(ct);

            await using var writer = new StreamWriter(
                http.Response.Body,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                bufferSize: 256,
                leaveOpen: true);

            async Task WriteSseAsync(AgUiEvent evt, CancellationToken token)
            {
                var line = JsonSerializer.Serialize((object)evt, evt.GetType(), TradeAgUiJson.Options);
                await writer.WriteAsync("data: ");
                await writer.WriteLineAsync(line);
                await writer.WriteLineAsync();
                await writer.FlushAsync();
            }

            long Ts(DateTimeOffset? ts) => (ts ?? DateTimeOffset.UtcNow).ToUnixTimeMilliseconds();

            await WriteSseAsync(new MessagesSnapshotEvent
            {
                Timestamp = Ts(DateTimeOffset.UtcNow),
                Messages = hub.GetMessagesSnapshot(maxMessages: 60)
            }, ct);

            await foreach (var evt in hub.Events.SubscribeAsync(replay: false, ct: ct))
            {
                ct.ThrowIfCancellationRequested();
                await WriteSseAsync(evt, ct);
            }
        });
    }
}

internal sealed class AgUiChatRequest
{
    public string Message { get; set; } = "";
    public string? UserId { get; set; }
}

