using Aevatar.Agents.Core.Extensions;
using Aevatar.Agents.Runtime.Local;
using Aevatar.Novel.Contracts;
using Aevatar.Novel.Sidecar.Api;
using Aevatar.Novel.Sidecar.Services;
using Aevatar.Novel.Sidecar.Services.Aevatar;
using Aevatar.Novel.Sidecar.Services.Branches;
using Aevatar.Novel.Sidecar.Services.CanonGovernance;
using Aevatar.Novel.Sidecar.Services.DeviationImpact;
using Aevatar.Novel.Sidecar.Services.Files;
using Aevatar.Novel.Sidecar.Services.NarrativeTests;
using Aevatar.Novel.Sidecar.Services.SetupPayoff;
using Aevatar.Novel.Sidecar.Services.WritingSessions;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Options;

// ============================================================
//  Aevatar.Novel.Sidecar
//
//  GOAL (v1):
//  - Provide a local sidecar process for Tauri/React.
//  - Enforce SSOT: chapter .txt + artifact .md are the truth; SQLite is rebuildable index.
//  - Host APIs + event stream (later) + background services (file watcher / indexing).
//
//  NOTE:
//  - This is a skeleton. We will wire Aevatar multi-agent + Cognitive Mesh workflows next.
// ============================================================

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddOpenApi();

// ---------- Aevatar (Local runtime for v1) ----------
builder.Services.AddAevatarAgentSystem(b => b.UseLocalRuntime());

// ---------- Options ----------
builder.Services.Configure<NovelOptions>(builder.Configuration.GetSection(NovelOptions.SectionName));

// ---------- Core services ----------
builder.Services.AddSingleton<SidecarEventHub>();
builder.Services.AddSingleton<ProjectRootManager>();
builder.Services.AddSingleton<NarrativeTestRunner>();
builder.Services.AddSingleton<NovelAgentRuntime>();
builder.Services.AddSingleton<SstFileSystemService>();
builder.Services.AddSingleton<ChapterRevisionStore>();
builder.Services.AddSingleton<DeviationImpactAnalyzer>();
builder.Services.AddSingleton<CanonAssetRevisionStore>();
builder.Services.AddSingleton<RewriteBranchService>();
builder.Services.AddSingleton<SetupPayoffLedgerRunner>();

// ---------- Background services ----------
builder.Services.AddHostedService<ProjectFileWatcherHostedService>();
builder.Services.AddHostedService<NarrativeTestsOrchestratorHostedService>();
builder.Services.AddHostedService<WritingSessionAggregatorHostedService>();
builder.Services.AddHostedService<DeviationImpactOrchestratorHostedService>();
builder.Services.AddHostedService<CanonGovernanceOrchestratorHostedService>();
builder.Services.AddHostedService<SetupPayoffOrchestratorHostedService>();

var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

// ------------------------------------------------------------
//  Protobuf JSON formatter (for debugging)
// ------------------------------------------------------------
var protoJson = new JsonFormatter(JsonFormatter.Settings.Default.WithIndentation());

// ------------------------------------------------------------
//  Project Root (SSOT entry point)
//  - Get/Set via Protobuf-defined contracts (novel_sidecar.proto)
// ------------------------------------------------------------
app.MapGet("/api/novel/project-root", (ProjectRootManager root) =>
    ProtoJsonHttp.Json(root.GetInfo()));

app.MapPost("/api/novel/project-root", async (HttpRequest request, ProjectRootManager root, CancellationToken ct) =>
{
    var input = await ProtoJsonHttp.ReadJsonAsync<SetProjectRootRequest>(request, ct);
    var info = root.SetProjectRoot(input.ProjectRoot);
    return ProtoJsonHttp.Json(new SetProjectRootResponse { Info = info });
});

// ------------------------------------------------------------
//  File APIs (SSOT)
// ------------------------------------------------------------
app.MapPost("/api/novel/fs/read", async (
    HttpRequest request,
    SstFileSystemService fs,
    CancellationToken ct) =>
{
    var input = await ProtoJsonHttp.ReadJsonAsync<ReadTextFileRequest>(request, ct);
    var resp = await fs.ReadTextFileAsync(input, ct);
    return ProtoJsonHttp.Json(resp);
});

app.MapPost("/api/novel/fs/write", async (
    HttpRequest request,
    SstFileSystemService fs,
    CancellationToken ct) =>
{
    var input = await ProtoJsonHttp.ReadJsonAsync<WriteTextFileRequest>(request, ct);
    var resp = await fs.WriteTextFileAsync(input, ct);
    return ProtoJsonHttp.Json(resp);
});

app.MapPost("/api/novel/fs/list", async (
    HttpRequest request,
    SstFileSystemService fs,
    CancellationToken ct) =>
{
    var input = await ProtoJsonHttp.ReadJsonAsync<ListDirectoryRequest>(request, ct);
    var resp = fs.ListDirectory(input);
    return ProtoJsonHttp.Json(resp);
});

