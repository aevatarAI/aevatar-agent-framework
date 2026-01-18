using System.Diagnostics;
using System.Linq;
using System.Text;
using Aevatar.Agents.AI.Core.Configuration;
using Aevatar.Platform;
using Aevatar.Platform.Cli.Commands;
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

        var delayMs = GetStreamDelayMs();
        var (result, response) = await RunWorkflowStreamingAsync(
            runtime,
            effective,
            compiler,
            engine,
            input.Text,
            resolved,
            async (chunk, token) =>
            {
                if (string.IsNullOrEmpty(chunk))
                    return;
                output.Markup(Markup.Escape(chunk));
                if (delayMs > 0)
                    await Task.Delay(delayMs, token);
            },
            ct);
        if (!string.IsNullOrWhiteSpace(result.SelectedWorkflow))
            runtime.Workflow = result.SelectedWorkflow!;
        await TryUpdateSessionWorkflowAsync(runtime, sessions, ct);

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
        output.WriteLine(string.Empty);
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
        var resolved = ResolveAttachments(input.Attachments, policy, new SilentTuiOutput());
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

        var result = await TryRunWorkflowAsync(
            runtime,
            effective,
            compiler,
            engine,
            input.Text,
            resolved,
            ct);
        if (!string.IsNullOrWhiteSpace(result.SelectedWorkflow))
            runtime.Workflow = result.SelectedWorkflow!;
        await TryUpdateSessionWorkflowAsync(runtime, sessions, ct);

        var response = Truncate(result.Note, MaxOutputChars);

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

    public static async Task<string> ChatStreamAsync(
        ParsedInput input,
        SessionRuntime runtime,
        AevatarEffectiveConfig effective,
        SessionService sessions,
        PlatformMeshCompiler? compiler,
        WorkflowEngine? engine,
        PlatformToolPolicy policy,
        Func<string, CancellationToken, Task> emitChunk,
        CancellationToken ct)
    {
        var resolved = ResolveAttachments(input.Attachments, policy, new SilentTuiOutput());
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

        var (result, response) = await RunWorkflowStreamingAsync(
            runtime,
            effective,
            compiler,
            engine,
            input.Text,
            resolved,
            emitChunk,
            ct);
        if (!string.IsNullOrWhiteSpace(result.SelectedWorkflow))
            runtime.Workflow = result.SelectedWorkflow!;
        await TryUpdateSessionWorkflowAsync(runtime, sessions, ct);

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

    // GUI 后端静默输出（避免污染 OpenTUI 屏幕）
    private sealed class SilentTuiOutput : ITuiOutput
    {
        public void Markup(string markup) { }
        public void MarkupLine(string markup) { }
        public void WriteLine(string text) { }
        public void Clear() { }
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
                var existingRuntime = new SessionRuntime(requestedSessionId, seq, existing.Profile, existing.ActiveWorkflow);
                NormalizeWorkflow(existingRuntime, effective.ConfigDirectory);
                return existingRuntime;
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
            var newRuntime = new SessionRuntime(requestedSessionId, 0, newState.Profile, newState.ActiveWorkflow);
            NormalizeWorkflow(newRuntime, effective.ConfigDirectory);
            return newRuntime;
        }

        if (options.Resume)
        {
            var list = await sessions.ListSessionsAsync(ct);
            var latest = list.FirstOrDefault();
            if (latest != null)
            {
                var events = await sessions.GetSessionEventsAsync(latest.SessionId, ct);
                var seq = (ulong)events.Count;
                var latestRuntime = new SessionRuntime(latest.SessionId, seq, latest.Profile, latest.ActiveWorkflow);
                NormalizeWorkflow(latestRuntime, effective.ConfigDirectory);
                return latestRuntime;
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
        var sessionRuntime = new SessionRuntime(sessionId, 0, state.Profile, state.ActiveWorkflow);
        NormalizeWorkflow(sessionRuntime, effective.ConfigDirectory);
        return sessionRuntime;
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

    private static async Task<(WorkflowRunResult Result, string Response)> RunWorkflowStreamingAsync(
        SessionRuntime runtime,
        AevatarEffectiveConfig effective,
        PlatformMeshCompiler? compiler,
        WorkflowEngine? engine,
        string userMessage,
        IReadOnlyList<string> attachedFiles,
        Func<string, CancellationToken, Task> emitChunk,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(emitChunk);

        var builder = new StringBuilder();
        var truncated = false;

        async Task OnDelta(string chunk, CancellationToken token)
        {
            if (string.IsNullOrEmpty(chunk))
                return;

            if (builder.Length < MaxOutputChars)
            {
                var remaining = MaxOutputChars - builder.Length;
                var slice = chunk.Length > remaining ? chunk.Substring(0, remaining) : chunk;
                builder.Append(slice);
                await emitChunk(slice, token);
                if (chunk.Length > remaining)
                    truncated = true;
            }
            else
            {
                truncated = true;
            }
        }

        var result = await TryRunWorkflowStreamingAsync(
            runtime,
            effective,
            compiler,
            engine,
            userMessage,
            attachedFiles,
            OnDelta,
            ct);

        if (truncated)
        {
            builder.Append("...");
            await emitChunk("...", ct);
        }

        return (result, builder.ToString());
    }

    private static async Task<WorkflowRunResult> TryRunWorkflowStreamingAsync(
        SessionRuntime runtime,
        AevatarEffectiveConfig effective,
        PlatformMeshCompiler? compiler,
        WorkflowEngine? engine,
        string userMessage,
        IReadOnlyList<string> attachedFiles,
        Func<string, CancellationToken, Task> onDelta,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(onDelta);

        var workflowFile = ResolveWorkflowFile(runtime.Workflow, effective.ConfigDirectory);
        if (workflowFile == null)
        {
            // 自动回退到可用 workflow，避免默认 workflow 不存在的误导
            var fallback = PickFallbackWorkflow(effective.ConfigDirectory);
            if (!string.IsNullOrWhiteSpace(fallback))
            {
                runtime.Workflow = fallback;
                workflowFile = ResolveWorkflowFile(runtime.Workflow, effective.ConfigDirectory);
            }
        }

        if (workflowFile == null || compiler == null || engine == null)
        {
            var stub = new WorkflowRunResult(
                RunId: $"run_{Guid.NewGuid():N}",
                Ok: false,
                Note: "(no workflow found; execution stub)");
            if (!string.IsNullOrWhiteSpace(stub.Note))
                await onDelta(stub.Note, ct);
            return stub;
        }

        try
        {
            var raw = await File.ReadAllTextAsync(workflowFile, ct);
            var compile = compiler.Compile(raw);
            if (!compile.Ok || compile.Definition == null)
            {
                var msg = string.Join("; ", compile.Errors.Select(e => e.Code));
                var failed = new WorkflowRunResult(
                    RunId: $"run_{Guid.NewGuid():N}",
                    Ok: false,
                    Note: $"workflow compile failed: {msg}");
                if (!string.IsNullOrWhiteSpace(failed.Note))
                    await onDelta(failed.Note, ct);
                return failed;
            }

            var plan = engine.Plan(compile.Definition);
            if (!plan.Ok || plan.Plan == null)
            {
                var msg = string.Join("; ", plan.Errors.Select(e => e.Code));
                var failed = new WorkflowRunResult(
                    RunId: $"run_{Guid.NewGuid():N}",
                    Ok: false,
                    Note: $"workflow plan failed: {msg}");
                if (!string.IsNullOrWhiteSpace(failed.Note))
                    await onDelta(failed.Note, ct);
                return failed;
            }

            var runInput = new WorkflowRunInput(
                UserMessage: userMessage,
                ConfigDirectory: effective.ConfigDirectory,
                ConfigPath: effective.ConfigPath,
                SecretsPath: effective.SecretsPath,
                WorkingDirectory: Directory.GetCurrentDirectory(),
                Profile: runtime.Profile,
                WorkflowName: runtime.Workflow,
                DefaultProvider: effective.Config.Models.DefaultProvider,
                DefaultModel: effective.Config.Models.DefaultModel,
                AttachedFiles: attachedFiles,
                ToolsConfig: effective.Config.Tools);

            return await engine.ExecuteStreamingAsync(plan.Plan, runInput, onDelta, ct);
        }
        catch (Exception ex)
        {
            var failed = new WorkflowRunResult(
                RunId: $"run_{Guid.NewGuid():N}",
                Ok: false,
                Note: $"workflow error: {ex.Message}");
            if (!string.IsNullOrWhiteSpace(failed.Note))
                await onDelta(failed.Note, ct);
            return failed;
        }
    }

    private static async Task<WorkflowRunResult> TryRunWorkflowAsync(
        SessionRuntime runtime,
        AevatarEffectiveConfig effective,
        PlatformMeshCompiler? compiler,
        WorkflowEngine? engine,
        string userMessage,
        IReadOnlyList<string> attachedFiles,
        CancellationToken ct)
    {
        var workflowFile = ResolveWorkflowFile(runtime.Workflow, effective.ConfigDirectory);
        if (workflowFile == null)
        {
            // 自动回退到可用 workflow，避免默认 workflow 不存在的误导
            var fallback = PickFallbackWorkflow(effective.ConfigDirectory);
            if (!string.IsNullOrWhiteSpace(fallback))
            {
                runtime.Workflow = fallback;
                workflowFile = ResolveWorkflowFile(runtime.Workflow, effective.ConfigDirectory);
            }
        }

        if (workflowFile == null || compiler == null || engine == null)
        {
            return new WorkflowRunResult(
                RunId: $"run_{Guid.NewGuid():N}",
                Ok: false,
                Note: "(no workflow found; execution stub)");
        }

        try
        {
            var raw = await File.ReadAllTextAsync(workflowFile, ct);
            var compile = compiler.Compile(raw);
            if (!compile.Ok || compile.Definition == null)
            {
                var msg = string.Join("; ", compile.Errors.Select(e => e.Code));
                return new WorkflowRunResult(
                    RunId: $"run_{Guid.NewGuid():N}",
                    Ok: false,
                    Note: $"workflow compile failed: {msg}");
            }

            var plan = engine.Plan(compile.Definition);
            if (!plan.Ok || plan.Plan == null)
            {
                var msg = string.Join("; ", plan.Errors.Select(e => e.Code));
                return new WorkflowRunResult(
                    RunId: $"run_{Guid.NewGuid():N}",
                    Ok: false,
                    Note: $"workflow plan failed: {msg}");
            }

            var runInput = new WorkflowRunInput(
                UserMessage: userMessage,
                ConfigDirectory: effective.ConfigDirectory,
                ConfigPath: effective.ConfigPath,
                SecretsPath: effective.SecretsPath,
                WorkingDirectory: Directory.GetCurrentDirectory(),
                Profile: runtime.Profile,
                WorkflowName: runtime.Workflow,
                DefaultProvider: effective.Config.Models.DefaultProvider,
                DefaultModel: effective.Config.Models.DefaultModel,
                AttachedFiles: attachedFiles,
                ToolsConfig: effective.Config.Tools);

            return await engine.ExecuteAsync(plan.Plan, runInput, ct);
        }
        catch (Exception ex)
        {
            return new WorkflowRunResult(
                RunId: $"run_{Guid.NewGuid():N}",
                Ok: false,
                Note: $"workflow error: {ex.Message}");
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

        // 1) 用户配置目录
        var fromConfig = ResolveWorkflowFileFromDir(name, Path.Combine(configDir, "workflows"));
        if (fromConfig != null)
            return fromConfig;

        // 2) 仓库默认 workflows 目录
        var repoDir = RepoPathResolver.ResolveWorkflowsSourceDirectory(null, Directory.GetCurrentDirectory());
        return ResolveWorkflowFileFromDir(name, repoDir);
    }

    private static string? ResolveWorkflowFileFromDir(string name, string? dir)
    {
        if (string.IsNullOrWhiteSpace(dir))
            return null;

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

    private static void NormalizeWorkflow(SessionRuntime runtime, string configDir)
    {
        if (ResolveWorkflowFile(runtime.Workflow, configDir) != null)
            return;

        var fallback = PickFallbackWorkflow(configDir);
        if (!string.IsNullOrWhiteSpace(fallback))
            runtime.Workflow = fallback;
    }

    private static string? PickFallbackWorkflow(string configDir)
    {
        var dir = Path.Combine(configDir, "workflows");
        var candidate = PickWorkflowFromDir(dir);
        if (!string.IsNullOrWhiteSpace(candidate))
            return candidate;

        var repoDir = RepoPathResolver.ResolveWorkflowsSourceDirectory(null, Directory.GetCurrentDirectory());
        return PickWorkflowFromDir(repoDir);
    }

    private static string? PickWorkflowFromDir(string? dir)
    {
        if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
            return null;

        var files = Directory.EnumerateFiles(dir, "*.*", SearchOption.TopDirectoryOnly)
            .Where(p =>
            {
                var ext = Path.GetExtension(p).ToLowerInvariant();
                return ext is ".json" or ".yaml" or ".yml";
            })
            .Select(p => Path.GetFileNameWithoutExtension(p))
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (files.Count == 0)
            return null;

        // 优先 hermes，其次 direct，最后按名称排序
        var hermes = files.FirstOrDefault(n => n.Equals("hermes", StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(hermes))
            return hermes;

        var direct = files.FirstOrDefault(n => n.Equals("direct", StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(direct))
            return direct;

        return files.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
    }

    private static async Task TryUpdateSessionWorkflowAsync(
        SessionRuntime runtime,
        SessionService sessions,
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


