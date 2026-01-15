using System.Text;
using Google.Protobuf;

namespace Aevatar.Novel.Sidecar.Api;

// ============================================================
//  ProtoJsonHttp
//
//  PURPOSE:
//  - Keep UI <-> Sidecar boundary strictly Protobuf-defined,
//    while using JSON as a transport-friendly encoding for HTTP.
// ============================================================

internal static class ProtoJsonHttp
{
    private static readonly JsonParser Parser = new(JsonParser.Settings.Default.WithIgnoreUnknownFields(true));
    private static readonly JsonFormatter Formatter = new(new JsonFormatter.Settings(formatDefaultValues: true));

    public static async Task<T> ReadJsonAsync<T>(HttpRequest request, CancellationToken ct)
        where T : IMessage<T>, new()
    {
        using var reader = new StreamReader(request.Body, Encoding.UTF8);
        var json = await reader.ReadToEndAsync(ct);
        if (string.IsNullOrWhiteSpace(json))
            return new T();

        return Parser.Parse<T>(json);
    }

    public static IResult Json(IMessage message)
    {
        // Minimal API doesn't know how to serialize Protobuf messages by default.
        // We output Protobuf JSON explicitly.
        var json = Formatter.Format(message);
        return Results.Text(json, "application/json; charset=utf-8");
    }
}


