using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.Invocation;
using System.CommandLine.Parsing;
using System.Text.Json;
using Aevatar.Agents.AI.Core.Configuration;
using Aevatar.Platform.Cli.Tui;
using Aevatar.Platform.Core.Config;
using Aevatar.Platform.Core.Sessions;
using Aevatar.Platform.Core.Workflow;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aevatar.Platform.Cli.Commands;

// ============================================================
//  RootCommands (OpenCode parity surface)
//
//  说明：
//  - 只提供 CLI 命令面与基本 wiring
//  - 具体业务执行由 Platform.Core 逐步补全
// ============================================================
public static class RootCommands
{
    public static Parser BuildParser()
        => new CommandLineBuilder(BuildRootCommand()).UseDefaults().Build();

    public static RootCommand BuildRootCommand()
    {
        var root = new RootCommand("Aevatar Platform CLI (OpenCode parity)");
        RootOptions.AddGlobalOptions(root);

        // Default action: enter TUI unless -c is provided.
        root.SetHandler(async (InvocationContext ctx) =>
        {
            var command = ctx.ParseResult.GetValueForOption(RootOptions.Command);
            if (!string.IsNullOrWhiteSpace(command))
            {
                await Handlers.RunOnceAsync(ctx, command);
                return;
            }

            await Handlers.StartTuiAsync(ctx);
        });

        root.AddCommand(BuildRunCommand());
        root.AddCommand(BuildServeCommand());
        root.AddCommand(BuildWebCommand());
        root.AddCommand(BuildAttachCommand());
        root.AddCommand(BuildSessionsCommand());
        root.AddCommand(BuildConfigCommand());
        root.AddCommand(BuildAgentsCommand());
        root.AddCommand(BuildWorkflowsCommand());
        root.AddCommand(BuildModelsCommand());
        root.AddCommand(BuildMcpCommand());
        root.AddCommand(BuildAuthCommand());
        root.AddCommand(BuildStatsCommand());
        root.AddCommand(BuildExportCommand());
        root.AddCommand(BuildImportCommand());
        root.AddCommand(BuildAcpCommand());
        root.AddCommand(BuildUpgradeCommand());
        root.AddCommand(BuildUninstallCommand());

        return root;
    }

    private static Command BuildRunCommand()
    {
        var promptArg = new Argument<string?>("prompt", () => null, "Prompt to run once");
        var cmd = new Command("run", "Run a single task and exit");
        cmd.AddArgument(promptArg);

        cmd.SetHandler(async (InvocationContext ctx) =>
        {
            var prompt = ctx.ParseResult.GetValueForArgument(promptArg);
            await Handlers.RunOnceAsync(ctx, prompt);
        });

        return cmd;
    }

    private static Command BuildServeCommand()
    {
        var port = new Option<int>("--port", () => 5678, "Server port (must not be 5000)");
        var host = new Option<string>("--host", () => "127.0.0.1", "Server host");
        var cmd = new Command("serve", "Start server for attach/web");
        cmd.AddOption(host);
        cmd.AddOption(port);

        cmd.SetHandler((InvocationContext ctx) =>
        {
            var serverHost = ctx.ParseResult.GetValueForOption(host);
            var serverPort = ctx.ParseResult.GetValueForOption(port);
            Handlers.PrintNotImplemented(ctx, $"serve {serverHost}:{serverPort}");
        });

        return cmd;
    }

    private static Command BuildWebCommand()
    {
        var cmd = new Command("web", "Open web UI (if available)");
        cmd.SetHandler((InvocationContext ctx) => Handlers.PrintNotImplemented(ctx, "web"));
        return cmd;
    }

    private static Command BuildAttachCommand()
    {
        var url = new Option<string?>("--url", "Server base URL");
        var session = new Option<string?>("--session", "Session ID");
        var cmd = new Command("attach", "Attach to a running server session");
        cmd.AddOption(url);
        cmd.AddOption(session);

        cmd.SetHandler((InvocationContext ctx) =>
        {
            var target = ctx.ParseResult.GetValueForOption(url) ?? "http://127.0.0.1:5678";
            var sid = ctx.ParseResult.GetValueForOption(session);
            Handlers.PrintNotImplemented(ctx, $"attach {target} {sid}".Trim());
        });

        return cmd;
    }

