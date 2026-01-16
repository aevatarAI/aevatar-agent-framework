using System.Text;
using AevatarKit;
using AevatarKit.Core.Serialization;
using AevatarKit.Runtime.DependencyInjection;
using AevatarKit.Runtime.Agents;
using AevatarKit.Runtime.Graphs;
using AevatarKit.Runtime.Mcp;
using AevatarKit.Runtime.Memory;
using AevatarKit.Runtime.Run;
using Microsoft.Extensions.FileProviders;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddAevatarKitRuntime();

var app = builder.Build();

// ------------------------------
// Static frontend (no build step)
// ------------------------------
// Prefer serving from experimental/aevatar-kit/frontend (source) so `dotnet run` works immediately.
// Fallback to wwwroot if present (e.g., in publish scenarios).
var frontendPath = Path.GetFullPath(Path.Combine(app.Environment.ContentRootPath, "../../frontend"));
if (Directory.Exists(frontendPath))
{
    var frontendProvider = new PhysicalFileProvider(frontendPath);
    app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = frontendProvider });
    app.UseStaticFiles(new StaticFileOptions { FileProvider = frontendProvider });
}

app.UseDefaultFiles();
app.UseStaticFiles();

// ------------------------------
// Memory Manager (MVP)
// ------------------------------
app.MapGet("/api/memory/resources", (HttpRequest request, IMemoryStore memory) =>
{
    var scopeTypeRaw = request.Query["scopeType"].ToString();
    MemoryScopeType? scopeType = null;
    if (!string.IsNullOrWhiteSpace(scopeTypeRaw) &&
        Enum.TryParse<MemoryScopeType>(scopeTypeRaw, ignoreCase: true, out var parsed))
    {
        scopeType = parsed;
    }

    var resp = new ListMemoryResourcesResponse();
    resp.Resources.AddRange(memory.ListResources(scopeType));
    return Results.Text(ProtobufJson.ToJson(resp), "application/json; charset=utf-8");
});

app.MapGet("/api/memory/{memoryId}/entries", (string memoryId, int? limit, IMemoryStore memory) =>
{
    var take = limit is > 0 ? Math.Clamp(limit.Value, 1, 2000) : 200;
    var resp = new ListMemoryEntriesResponse();
    resp.Entries.AddRange(memory.ListEntries(memoryId, take));
    return Results.Text(ProtobufJson.ToJson(resp), "application/json; charset=utf-8");
});

app.MapPost("/api/memory/search", async (HttpRequest request, IMemoryStore memory) =>
{
    var body = await ReadBodyAsStringAsync(request).ConfigureAwait(false);
    var search = ProtobufJson.Parse<SearchMemoryRequest>(body);

    var limit = search.Limit > 0 ? search.Limit : 50;
    MemoryScopeType? scopeType = search.ScopeType != MemoryScopeType.Unspecified ? search.ScopeType : null;

    var resp = new SearchMemoryResponse();
    resp.Entries.AddRange(memory.Search(search.Query ?? string.Empty, limit, scopeType));
    return Results.Text(ProtobufJson.ToJson(resp), "application/json; charset=utf-8");
});

// ------------------------------
// Graph Library (MVP)
// ------------------------------
app.MapGet("/api/graphs", (HttpRequest request, IGraphRegistry graphs) =>
{
    var q = request.Query["q"].ToString();
    var resp = new ListGraphsResponse();
    resp.Graphs.AddRange(graphs.List(q));
    return Results.Text(ProtobufJson.ToJson(resp), "application/json; charset=utf-8");
});

app.MapGet("/api/graphs/{graphId}", (string graphId, IGraphRegistry graphs) =>
{
    var g = graphs.Get(graphId);
    if (g == null)
    {
        return Results.NotFound("graph not found");
    }

    var resp = new GetGraphResponse { Graph = g };
    return Results.Text(ProtobufJson.ToJson(resp), "application/json; charset=utf-8");
});

app.MapPost("/api/graphs", async (HttpRequest request, IGraphRegistry graphs) =>
{
    var body = await ReadBodyAsStringAsync(request).ConfigureAwait(false);
    var create = ProtobufJson.Parse<CreateGraphRequest>(body);
    var g = graphs.Create(create);
    var resp = new CreateGraphResponse { Graph = g };
    return Results.Text(ProtobufJson.ToJson(resp), "application/json; charset=utf-8");
});

