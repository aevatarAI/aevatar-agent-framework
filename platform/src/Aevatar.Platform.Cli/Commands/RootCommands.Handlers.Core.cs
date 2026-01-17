using System.CommandLine;
using System.CommandLine.Invocation;
using System.CommandLine.IO;
using System.CommandLine.Parsing;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Aevatar.Platform.Cli.Tui;
using Aevatar.Platform.Core.Config;
using Aevatar.Platform.Core.Sessions;
using Aevatar.Platform.Core.Tools;
using Aevatar.Platform.Core.Workflow;
using Aevatar.Platform.Server.Endpoints;
using Google.Protobuf.WellKnownTypes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Aevatar.Platform.Cli.Commands;

public static partial class RootCommands
{
    private sealed record RunOptions(
        string? Prompt,
        string? SessionId,
        bool Continue,
        bool Share,
        string? Provider,
        string? Model,
        string? Agent,
        string? Workflow,
        string? Title,
        string? Format,
        string? AttachUrl,
        string? Hostname,
        int? Port,
        string? WorkingDirectory,
        IReadOnlyList<string> Files);

    private sealed record RunResult(
        bool Ok,
        string SessionId,
        string MessageId,
        string Output,
        string? Error);

    private sealed record ServerOptions(
        string Hostname,
        int Port,
        IReadOnlyList<string> CorsOrigins,
        bool Mdns,
        string? Token);

    private sealed record SessionSummaryDto(
        string SessionId,
        string Profile,
        string ActiveWorkflow,
        DateTime? LastActivityUtc);

    private static partial class Handlers
    {
        private static readonly JsonSerializerOptions JsonIndented = new()
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        private static readonly JsonSerializerOptions JsonCaseInsensitive = new()
        {
            PropertyNameCaseInsensitive = true
        };

        public static Task StartTuiAsync(InvocationContext ctx, string? project = null)
        {
            ApplyLoggingOverrides(ctx.ParseResult);
            ApplyConfigOverrides(ctx.ParseResult);
            var profile = ResolveProfile(ctx.ParseResult);
            var workingDir = ResolveWorkingDirectory(ctx.ParseResult, project, null);

            var options = new TuiOptions(
                Profile: profile,
                Workflow: ctx.ParseResult.GetValueForOption(RootOptions.Workflow),
                Model: ctx.ParseResult.GetValueForOption(RootOptions.Model),
                Provider: ctx.ParseResult.GetValueForOption(RootOptions.Provider),
                Resume: ctx.ParseResult.GetValueForOption(RootOptions.Continue),
                SessionId: ctx.ParseResult.GetValueForOption(RootOptions.Session),
                WorkingDirectory: workingDir,
                ConfigDir: ctx.ParseResult.GetValueForOption(RootOptions.ConfigDir),
                ConfigPath: ctx.ParseResult.GetValueForOption(RootOptions.ConfigPath),
                SecretsPath: ctx.ParseResult.GetValueForOption(RootOptions.SecretsPath));

            return TuiApp.RunAsync(options, ctx.GetCancellationToken());
        }

        public static RunOptions BuildRunOptions(
            ParseResult parse,
            string? prompt,
            Option<string?>? session = null,
            Option<string?>? title = null,
            Option<bool>? share = null,
            Option<string>? format = null,
            Option<string?>? attach = null,
            Option<string?>? hostname = null,
            Option<int?>? port = null,
            Option<string?>? cwd = null,
            Option<string[]>? files = null)
        {
            var profile = ResolveProfile(parse);
            var sessionId = session != null
                ? parse.GetValueForOption(session)
                : parse.GetValueForOption(RootOptions.Session);
            var titleValue = title != null ? parse.GetValueForOption(title) : null;
            var shareValue = share != null && parse.GetValueForOption(share);
            var formatValue = format != null ? parse.GetValueForOption(format) : "default";
            var attachValue = attach != null ? parse.GetValueForOption(attach) : null;
            var hostValue = hostname != null ? parse.GetValueForOption(hostname) : null;
            var portValue = port != null ? parse.GetValueForOption(port) : null;
            var dirValue = cwd != null ? parse.GetValueForOption(cwd) : null;
            var workingDir = ResolveWorkingDirectory(parse, null, dirValue);
            var fileList = files != null ? parse.GetValueForOption(files) ?? Array.Empty<string>() : Array.Empty<string>();

            return new RunOptions(
                Prompt: prompt,
                SessionId: sessionId,
                Continue: parse.GetValueForOption(RootOptions.Continue),
                Share: shareValue,
                Provider: parse.GetValueForOption(RootOptions.Provider),
                Model: parse.GetValueForOption(RootOptions.Model),
                Agent: parse.GetValueForOption(RootOptions.Agent) ?? profile,
                Workflow: parse.GetValueForOption(RootOptions.Workflow),
                Title: titleValue,
                Format: formatValue,
                AttachUrl: attachValue,
                Hostname: hostValue,
                Port: portValue,
                WorkingDirectory: workingDir,
                Files: fileList);
        }