// ------------------------------------------------------------
//  Rewrite Branch / Merge APIs
// ------------------------------------------------------------
app.MapPost("/api/novel/branches/create", async (
    HttpRequest request,
    RewriteBranchService branches,
    SidecarEventHub hub,
    CancellationToken ct) =>
{
    var input = await ProtoJsonHttp.ReadJsonAsync<CreateRewriteBranchRequest>(request, ct);
    var resp = await branches.CreateAsync(input, ct);

    if (resp.Branch is not null && resp.BranchManifest is not null && !string.IsNullOrWhiteSpace(resp.Branch.BranchId))
    {
        hub.Publish(new SidecarEvent
        {
            EventId = Guid.NewGuid().ToString("N"),
            Timestamp = Timestamp.FromDateTime(DateTime.UtcNow),
            RewriteBranchCreated = new RewriteBranchCreatedEvent
            {
                EventId = Guid.NewGuid().ToString("N"),
                Timestamp = Timestamp.FromDateTime(DateTime.UtcNow),
                Branch = resp.Branch,
                BranchManifest = resp.BranchManifest
            }
        });
    }

    return ProtoJsonHttp.Json(resp);
});

app.MapPost("/api/novel/branches/list", async (
    HttpRequest request,
    RewriteBranchService branches,
    CancellationToken ct) =>
{
    var input = await ProtoJsonHttp.ReadJsonAsync<ListRewriteBranchesRequest>(request, ct);
    var resp = await branches.ListAsync(input, ct);
    return ProtoJsonHttp.Json(resp);
});

app.MapPost("/api/novel/branches/diff", async (
    HttpRequest request,
    RewriteBranchService branches,
    CancellationToken ct) =>
{
    var input = await ProtoJsonHttp.ReadJsonAsync<DiffBranchChapterRequest>(request, ct);
    var resp = await branches.DiffChapterAsync(input, ct);
    return ProtoJsonHttp.Json(resp);
});

app.MapPost("/api/novel/branches/merge-chapter", async (
    HttpRequest request,
    RewriteBranchService branches,
    SidecarEventHub hub,
    CancellationToken ct) =>
{
    var input = await ProtoJsonHttp.ReadJsonAsync<MergeBranchChapterRequest>(request, ct);
    var resp = await branches.MergeChapterAsync(input, ct);

    if (resp.Success && resp.MergeRecord is not null)
    {
        hub.Publish(new SidecarEvent
        {
            EventId = Guid.NewGuid().ToString("N"),
            Timestamp = Timestamp.FromDateTime(DateTime.UtcNow),
            RewriteBranchMerged = new RewriteBranchMergedEvent
            {
                EventId = Guid.NewGuid().ToString("N"),
                Timestamp = Timestamp.FromDateTime(DateTime.UtcNow),
                Branch = new RewriteBranchInfo
                {
                    StoryRelativePath = input.StoryRelativePath ?? "",
                    BranchId = input.BranchId ?? "",
                    Name = "",
                    BasedOnBranchId = "",
                    CreatedAt = Timestamp.FromDateTime(DateTime.UtcNow)
                },
                ChapterFile = input.ChapterFile ?? "",
                MergeRecord = resp.MergeRecord
            }
        });
    }

    return ProtoJsonHttp.Json(resp);
});

// (v1 stub) Basic server info.
app.MapGet("/api/novel/info", () =>
{
    return Results.Ok(new
    {
        name = "Aevatar.Novel.Sidecar",
        contracts = "Aevatar.Novel.Contracts (Protobuf)",
        ssot = "chapter .txt + artifacts .md; sqlite is rebuildable index",
    });
});

// ------------------------------------------------------------
//  Event Stream (SSE): SidecarEvent (Protobuf JSON)
// ------------------------------------------------------------
app.MapGet("/api/novel/events", async (HttpContext ctx, SidecarEventHub hub) =>
{
    ctx.Response.Headers.ContentType = "text/event-stream";
    ctx.Response.Headers.CacheControl = "no-cache";

    // Send an initial comment so clients know it's connected.
    await ctx.Response.WriteAsync(": connected\n\n", ctx.RequestAborted);
    await ctx.Response.Body.FlushAsync(ctx.RequestAborted);

    var reader = hub.Subscribe(ctx.RequestAborted);
    await foreach (var evt in reader.ReadAllAsync(ctx.RequestAborted))
    {
        // One SSE message per event.
        // event: sidecar_event
        // data: { ...protobuf json... }
        await ctx.Response.WriteAsync("event: sidecar_event\n", ctx.RequestAborted);
        await ctx.Response.WriteAsync("data: ", ctx.RequestAborted);
        await ctx.Response.WriteAsync(protoJson.Format(evt).Replace("\n", ""), ctx.RequestAborted);
        await ctx.Response.WriteAsync("\n\n", ctx.RequestAborted);
        await ctx.Response.Body.FlushAsync(ctx.RequestAborted);
    }
});

app.Run();


