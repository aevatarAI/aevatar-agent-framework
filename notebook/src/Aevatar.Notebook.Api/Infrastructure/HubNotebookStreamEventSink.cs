using System.Text.Json;
using Aevatar.Notebook.Streaming;

namespace Aevatar.Notebook.Api.Infrastructure;

// ============================================================
//  HubNotebookStreamEventSink
//
//  Purpose:
//  - Emit tool_start / tool_end NDJSON lines into a BroadcastEventHub<string>.
//  - Used by background runs so generation can continue after client disconnects.
//
//  Note:
//  - INotebookStreamEventSink is internal (in Core) but exposed to Api via InternalsVisibleTo.
// ============================================================
internal sealed class HubNotebookStreamEventSink : INotebookStreamEventSink
{
    private readonly BroadcastEventHub<string> _hub;
    private readonly JsonSerializerOptions _json;

    public HubNotebookStreamEventSink(BroadcastEventHub<string> hub, JsonSerializerOptions json)
    {
        _hub = hub ?? throw new ArgumentNullException(nameof(hub));
        _json = json ?? throw new ArgumentNullException(nameof(json));
    }

    public Task EmitToolStartAsync(string toolCallId, string toolName, CancellationToken ct)
    {
        Publish(new { type = "tool_start", toolCallId, toolName });
        return Task.CompletedTask;
    }

    public Task EmitToolEndAsync(
        string toolCallId,
        string toolName,
        bool success,
        long durationMs,
        string? error,
        CancellationToken ct)
    {
        Publish(new
        {
            type = "tool_end",
            toolCallId,
            toolName,
            success,
            durationMs,
            error = Trunc(error, 200)
        });
        return Task.CompletedTask;
    }

    private void Publish(object payload)
    {
        try
        {
            _hub.Publish(JsonSerializer.Serialize(payload, _json));
        }
        catch
        {
            // best-effort
        }
    }

    private static string Trunc(string? s, int maxChars)
    {
        var x = (s ?? string.Empty).Replace("\r", "").Trim();
        if (x.Length <= maxChars)
            return x;
        return x[..maxChars];
    }
}


