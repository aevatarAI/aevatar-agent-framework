using System.Net;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
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
using Microsoft.Extensions.Logging;

namespace Aevatar.Platform.Cli.Tui;

// ============================================================
//  OpenTuiBackendServer
//
//  说明：
//  - 为 OpenTUI 前端提供本地 HTTP API（随机端口，避免冲突；且不使用 :5000）
//  - 最小聊天闭环：POST /api/chat（非流式） /api/chat/stream（流式）
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

        // ------------------------------------------------------------
        // 静默日志：避免污染 TUI 界面
        // ------------------------------------------------------------
        builder.Logging.ClearProviders();
        builder.Logging.SetMinimumLevel(LogLevel.None);

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

        app.MapGet("/api/workflows", (AevatarEffectiveConfig eff, SessionRuntime rt) =>
        {
            var list = ListWorkflowNames(eff.ConfigDirectory);
            #region agent log
            DebugLog(
                "OpenTuiBackendServer.cs:/api/workflows",
                "workflows_list",
                new { count = list.Count, current = rt.Workflow ?? string.Empty },
                rt.SessionId,
                $"workflows_{Guid.NewGuid():N}",
                "H4");
            #endregion
            return Results.Json(new WorkflowsResponse(rt.Workflow ?? string.Empty, list), json);
        });

        app.MapPost("/api/workflows/select", async (HttpContext http, AevatarEffectiveConfig eff, SessionService ss, SessionRuntime rt) =>
        {
            var req = await http.Request.ReadFromJsonAsync<WorkflowSelectRequest>(json, http.RequestAborted);
            var workflow = (req?.Workflow ?? string.Empty).Trim();
            if (workflow.Length == 0)
                return Results.Json(new WorkflowSelectResponse(false, "workflow_required"), json, statusCode: StatusCodes.Status400BadRequest);

            if (!workflow.StartsWith("agent:", StringComparison.OrdinalIgnoreCase))
            {
                var list = ListWorkflowNames(eff.ConfigDirectory);
                if (!list.Contains(workflow, StringComparer.OrdinalIgnoreCase))
                    return Results.Json(new WorkflowSelectResponse(false, "workflow_not_found"), json, statusCode: StatusCodes.Status404NotFound);
            }

            rt.Workflow = workflow;
            await UpdateSessionWorkflowAsync(ss, rt, http.RequestAborted);
            Environment.SetEnvironmentVariable("AEVATAR_WORKFLOW", workflow);

            #region agent log
            DebugLog(
                "OpenTuiBackendServer.cs:/api/workflows/select",
                "workflow_selected",
                new { workflow },
                rt.SessionId,
                $"workflows_{Guid.NewGuid():N}",
                "H4");
            #endregion

            var listAfter = ListWorkflowNames(eff.ConfigDirectory);
            return Results.Json(new WorkflowsResponse(rt.Workflow ?? string.Empty, listAfter), json);
        });

        app.MapGet("/api/providers", (AevatarEffectiveConfig eff) =>
        {
            var snapshot = BuildProvidersSnapshot(eff);
            return Results.Json(snapshot, json);
        });

        app.MapPost("/api/providers/default", async (HttpContext http, AevatarEffectiveConfig eff, SessionService ss, SessionRuntime rt) =>
        {
            var req = await http.Request.ReadFromJsonAsync<ProviderSetRequest>(json, http.RequestAborted);
            var provider = (req?.Provider ?? string.Empty).Trim();
            if (provider.Length == 0)
                return Results.Json(new ProviderSetResponse(false, "provider_required"), json, statusCode: StatusCodes.Status400BadRequest);

            if (!eff.Config.Models.Providers.ContainsKey(provider))
                return Results.Json(new ProviderSetResponse(false, "provider_not_found"), json, statusCode: StatusCodes.Status400BadRequest);

            ApplyDefaultProvider(eff, provider);
            await PersistDefaultProviderAsync(eff, provider, http.RequestAborted);
            await UpdateSessionProviderAsync(ss, rt, eff, http.RequestAborted);

            var snapshot = BuildProvidersSnapshot(eff);
            return Results.Json(snapshot, json);
        });

        app.MapPost("/api/chat/stream", async (HttpContext http, SessionRuntime rt, AevatarEffectiveConfig eff, SessionService ss, PlatformMeshCompiler comp, WorkflowEngine eng, PlatformToolPolicy pol) =>
        {
            var req = await http.Request.ReadFromJsonAsync<ChatRequest>(json, http.RequestAborted);
            var text = (req?.Text ?? string.Empty).Trim();
            if (text.Length == 0)
            {
                http.Response.StatusCode = StatusCodes.Status204NoContent;
                return;
            }

            // Reuse existing parsing semantics (/command, !shell, @file)
            var parsed = InputParser.Parse(text);
            var runId = $"stream_{Guid.NewGuid():N}";
            #region agent log
            DebugLog(
                "OpenTuiBackendServer.cs:/api/chat/stream",
                "stream_entry",
                new
                {
                    textLen = text.Length,
                    parsedKind = parsed.Kind.ToString(),
                    provider = eff.Config.Models.DefaultProvider ?? string.Empty,
                    model = eff.Config.Models.DefaultModel ?? string.Empty
                },
                rt.SessionId,
                runId,
                "H1");
            #endregion
            if (parsed.Kind != ParsedInputKind.Message)
            {
                http.Response.StatusCode = StatusCodes.Status400BadRequest;
                await http.Response.WriteAsJsonAsync(
                    new ChatResponse(false, "Only plain chat messages are supported in GUI for now. Use REPL for /commands."),
                    json,
                    http.RequestAborted);
                return;
            }

            http.Response.Headers.CacheControl = "no-cache";
            http.Response.Headers.Append("X-Accel-Buffering", "no");
            http.Response.ContentType = "text/plain; charset=utf-8";

            var emittedAny = false;
            async Task EmitChunk(string chunk, CancellationToken token)
            {
                if (string.IsNullOrEmpty(chunk))
                    return;
                await http.Response.WriteAsync(chunk, token);
                await http.Response.Body.FlushAsync(token);
                if (!emittedAny)
                {
                    emittedAny = true;
                    #region agent log
                    DebugLog(
                        "OpenTuiBackendServer.cs:/api/chat/stream",
                        "stream_first_chunk",
                        new { chunkLen = chunk.Length },
                        rt.SessionId,
                        runId,
                        "H2");
                    #endregion
                }
            }

            try
            {
                await TuiHandlers.ChatStreamAsync(parsed, rt, eff, ss, comp, eng, pol, EmitChunk, http.RequestAborted);
            }
            catch (OperationCanceledException) when (http.RequestAborted.IsCancellationRequested)
            {
                // client aborted; ignore
            }
            catch (Exception ex)
            {
                if (IsDebugEnabled())
                {
                    try
                    {
                        await EmitChunk($"[stream_error] {ex.GetType().Name}: {ex.Message}", http.RequestAborted);
                    }
                    catch
                    {
                        // ignore secondary failures
                    }
                }
            }
        });

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
    private sealed record WorkflowSelectRequest(string Workflow);
    private sealed record WorkflowSelectResponse(bool Ok, string Error);
    private sealed record WorkflowsResponse(string Current, IReadOnlyList<string> Workflows);
    private sealed record ProviderSetRequest(string Provider);
    private sealed record ProviderSetResponse(bool Ok, string Error);
    private sealed record ProvidersSnapshot(string DefaultProvider, string DefaultModel, IReadOnlyList<ProviderEntry> Providers);
    private sealed record ProviderEntry(string Name, string DefaultModel);

    private const string DebugLogPath = "/Users/zhaoyiqi/Code/aevatar-agent-framework/.cursor/debug.log";

    private static ProvidersSnapshot BuildProvidersSnapshot(AevatarEffectiveConfig effective)
    {
        var models = effective.Config.Models;
        var list = models.Providers
            .OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase)
            .Select(p => new ProviderEntry(
                p.Key,
                (p.Value.DefaultModel ?? string.Empty).Trim()))
            .ToList();

        var defaultProvider = (models.DefaultProvider ?? string.Empty).Trim();
        var defaultModel = (models.DefaultModel ?? string.Empty).Trim();
        if (defaultModel.Length == 0 && defaultProvider.Length > 0 &&
            models.Providers.TryGetValue(defaultProvider, out var entry))
        {
            defaultModel = (entry.DefaultModel ?? string.Empty).Trim();
        }

        return new ProvidersSnapshot(defaultProvider, defaultModel, list);
    }

    private static void ApplyDefaultProvider(AevatarEffectiveConfig effective, string provider)
    {
        var models = effective.Config.Models;
        var model = string.Empty;
        if (models.Providers.TryGetValue(provider, out var entry))
            model = (entry.DefaultModel ?? string.Empty).Trim();

        models.DefaultProvider = provider;
        models.DefaultModel = model;

        Environment.SetEnvironmentVariable("AEVATAR_PROVIDER", provider);
        Environment.SetEnvironmentVariable("AEVATAR_MODEL", model);
    }

    private static async Task PersistDefaultProviderAsync(
        AevatarEffectiveConfig effective,
        string provider,
        CancellationToken ct)
    {
        try
        {
            var path = effective.ConfigPath;
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(dir))
                Directory.CreateDirectory(dir);

            JsonNode? rootNode = null;
            if (File.Exists(path))
            {
                var raw = await File.ReadAllTextAsync(path, ct);
                if (!string.IsNullOrWhiteSpace(raw))
                    rootNode = JsonNode.Parse(raw);
            }

            var root = rootNode as JsonObject ?? new JsonObject();
            var target = root;
            if (root["Aevatar"] is JsonObject aevatar)
                target = aevatar;

            var models = target["Models"] as JsonObject ?? new JsonObject();
            models["DefaultProvider"] = provider;
            var model = (effective.Config.Models.DefaultModel ?? string.Empty).Trim();
            if (model.Length > 0)
                models["DefaultModel"] = model;
            else
                models.Remove("DefaultModel");
            target["Models"] = models;

            if (root["LLMProviders"] is JsonObject llm)
            {
                llm["Default"] = provider;
                root["LLMProviders"] = llm;
            }

            var json = root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(path, json + Environment.NewLine, ct);
        }
        catch
        {
            // best-effort only
        }
    }

    private static async Task UpdateSessionProviderAsync(
        SessionService sessions,
        SessionRuntime runtime,
        AevatarEffectiveConfig effective,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(runtime.SessionId))
            return;

        var state = await sessions.GetSessionStateAsync(runtime.SessionId, ct);
        if (state == null)
            return;

        var provider = (effective.Config.Models.DefaultProvider ?? string.Empty).Trim();
        var model = ModelDefaults.ResolveModel(effective.Config.Models, null);

        var updated = false;
        if (!string.IsNullOrWhiteSpace(provider) && state.Provider != provider)
        {
            state.Provider = provider;
            updated = true;
        }

        if (!string.IsNullOrWhiteSpace(model) && state.Model != model)
        {
            state.Model = model;
            updated = true;
        }

        if (updated)
            await sessions.UpdateSessionStateAsync(state, ct);
    }

    private static bool IsDebugEnabled()
    {
        var raw = (Environment.GetEnvironmentVariable("AEVATAR_TUI_DEBUG") ?? string.Empty).Trim();
        return raw == "1" || raw.Equals("true", StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<string> ListWorkflowNames(string configDir)
    {
        try
        {
            var dir = Path.Combine(configDir, "workflows");
            if (!Directory.Exists(dir))
                return Array.Empty<string>();

            var list = Directory.EnumerateFiles(dir, "*.*", SearchOption.TopDirectoryOnly)
                .Where(p =>
                {
                    var ext = Path.GetExtension(p).ToLowerInvariant();
                    return ext is ".json" or ".yaml" or ".yml";
                })
                .Select(p => Path.GetFileNameWithoutExtension(p))
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToList();

            return list;
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    private static async Task UpdateSessionWorkflowAsync(
        SessionService sessions,
        SessionRuntime runtime,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(runtime.SessionId))
            return;

        var state = await sessions.GetSessionStateAsync(runtime.SessionId, ct);
        if (state == null)
            return;

        if (string.Equals(state.ActiveWorkflow, runtime.Workflow, StringComparison.Ordinal))
            return;

        state.ActiveWorkflow = runtime.Workflow;
        await sessions.UpdateSessionStateAsync(state, ct);
    }

    private static void DebugLog(
        string location,
        string message,
        object data,
        string sessionId,
        string runId,
        string hypothesisId)
    {
        try
        {
            var payload = new
            {
                location,
                message,
                data,
                timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                sessionId,
                runId,
                hypothesisId
            };
            var json = JsonSerializer.Serialize(payload);
            File.AppendAllText(DebugLogPath, json + Environment.NewLine);
        }
        catch
        {
            // best-effort only
        }
    }
}

