using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http;

namespace Aevatar.Agents.AGUI;

public sealed class AgUiSseWriter : IAsyncDisposable
{
    private readonly HttpResponse _response;
    private readonly JsonSerializerOptions _jsonOptions;

    public AgUiSseWriter(HttpResponse response, JsonSerializerOptions? jsonOptions = null)
    {
        _response = response ?? throw new ArgumentNullException(nameof(response));
        _jsonOptions = jsonOptions ?? new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
    }

    public async Task WriteAsync(AgUiEvent evt, CancellationToken ct)
    {
        if (evt == null)
            return;

        // 直接写入 response body，无中间缓冲
        var json = JsonSerializer.Serialize((object)evt, evt.GetType(), _jsonOptions);
        var bytes = Encoding.UTF8.GetBytes($"data: {json}\n\n");
        await _response.Body.WriteAsync(bytes, ct);
        await _response.Body.FlushAsync(ct);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