app.MapPut("/api/graphs/{graphId}", async (HttpRequest request, string graphId, IGraphRegistry graphs) =>
{
    var body = await ReadBodyAsStringAsync(request).ConfigureAwait(false);
    var update = ProtobufJson.Parse<UpdateGraphRequest>(body);

    try
    {
        var g = graphs.Update(graphId, update);
        var resp = new UpdateGraphResponse { Graph = g };
        return Results.Text(ProtobufJson.ToJson(resp), "application/json; charset=utf-8");
    }
    catch (KeyNotFoundException)
    {
        return Results.NotFound("graph not found");
    }
});

// ------------------------------
// MCP Manager (MVP)
// ------------------------------
app.MapGet("/api/mcp/servers", (IMcpServerRegistry mcp) =>
{
    var resp = new ListMcpServersResponse();
    resp.Servers.AddRange(mcp.List());
    return Results.Text(ProtobufJson.ToJson(resp), "application/json; charset=utf-8");
});

app.MapPost("/api/mcp/servers", async (HttpRequest request, IMcpServerRegistry mcp) =>
{
    var body = await ReadBodyAsStringAsync(request).ConfigureAwait(false);
    var create = ProtobufJson.Parse<CreateMcpServerRequest>(body);
    var s = mcp.Create(create);
    var resp = new CreateMcpServerResponse { Server = s };
    return Results.Text(ProtobufJson.ToJson(resp), "application/json; charset=utf-8");
});

app.MapPut("/api/mcp/servers/{serverId}", async (HttpRequest request, string serverId, IMcpServerRegistry mcp) =>
{
    var body = await ReadBodyAsStringAsync(request).ConfigureAwait(false);
    var update = ProtobufJson.Parse<UpdateMcpServerRequest>(body);

    try
    {
        var s = mcp.Update(serverId, update);
        var resp = new UpdateMcpServerResponse { Server = s };
        return Results.Text(ProtobufJson.ToJson(resp), "application/json; charset=utf-8");
    }
    catch (KeyNotFoundException)
    {
        return Results.NotFound("server not found");
    }
});

// ------------------------------
// Workspace: Agents (MVP)
// ------------------------------
app.MapGet("/api/agents", (HttpRequest request, IAgentRegistry agents) =>
{
    var q = request.Query["q"].ToString();
    var resp = new ListAgentsResponse();
    resp.Agents.AddRange(agents.List(q));
    return Results.Text(ProtobufJson.ToJson(resp), "application/json; charset=utf-8");
});

app.MapGet("/api/agents/categories", (HttpRequest request, IAgentRegistry agents) =>
{
    var q = request.Query["q"].ToString();
    var resp = new ListAgentCategoriesResponse();
    resp.Categories.AddRange(agents.ListCategories(q));
    return Results.Text(ProtobufJson.ToJson(resp), "application/json; charset=utf-8");
});

app.MapPost("/api/agents", async (HttpRequest request, IAgentRegistry agents) =>
{
    var body = await ReadBodyAsStringAsync(request).ConfigureAwait(false);
    var create = ProtobufJson.Parse<CreateAgentRequest>(body);

    var agent = agents.Create(create);
    var resp = new CreateAgentResponse { Agent = agent };
    return Results.Text(ProtobufJson.ToJson(resp), "application/json; charset=utf-8");
});

// ------------------------------
// Runs: history (MVP)
// ------------------------------
app.MapGet("/api/runs", (IRunRegistry registry) =>
{
    var resp = new ListRunsResponse();
    foreach (var r in registry.List())
    {
        var last = r.EventHistory.LastOrDefault();
        resp.Runs.Add(new RunSummary
        {
            RunId = r.RunId,
            GraphId = r.GraphId ?? string.Empty,
            GraphName = r.GraphName ?? string.Empty,
            SessionId = r.SessionId ?? string.Empty,
            InputPreview = (r.Input ?? string.Empty).Length > 80 ? (r.Input ?? string.Empty)[..80] : (r.Input ?? string.Empty),
            CreatedAt = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(r.CreatedAtUtc),
            LastStatus = last?.Status ?? WorkflowStepStatus.Unspecified
        });
    }

    return Results.Text(ProtobufJson.ToJson(resp), "application/json; charset=utf-8");
});

