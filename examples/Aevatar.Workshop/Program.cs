using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Aevatar.Agents.AGUI;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.Attributes;
using Aevatar.Agents.Abstractions.Memory;
using Aevatar.Agents.AI.Abstractions.Configuration;
using Aevatar.Agents.AI.Abstractions.Providers;
using Aevatar.Agents.AI.Core;
using Aevatar.Agents.AI.Core.Configuration;
using Aevatar.Agents.AI.Core.Messages;
using Aevatar.Agents.AI.Tool.MCP.Configuration;
using Aevatar.Agents.AI.MEAI;
using Aevatar.Agents.AI.Tools;
using Aevatar.Agents.Core.Extensions;
using Aevatar.Agents.Core.Secrets;
using Aevatar.Agents.Persistence.SQLite.GAgent.DependencyInjection;
using Aevatar.Agents.Persistence.SQLite.GAgent.Stores;
using Aevatar.Agents.Persistence.SQLite.Memory.DependencyInjection;
using Aevatar.Agents.Runtime.Local;
using Aevatar.Workshop;
using Aevatar.Workshop.Messages;
using Aevatar.Workshop.RoleWorkspace;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration
    .AddJsonFile("appsettings.json", optional: true)
    .AddJsonFile("mcp.json", optional: true, reloadOnChange: true)
    .AddAevatarUserConfig() // ~/.aevatar/secrets.json (Aevatar.Config)
    .AddJsonFile("appsettings.secrets.json", optional: true)
    .AddEnvironmentVariables();

var workshopOptions = new WorkshopOptions();
builder.Configuration.GetSection("Aevatar.Workshop").Bind(workshopOptions);
WorkshopAgentYamlBootstrap.EnsureDefaultAgentYaml(workshopOptions);
WorkshopWorkflowYamlBootstrap.EnsureDefaultWorkflowYaml(workshopOptions);

builder.Services.Configure<WorkshopOptions>(builder.Configuration.GetSection("Aevatar.Workshop"));
builder.Services.Configure<LLMProvidersConfig>(builder.Configuration.GetSection("LLMProviders"));
builder.Services.AddAevatarUserSecretsStore();

builder.Services.AddSingleton<ILLMProviderFactory, MEAILLMProviderFactory>();
builder.Services.AddAevatarAiToolsPack();
builder.Services.AddSingleton<GlobalAgentYamlRegistry>();
builder.Services.AddSingleton<RoleAgentFactory>();
builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IEventModuleFactory, WorkshopEventModuleFactory>());
builder.Services.TryAddSingleton<IEventRouteEvaluator, DefaultEventRouteEvaluator>();

builder.Services.AddSingleton<WorkshopMeshCompiler>();
builder.Services.AddSingleton<WorkflowMeshService>();
builder.Services.AddSingleton<WorkshopToolCatalog>();
builder.Services.AddSingleton<WorkshopGroupAgUiHub>();
builder.Services.AddSingleton<WorkshopRoleWorkspace>();

var sqliteConn = builder.Configuration["Aevatar.Workshop:SqliteConnection"];
if (string.IsNullOrWhiteSpace(sqliteConn))
{
    var dbPath = Path.Combine(builder.Environment.ContentRootPath, "workshop-demo.db");
    sqliteConn = $"Data Source={dbPath}";
}
builder.Services.AddAevatarSQLiteGAgent(sqliteConn);
builder.Services.AddAevatarSQLiteMemory();

builder.Services.AddAevatarAgentSystem(
    configureStores: options => { options.StateStoreType = typeof(SQLiteStateStore<>); },
    configure: b => b.UseLocalRuntime());

builder.Services.AddSingleton<SessionStore>();

var app = builder.Build();

var jsonOptions = new JsonSerializerOptions
{
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
};

// Port policy: never use 5000. Use 5691 unless user overrides.
var configuredUrls = builder.Configuration["urls"];
if (string.IsNullOrWhiteSpace(configuredUrls))
{
    app.Urls.Clear();
    app.Urls.Add("http://127.0.0.1:5691");
}

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/api/sessions/new", (SessionStore store, string? mode, string? role) =>
{
    var session = store.CreateSession(ParseMode(mode), role);
    return Results.Ok(new { sessionId = session.SessionId, mode = session.Mode.ToString().ToLowerInvariant(), role = session.Role });
});

app.MapGet("/api/sessions", (SessionStore store) =>
{
    return Results.Ok(new { sessions = store.ListSessionSummaries() });
});

