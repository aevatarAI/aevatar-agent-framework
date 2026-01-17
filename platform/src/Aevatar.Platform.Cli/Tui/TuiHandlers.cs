using System.Diagnostics;
using System.Linq;
using Aevatar.Agents.AI.Core.Configuration;
using Aevatar.Platform;
using Aevatar.Platform.Core.Config;
using Aevatar.Platform.Core.Sessions;
using Aevatar.Platform.Core.Tools;
using Aevatar.Platform.Core.Workflow;
using Microsoft.Extensions.Logging.Abstractions;
using Spectre.Console;

namespace Aevatar.Platform.Cli.Tui;

// ============================================================
//  TUI Output
//
//  说明：
//  - 统一 TUI 输出接口（Console/GUI 共用）
// ============================================================
public interface ITuiOutput
{
    void Markup(string markup);

    void MarkupLine(string markup);

    void WriteLine(string text);

    void Clear();
}

public sealed class ConsoleTuiOutput : ITuiOutput
{
    public void Markup(string markup) => AnsiConsole.Markup(markup);

    public void MarkupLine(string markup) => AnsiConsole.MarkupLine(markup);

    public void WriteLine(string text) => AnsiConsole.WriteLine(text);

    public void Clear() => AnsiConsole.Clear();
}

// ============================================================
//  TuiHandlers
//
//  说明：
//  - 封装 /commands 与消息处理逻辑
// ============================================================
public static class TuiHandlers
{
    private const int MaxOutputChars = 4000;
    private const string HelpTitle = "TUI Commands";
    private static readonly string[] HelpLines =
    {
        "/help          Show this help",
        "/sessions      List sessions",
        "/sessions show <id>  Show session",
        "/workflow <name>     Switch workflow",
        "/profile <name>      Switch profile",
        "/editor        Open external editor",
        "/clear         Clear screen",
        "/quit          Exit",
        "!<cmd>         Run shell command (policy controlled)",
        "@<file>        Attach file (fuzzy match)"
    };

    public static string BuildHelpText()
    {
        var lines = new string[HelpLines.Length + 1];
        lines[0] = HelpTitle;
        Array.Copy(HelpLines, 0, lines, 1, HelpLines.Length);
        return string.Join(Environment.NewLine, lines);
    }

    public static async Task<bool> HandleCommandAsync(
        ParsedInput input,
        SessionRuntime runtime,
        AevatarEffectiveConfig effective,
        SessionService sessions,
        PlatformMeshCompiler? compiler,
        WorkflowEngine? engine,
        PlatformToolPolicy policy,
        ITuiOutput output,
        CancellationToken ct)
    {
        var command = (input.Command ?? string.Empty).Trim().ToLowerInvariant();
        var args = (input.CommandArgs ?? string.Empty).Trim();

        switch (command)
        {
            case "help":
                PrintHelp(output);
                return true;
            case "exit":
            case "quit":
                return false;
            case "clear":
                output.Clear();
                return true;
            case "sessions":
            case "session":
                await HandleSessionsCommandAsync(args, runtime, sessions, output, ct);
                return true;
            case "editor":
                await HandleEditorCommandAsync(runtime, effective, sessions, compiler, engine, policy, output, ct);
                return true;
            case "workflow":
                if (!string.IsNullOrWhiteSpace(args))
                    runtime.Workflow = args.Trim();
                output.MarkupLine($"Workflow: [cyan]{Markup.Escape(runtime.Workflow)}[/]");
                return true;
            case "profile":
                if (!string.IsNullOrWhiteSpace(args))
                    runtime.Profile = args.Trim();
                output.MarkupLine($"Profile: [cyan]{Markup.Escape(runtime.Profile)}[/]");
                return true;
            case "theme":
                output.MarkupLine("theme: not implemented yet.");
                return true;
            default:
                output.MarkupLine($"Unknown command: [yellow]/{Markup.Escape(command)}[/]");
                return true;
        }
    }