        public static RunOptions BuildRunOptions(ParseResult parse, string? prompt)
            => BuildRunOptions(parse, prompt, null, null, null, null, null, null, null, null, null);

        public static void ApplyLoggingOverrides(ParseResult parse)
        {
            var level = (parse.GetValueForOption(RootOptions.LogLevel) ?? string.Empty).Trim();
            if (level.Length > 0)
                Environment.SetEnvironmentVariable("AEVATAR_LOG_LEVEL", level);

            if (parse.GetValueForOption(RootOptions.PrintLogs))
                Environment.SetEnvironmentVariable("AEVATAR_LOG_STDERR", "1");
        }

        public static async Task RunOnceAsync(InvocationContext ctx, RunOptions options)
        {
            ApplyLoggingOverrides(ctx.ParseResult);
            ApplyConfigOverrides(ctx.ParseResult);

            if (!TryApplyWorkingDirectory(options.WorkingDirectory, out var error))
            {
                ctx.Console.Error.WriteLine($"Invalid working directory: {error}");
                return;
            }

            var attachUrl = ResolveAttachUrl(options);
            var result = attachUrl == null
                ? await RunLocalAsync(ctx, options, ctx.GetCancellationToken())
                : await RunRemoteAsync(ctx, options, attachUrl, ctx.GetCancellationToken());

            PrintRunResult(ctx, result, options.Format);
        }

        public static ServerOptions BuildServerOptions(
            ParseResult parse,
            Option<string> host,
            Option<int> port,
            Option<string[]> cors,
            Option<bool> mdns,
            Option<string?> token)
        {
            var hostname = parse.GetValueForOption(host);
            var portValue = parse.GetValueForOption(port);
            var origins = parse.GetValueForOption(cors) ?? Array.Empty<string>();
            var mdnsValue = parse.GetValueForOption(mdns);
            var tokenValue = parse.GetValueForOption(token);

            if (portValue == 5000)
                portValue = 5678;

            return new ServerOptions(hostname, portValue, origins, mdnsValue, tokenValue);
        }

        public static async Task RunServerAsync(ServerOptions options, bool openBrowser, CancellationToken ct)
        {
            var effective = new AevatarConfigLoader().Load();
            var sessionsRoot = Path.Combine(effective.ConfigDirectory, "sessions");

            var builder = WebApplication.CreateBuilder();
            builder.Services.AddSingleton(effective);
            builder.Services.AddSingleton<FileEventStore>(_ => new FileEventStore(sessionsRoot));
            builder.Services.AddSingleton<SessionService>(sp =>
                new SessionService(sp.GetRequiredService<FileEventStore>(), effective.ConfigDirectory));

            builder.Services.AddCors(cors =>
            {
                cors.AddDefaultPolicy(policy =>
                {
                    if (options.CorsOrigins.Count == 0)
                        policy.AllowAnyOrigin();
                    else
                        policy.WithOrigins(options.CorsOrigins.ToArray());
                    policy.AllowAnyHeader().AllowAnyMethod();
                });
            });

            var url = $"http://{options.Hostname}:{options.Port}";
            builder.WebHost.UseUrls(url);

            var app = builder.Build();
            app.UseCors();
            UseAuthMiddleware(app, options.Token);
            app.MapGet("/", () => Results.Text("Aevatar Platform Server"));
            SessionsEndpoints.Map(app);

            await app.StartAsync(ct);

            if (openBrowser)
                TryOpenBrowser(url);

            await app.WaitForShutdownAsync(ct);
        }

        public static AevatarEffectiveConfig LoadEffectiveConfig(InvocationContext ctx)
        {
            ApplyConfigOverrides(ctx.ParseResult);
            return new AevatarConfigLoader().Load();
        }