    private static Command BuildSessionsCommand()
    {
        var list = new Command("list", "List sessions");
        list.SetHandler(async (InvocationContext ctx) =>
        {
            var service = Handlers.CreateSessionService(ctx);
            var sessions = await service.ListSessionsAsync(ctx.GetCancellationToken());
            if (sessions.Count == 0)
            {
                Console.WriteLine("(no sessions)");
                return;
            }

            foreach (var s in sessions)
            {
                var ts = s.LastActivityUtc?.ToString("O") ?? "-";
                Console.WriteLine($"{s.SessionId}\t{s.Profile}\t{s.ActiveWorkflow}\t{ts}");
            }
        });

        var showId = new Argument<string>("session-id", "Session ID");
        var show = new Command("show", "Show session state");
        show.AddArgument(showId);
        show.SetHandler(async (InvocationContext ctx) =>
        {
            var sessionId = ctx.ParseResult.GetValueForArgument(showId);
            var service = Handlers.CreateSessionService(ctx);
            var state = await service.GetSessionStateAsync(sessionId, ctx.GetCancellationToken());
            if (state == null)
            {
                Console.WriteLine($"Session '{sessionId}' not found.");
                return;
            }

            Console.WriteLine(JsonSerializer.Serialize(state, new JsonSerializerOptions
            {
                WriteIndented = true
            }));
        });

        var exportId = new Argument<string>("session-id", "Session ID");
        var exportPath = new Argument<string>("output", "Output file path");
        var export = new Command("export", "Export session events");
        export.AddArgument(exportId);
        export.AddArgument(exportPath);
        export.SetHandler(async (InvocationContext ctx) =>
        {
            var sessionId = ctx.ParseResult.GetValueForArgument(exportId);
            var path = ctx.ParseResult.GetValueForArgument(exportPath);
            var service = Handlers.CreateSessionService(ctx);
            await service.ExportSessionAsync(sessionId, path, ctx.GetCancellationToken());
            Console.WriteLine($"Exported session '{sessionId}' to {path}");
        });

        var importPath = new Argument<string>("input", "Export file path");
        var overwrite = new Option<bool>("--overwrite", "Overwrite existing session if id collides");
        var import = new Command("import", "Import session events");
        import.AddArgument(importPath);
        import.AddOption(overwrite);
        import.SetHandler(async (InvocationContext ctx) =>
        {
            var path = ctx.ParseResult.GetValueForArgument(importPath);
            var overwriteExisting = ctx.ParseResult.GetValueForOption(overwrite);
            var service = Handlers.CreateSessionService(ctx);
            var sessionId = await service.ImportSessionAsync(path, overwriteExisting, ctx.GetCancellationToken());
            Console.WriteLine($"Imported session as '{sessionId}'");
        });

        var sessions = new Command("sessions", "Session management");
        sessions.AddAlias("session");
        sessions.AddCommand(list);
        sessions.AddCommand(show);
        sessions.AddCommand(export);
        sessions.AddCommand(import);

        return sessions;
    }

    private static Command BuildConfigCommand()
    {
        var show = new Command("show", "Show config (secrets redacted)");
        show.SetHandler((InvocationContext ctx) =>
        {
            var effective = Handlers.LoadEffectiveConfig(ctx);
            Console.WriteLine(JsonSerializer.Serialize(effective.Config, new JsonSerializerOptions
            {
                WriteIndented = true
            }));
        });

        var edit = new Command("edit", "Edit config file");
        edit.SetHandler((InvocationContext ctx) => Handlers.PrintNotImplemented(ctx, "config edit"));

        var init = new Command("init", "Initialize config in ~/.aevatar");
        init.SetHandler((InvocationContext ctx) => Handlers.PrintNotImplemented(ctx, "config init"));

        var config = new Command("config", "Config management");
        config.AddCommand(show);
        config.AddCommand(edit);
        config.AddCommand(init);
        return config;
    }

    private static Command BuildAgentsCommand()
    {
        var list = new Command("list", "List agent roles");
        list.SetHandler((InvocationContext ctx) =>
        {
            var dir = Handlers.GetAgentsDirectory(ctx);
            if (!Directory.Exists(dir))
            {
                Console.WriteLine("(no agents)");
                return;
            }

            foreach (var file in Directory.EnumerateFiles(dir, "*.yaml"))
            {
                var name = Path.GetFileNameWithoutExtension(file);
                Console.WriteLine(name);
            }
        });

        var showId = new Argument<string>("role", "Agent role name");
        var show = new Command("show", "Show agent role YAML");
        show.AddArgument(showId);
        show.SetHandler((InvocationContext ctx) =>
        {
            var role = ctx.ParseResult.GetValueForArgument(showId);
            var path = Path.Combine(Handlers.GetAgentsDirectory(ctx), $"{role}.yaml");
            if (!File.Exists(path))
            {
                Console.WriteLine($"Agent role '{role}' not found.");
                return;
            }

            Console.WriteLine(File.ReadAllText(path));
        });

        var agents = new Command("agents", "Agent role management");
        agents.AddAlias("agent");
        agents.AddCommand(list);
        agents.AddCommand(show);
        return agents;
    }

