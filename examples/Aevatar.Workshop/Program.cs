using System.Net;
using Aevatar.Agents.AI.Abstractions.Configuration;
using Aevatar.Agents.AI.Abstractions.Providers;
using Aevatar.Agents.AI.Core;
using Aevatar.Agents.AI.Core.Configuration;
using Aevatar.Agents.AI.Tool.MCP.Configuration;
using Aevatar.Agents.AI.MEAI;
using Aevatar.Agents.AI.Tools;
using Aevatar.Agents.Core.Extensions;
using Aevatar.Agents.Core.Secrets;
using Aevatar.Agents.Persistence.SQLite.GAgent.DependencyInjection;
using Aevatar.Agents.Persistence.SQLite.GAgent.Stores;
using Aevatar.Agents.Persistence.SQLite.Memory.DependencyInjection;
using Aevatar.Agents.Runtime.Local;
using Aevatar.Agents.Sessions;
using Aevatar.Agents.Sessions.Bootstrap;
using Aevatar.Agents.Sessions.Endpoints;
using Aevatar.Agents.Sessions.Runtime;
using Aevatar.Agents.Sessions.Services;
using Aevatar.Agents.Workspaces;
using Aevatar.Agents.Workspaces.Bootstrap;
using Aevatar.Agents.Workspaces.Core;
using Aevatar.Agents.Workspaces.Endpoints;
using Aevatar.Workshop;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration
    .AddJsonFile("appsettings.json", optional: true)
    .AddJsonFile("mcp.json", optional: true, reloadOnChange: true)
    .AddAevatarUserConfig() // ~/.aevatar/secrets.json (Aevatar.Config)
    .AddJsonFile("appsettings.secrets.json", optional: true)
    .AddEnvironmentVariables();

var sessionOptions = new SessionRuntimeOptions();
builder.Configuration.GetSection("Aevatar.Workshop").Bind(sessionOptions);
SessionWorkflowYamlBootstrap.EnsureDefaultWorkflowYaml(sessionOptions);

var workspaceOptions = new RoleWorkspaceOptions();
builder.Configuration.GetSection("Aevatar.Workshop").Bind(workspaceOptions);
RoleWorkspaceYamlBootstrap.EnsureDefaultRoleYaml(workspaceOptions);

builder.Services.Configure<LLMProvidersConfig>(builder.Configuration.GetSection("LLMProviders"));
builder.Services.AddAevatarUserSecretsStore();

builder.Services.AddSingleton<ILLMProviderFactory, MEAILLMProviderFactory>();
builder.Services.AddAevatarAiToolsPack();
builder.Services.AddAevatarCognitiveSessions(options =>
{
    builder.Configuration.GetSection("Aevatar.Sessions").Bind(options);
});
builder.Services.AddAevatarSessionRuntime(options =>
{
    builder.Configuration.GetSection("Aevatar.Workshop").Bind(options);
});
builder.Services.AddAevatarSessionTooling(options =>
{
    builder.Configuration.GetSection("Aevatar.Workshop").Bind(options);
});
builder.Services.AddAevatarRoleWorkspace(options =>
{
    builder.Configuration.GetSection("Aevatar.Workshop").Bind(options);
});
builder.Services.TryAddSingleton<IEventRouteEvaluator, DefaultEventRouteEvaluator>();

// var sqliteConn = builder.Configuration["Aevatar.Workshop:SqliteConnection"];
// if (string.IsNullOrWhiteSpace(sqliteConn))
// {
//     var dbPath = Path.Combine(builder.Environment.ContentRootPath, "workshop-demo.db");
//     sqliteConn = $"Data Source={dbPath}";
// }
// builder.Services.AddAevatarSQLiteGAgent(sqliteConn);
// builder.Services.AddAevatarSQLiteMemory();

builder.Services.AddAevatarAgentSystem(
   // configureStores: options => { options.StateStoreType = typeof(SQLiteStateStore<>); },
    configure: b => b.UseLocalRuntime());

builder.Services.AddHostedService<SessionCleanupService>();

var app = builder.Build();

// Port policy: never use 5000. Use 5691 unless user overrides.
var configuredUrls = builder.Configuration["urls"];
if (string.IsNullOrWhiteSpace(configuredUrls))
{
    app.Urls.Clear();
    app.Urls.Add("http://127.0.0.1:5691");
}

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapAevatarSessionApi();
app.MapSessionUiEndpoints();
app.MapRoleWorkspaceEndpoints();

app.MapGet("/api/llm/providers", (IOptionsMonitor<LLMProvidersConfig> llm) =>
{
    var cfg = llm.CurrentValue;
    var fallback = cfg.Providers.Keys.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).FirstOrDefault() ?? string.Empty;
    var defaultProvider = string.IsNullOrWhiteSpace(cfg.Default) ? fallback : cfg.Default;

    var providers = cfg.Providers
        .Select(kv => new
        {
            name = kv.Key,
            providerType = kv.Value.ProviderType,
            model = kv.Value.Model,
            endpoint = kv.Value.Endpoint ?? string.Empty,
            enableStreaming = kv.Value.EnableStreaming,
            hasApiKey = !string.IsNullOrWhiteSpace(kv.Value.ApiKey)
        })
        .OrderBy(p => p.name, StringComparer.OrdinalIgnoreCase)
        .ToList();

    return Results.Ok(new { defaultProvider, providers });
});