app.MapGet("/api/sessions/{sessionId}/info", async (
    string sessionId,
    SessionStore store,
    IOptions<WorkshopOptions> options,
    CancellationToken ct) =>
{
    var info = await store.GetSessionInfoAsync(sessionId, options.Value.MaxSnapshotMessages, ct);
    return Results.Ok(new
    {
        sessionId = info.SessionId,
        createdAt = info.CreatedAt,
        updatedAt = info.UpdatedAt,
        messageCount = info.MessageCount,
        memoryEnabled = info.MemoryEnabled,
        memoryEntries = info.MemoryEntries,
        memoryHasMore = info.MemoryHasMore,
        lastMessage = info.LastMessage == null
            ? null
            : new
            {
                id = info.LastMessage.Id,
                role = info.LastMessage.Role,
                content = info.LastMessage.Content
            }
    });
});

app.MapGet("/api/sessions/{sessionId}/state/history", async (
    string sessionId,
    SessionStore store,
    IOptions<WorkshopOptions> options,
    CancellationToken ct) =>
{
    var history = await store.GetStateHistoryAsync(sessionId, options.Value.MaxSnapshotMessages, ct);
    var list = history.Select(msg => new
        {
            id = msg.Id,
            role = msg.Role.ToString().ToLowerInvariant(),
            content = msg.Content ?? string.Empty,
            timestamp = msg.Timestamp == null
                ? null
                : msg.Timestamp.ToDateTime().ToUniversalTime().ToString("O")
        })
        .ToList();

    return Results.Ok(new { history = list });
});

app.MapPost("/api/sessions/{sessionId}/input", async (
    string sessionId,
    HttpRequest req,
    SessionStore store,
    CancellationToken ct) =>
{
    var input = await req.ReadFromJsonAsync<ChatRequestInput>(cancellationToken: ct);
    var message = (input?.Message ?? string.Empty).Trim();
    if (message.Length == 0)
        return Results.BadRequest(new { error = "message is required" });

    var request = new ChatRequestEvent
    {
        RequestId = (input?.RequestId ?? string.Empty).Trim(),
        UserId = (input?.UserId ?? string.Empty).Trim(),
        Message = message,
        Temperature = input?.Temperature ?? 0,
        MaxTokens = input?.MaxTokens ?? 0,
        StreamChunkEveryN = input?.StreamChunkEveryN ?? 0
    };

    if (input?.Context != null)
    {
        foreach (var (key, value) in input.Context)
        {
            if (!string.IsNullOrWhiteSpace(key))
            {
                request.Context[key] = value ?? string.Empty;
            }
        }
    }

    if (!string.IsNullOrWhiteSpace(input?.Timestamp) &&
        DateTimeOffset.TryParse(input.Timestamp, out var parsedTimestamp))
    {
        request.Timestamp = Timestamp.FromDateTime(parsedTimestamp.UtcDateTime);
    }
    else
    {
        request.Timestamp = Timestamp.FromDateTime(DateTime.UtcNow);
    }

    var session = store.GetOrCreate(sessionId);
    var runId = await session.StartInputAsync(request, ct);
    return Results.Ok(new { runId });
});