        public static SessionService CreateSessionService(InvocationContext ctx)
        {
            var effective = LoadEffectiveConfig(ctx);
            var storeRoot = Path.Combine(effective.ConfigDirectory, "sessions");
            var store = new FileEventStore(storeRoot);
            return new SessionService(store, effective.ConfigDirectory);
        }

        public static async Task ShowStatsAsync(
            InvocationContext ctx,
            int? days,
            int toolCount,
            int modelCount,
            string? projectDir)
        {
            var sessions = CreateSessionService(ctx);
            var list = await sessions.ListSessionsAsync(ctx.GetCancellationToken());
            if (list.Count == 0)
            {
                ctx.Console.WriteLine("(no sessions)");
                return;
            }

            var cutoff = days.HasValue ? DateTime.UtcNow.AddDays(-days.Value) : (DateTime?)null;
            var filtered = new List<SessionSummaryDto>();
            foreach (var s in list)
            {
                if (cutoff.HasValue && s.LastActivityUtc.HasValue && s.LastActivityUtc.Value < cutoff.Value)
                    continue;

                if (!string.IsNullOrWhiteSpace(projectDir))
                {
                    var state = await sessions.GetSessionStateAsync(s.SessionId, ctx.GetCancellationToken());
                    if (state == null || !IsUnderProject(state.WorkingDirectory, projectDir))
                        continue;
                }

                filtered.Add(new SessionSummaryDto(s.SessionId, s.Profile, s.ActiveWorkflow, s.LastActivityUtc));
            }

            if (filtered.Count == 0)
            {
                ctx.Console.WriteLine("(no sessions)");
                return;
            }

            var userMessages = 0;
            var agentOutputs = 0;
            var toolCalls = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var models = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            foreach (var s in filtered)
            {
                var state = await sessions.GetSessionStateAsync(s.SessionId, ctx.GetCancellationToken());
                if (state != null && !string.IsNullOrWhiteSpace(state.Model))
                    models[state.Model] = models.TryGetValue(state.Model, out var count) ? count + 1 : 1;

                var events = await sessions.GetSessionEventsAsync(s.SessionId, ctx.GetCancellationToken());
                foreach (var evt in events)
                {
                    if (evt.UserMessage != null)
                        userMessages++;
                    if (evt.AgentOutputDelta != null)
                        agentOutputs++;
                    if (evt.ToolFinished != null)
                    {
                        var name = evt.ToolFinished.ToolName ?? string.Empty;
                        if (name.Length > 0)
                            toolCalls[name] = toolCalls.TryGetValue(name, out var count) ? count + 1 : 1;
                    }
                }
            }

            ctx.Console.WriteLine($"sessions\t{filtered.Count}");
            ctx.Console.WriteLine($"user_messages\t{userMessages}");
            ctx.Console.WriteLine($"agent_outputs\t{agentOutputs}");

            if (toolCalls.Count > 0)
            {
                ctx.Console.WriteLine("tools");
                foreach (var (name, count) in toolCalls.OrderByDescending(x => x.Value)
                             .Take(toolCount == 0 ? int.MaxValue : toolCount))
                {
                    ctx.Console.WriteLine($"{name}\t{count}");
                }
            }

            if (models.Count > 0)
            {
                ctx.Console.WriteLine("models");
                foreach (var (name, count) in models.OrderByDescending(x => x.Value)
                             .Take(modelCount == 0 ? int.MaxValue : modelCount))
                {
                    ctx.Console.WriteLine($"{name}\t{count}");
                }
            }
        }

        public static async Task ExportSessionAsync(InvocationContext ctx, string sessionId, string output)
        {
            var service = CreateSessionService(ctx);
            await service.ExportSessionAsync(sessionId, output, ctx.GetCancellationToken());
            ctx.Console.WriteLine($"Exported session '{sessionId}' to {output}");
        }

        public static async Task ImportSessionAsync(InvocationContext ctx, string input, bool overwriteExisting)
        {
            var service = CreateSessionService(ctx);
            var sessionId = await service.ImportSessionAsync(input, overwriteExisting, ctx.GetCancellationToken());
            ctx.Console.WriteLine($"Imported session as '{sessionId}'");
        }

