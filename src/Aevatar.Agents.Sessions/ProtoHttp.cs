using Google.Protobuf;
using Microsoft.AspNetCore.Http;

namespace Aevatar.Agents.Sessions;

internal static class ProtoHttp
{
    public const string ContentType = "application/x-protobuf";

    public static async Task<T> ReadAsync<T>(HttpRequest request, CancellationToken ct)
        where T : IMessage<T>, new()
    {
        var parser = new MessageParser<T>(() => new T());
        await using var stream = new MemoryStream();
        await request.Body.CopyToAsync(stream, ct);
        stream.Position = 0;
        return parser.ParseFrom(stream);
    }

    public static IResult Ok<T>(T message) where T : IMessage
        => Results.Bytes(message.ToByteArray(), ContentType);
}