    public static async Task HandleMessageAsync(
        ParsedInput input,
        SessionRuntime runtime,
        AevatarEffectiveConfig effective,
        SessionService sessions,
        PlatformMeshCompiler? compiler,
        WorkflowEngine? engine,
        PlatformToolPolicy policy,
        ITuiOutput output,
        CancellationToken ct)
    {
        var resolved = ResolveAttachments(input.Attachments, policy, output);
        foreach (var file in resolved)
        {
            runtime.AttachedFiles.Add(file);
            output.MarkupLine($"Attached: [cyan]{Markup.Escape(file)}[/]");
        }

        if (input.Text.Length == 0)
            return;

        var userEvent = new PlatformSessionEvent
        {
            Seq = ++runtime.Seq,
            Timestamp = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(DateTime.UtcNow),
            UserMessage = new UserMessageEvent
            {
                MessageId = Guid.NewGuid().ToString("N"),
                Text = input.Text,
                AttachedFiles = { resolved }
            }
        };

        await sessions.AppendEventAsync(runtime.SessionId, userEvent, ct);

        var response = await TryRunWorkflowAsync(runtime, effective, compiler, engine, ct);
        response = Truncate(response, MaxOutputChars);

        var agentEvent = new PlatformSessionEvent
        {
            Seq = ++runtime.Seq,
            Timestamp = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(DateTime.UtcNow),
            AgentOutputDelta = new AgentOutputDeltaEvent
            {
                Agent = "system",
                MessageId = userEvent.UserMessage.MessageId,
                Delta = response,
                IsFinal = true
            }
        };

        await sessions.AppendEventAsync(runtime.SessionId, agentEvent, ct);
        await RenderStreamingAsync(response, output, ct);
    }

    // ============================================================
    //  GUI Chat API (OpenTUI)
    //
    //  说明：
    //  - 给 OpenTUI 前端一个“最小能聊几句”的后端能力
    //  - 复用现有 session event + workflow 执行，但直接返回 assistant 文本
    // ============================================================
    public static async Task<string> ChatOnceAsync(
        ParsedInput input,
        SessionRuntime runtime,
        AevatarEffectiveConfig effective,
        SessionService sessions,
        PlatformMeshCompiler? compiler,
        WorkflowEngine? engine,
        PlatformToolPolicy policy,
        CancellationToken ct)
    {
        var resolved = ResolveAttachments(input.Attachments, policy, new ConsoleTuiOutput());
        foreach (var file in resolved)
            runtime.AttachedFiles.Add(file);

        if (input.Text.Length == 0)
            return string.Empty;

        var userEvent = new PlatformSessionEvent
        {
            Seq = ++runtime.Seq,
            Timestamp = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(DateTime.UtcNow),
            UserMessage = new UserMessageEvent
            {
                MessageId = Guid.NewGuid().ToString("N"),
                Text = input.Text,
                AttachedFiles = { resolved }
            }
        };

        await sessions.AppendEventAsync(runtime.SessionId, userEvent, ct);

        var response = await TryRunWorkflowAsync(runtime, effective, compiler, engine, ct);
        response = Truncate(response, MaxOutputChars);

        var agentEvent = new PlatformSessionEvent
        {
            Seq = ++runtime.Seq,
            Timestamp = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(DateTime.UtcNow),
            AgentOutputDelta = new AgentOutputDeltaEvent
            {
                Agent = "system",
                MessageId = userEvent.UserMessage.MessageId,
                Delta = response,
                IsFinal = true
            }
        };

        await sessions.AppendEventAsync(runtime.SessionId, agentEvent, ct);
        return response;
    }

