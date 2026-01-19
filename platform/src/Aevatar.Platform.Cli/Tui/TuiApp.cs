using System.Diagnostics;
using Aevatar.Agents.AI.Core.Configuration;
using Aevatar.Platform.Core.Config;
using Aevatar.Platform.Core.Sessions;
using Aevatar.Platform.Core.Tools;
using Aevatar.Platform.Core.Workflow;
using Microsoft.Extensions.Logging.Abstractions;
using Spectre.Console;

namespace Aevatar.Platform.Cli.Tui;

// ============================================================
//  TuiApp (OpenTUI edition)
//
//  说明：
//  - 彻底移除旧的 .NET Terminal GUI 实现（macOS 下输入/Tab 不稳定）
//  - GUI 模式改为启动 OpenTUI 前端（TypeScript + Zig）
//  - 若本机缺少 bun/zig，则回退到 Console REPL（保证可用）
//
//  参考：
//  - OpenTUI: https://github.com/anomalyco/opentui
// ============================================================
public sealed class TuiApp
{
    private enum TuiMode
    {
        Auto = 0,
        Console = 1,
        Gui = 2
    }

    private sealed record RuntimeContext(
        AevatarEffectiveConfig Effective,
        SessionService Sessions,
        PlatformMeshCompiler Compiler,
        WorkflowEngine Engine,
        PlatformToolPolicy Policy,
        SessionRuntime Runtime);

    public static async Task RunAsync(TuiOptions options, CancellationToken ct = default)
    {
        var mode = ResolveTuiMode();

        // GUI: OpenTUI frontend (external process)
        if (mode != TuiMode.Console && CanUseGui())
        {
            var ok = await TryRunOpenTuiAsync(options, ct);
            if (ok)
                return;
        }

        // Fallback: Console REPL (Spectre.Console)
        await RunConsoleAsync(options, ct);
    }

    private static async Task<bool> TryRunOpenTuiAsync(TuiOptions options, CancellationToken ct)
    {
        var output = new ConsoleTuiOutput();
        var runtime = await BuildRuntimeAsync(options, output, ct);
        if (runtime == null)
            return false;

        var frontendDir = ResolveOpenTuiFrontendDir();
        if (frontendDir == null)
        {
            output.MarkupLine("[yellow]OpenTUI frontend not found in tool package. Falling back to console.[/]");
            return false;
        }

        if (!TryFindOnPath("bun") || !TryFindOnPath("zig"))
        {
            output.MarkupLine("[yellow]Missing 'bun' or 'zig'. Install both to use OpenTUI GUI. Falling back to console.[/]");
            output.MarkupLine("See: [link]https://github.com/anomalyco/opentui[/]");
            return false;
        }

        try
        {
            await using var backend = await OpenTuiBackendServer.StartAsync(
                runtime.Effective,
                runtime.Sessions,
                runtime.Compiler,
                runtime.Engine,
                runtime.Policy,
                runtime.Runtime,
                ct);

            if (!await EnsureOpenTuiDependenciesAsync(frontendDir, output, ct))
                return false;

            // ------------------------------------------------------------
            // Launch OpenTUI frontend with env-based contract
            // NOTE: We avoid any fixed ports (repo policy forbids :5000).
            // ------------------------------------------------------------
            var env = new Dictionary<string, string>
            {
                ["AEVATAR_CONFIG_DIR"] = runtime.Effective.ConfigDirectory,
                ["AEVATAR_SESSION_ID"] = runtime.Runtime.SessionId,
                ["AEVATAR_WORKFLOW"] = runtime.Runtime.Workflow,
                ["AEVATAR_PROFILE"] = runtime.Runtime.Profile,
                ["AEVATAR_PROVIDER"] = runtime.Effective.Config.Models.DefaultProvider ?? string.Empty,
                ["AEVATAR_MODEL"] = runtime.Effective.Config.Models.DefaultModel ?? string.Empty,
                ["AEVATAR_CWD"] = Directory.GetCurrentDirectory(),
                ["AEVATAR_TUI_BACKEND_URL"] = backend.BaseUrl
            };

            var capture = (Environment.GetEnvironmentVariable("AEVATAR_TUI_CAPTURE_LOG") ?? string.Empty).Trim() == "1";
            if (capture)
            {
                var result = await RunBunAsync("run start", frontendDir, env, ct);
                if (result.ExitCode == 0)
                    return true;

                output.MarkupLine($"[yellow]OpenTUI frontend exited with code {result.ExitCode}. Falling back to console.[/]");
                PrintBunError(output, result);
                return false;
            }

            var exitCode = await RunBunInteractiveAsync("run start", frontendDir, env, ct);
            if (exitCode == 0)
                return true;

            output.MarkupLine($"[yellow]OpenTUI frontend exited with code {exitCode}. Falling back to console.[/]");
            return false;
        }
        catch (Exception ex)
        {
            output.MarkupLine($"[yellow]OpenTUI GUI failed: {Markup.Escape(ex.Message)}[/]");
            return false;
        }
    }

