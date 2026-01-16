using Aevatar.Platform.Core.Config;
using Aevatar.Platform.Core.Sessions;
using Aevatar.Platform.Server.Endpoints;

var builder = WebApplication.CreateBuilder(args);

var effective = new AevatarConfigLoader().Load();
var sessionsRoot = Path.Combine(effective.ConfigDirectory, "sessions");

builder.Services.AddSingleton(effective);
builder.Services.AddSingleton<FileEventStore>(_ => new FileEventStore(sessionsRoot));
builder.Services.AddSingleton<SessionService>(sp =>
    new SessionService(sp.GetRequiredService<FileEventStore>(), effective.ConfigDirectory));

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
        policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod());
});

ApplyServerUrls(builder);

var app = builder.Build();

app.UseCors();
UseAuthMiddleware(app);

app.MapGet("/", () => Results.Text("Aevatar Platform Server"));
SessionsEndpoints.Map(app);

app.Run();

static void ApplyServerUrls(WebApplicationBuilder builder)
{
    var host = (Environment.GetEnvironmentVariable("AEVATAR_SERVER_HOST") ?? string.Empty).Trim();
    if (host.Length == 0)
        host = "127.0.0.1";

    var portRaw = (Environment.GetEnvironmentVariable("AEVATAR_SERVER_PORT") ?? string.Empty).Trim();
    var port = int.TryParse(portRaw, out var parsed) ? parsed : 5678;
    if (port == 5000)
        port = 5678;

    builder.WebHost.UseUrls($"http://{host}:{port}");
}

static void UseAuthMiddleware(WebApplication app)
{
    var token = (Environment.GetEnvironmentVariable("AEVATAR_SERVER_AUTH_TOKEN") ?? string.Empty).Trim();
    if (token.Length == 0)
        return;

    app.Use(async (ctx, next) =>
    {
        if (ctx.Request.Path.StartsWithSegments("/api"))
        {
            var ok = IsAuthorized(ctx, token);
            if (!ok)
            {
                ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await ctx.Response.WriteAsync("Unauthorized");
                return;
            }
        }

        await next();
    });
}

static bool IsAuthorized(HttpContext ctx, string token)
{
    if (ctx.Request.Headers.TryGetValue("Authorization", out var auth))
    {
        var value = auth.ToString();
        if (value.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            var provided = value[7..].Trim();
            if (string.Equals(provided, token, StringComparison.Ordinal))
                return true;
        }
    }

    if (ctx.Request.Headers.TryGetValue("X-API-Key", out var key))
    {
        var provided = key.ToString().Trim();
        if (string.Equals(provided, token, StringComparison.Ordinal))
            return true;
    }

    return false;
}


