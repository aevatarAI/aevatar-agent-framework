using System.Text;
using Aevatar.Agents.AI.Abstractions.Configuration;
using Aevatar.Agents.AI.Abstractions.Providers;
using Aevatar.Agents.AI.MEAI;
using Aevatar.Agents.Core.Extensions;
using Aevatar.Agents.Runtime.Local;
using InterruptibleChatWebDemo;

var builder = WebApplication.CreateBuilder(args);

// Demo config: support appsettings.secrets.json + env vars for OPENAI_API_KEY
builder.Configuration
    .AddJsonFile("appsettings.json", optional: true)
    .AddAevatarUserSecrets()
    .AddJsonFile("appsettings.secrets.json", optional: true)
    .AddEnvironmentVariables();

builder.Services.Configure<DemoOptions>(builder.Configuration.GetSection("InterruptibleChatWebDemo"));
builder.Services.Configure<LLMProvidersConfig>(builder.Configuration.GetSection("LLMProviders"));

// LLM provider factory (OpenAI-compatible + Azure OpenAI via MEAI)
builder.Services.AddSingleton<ILLMProviderFactory, MEAILLMProviderFactory>();

// Needed so agents can be created with proper injectors (Logger, LLMProviderFactory, etc.)
builder.Services.AddAevatarLocalRuntime();

builder.Services.AddSingleton<SessionStore>();

var app = builder.Build();

// Port policy: never use 5000. For demos, bind a safe default unless user explicitly sets urls.
// - Respect command-line `--urls` / env `ASPNETCORE_URLS` / config `urls`
// - Otherwise bind to 5688 to avoid conflicts with SRA (often on 5678/5679)
var configuredUrls = builder.Configuration["urls"];
if (string.IsNullOrWhiteSpace(configuredUrls))
{
    app.Urls.Clear();
    app.Urls.Add("http://127.0.0.1:5688");
}

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/api/sessions/new", () =>
{
    var id = Guid.NewGuid().ToString("N");
    return Results.Ok(new { sessionId = id });
});

app.MapPost("/api/sessions/{sessionId}/input", async (string sessionId, HttpRequest req, SessionStore store) =>
{
    using var reader = new StreamReader(req.Body, Encoding.UTF8);
    var body = await reader.ReadToEndAsync();
    if (string.IsNullOrWhiteSpace(body))
        return Results.BadRequest(new { error = "empty body" });

    // Minimal JSON parsing (avoid extra deps)
    var msg = ExtractJsonField(body, "message");
    if (string.IsNullOrWhiteSpace(msg))
        return Results.BadRequest(new { error = "missing field: message" });

    var session = store.GetOrCreate(sessionId);
    var runId = await session.StartInputAsync(msg);
    return Results.Ok(new { runId });
});

app.MapGet("/api/sessions/{sessionId}/events", async (string sessionId, HttpContext ctx, SessionStore store) =>
{
    ctx.Response.Headers.CacheControl = "no-cache";
    ctx.Response.Headers.Connection = "keep-alive";
    ctx.Response.Headers.ContentType = "text/event-stream";

    var session = store.GetOrCreate(sessionId);
    var reader = session.Subscribe();

    // initial comment to open stream
    await ctx.Response.WriteAsync(":ok\n\n");
    await ctx.Response.Body.FlushAsync();

    await foreach (var jsonLine in reader.ReadAllAsync(ctx.RequestAborted))
    {
        // Single event type "message" with JSON payload.
        await ctx.Response.WriteAsync("event: message\n");
        await ctx.Response.WriteAsync($"data: {jsonLine}\n\n");
        await ctx.Response.Body.FlushAsync();
    }
});

app.MapGet("/healthz", () => Results.Ok("ok"));

app.Run();

static string? ExtractJsonField(string rawJson, string field)
{
    // Super small JSON extractor for demo payloads like {"message":"..."}.
    // Not a general JSON parser; keeps demo dependency-free.
    var needle = $"\"{field}\"";
    var i = rawJson.IndexOf(needle, StringComparison.Ordinal);
    if (i < 0) return null;
    i = rawJson.IndexOf(':', i);
    if (i < 0) return null;
    i++;

    // Skip whitespace
    while (i < rawJson.Length && char.IsWhiteSpace(rawJson[i])) i++;
    if (i >= rawJson.Length) return null;

    if (rawJson[i] != '"') return null;
    i++;

    var sb = new StringBuilder();
    while (i < rawJson.Length)
    {
        var c = rawJson[i++];
        if (c == '"') break;
        if (c == '\\' && i < rawJson.Length)
        {
            var esc = rawJson[i++];
            sb.Append(esc switch
            {
                '"' => '"',
                '\\' => '\\',
                '/' => '/',
                'b' => '\b',
                'f' => '\f',
                'n' => '\n',
                'r' => '\r',
                't' => '\t',
                _ => esc
            });
            continue;
        }
        sb.Append(c);
    }
    return sb.ToString();
}


