using Aevatar.Agents.AI.Core.Configuration;
using Aevatar.Platform.Core.Config;
using Aevatar.Platform.Core.Sessions;
using Aevatar.Platform.Core.Tools;
using Aevatar.Platform.Core.Workflow;
using Microsoft.Extensions.Logging.Abstractions;
using Spectre.Console;

namespace Aevatar.Platform.Cli.Tui;

// ============================================================
//  TuiApp
//
//  说明：
//  - OpenCode parity 的 TUI 入口（MVP）
//  - 只做运行时组装 + 事件循环
// ============================================================
public sealed class TuiApp
{
    public static async Task RunAsync(TuiOptions options, CancellationToken ct = default)
    {
        ApplyConfigOverrides(options);
        var effective = new AevatarConfigLoader().Load();

        var sessions = new SessionService(
            new FileEventStore(Path.Combine(effective.ConfigDirectory, "sessions")),
            effective.ConfigDirectory);

        var compiler = new PlatformMeshCompiler(
            new GlobalAgentYamlRegistry(NullLogger<GlobalAgentYamlRegistry>.Instance));

        var engine = new WorkflowEngine();
        var policy = PlatformToolPolicy.Create(
            effective.Config,
            TuiHandlers.ResolveToolPolicyPreset(options.Profile ?? effective.Config.Agents.DefaultProfile));

        var runtime = await TuiHandlers.EnsureSessionAsync(options, effective, sessions, ct);
        RenderBanner(runtime);

        while (!ct.IsCancellationRequested)
        {
            var line = AnsiConsole.Prompt(new TextPrompt<string>("[green]>[/] ").AllowEmpty());
            var parsed = InputParser.Parse(line);

            if (parsed.Kind == ParsedInputKind.Empty)
                continue;

            if (parsed.Kind == ParsedInputKind.Command)
            {
                var keep = await TuiHandlers.HandleCommandAsync(
                    parsed,
                    runtime,
                    effective,
                    sessions,
                    compiler,
                    engine,
                    policy,
                    ct);
                if (!keep)
                    break;
                continue;
            }

            if (parsed.Kind == ParsedInputKind.Shell)
            {
                await ShellRunner.RunAsync(parsed.Text, policy, ct);
                continue;
            }

            await TuiHandlers.HandleMessageAsync(
                parsed,
                runtime,
                effective,
                sessions,
                compiler,
                engine,
                policy,
                ct);
        }
    }

    private static void RenderBanner(SessionRuntime runtime)
    {
        AnsiConsole.MarkupLine("[bold]Aevatar Platform[/] TUI (OpenCode parity)");
        AnsiConsole.MarkupLine($"Session: [cyan]{runtime.SessionId}[/]  Workflow: [cyan]{runtime.Workflow}[/]");
        AnsiConsole.MarkupLine("Type /help for commands. Ctrl+C to exit.");
    }

    private static void ApplyConfigOverrides(TuiOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.ConfigDir))
            Environment.SetEnvironmentVariable("AEVATAR_CONFIG_DIR", options.ConfigDir);

        if (!string.IsNullOrWhiteSpace(options.ConfigPath))
            Environment.SetEnvironmentVariable("AEVATAR_CONFIG", options.ConfigPath);

        if (!string.IsNullOrWhiteSpace(options.SecretsPath))
            Environment.SetEnvironmentVariable("AEVATAR_SECRETS", options.SecretsPath);
    }
}