    private static async Task RunConsoleAsync(TuiOptions options, CancellationToken ct)
    {
        var output = new ConsoleTuiOutput();
        var runtime = await BuildRuntimeAsync(options, output, ct);
        if (runtime == null)
            return;

        RenderBanner(runtime.Runtime, output);

        while (!ct.IsCancellationRequested)
        {
            var line = AnsiConsole.Prompt(new TextPrompt<string>("[green]>[/] ").AllowEmpty());
            var parsed = InputParser.Parse(line);

            if (parsed.Kind == ParsedInputKind.Empty)
                continue;

            var keep = await HandleParsedInputAsync(parsed, runtime, output, ct);
            if (!keep)
                break;
        }
    }

    private static async Task<bool> HandleParsedInputAsync(
        ParsedInput parsed,
        RuntimeContext runtime,
        ITuiOutput output,
        CancellationToken ct)
    {
        if (parsed.Kind == ParsedInputKind.Command)
        {
            return await TuiHandlers.HandleCommandAsync(
                parsed,
                runtime.Runtime,
                runtime.Effective,
                runtime.Sessions,
                runtime.Compiler,
                runtime.Engine,
                runtime.Policy,
                output,
                ct);
        }

        if (parsed.Kind == ParsedInputKind.Shell)
        {
            await ShellRunner.RunAsync(parsed.Text, runtime.Policy, output, ct);
            return true;
        }

        if (parsed.Kind == ParsedInputKind.Message)
        {
            await TuiHandlers.HandleMessageAsync(
                parsed,
                runtime.Runtime,
                runtime.Effective,
                runtime.Sessions,
                runtime.Compiler,
                runtime.Engine,
                runtime.Policy,
                output,
                ct);
        }

        return true;
    }

    private static async Task<RuntimeContext?> BuildRuntimeAsync(TuiOptions options, ITuiOutput output, CancellationToken ct)
    {
        ApplyConfigOverrides(options);
        var effective = new AevatarConfigLoader().Load();
        AevatarConfigLoader.EnsureBootstrapAssets(effective);
        if (!ApplyWorkingDirectory(options, output))
            return null;

        var sessions = new SessionService(
            new FileEventStore(Path.Combine(effective.ConfigDirectory, "sessions")),
            effective.ConfigDirectory);

        var compiler = new PlatformMeshCompiler(
            new GlobalAgentYamlRegistry(NullLogger<GlobalAgentYamlRegistry>.Instance),
            configAgentsDir: Path.Combine(effective.ConfigDirectory, "agents"));

        var engine = new WorkflowEngine();
        var policy = PlatformToolPolicy.Create(
            effective.Config,
            TuiHandlers.ResolveToolPolicyPreset(options.Profile ?? effective.Config.Agents.DefaultProfile));

        var runtime = await TuiHandlers.EnsureSessionAsync(options, effective, sessions, ct);
        return new RuntimeContext(effective, sessions, compiler, engine, policy, runtime);
    }