        public static async Task RunAcpAsync(InvocationContext ctx, string? workingDir)
        {
            if (!TryApplyWorkingDirectory(workingDir, out var error))
            {
                ctx.Console.Error.WriteLine($"Invalid working directory: {error}");
                return;
            }

            var ct = ctx.GetCancellationToken();
            while (!ct.IsCancellationRequested)
            {
                var line = await Console.In.ReadLineAsync();
                if (line == null)
                    break;

                if (string.IsNullOrWhiteSpace(line))
                    continue;

                try
                {
                    var request = JsonSerializer.Deserialize<AcpRequest>(line, JsonCaseInsensitive);
                    if (request == null)
                    {
                        WriteAcpError("invalid_request");
                        continue;
                    }

                    var runOptions = new RunOptions(
                        Prompt: request.Prompt,
                        SessionId: request.SessionId,
                        Continue: request.Continue,
                        Share: request.Share,
                        Provider: request.Provider,
                        Model: request.Model,
                        Agent: request.Agent,
                        Workflow: request.Workflow,
                        Title: request.Title,
                        Format: "json",
                        AttachUrl: null,
                        Hostname: null,
                        Port: null,
                        WorkingDirectory: request.WorkingDirectory,
                        Files: request.Files ?? Array.Empty<string>());

                    var result = await RunLocalAsync(ctx, runOptions, ct);
                    Console.WriteLine(JsonSerializer.Serialize(result, JsonIndented));
                }
                catch (JsonException)
                {
                    WriteAcpError("invalid_json");
                }
                catch (Exception ex)
                {
                    WriteAcpError(ex.Message);
                }
            }
        }

        public static string GetAgentsDirectory(InvocationContext ctx)
            => Path.Combine(LoadEffectiveConfig(ctx).ConfigDirectory, "agents");

        public static string GetWorkflowsDirectory(InvocationContext ctx)
            => Path.Combine(LoadEffectiveConfig(ctx).ConfigDirectory, "workflows");

        private static async Task<RunResult> RunLocalAsync(
            InvocationContext ctx,
            RunOptions options,
            CancellationToken ct)
        {
            var effective = LoadEffectiveConfig(ctx);
            var sessions = CreateSessionService(ctx);

            var profile = ResolveProfileFromOptions(options, effective);
            var policy = PlatformToolPolicy.Create(effective.Config, TuiHandlers.ResolveToolPolicyPreset(profile));
            var attachments = ResolveAttachments(options.Files, policy);

            var (state, seq) = await EnsureSessionStateAsync(options, effective, sessions, attachments, ct);
            var prompt = (options.Prompt ?? string.Empty).Trim();
            var messageId = Guid.NewGuid().ToString("N");

            var userEvent = new PlatformSessionEvent
            {
                Seq = ++seq,
                Timestamp = Timestamp.FromDateTime(DateTime.UtcNow),
                UserMessage = new UserMessageEvent
                {
                    MessageId = messageId,
                    Text = prompt,
                    AttachedFiles = { attachments }
                }
            };

            await sessions.AppendEventAsync(state.SessionId, userEvent, ct);

            var output = await WorkflowEngine.RunWorkflowAsync(state.ActiveWorkflow, effective.ConfigDirectory, ct);
            var agentEvent = new PlatformSessionEvent
            {
                Seq = ++seq,
                Timestamp = Timestamp.FromDateTime(DateTime.UtcNow),
                AgentOutputDelta = new AgentOutputDeltaEvent
                {
                    Agent = options.Agent ?? profile,
                    MessageId = messageId,
                    Delta = output,
                    IsFinal = true
                }
            };

            await sessions.AppendEventAsync(state.SessionId, agentEvent, ct);

            return new RunResult(true, state.SessionId, messageId, output, null);
        }