app.MapGet("/api/llm/default", (HttpContext http, IOptionsMonitor<LLMProvidersConfig> llm) =>
{
    if (!IsLocal(http))
        return Results.Forbid();

    var cfg = llm.CurrentValue;
    var fallback = cfg.Providers.Keys.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).FirstOrDefault() ?? string.Empty;
    var defaultProvider = string.IsNullOrWhiteSpace(cfg.Default) ? fallback : cfg.Default;
    return Results.Ok(new { ok = true, providerName = defaultProvider });
});

app.MapPost("/api/llm/default", (
    HttpContext http,
    SetDefaultProviderInput input,
    IAevatarUserSecretsStore secrets) =>
{
    if (!IsLocal(http))
        return Results.Forbid();

    var name = (input.ProviderName ?? string.Empty).Trim();
    if (string.IsNullOrWhiteSpace(name))
        return Results.BadRequest(new { ok = false, error = "providerName is required" });

    secrets.Set("LLMProviders:Default", name);
    return Results.Ok(new { ok = true, providerName = name });
});

app.MapGet("/api/llm/api-key/{providerName}", (
    string providerName,
    bool? reveal,
    HttpContext http,
    IAevatarUserSecretsStore secrets,
    IOptionsMonitor<LLMProvidersConfig> llm) =>
{
    if (!IsLocal(http))
        return Results.Forbid();

    var key = $"LLMProviders:Providers:{providerName}:ApiKey";
    string? value = null;
    if (!secrets.TryGet(key, out value) || string.IsNullOrWhiteSpace(value))
    {
        llm.CurrentValue.Providers.TryGetValue(providerName, out var cfg);
        value = cfg?.ApiKey;
    }

    if (string.IsNullOrWhiteSpace(value))
    {
        return Results.Ok(new { ok = true, configured = false, masked = "" });
    }

    var trimmed = value.Trim();
    var masked = MaskSecret(trimmed);
    if (reveal == true && secrets.TryGet(key, out var secretValue))
    {
        return Results.Ok(new { ok = true, configured = true, masked, value = secretValue?.Trim() ?? trimmed });
    }

    return Results.Ok(new { ok = true, configured = true, masked });
});

app.MapPost("/api/llm/api-key", (
    HttpContext http,
    SetApiKeyInput input,
    IAevatarUserSecretsStore secrets) =>
{
    if (!IsLocal(http))
        return Results.Forbid();

    var name = (input.ProviderName ?? string.Empty).Trim();
    var apiKey = (input.ApiKey ?? string.Empty).Trim();
    if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(apiKey))
        return Results.BadRequest(new { ok = false, error = "providerName and apiKey are required" });

    secrets.Set($"LLMProviders:Providers:{name}:ApiKey", apiKey);
    return Results.Ok(new { ok = true });
});

app.MapDelete("/api/llm/api-key/{providerName}", (
    string providerName,
    HttpContext http,
    IAevatarUserSecretsStore secrets) =>
{
    if (!IsLocal(http))
        return Results.Forbid();

    var name = (providerName ?? string.Empty).Trim();
    if (string.IsNullOrWhiteSpace(name))
        return Results.BadRequest(new { ok = false, error = "providerName is required" });

    var removed = secrets.Remove($"LLMProviders:Providers:{name}:ApiKey");
    return Results.Ok(new { ok = true, removed });
});

app.MapGet("/api/mcp/servers", (IConfiguration cfg) =>
{
    var resolved = MCPServersConfigReader.Resolve(cfg);
    var servers = resolved.Servers
        .Where(s => s.Enabled)
        .Select(s => new
        {
            key = s.Key,
            name = s.Config.Name,
            transport = s.Config.TransportType.ToString(),
            url = s.Config.ServerUrl ?? string.Empty,
            command = s.Config.Command ?? string.Empty,
            args = s.Config.Arguments ?? new List<string>(),
            timeoutMs = s.Config.TimeoutMs
        })
        .ToList();

    return Results.Ok(new
    {
        source = resolved.Source,
        autoConnect = resolved.AutoConnect,
        namespaceTools = resolved.NamespaceTools,
        servers
    });
});

app.MapGet("/healthz", () => Results.Ok("ok"));

app.Run();

static bool IsLocal(HttpContext http)
{
    var ip = http.Connection.RemoteIpAddress;
    return ip == null || IPAddress.IsLoopback(ip);
}

static string MaskSecret(string value)
{
    if (string.IsNullOrWhiteSpace(value))
        return string.Empty;

    var trimmed = value.Trim();
    if (trimmed.Length <= 6)
        return new string('*', trimmed.Length);

    return $"{trimmed[..2]}{new string('*', trimmed.Length - 4)}{trimmed[^2..]}";
}