    private static void RenderBanner(SessionRuntime runtime, ITuiOutput output)
    {
        output.MarkupLine("[bold]Aevatar Platform[/] TUI (OpenCode parity)");
        output.MarkupLine($"Session: [cyan]{Markup.Escape(runtime.SessionId)}[/]  Workflow: [cyan]{Markup.Escape(runtime.Workflow)}[/]");
        output.MarkupLine("Type /help for commands. Ctrl+C to exit.");
    }

    private static TuiMode ResolveTuiMode()
    {
        var raw = (Environment.GetEnvironmentVariable("AEVATAR_TUI_MODE") ?? "auto").Trim().ToLowerInvariant();
        return raw switch
        {
            "repl" or "console" or "plain" => TuiMode.Console,
            "gui" or "full" => TuiMode.Gui,
            _ => TuiMode.Auto
        };
    }

    private static bool CanUseGui()
        => !Console.IsInputRedirected && !Console.IsOutputRedirected;

    private static void ApplyConfigOverrides(TuiOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.ConfigDir))
        {
            Environment.SetEnvironmentVariable("AEVATAR_CONFIG_DIR", options.ConfigDir);
            Environment.SetEnvironmentVariable("AEVATAR_SECRETS_DIR", options.ConfigDir);
        }

        if (!string.IsNullOrWhiteSpace(options.ConfigPath))
            Environment.SetEnvironmentVariable("AEVATAR_CONFIG", options.ConfigPath);

        if (!string.IsNullOrWhiteSpace(options.SecretsPath))
        {
            Environment.SetEnvironmentVariable("AEVATAR_SECRETS_PATH", options.SecretsPath);
            Environment.SetEnvironmentVariable("AEVATAR_SECRETS", options.SecretsPath);
        }
    }

    private static bool ApplyWorkingDirectory(TuiOptions options, ITuiOutput output)
    {
        var dir = (options.WorkingDirectory ?? string.Empty).Trim();
        if (dir.Length == 0)
            return true;

        var full = Path.GetFullPath(AttachmentResolver.ExpandHome(dir));
        if (!Directory.Exists(full))
        {
            output.MarkupLine($"Working directory not found: [red]{Markup.Escape(full)}[/]");
            return false;
        }

        Directory.SetCurrentDirectory(full);
        return true;
    }

    private static string? ResolveOpenTuiFrontendDir()
    {
        // When packed as dotnet tool, content is copied next to the executable.
        var baseDir = AppContext.BaseDirectory;
        var dir = Path.Combine(baseDir, "tui-opentui");
        return Directory.Exists(dir) ? dir : null;
    }

    private static bool TryFindOnPath(string exe)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = OperatingSystem.IsWindows() ? "where" : "which",
                Arguments = exe,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            using var p = Process.Start(psi);
            if (p == null)
                return false;
            p.WaitForExit(2000);
            return p.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private static async Task<bool> EnsureOpenTuiDependenciesAsync(string frontendDir, ITuiOutput output, CancellationToken ct)
    {
        var nodeModules = Path.Combine(frontendDir, "node_modules");
        if (Directory.Exists(nodeModules))
            return true;

        if ((Environment.GetEnvironmentVariable("AEVATAR_TUI_SKIP_INSTALL") ?? string.Empty).Trim() == "1")
            return true;

        output.MarkupLine($"[yellow]Installing OpenTUI frontend dependencies (bun install)...[/]");
        output.MarkupLine($"[yellow]Working dir: {Markup.Escape(frontendDir)}[/]");
        output.MarkupLine("[yellow]This may take a while on first run. Press Ctrl+C to cancel.[/]");

        var timeoutMs = GetInstallTimeoutMs();
        using var timeoutCts = timeoutMs > 0 ? new CancellationTokenSource(timeoutMs) : null;
        using var linked = timeoutCts != null
            ? CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token)
            : CancellationTokenSource.CreateLinkedTokenSource(ct);

        var result = await RunBunStreamingAsync("install", frontendDir, linked.Token, output);
        if (timeoutCts?.IsCancellationRequested == true)
        {
            output.MarkupLine("[yellow]OpenTUI dependency install timed out. Falling back to console.[/]");
            output.MarkupLine("Tip: set AEVATAR_TUI_INSTALL_TIMEOUT_MS to a larger value or run bun install manually.");
            return false;
        }

        if (result.ExitCode == 0)
            return true;

        output.MarkupLine($"[yellow]OpenTUI dependency install failed (code {result.ExitCode}). Falling back to console.[/]");
        PrintBunError(output, result);
        return false;
    }

    private static async Task<ProcessResult> RunBunAsync(
        string args,
        string workingDir,
        IReadOnlyDictionary<string, string>? env,
        CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "bun",
            Arguments = args,
            WorkingDirectory = workingDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        if (env != null)
        {
            foreach (var kv in env)
                psi.Environment[kv.Key] = kv.Value;
        }

        using var proc = Process.Start(psi);
        if (proc == null)
            return new ProcessResult(-1, string.Empty, "Failed to start bun process.");

        var stdoutTask = proc.StandardOutput.ReadToEndAsync();
        var stderrTask = proc.StandardError.ReadToEndAsync();
        await proc.WaitForExitAsync(ct);

        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        return new ProcessResult(proc.ExitCode, stdout, stderr);
    }

    private static async Task<ProcessResult> RunBunStreamingAsync(
        string args,
        string workingDir,
        CancellationToken ct,
        ITuiOutput output)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "bun",
            Arguments = args,
            WorkingDirectory = workingDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        using var proc = Process.Start(psi);
        if (proc == null)
            return new ProcessResult(-1, string.Empty, "Failed to start bun process.");

        var stdoutLines = new List<string>();
        var stderrLines = new List<string>();

        var stdoutTask = Task.Run(async () =>
        {
            string? line;
            while ((line = await proc.StandardOutput.ReadLineAsync()) != null)
            {
                stdoutLines.Add(line);
                output.WriteLine($"[bun] {line}");
            }
        }, ct);

        var stderrTask = Task.Run(async () =>
        {
            string? line;
            while ((line = await proc.StandardError.ReadLineAsync()) != null)
            {
                stderrLines.Add(line);
                output.WriteLine($"[bun] {line}");
            }
        }, ct);

        using var reg = ct.Register(() =>
        {
            try { if (!proc.HasExited) proc.Kill(entireProcessTree: true); } catch { }
        });

        await proc.WaitForExitAsync(ct);
        await Task.WhenAll(stdoutTask, stderrTask);

        return new ProcessResult(proc.ExitCode, string.Join(Environment.NewLine, stdoutLines), string.Join(Environment.NewLine, stderrLines));
    }

    private static async Task<int> RunBunInteractiveAsync(
        string args,
        string workingDir,
        IReadOnlyDictionary<string, string> env,
        CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "bun",
            Arguments = args,
            WorkingDirectory = workingDir,
            UseShellExecute = false
        };

        foreach (var kv in env)
            psi.Environment[kv.Key] = kv.Value;

        using var proc = Process.Start(psi);
        if (proc == null)
            return -1;

        await proc.WaitForExitAsync(ct);
        return proc.ExitCode;
    }

    private static void PrintBunError(ITuiOutput output, ProcessResult result)
    {
        var combined = string.Join(Environment.NewLine, new[] { result.Stdout, result.Stderr }.Where(s => !string.IsNullOrWhiteSpace(s)));
        if (string.IsNullOrWhiteSpace(combined))
            return;

        var lines = combined.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
        var start = Math.Max(0, lines.Length - 20);
        var tail = string.Join(Environment.NewLine, lines.Skip(start));
        output.WriteLine(tail);
    }

    private static int GetInstallTimeoutMs()
    {
        var raw = (Environment.GetEnvironmentVariable("AEVATAR_TUI_INSTALL_TIMEOUT_MS") ?? string.Empty).Trim();
        if (int.TryParse(raw, out var ms))
            return Math.Clamp(ms, 0, 60 * 60 * 1000);
        return 120_000; // 2 minutes default
    }

    private readonly record struct ProcessResult(int ExitCode, string Stdout, string Stderr);
}

