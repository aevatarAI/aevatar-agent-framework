using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.Invocation;
using System.CommandLine.Parsing;

namespace Aevatar.Platform.Cli.Commands;

// ============================================================
//  RootCommands (OpenCode parity surface)
//
//  说明：
//  - 只提供 CLI 命令面与基本 wiring
//  - 具体业务执行由 Platform.Core 逐步补全
// ============================================================
public static partial class RootCommands
{
    public static Parser BuildParser()
        => new CommandLineBuilder(BuildRootCommand()).UseDefaults().Build();

    public static RootCommand BuildRootCommand()
    {
        var root = new RootCommand("Aevatar Platform CLI (OpenCode parity)");
        var projectArg = new Argument<string?>("project", () => null, "Project directory (default: current)");
        root.AddArgument(projectArg);
        RootOptions.AddGlobalOptions(root);

        // Default action: enter TUI unless --command is provided.
        root.SetHandler(async (InvocationContext ctx) =>
        {
            Handlers.ApplyLoggingOverrides(ctx.ParseResult);

            var command = ctx.ParseResult.GetValueForOption(RootOptions.Command);
            if (!string.IsNullOrWhiteSpace(command))
            {
                var options = Handlers.BuildRunOptions(ctx.ParseResult, command);
                await Handlers.RunOnceAsync(ctx, options);
                return;
            }

            var project = ctx.ParseResult.GetValueForArgument(projectArg);
            await Handlers.StartTuiAsync(ctx, project);
        });

        root.AddCommand(BuildTuiCommand());
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
        root.AddCommand(BuildGithubCommand());
        root.AddCommand(BuildStatsCommand());
        root.AddCommand(BuildExportCommand());
        root.AddCommand(BuildImportCommand());
        root.AddCommand(BuildAcpCommand());
        root.AddCommand(BuildUpgradeCommand());
        root.AddCommand(BuildUninstallCommand());

        return root;
    }

    private static class RootOptions
    {
        public static readonly Option<string?> LogLevel =
            new("--log-level", "Set log level (debug|info|warn|error)");

        public static readonly Option<bool> PrintLogs =
            new("--print-logs", "Print logs to stderr");

        public static readonly Option<string?> ConfigDir =
            new("--config-dir", "Override config directory (default ~/.aevatar)");

        public static readonly Option<string?> ConfigPath =
            new("--config", "Override config.json path");

        public static readonly Option<string?> SecretsPath =
            new("--secrets", "Override secrets.json path");

        public static readonly Option<string?> Workflow =
            new("--workflow", "Select workflow");

        public static readonly Option<string?> Provider =
            new("--provider", "Select provider");

        public static readonly Option<string?> Model =
            new(["--model", "-m"], "Select model");

        public static readonly Option<string?> Profile =
            new("--profile", "Select profile");

        public static readonly Option<string?> Agent =
            new("--agent", "Select agent/profile");

        public static readonly Option<string?> Command =
            new("--command", "Run a single command and exit");

        public static readonly Option<bool> Continue =
            new(new[] { "-c", "--continue", "--resume" }, "Continue last session");

        public static readonly Option<string?> Session =
            new(new[] { "--session", "-s" }, "Session ID to continue");

        public static readonly Option<string?> WorkingDirectory =
            new("--dir", "Working directory");

        public static void AddGlobalOptions(Command command)
        {
            command.AddGlobalOption(LogLevel);
            command.AddGlobalOption(PrintLogs);
            command.AddGlobalOption(ConfigDir);
            command.AddGlobalOption(ConfigPath);
            command.AddGlobalOption(SecretsPath);
            command.AddGlobalOption(Workflow);
            command.AddGlobalOption(Provider);
            command.AddGlobalOption(Model);
            command.AddGlobalOption(Profile);
            command.AddGlobalOption(Agent);
            command.AddGlobalOption(Command);
            command.AddGlobalOption(Continue);
            command.AddGlobalOption(Session);
            command.AddGlobalOption(WorkingDirectory);
        }
    }
}