    private static Command BuildWorkflowsCommand()
    {
        var list = new Command("list", "List workflow files");
        list.SetHandler((InvocationContext ctx) =>
        {
            var dir = Handlers.GetWorkflowsDirectory(ctx);
            if (!Directory.Exists(dir))
            {
                Console.WriteLine("(no workflows)");
                return;
            }

            foreach (var file in Directory.EnumerateFiles(dir))
            {
                Console.WriteLine(Path.GetFileName(file));
            }
        });

        var fileArg = new Argument<string>("file", "Workflow file path (JSON/YAML)");
        var validate = new Command("validate", "Validate workflow DSL");
        validate.AddArgument(fileArg);
        validate.SetHandler((InvocationContext ctx) =>
        {
            var path = ctx.ParseResult.GetValueForArgument(fileArg);
            if (!File.Exists(path))
            {
                Console.WriteLine($"File not found: {path}");
                return;
            }

            var raw = File.ReadAllText(path);
            var roles = new GlobalAgentYamlRegistry(NullLogger<GlobalAgentYamlRegistry>.Instance);
            var compiler = new PlatformMeshCompiler(roles);
            var result = compiler.Compile(raw);

            if (result.Ok)
            {
                Console.WriteLine("OK");
                return;
            }

            foreach (var err in result.Errors)
            {
                var pathInfo = string.IsNullOrWhiteSpace(err.Path) ? "" : $" ({err.Path})";
                Console.WriteLine($"{err.Code}: {err.Message}{pathInfo}");
            }
        });

        var workflows = new Command("workflows", "Workflow management");
        workflows.AddAlias("workflow");
        workflows.AddCommand(list);
        workflows.AddCommand(validate);
        return workflows;
    }

    private static Command BuildModelsCommand()
    {
        var list = new Command("list", "List model providers");
        list.SetHandler((InvocationContext ctx) =>
        {
            var config = Handlers.LoadEffectiveConfig(ctx).Config;
            if (config.Models.Providers.Count == 0)
            {
                Console.WriteLine("(no providers)");
                return;
            }

            foreach (var (name, provider) in config.Models.Providers.OrderBy(x => x.Key))
            {
                var model = provider.DefaultModel ?? "";
                Console.WriteLine($"{name}\t{model}");
            }
        });

        var models = new Command("models", "Model providers");
        models.AddCommand(list);
        return models;
    }

    private static Command BuildMcpCommand()
    {
        var list = new Command("list", "List MCP servers");
        list.SetHandler((InvocationContext ctx) => Handlers.PrintNotImplemented(ctx, "mcp list"));

        var mcp = new Command("mcp", "MCP servers");
        mcp.AddCommand(list);
        return mcp;
    }

    private static Command BuildAuthCommand()
    {
        var login = new Command("login", "Authenticate provider/MCP");
        login.SetHandler((InvocationContext ctx) => Handlers.PrintNotImplemented(ctx, "auth login"));

        var logout = new Command("logout", "Clear auth");
        logout.SetHandler((InvocationContext ctx) => Handlers.PrintNotImplemented(ctx, "auth logout"));

        var auth = new Command("auth", "Authentication");
        auth.AddCommand(login);
        auth.AddCommand(logout);
        return auth;
    }

    private static Command BuildStatsCommand()
    {
        var stats = new Command("stats", "Show session stats");
        stats.SetHandler((InvocationContext ctx) => Handlers.PrintNotImplemented(ctx, "stats"));
        return stats;
    }

    private static Command BuildExportCommand()
    {
        var id = new Argument<string>("session-id", "Session ID");
        var path = new Argument<string>("output", "Output file path");
        var export = new Command("export", "Export session events");
        export.AddArgument(id);
        export.AddArgument(path);

        export.SetHandler(async (InvocationContext ctx) =>
        {
            var sessionId = ctx.ParseResult.GetValueForArgument(id);
            var output = ctx.ParseResult.GetValueForArgument(path);
            var service = Handlers.CreateSessionService(ctx);
            await service.ExportSessionAsync(sessionId, output, ctx.GetCancellationToken());
            Console.WriteLine($"Exported session '{sessionId}' to {output}");
        });

        return export;
    }

    private static Command BuildImportCommand()
    {
        var path = new Argument<string>("input", "Export file path");
        var overwrite = new Option<bool>("--overwrite", "Overwrite existing session if id collides");
        var import = new Command("import", "Import session events");
        import.AddArgument(path);
        import.AddOption(overwrite);

        import.SetHandler(async (InvocationContext ctx) =>
        {
            var input = ctx.ParseResult.GetValueForArgument(path);
            var overwriteExisting = ctx.ParseResult.GetValueForOption(overwrite);
            var service = Handlers.CreateSessionService(ctx);
            var sessionId = await service.ImportSessionAsync(input, overwriteExisting, ctx.GetCancellationToken());
            Console.WriteLine($"Imported session as '{sessionId}'");
        });

        return import;
    }