    public static async Task<SessionRuntime> EnsureSessionAsync(
        TuiOptions options,
        AevatarEffectiveConfig effective,
        SessionService sessions,
        CancellationToken ct)
    {
        var requestedSessionId = (options.SessionId ?? string.Empty).Trim();
        if (requestedSessionId.Length > 0)
        {
            var existing = await sessions.GetSessionStateAsync(requestedSessionId, ct);
            if (existing != null)
            {
                var events = await sessions.GetSessionEventsAsync(requestedSessionId, ct);
                var seq = (ulong)events.Count;
                return new SessionRuntime(requestedSessionId, seq, existing.Profile, existing.ActiveWorkflow);
            }

            var newState = new PlatformSessionState
            {
                SessionId = requestedSessionId,
                Profile = options.Profile ?? effective.Config.Agents.DefaultProfile,
                ActiveWorkflow = options.Workflow ?? effective.Config.Agents.DefaultWorkflow,
                WorkingDirectory = options.WorkingDirectory ?? Directory.GetCurrentDirectory(),
                Provider = ModelDefaults.ResolveProvider(
                    effective.Config.Models,
                    options.Provider,
                    options.Model),
                Model = ModelDefaults.ResolveModel(effective.Config.Models, options.Model)
            };

            await sessions.CreateSessionAsync(newState, ct);
            return new SessionRuntime(requestedSessionId, 0, newState.Profile, newState.ActiveWorkflow);
        }

        if (options.Resume)
        {
            var list = await sessions.ListSessionsAsync(ct);
            var latest = list.FirstOrDefault();
            if (latest != null)
            {
                var events = await sessions.GetSessionEventsAsync(latest.SessionId, ct);
                var seq = (ulong)events.Count;
                return new SessionRuntime(latest.SessionId, seq, latest.Profile, latest.ActiveWorkflow);
            }
        }

        var state = new PlatformSessionState
        {
            SessionId = string.Empty,
            Profile = options.Profile ?? effective.Config.Agents.DefaultProfile,
            ActiveWorkflow = options.Workflow ?? effective.Config.Agents.DefaultWorkflow,
            WorkingDirectory = options.WorkingDirectory ?? Directory.GetCurrentDirectory(),
            Provider = ModelDefaults.ResolveProvider(
                effective.Config.Models,
                options.Provider,
                options.Model),
            Model = ModelDefaults.ResolveModel(effective.Config.Models, options.Model)
        };

        var sessionId = await sessions.CreateSessionAsync(state, ct);
        return new SessionRuntime(sessionId, 0, state.Profile, state.ActiveWorkflow);
    }

    public static string ResolveToolPolicyPreset(string? profile)
    {
        var p = (profile ?? string.Empty).Trim().ToLowerInvariant();
        return p switch
        {
            "vibe" => "vibe_default",
            "worldbuilding" => "writing_default",
            _ => "coding_default"
        };
    }


    private static async Task HandleSessionsCommandAsync(
        string args,
        SessionRuntime runtime,
        SessionService sessions,
        ITuiOutput output,
        CancellationToken ct)
    {
        var parts = (args ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var verb = parts.Length > 0 ? parts[0] : "list";

        if (verb.Equals("list", StringComparison.OrdinalIgnoreCase))
        {
            var list = await sessions.ListSessionsAsync(ct);
            if (list.Count == 0)
            {
                output.MarkupLine("(no sessions)");
                return;
            }

            foreach (var s in list)
            {
                var ts = s.LastActivityUtc?.ToString("O") ?? "-";
                output.MarkupLine($"{Markup.Escape(s.SessionId)}\t{Markup.Escape(s.Profile)}\t{Markup.Escape(s.ActiveWorkflow)}\t{Markup.Escape(ts)}");
            }

            return;
        }

        var sessionId = parts.Length > 1 ? parts[1] : runtime.SessionId;
        var state = await sessions.GetSessionStateAsync(sessionId, ct);
        if (state == null)
        {
            output.MarkupLine($"Session '{Markup.Escape(sessionId)}' not found.");
            return;
        }

        var json = System.Text.Json.JsonSerializer.Serialize(state, new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = true
        });
        output.WriteLine(json);
    }

    private static async Task HandleEditorCommandAsync(
        SessionRuntime runtime,
        AevatarEffectiveConfig effective,
        SessionService sessions,
        PlatformMeshCompiler? compiler,
        WorkflowEngine? engine,
        PlatformToolPolicy policy,
        ITuiOutput output,
        CancellationToken ct)
    {
        var editor = (Environment.GetEnvironmentVariable("EDITOR") ?? string.Empty).Trim();
        if (editor.Length == 0)
            editor = (effective.Config.Ui.Editor ?? string.Empty).Trim();

        if (editor.Length == 0)
        {
            output.MarkupLine("EDITOR is not set.");
            return;
        }

        var tempFile = Path.Combine(Path.GetTempPath(), $"aevatar_{Guid.NewGuid():N}.txt");
        await File.WriteAllTextAsync(tempFile, string.Empty, ct);

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = editor,
                Arguments = QuoteArg(tempFile),
                UseShellExecute = false
            };

            using var proc = Process.Start(psi);
            if (proc == null)
            {
                output.MarkupLine("Failed to start editor.");
                return;
            }

            await proc.WaitForExitAsync(ct);
            var text = (await File.ReadAllTextAsync(tempFile, ct)).Trim();

            if (text.Length == 0)
                return;

