using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Aevatar.Agents.AGUI;
using Aevatar.Agents.AI.Abstractions.Configuration;
using Aevatar.Agents.AI.Abstractions.Providers;
using Aevatar.Agents.AI.MEAI;
using Aevatar.Agents.Core.Extensions;
using Aevatar.Agents.Runtime.Local;
using Microsoft.Extensions.Options;
using ProgressHookChatWebDemo;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration
    .AddJsonFile("appsettings.json", optional: true)
    .AddAevatarUserConfig() // ~/.aevatar/secrets.json (Aevatar.Config)
    .AddJsonFile("appsettings.secrets.json", optional: true)
    .AddEnvironmentVariables();

builder.Services.Configure<DemoOptions>(builder.Configuration.GetSection("ProgressHookChatWebDemo"));
builder.Services.Configure<LLMProvidersConfig>(builder.Configuration.GetSection("LLMProviders"));

builder.Services.AddSingleton<ILLMProviderFactory, MEAILLMProviderFactory>();
builder.Services.AddAevatarLocalRuntime();
builder.Services.AddSingleton<SessionStore>();

var app = builder.Build();

var jsonOptions = new JsonSerializerOptions
{
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
};

// Port policy: never use 5000. Use 5690 unless user overrides.
var configuredUrls = builder.Configuration["urls"];
if (string.IsNullOrWhiteSpace(configuredUrls))
{
    app.Urls.Clear();
    app.Urls.Add("http://127.0.0.1:5690");
}

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/api/sessions/new", (SessionStore store) =>
{
    var id = Guid.NewGuid().ToString("N");
    store.GetOrCreate(id);
    return Results.Ok(new { sessionId = id });
});

app.MapPost("/api/sessions/{sessionId}/input", async (
    string sessionId,
    HttpRequest req,
    SessionStore store,
    CancellationToken ct) =>
{
    var input = await req.ReadFromJsonAsync<ChatInput>(cancellationToken: ct);
    var message = (input?.Message ?? string.Empty).Trim();
    if (message.Length == 0)
        return Results.BadRequest(new { error = "message is required" });

    var session = store.GetOrCreate(sessionId);
    var runId = await session.StartInputAsync(message, ct);
    return Results.Ok(new { runId });
});

app.MapGet("/api/sessions/{sessionId}/agui/events", async (
    HttpContext http,
    string sessionId,
    SessionStore store,
    IOptions<DemoOptions> options,
    CancellationToken ct) =>
{
    var session = store.GetOrCreate(sessionId);

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
        var line = JsonSerializer.Serialize((object)evt, evt.GetType(), jsonOptions);
        await writer.WriteAsync("data: ");
        await writer.WriteLineAsync(line);
        await writer.WriteLineAsync();
        await writer.FlushAsync();
    }

    var snapshot = session.GetMessagesSnapshot(options.Value.MaxSnapshotMessages);
    await WriteSseAsync(new MessagesSnapshotEvent
    {
        Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
        Messages = snapshot.ToList()
    }, ct);

    await foreach (var evt in session.SubscribeAsync(ct))
    {
        ct.ThrowIfCancellationRequested();
        await WriteSseAsync(evt, ct);
    }
});

app.MapGet("/healthz", () => Results.Ok("ok"));

app.Run();

public sealed record ChatInput(string? Message);