    private static Command BuildAcpCommand()
    {
        var acp = new Command("acp", "Agent Control Plane (placeholder)");
        acp.SetHandler((InvocationContext ctx) => Handlers.PrintNotImplemented(ctx, "acp"));
        return acp;
    }

    private static Command BuildUpgradeCommand()
    {
        var upgrade = new Command("upgrade", "Upgrade Aevatar CLI");
        upgrade.SetHandler((InvocationContext ctx) => Handlers.PrintNotImplemented(ctx, "upgrade"));
        return upgrade;
    }

    private static Command BuildUninstallCommand()
    {
        var uninstall = new Command("uninstall", "Uninstall Aevatar CLI");
        uninstall.SetHandler((InvocationContext ctx) => Handlers.PrintNotImplemented(ctx, "uninstall"));
        return uninstall;
    }

    private static class RootOptions
    {
        public static readonly Option<string?> ConfigDir =
            new("--config-dir", "Override config directory (default ~/.aevatar)");

        public static readonly Option<string?> ConfigPath =
            new("--config", "Override config.yaml path");

        public static readonly Option<string?> SecretsPath =
            new("--secrets", "Override secrets.yaml path");

        public static readonly Option<string?> Workflow =
            new("--workflow", "Select workflow");

        public static readonly Option<string?> Model =
            new("--model", "Select model");

        public static readonly Option<string?> Profile =
            new("--profile", "Select profile");

        public static readonly Option<string?> Command =
            new(new[] { "-c", "--command" }, "Run a single command and exit");

        public static readonly Option<bool> Resume =
            new("--resume", "Resume last session");

        public static void AddGlobalOptions(Command command)
        {
            command.AddGlobalOption(ConfigDir);
            command.AddGlobalOption(ConfigPath);
            command.AddGlobalOption(SecretsPath);
            command.AddGlobalOption(Workflow);
            command.AddGlobalOption(Model);
            command.AddGlobalOption(Profile);
            command.AddGlobalOption(Command);
            command.AddGlobalOption(Resume);
        }
    }

    private static class Handlers
    {
        public static Task StartTuiAsync(InvocationContext ctx)
        {
            var options = new TuiOptions(
                Profile: ctx.ParseResult.GetValueForOption(RootOptions.Profile),
                Workflow: ctx.ParseResult.GetValueForOption(RootOptions.Workflow),
                Model: ctx.ParseResult.GetValueForOption(RootOptions.Model),
                Resume: ctx.ParseResult.GetValueForOption(RootOptions.Resume),
                ConfigDir: ctx.ParseResult.GetValueForOption(RootOptions.ConfigDir),
                ConfigPath: ctx.ParseResult.GetValueForOption(RootOptions.ConfigPath),
                SecretsPath: ctx.ParseResult.GetValueForOption(RootOptions.SecretsPath));

            return TuiApp.RunAsync(options, ctx.GetCancellationToken());
        }

        public static Task RunOnceAsync(InvocationContext ctx, string? prompt)
        {
            var text = string.IsNullOrWhiteSpace(prompt) ? "(empty)" : prompt.Trim();
            PrintNotImplemented(ctx, $"run {text}");
            return Task.CompletedTask;
        }

        public static void PrintNotImplemented(InvocationContext ctx, string action)
        {
            var msg = string.IsNullOrWhiteSpace(action)
                ? "Not implemented yet."
                : $"Not implemented yet: {action}.";
            ctx.Console.WriteLine(msg);
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

        public static string GetAgentsDirectory(InvocationContext ctx)
            => Path.Combine(LoadEffectiveConfig(ctx).ConfigDirectory, "agents");

        public static string GetWorkflowsDirectory(InvocationContext ctx)
            => Path.Combine(LoadEffectiveConfig(ctx).ConfigDirectory, "workflows");

        private static void ApplyConfigOverrides(ParseResult parse)
        {
            var configDir = parse.GetValueForOption(RootOptions.ConfigDir);
            if (!string.IsNullOrWhiteSpace(configDir))
                Environment.SetEnvironmentVariable("AEVATAR_CONFIG_DIR", configDir);

            var configPath = parse.GetValueForOption(RootOptions.ConfigPath);
            if (!string.IsNullOrWhiteSpace(configPath))
                Environment.SetEnvironmentVariable("AEVATAR_CONFIG", configPath);

            var secretsPath = parse.GetValueForOption(RootOptions.SecretsPath);
            if (!string.IsNullOrWhiteSpace(secretsPath))
                Environment.SetEnvironmentVariable("AEVATAR_SECRETS", secretsPath);
        }
    }
}