        private static async Task<(PlatformSessionState State, ulong Seq)> EnsureSessionStateAsync(
            RunOptions options,
            AevatarEffectiveConfig effective,
            SessionService sessions,
            IReadOnlyList<string> attachments,
            CancellationToken ct)
        {
            PlatformSessionState? state = null;
            if (!string.IsNullOrWhiteSpace(options.SessionId))
                state = await sessions.GetSessionStateAsync(options.SessionId, ct);

            if (state == null && options.Continue)
            {
                var list = await sessions.ListSessionsAsync(ct);
                var latest = list.FirstOrDefault();
                if (latest != null)
                    state = await sessions.GetSessionStateAsync(latest.SessionId, ct);
            }

            var profile = ResolveProfileFromOptions(options, effective);
            var workflow = options.Workflow ?? effective.Config.Agents.DefaultWorkflow;
            var model = ModelDefaults.ResolveModel(effective.Config.Models, options.Model);
            var provider = ModelDefaults.ResolveProvider(effective.Config.Models, options.Provider, options.Model);

            if (state == null)
            {
                state = new PlatformSessionState
                {
                    SessionId = options.SessionId ?? string.Empty,
                    Profile = profile,
                    ActiveWorkflow = workflow,
                    WorkingDirectory = options.WorkingDirectory ?? Directory.GetCurrentDirectory(),
                    Provider = provider,
                    Model = model
                };

                if (!string.IsNullOrWhiteSpace(options.Title))
                    state.Tags["title"] = options.Title;
                if (options.Share)
                    state.Tags["shared"] = "true";

                if (attachments.Count > 0)
                    state.AttachedFiles.AddRange(attachments);

                var sessionId = await sessions.CreateSessionAsync(state, ct);
                state.SessionId = sessionId;
            }
            else
            {
                var updated = false;
                var profileValue = profile;
                if (!string.IsNullOrWhiteSpace(profileValue) && state.Profile != profileValue)
                {
                    state.Profile = profileValue;
                    updated = true;
                }

                if (!string.IsNullOrWhiteSpace(workflow) && state.ActiveWorkflow != workflow)
                {
                    state.ActiveWorkflow = workflow;
                    updated = true;
                }

                if (!string.IsNullOrWhiteSpace(model) && state.Model != model)
                {
                    state.Model = model;
                    updated = true;
                }

                if (!string.IsNullOrWhiteSpace(provider) && state.Provider != provider)
                {
                    state.Provider = provider;
                    updated = true;
                }

                if (!string.IsNullOrWhiteSpace(options.WorkingDirectory))
                {
                    var cwd = options.WorkingDirectory.Trim();
                    if (!string.Equals(state.WorkingDirectory, cwd, StringComparison.Ordinal))
                    {
                        state.WorkingDirectory = cwd;
                        updated = true;
                    }
                }

                if (!string.IsNullOrWhiteSpace(options.Title))
                {
                    state.Tags["title"] = options.Title;
                    updated = true;
                }

                if (options.Share)
                {
                    state.Tags["shared"] = "true";
                    updated = true;
                }

                if (attachments.Count > 0)
                {
                    foreach (var file in attachments)
                    {
                        if (!state.AttachedFiles.Contains(file))
                        {
                            state.AttachedFiles.Add(file);
                            updated = true;
                        }
                    }
                }

                if (updated)
                    await sessions.UpdateSessionStateAsync(state, ct);
            }

            var events = await sessions.GetSessionEventsAsync(state.SessionId, ct);
            var seq = events.Count == 0 ? 0UL : events.Max(e => e.Seq);
            return (state, seq);
        }

        private static IReadOnlyList<string> ResolveAttachments(
            IReadOnlyList<string> inputs,
            PlatformToolPolicy policy)
        {
            if (inputs.Count == 0)
                return Array.Empty<string>();

            var list = new List<string>();
            var cwd = Directory.GetCurrentDirectory();

            foreach (var raw in inputs)
            {
                var path = (raw ?? string.Empty).Trim();
                if (path.Length == 0)
                    continue;

                var full = AttachmentResolver.ResolvePath(path, cwd);
                if (full == null)
                {
                    var matches = AttachmentResolver.FindFuzzyMatches(cwd, path, maxResults: 5);
                    if (matches.Count == 1)
                        full = matches[0];
                }

                if (full == null)
                    continue;

                if (!policy.TryValidatePath(full, out _, out var reason))
                {
                    if (reason != "path_allowlist_empty")
                        continue;
                }

                list.Add(full);
            }

            return list;
        }

        private static string? ResolveProfile(ParseResult parse)
        {
            var profile = (parse.GetValueForOption(RootOptions.Profile) ?? string.Empty).Trim();
            if (profile.Length > 0)
                return profile;

            var agent = (parse.GetValueForOption(RootOptions.Agent) ?? string.Empty).Trim();
            return agent.Length == 0 ? null : agent;
        }

        private static string ResolveProfileFromOptions(RunOptions options, AevatarEffectiveConfig effective)
        {
            var profile = (options.Agent ?? string.Empty).Trim();
            if (profile.Length > 0)
                return profile;

            return effective.Config.Agents.DefaultProfile;
        }


        private static string? ResolveWorkingDirectory(ParseResult parse, string? project, string? overrideDir)
        {
            var value = (overrideDir ?? string.Empty).Trim();
            if (value.Length == 0)
                value = (project ?? string.Empty).Trim();
            if (value.Length == 0)
                value = (parse.GetValueForOption(RootOptions.WorkingDirectory) ?? string.Empty).Trim();
            return value.Length == 0 ? null : value;
        }