app.MapGet("/api/runs/{runId}", (string runId, IRunRegistry registry) =>
{
    if (!registry.TryGet(runId, out var run))
    {
        return Results.NotFound("run not found");
    }

    var resp = new GetRunResponse
    {
        RunId = run.RunId,
        GraphId = run.GraphId ?? string.Empty,
        GraphName = run.GraphName ?? string.Empty,
        SessionId = run.SessionId ?? string.Empty,
        Input = run.Input ?? string.Empty,
        CreatedAt = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(run.CreatedAtUtc)
    };
    resp.Events.AddRange(run.EventHistory.ToArray());

    return Results.Text(ProtobufJson.ToJson(resp), "application/json; charset=utf-8");
});

// ------------------------------
// API: Start run
// ------------------------------
app.MapPost("/api/runs", async (HttpRequest request, IRunRegistry registry, IRunEngine engine) =>
{
    var body = await ReadBodyAsStringAsync(request).ConfigureAwait(false);
    var start = ProtobufJson.Parse<RunStartRequest>(body);

    var run = registry.Create(
        sessionId: string.IsNullOrWhiteSpace(start.SessionId) ? null : start.SessionId,
        graphId: string.IsNullOrWhiteSpace(start.GraphId) ? null : start.GraphId,
        graphName: string.IsNullOrWhiteSpace(start.GraphName) ? null : start.GraphName,
        graphSource: string.IsNullOrWhiteSpace(start.GraphSource) ? null : start.GraphSource,
        input: string.IsNullOrWhiteSpace(start.Input) ? null : start.Input);

    await engine.StartAsync(run).ConfigureAwait(false);

    var resp = new RunStartResponse { RunId = run.RunId };
    return Results.Text(ProtobufJson.ToJson(resp), "application/json; charset=utf-8");
});

// ------------------------------
// API: SSE event stream
// ------------------------------
app.MapGet("/api/runs/{runId}/events", async (HttpContext ctx, string runId, IRunRegistry registry) =>
{
    if (!registry.TryGet(runId, out var run))
    {
        ctx.Response.StatusCode = StatusCodes.Status404NotFound;
        await ctx.Response.WriteAsync("run not found").ConfigureAwait(false);
        return;
    }

    ctx.Response.Headers.CacheControl = "no-cache";
    ctx.Response.Headers.Connection = "keep-alive";
    ctx.Response.ContentType = "text/event-stream";

    // Initial hello event (helps UI show connected state)
    await WriteSseAsync(ctx, "hello", ProtobufJson.ToJson(new WorkflowStepEvent
    {
        RunId = run.RunId,
        StepId = "sse",
        StepName = "SSE Connected",
        Status = WorkflowStepStatus.Running,
        OutputChunk = "",
        Error = "",
        Timestamp = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(DateTime.UtcNow)
    })).ConfigureAwait(false);

    var reader = run.EventChannel.Reader;
    while (await reader.WaitToReadAsync(ctx.RequestAborted).ConfigureAwait(false))
    {
        while (reader.TryRead(out var evt))
        {
            await WriteSseAsync(ctx, "step", ProtobufJson.ToJson(evt)).ConfigureAwait(false);
        }
    }
});

// ------------------------------
// API: Memory entries (MVP)
// ------------------------------
app.MapGet("/api/runs/{runId}/memory", (string runId, int? limit, IRunRegistry registry) =>
{
    if (!registry.TryGet(runId, out var run))
    {
        return Results.NotFound("run not found");
    }

    var take = limit is > 0 ? Math.Clamp(limit.Value, 1, 2000) : 200;
    var entries = run.MemoryEntries.ToArray();
    if (entries.Length > take)
    {
        entries = entries[^take..];
    }

    var resp = new ListMemoryEntriesResponse();
    resp.Entries.AddRange(entries);
    return Results.Text(ProtobufJson.ToJson(resp), "application/json; charset=utf-8");
});

app.Run();

static async Task<string> ReadBodyAsStringAsync(HttpRequest request)
{
    request.EnableBuffering();
    request.Body.Position = 0;
    using var reader = new StreamReader(request.Body, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
    var body = await reader.ReadToEndAsync().ConfigureAwait(false);
    request.Body.Position = 0;
    return body;
}

static async Task WriteSseAsync(HttpContext ctx, string eventName, string dataJson)
{
    // SSE format:
    // event: <name>
    // data: <payload>
    // \n
    await ctx.Response.WriteAsync($"event: {eventName}\n").ConfigureAwait(false);
    await ctx.Response.WriteAsync($"data: {dataJson}\n\n").ConfigureAwait(false);
    await ctx.Response.Body.FlushAsync().ConfigureAwait(false);
}