            var parsed = InputParser.Parse(text);
            if (parsed.Kind == ParsedInputKind.Message)
                await HandleMessageAsync(parsed, runtime, effective, sessions, compiler, engine, policy, output, ct);
        }
        catch (Exception ex)
        {
            output.MarkupLine($"Editor failed: {Markup.Escape(ex.Message)}");
        }
        finally
        {
            try { File.Delete(tempFile); } catch { }
        }
    }

    private static async Task<string> TryRunWorkflowAsync(
        SessionRuntime runtime,
        AevatarEffectiveConfig effective,
        PlatformMeshCompiler? compiler,
        WorkflowEngine? engine,
        CancellationToken ct)
    {
        var workflowFile = ResolveWorkflowFile(runtime.Workflow, effective.ConfigDirectory);
        if (workflowFile == null || compiler == null || engine == null)
            return $"(workflow '{runtime.Workflow}' not found, execution stub)";

        try
        {
            var raw = await File.ReadAllTextAsync(workflowFile, ct);
            var compile = compiler.Compile(raw);
            if (!compile.Ok || compile.Definition == null)
            {
                var msg = string.Join("; ", compile.Errors.Select(e => e.Code));
                return $"workflow compile failed: {msg}";
            }

            var plan = engine.Plan(compile.Definition);
            if (!plan.Ok || plan.Plan == null)
            {
                var msg = string.Join("; ", plan.Errors.Select(e => e.Code));
                return $"workflow plan failed: {msg}";
            }

            var result = await engine.ExecuteAsync(plan.Plan, ct);
            return result.Note;
        }
        catch (Exception ex)
        {
            return $"workflow error: {ex.Message}";
        }
    }

    private static IReadOnlyList<string> ResolveAttachments(
        IReadOnlyList<string> inputs,
        PlatformToolPolicy policy,
        ITuiOutput output)
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
                {
                    full = matches[0];
                }
                else if (matches.Count > 1)
                {
                    output.MarkupLine($"Fuzzy matches: {string.Join(", ", matches.Select(Markup.Escape))}");
                }
            }

            if (full == null)
            {
                output.MarkupLine($"Attach failed: {Markup.Escape(path)}");
                continue;
            }

            if (!policy.TryValidatePath(full, out _, out var reason))
            {
                if (reason != "path_allowlist_empty")
                {
                    output.MarkupLine($"Attach denied: [red]{Markup.Escape(reason)}[/]");
                    continue;
                }
            }

            list.Add(full);
        }

        return list;
    }

    private static async Task RenderStreamingAsync(string text, ITuiOutput output, CancellationToken ct)
    {
        var chunkSize = 16;
        var delayMs = GetStreamDelayMs();
        var i = 0;
        while (i < text.Length)
        {
            var len = Math.Min(chunkSize, text.Length - i);
            var chunk = text.Substring(i, len);
            output.Markup(Markup.Escape(chunk));
            i += len;

            if (delayMs > 0)
                await Task.Delay(delayMs, ct);
        }

        output.WriteLine(string.Empty);
    }

    private static int GetStreamDelayMs()
    {
        var raw = Environment.GetEnvironmentVariable("AEVATAR_TUI_STREAM_DELAY_MS");
        if (int.TryParse(raw, out var ms))
            return Math.Clamp(ms, 0, 200);
        return 0;
    }

    private static string? ResolveWorkflowFile(string workflow, string configDir)
    {
        var name = (workflow ?? string.Empty).Trim();
        if (name.Length == 0)
            return null;

        if (File.Exists(name))
            return Path.GetFullPath(name);

        var dir = Path.Combine(configDir, "workflows");
        var json = Path.Combine(dir, $"{name}.json");
        if (File.Exists(json))
            return json;

        var yaml = Path.Combine(dir, $"{name}.yaml");
        if (File.Exists(yaml))
            return yaml;

        var yml = Path.Combine(dir, $"{name}.yml");
        if (File.Exists(yml))
            return yml;

        return null;
    }

    private static string QuoteArg(string value)
        => value.Contains(' ') ? $"\"{value}\"" : value;

    private static string Truncate(string value, int max)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= max)
            return value;

        return value.Substring(0, max) + "...";
    }

    private static void PrintHelp(ITuiOutput output)
    {
        output.MarkupLine($"[bold]{HelpTitle}[/]");
        foreach (var line in HelpLines)
            output.MarkupLine(line);
    }
}


