using System.Net;
using System.Text.Json;
using Aevatar.Platform.Core.Config;
using Aevatar.Platform.Core.Sessions;
using Aevatar.Platform.Core.Tools;
using Aevatar.Platform.Core.Workflow;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Aevatar.Platform.Cli.Tui;

// ============================================================
//  OpenTuiBackendServer
//
//  说明：
//  - 为 OpenTUI 前端提供本地 HTTP API（随机端口，避免冲突；且不使用 :5000）
//  - 仅实现最小聊天闭环：POST /api/chat
// ============================================================
internal sealed class OpenTuiBackendServer : IAsyncDisposable
{
    private readonly IHost _host;

    public OpenTuiBackendServer(IHost host, string baseUrl)
    {
        _host = host;
        BaseUrl = baseUrl.TrimEnd('/');
    }

    public string BaseUrl { get; }

    public static async Task<OpenTuiBackendServer> StartAsync(
        AevatarEffectiveConfig effective,
        SessionService sessions,
        PlatformMeshCompiler compiler,
        WorkflowEngine engine,
        PlatformToolPolicy policy,
        SessionRuntime runtime,
        CancellationToken ct)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = Array.Empty<string>(),
            EnvironmentName = Environments.Production
        });

        builder.WebHost.UseKestrel(o =>
        {
            // bind ephemeral port on loopback
            o.Listen(IPAddress.Loopback, 0, lo => lo.Protocols = HttpProtocols.Http1);
        });

        builder.Services.AddSingleton(effective);
        builder.Services.AddSingleton(sessions);
        builder.Services.AddSingleton(compiler);
        builder.Services.AddSingleton(engine);
        builder.Services.AddSingleton(policy);
        builder.Services.AddSingleton(runtime);

        var app = builder.Build();
        var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);

        app.MapGet("/health", () => Results.Text("ok"));

        app.MapPost("/api/chat", async (HttpContext http, SessionRuntime rt, AevatarEffectiveConfig eff, SessionService ss, PlatformMeshCompiler comp, WorkflowEngine eng, PlatformToolPolicy pol) =>
        {
            var req = await http.Request.ReadFromJsonAsync<ChatRequest>(json, http.RequestAborted);
            var text = (req?.Text ?? string.Empty).Trim();
            if (text.Length == 0)
                return Results.Json(new ChatResponse(true, string.Empty), json);

            // Reuse existing parsing semantics (/command, !shell, @file)
            var parsed = InputParser.Parse(text);
            if (parsed.Kind != ParsedInputKind.Message)
                return Results.Json(new ChatResponse(false, "Only plain chat messages are supported in GUI for now. Use REPL for /commands."), json);

            var assistant = await TuiHandlers.ChatOnceAsync(parsed, rt, eff, ss, comp, eng, pol, http.RequestAborted);
            return Results.Json(new ChatResponse(true, assistant), json);
        });

        await app.StartAsync(ct);

        var addresses = app.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()
            ?.Addresses ?? new List<string>();

        var baseUrl = addresses.FirstOrDefault(a => a.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
                      ?? addresses.FirstOrDefault()
                      ?? "http://127.0.0.1:0";

        return new OpenTuiBackendServer(app, baseUrl);
    }

    public async ValueTask DisposeAsync()
    {
        try { await _host.StopAsync(); } catch { }
        _host.Dispose();
    }

    private sealed record ChatRequest(string Text);
    private sealed record ChatResponse(bool Ok, string Response);
}