        private static bool TryApplyWorkingDirectory(string? workingDir, out string? error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(workingDir))
                return true;

            var full = Path.GetFullPath(AttachmentResolver.ExpandHome(workingDir.Trim()));
            if (!Directory.Exists(full))
            {
                error = full;
                return false;
            }

            Directory.SetCurrentDirectory(full);
            return true;
        }

        private static void PrintRunResult(InvocationContext ctx, RunResult result, string? format)
        {
            var fmt = (format ?? string.Empty).Trim().ToLowerInvariant();
            if (fmt == "json")
            {
                ctx.Console.WriteLine(JsonSerializer.Serialize(result, JsonIndented));
                return;
            }

            if (!string.IsNullOrWhiteSpace(result.Error))
                ctx.Console.Error.WriteLine(result.Error);

            if (!string.IsNullOrWhiteSpace(result.Output))
                ctx.Console.WriteLine(result.Output);
        }

        private static void ApplyConfigOverrides(ParseResult parse)
        {
            var configDir = parse.GetValueForOption(RootOptions.ConfigDir);
            if (!string.IsNullOrWhiteSpace(configDir))
            {
                Environment.SetEnvironmentVariable("AEVATAR_CONFIG_DIR", configDir);
                Environment.SetEnvironmentVariable("AEVATAR_SECRETS_DIR", configDir);
            }

            var configPath = parse.GetValueForOption(RootOptions.ConfigPath);
            if (!string.IsNullOrWhiteSpace(configPath))
                Environment.SetEnvironmentVariable("AEVATAR_CONFIG", configPath);

            var secretsPath = parse.GetValueForOption(RootOptions.SecretsPath);
            if (!string.IsNullOrWhiteSpace(secretsPath))
            {
                Environment.SetEnvironmentVariable("AEVATAR_SECRETS_PATH", secretsPath);
                Environment.SetEnvironmentVariable("AEVATAR_SECRETS", secretsPath);
            }

            var profile = parse.GetValueForOption(RootOptions.Profile);
            if (!string.IsNullOrWhiteSpace(profile))
                Environment.SetEnvironmentVariable("AEVATAR_PROFILE", profile);

            var workflow = parse.GetValueForOption(RootOptions.Workflow);
            if (!string.IsNullOrWhiteSpace(workflow))
                Environment.SetEnvironmentVariable("AEVATAR_WORKFLOW", workflow);

            var provider = parse.GetValueForOption(RootOptions.Provider);
            if (!string.IsNullOrWhiteSpace(provider))
                Environment.SetEnvironmentVariable("AEVATAR_PROVIDER", provider);

            var model = parse.GetValueForOption(RootOptions.Model);
            if (!string.IsNullOrWhiteSpace(model))
                Environment.SetEnvironmentVariable("AEVATAR_MODEL", model);
        }

        private static void UseAuthMiddleware(WebApplication app, string? token)
        {
            var auth = (token ?? string.Empty).Trim();
            if (auth.Length == 0)
                return;

            app.Use(async (ctx, next) =>
            {
                if (ctx.Request.Path.StartsWithSegments("/api"))
                {
                    if (!IsAuthorized(ctx, auth))
                    {
                        ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                        await ctx.Response.WriteAsync("Unauthorized");
                        return;
                    }
                }

                await next();
            });
        }

        private static bool IsAuthorized(HttpContext ctx, string token)
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

        private static void TryOpenBrowser(string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            }
            catch
            {
                // ignore browser failures
            }
        }

        private static bool IsUnderProject(string? path, string projectDir)
        {
            if (string.IsNullOrWhiteSpace(path))
                return false;

            try
            {
                var full = Path.GetFullPath(AttachmentResolver.ExpandHome(path));
                var root = Path.GetFullPath(AttachmentResolver.ExpandHome(projectDir));
                return full.StartsWith(root, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private static void WriteAcpError(string code)
        {
            var payload = new
            {
                ok = false,
                error = code
            };
            Console.WriteLine(JsonSerializer.Serialize(payload, JsonIndented));
        }

        private sealed record AcpRequest(
            string? Prompt,
            string? SessionId,
            bool Continue,
            bool Share,
            string? Provider,
            string? Model,
            string? Agent,
            string? Workflow,
            string? Title,
            string? WorkingDirectory,
            string[]? Files);
    }
}