app.MapGet("/api/sessions/{sessionId}/agui/events", async (
    HttpContext http,
    string sessionId,
    SessionStore store,
    IOptions<WorkshopOptions> options,
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

app.MapGet("/api/agent/handlers", (string sessionId, SessionStore store) =>
{
    var session = store.GetOrCreate(sessionId);
    var agent = session.Agent;
    var handlers = DescribeHandlers(agent);
    var modules = session.GetEventModulesSnapshot();

    return Results.Ok(new
    {
        agentId = session.AgentId,
        agentType = agent.GetType().Name,
        mode = session.Mode.ToString().ToLowerInvariant(),
        role = session.Role,
        handlers,
        modules
    });
});

app.MapGet("/api/agent/state", (string sessionId, SessionStore store) =>
{
    var session = store.GetOrCreate(sessionId);
    var state = session.GetStateSnapshot();
    state.Context.TryGetValue("history_summary", out var summary);
    return Results.Ok(new
    {
        agentId = session.AgentId,
        agentType = session.Agent.GetType().Name,
        mode = session.Mode.ToString().ToLowerInvariant(),
        role = session.Role,
        historyCount = state.History?.Count ?? 0,
        historySummary = summary ?? string.Empty,
        contextKeys = state.Context.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList()
    });
});

app.MapGet("/api/agent/memory", async (
    string sessionId,
    SessionStore store,
    IMemoryStore memoryStore,
    CancellationToken ct) =>
{
    var session = store.GetOrCreate(sessionId);
    var agentMemoryId = $"privateagent::{session.AgentId}";
    var sessionMemoryId = $"session::{sessionId}";

    try
    {
        var agentEntries = await memoryStore.ListEntriesAsync(agentMemoryId, limit: 50, ct);
        var sessionEntries = await memoryStore.ListEntriesAsync(sessionMemoryId, limit: 50, ct);

        return Results.Ok(new
        {
            agentMemoryId,
            sessionMemoryId,
            agentEntries = agentEntries.Select(MapEntry).ToList(),
            sessionEntries = sessionEntries.Select(MapEntry).ToList()
        });
    }
    catch (Exception ex)
    {
        return Results.Ok(new
        {
            agentMemoryId,
            sessionMemoryId,
            error = ex.Message,
            agentEntries = Array.Empty<object>(),
            sessionEntries = Array.Empty<object>()
        });
    }
});

app.MapPost("/api/agent/ping", async (
    HttpRequest req,
    SessionStore store,
    CancellationToken ct) =>
{
    var input = await req.ReadFromJsonAsync<PingInput>(cancellationToken: ct);
    if (string.IsNullOrWhiteSpace(input?.SessionId))
        return Results.BadRequest(new { error = "sessionId is required" });

    var session = store.GetOrCreate(input.SessionId);
    var requestId = await session.SendPingAsync(input.Content ?? "ping", ct);
    return Results.Ok(new { requestId });
});

app.MapPost("/api/agent/settings", async (
    HttpRequest req,
    SessionStore store,
    CancellationToken ct) =>
{
    var input = await req.ReadFromJsonAsync<AgentSettingsInput>(cancellationToken: ct);
    if (string.IsNullOrWhiteSpace(input?.SessionId))
        return Results.BadRequest(new { error = "sessionId is required" });

    var session = store.GetOrCreate(input.SessionId);
    session.ApplySettings(input);
    return Results.Ok(session.GetSettingsSnapshot());
});

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

app.MapGet("/api/tools/catalog", async (
    string sessionId,
    SessionStore store,
    WorkshopToolCatalog catalog,
    CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(sessionId))
        return Results.BadRequest(new { error = "sessionId is required" });

    var session = store.GetOrCreate(sessionId);
    var tools = await catalog.GetToolsAsync(session, ct);
    return Results.Ok(new { sessionId, tools });
});

app.MapGet("/api/tools/dotnet", async (
    WorkshopToolCatalog catalog,
    CancellationToken ct) =>
{
    var tools = await catalog.ListDotNetFilesAsync(ct);
    return Results.Ok(new { tools });
});

app.MapPost("/api/tools/dotnet/register", async (
    HttpRequest req,
    SessionStore store,
    WorkshopToolCatalog catalog,
    CancellationToken ct) =>
{
    var input = await req.ReadFromJsonAsync<RegisterDotNetToolInput>(cancellationToken: ct);
    if (string.IsNullOrWhiteSpace(input?.SessionId) || string.IsNullOrWhiteSpace(input.FilePath))
        return Results.BadRequest(new { error = "sessionId and filePath are required" });

    var session = store.GetOrCreate(input.SessionId);
    var tool = await catalog.RegisterDotNetFileAsync(session, input.FilePath, ct);
    if (tool == null)
        return Results.BadRequest(new { error = "dotnet tool file not allowed or invalid" });

    return Results.Ok(new { tool });
});

app.MapGet("/api/agent/yaml", (string? role, GlobalAgentYamlRegistry registry) =>
{
    if (!string.IsNullOrWhiteSpace(role))
    {
        var loader = new AgentYamlConfigLoader();
        var key = GlobalAgentYamlRegistry.NormalizeRoleKey(role);
        var path = AgentYamlConfigLoader.GetConfigFilePath(key);
        var yaml = File.Exists(path) ? File.ReadAllText(path) : string.Empty;
        return Results.Ok(new { role = key, path, yaml });
    }

    var roles = registry.GetKnownRoles()
        .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
        .ToList();
    return Results.Ok(new { roles });
});

app.MapPost("/api/agent/yaml", async (
    HttpRequest req,
    GlobalAgentYamlRegistry registry,
    SessionStore store,
    CancellationToken ct) =>
{
    var input = await req.ReadFromJsonAsync<AgentYamlInput>(cancellationToken: ct);
    var yaml = (input?.Yaml ?? string.Empty).Trim();
    if (yaml.Length == 0)
        return Results.BadRequest(new { error = "yaml is required" });

    var loader = new AgentYamlConfigLoader();
    AgentYamlConfig cfg;
    try
    {
        cfg = loader.LoadFromString(yaml);
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }

    var role = GlobalAgentYamlRegistry.NormalizeRoleKey(cfg.Id);
    if (string.IsNullOrWhiteSpace(role))
        return Results.BadRequest(new { error = "invalid role id" });

    var dir = AgentYamlConfigLoader.GetDefaultConfigDirectory();
    Directory.CreateDirectory(dir);
    var path = AgentYamlConfigLoader.GetConfigFilePath(role);
    await File.WriteAllTextAsync(path, yaml, ct);

    if (input?.CreateSession == true)
    {
        var session = store.CreateSession(WorkshopAgentMode.Role, role);
        return Results.Ok(new { role, sessionId = session.SessionId, path });
    }

    return Results.Ok(new { role, path });
});

app.MapPost("/api/workflow/yaml", async (
    HttpRequest req,
    WorkflowMeshService mesh,
    CancellationToken ct) =>
{
    var input = await req.ReadFromJsonAsync<WorkflowYamlInput>(cancellationToken: ct);
    var yaml = (input?.Yaml ?? string.Empty).Trim();
    if (yaml.Length == 0)
        return Results.BadRequest(new { error = "yaml is required" });

    var workflowName = (input?.Name ?? "workshop_mesh").Trim();
    var dir = WorkshopWorkflowYamlBootstrap.GetWorkflowDirectory();
    Directory.CreateDirectory(dir);
    var path = WorkshopWorkflowYamlBootstrap.GetWorkflowPath(workflowName);
    await File.WriteAllTextAsync(path, yaml, ct);

    var result = await mesh.LoadFromYamlAsync(yaml, ct);
    if (!result.Ok)
    {
        return Results.Ok(new { ok = false, errors = result.Errors.Select(e => new { e.Code, e.Message, e.Path }) });
    }

    return Results.Ok(new
    {
        ok = true,
        workflowId = result.Graph?.WorkflowId ?? workflowName,
        graph = result.Graph,
        agents = result.Agents
    });
});

app.MapGet("/api/workflow/graph", (WorkflowMeshService mesh) =>
{
    return Results.Ok(new { graph = mesh.GetLatestGraph() });
});

app.MapRoleWorkspaceEndpoints();

app.MapGet("/healthz", () => Results.Ok("ok"));

app.Run();

static WorkshopAgentMode ParseMode(string? mode)
{
    return string.Equals(mode, "workshop", StringComparison.OrdinalIgnoreCase)
        ? WorkshopAgentMode.Workshop
        : WorkshopAgentMode.Role;
}

static List<object> DescribeHandlers(IGAgent agent)
{
    var list = new List<object>();
    var methods = agent.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
    foreach (var method in methods)
    {
        var eventAttr = method.GetCustomAttribute<EventHandlerAttribute>();
        var allAttr = method.GetCustomAttribute<AllEventHandlerAttribute>();
        if (eventAttr == null && allAttr == null)
            continue;

        var visibility = method.IsPublic
            ? "public"
            : method.IsFamily
                ? "protected"
                : method.IsPrivate
                    ? "private"
                    : "internal";

        var parameter = method.GetParameters().FirstOrDefault();
        var paramType = parameter?.ParameterType.FullName ?? "(none)";
        list.Add(new
        {
            name = method.Name,
            visibility,
            parameterType = paramType,
            handlerType = allAttr != null ? "all" : "event",
            priority = eventAttr?.Priority ?? allAttr?.Priority,
            allowSelfHandling = eventAttr?.AllowSelfHandling ?? allAttr?.AllowSelfHandling ?? false
        });
    }

    return list.OrderBy(x => ((dynamic)x).visibility).ThenBy(x => ((dynamic)x).name).ToList();
}

static object MapEntry(Aevatar.Agents.Abstractions.Memory.MemoryEntry entry)
{
    return new
    {
        id = entry.EntryId,
        role = entry.Role,
        content = entry.Content ?? string.Empty,
        timestamp = entry.CreatedAt?.ToDateTime().ToUniversalTime().ToString("O")
    };
}

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
