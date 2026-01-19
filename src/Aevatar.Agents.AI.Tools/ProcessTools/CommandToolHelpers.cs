using System.Diagnostics;

namespace Aevatar.Agents.AI.Tools;

// ============================================================
//  CommandToolHelpers
//
//  说明：
//  - 统一执行外部命令（bash/git/ast-grep/lsp）
//  - 处理超时与输出裁剪
// ============================================================
internal static class CommandToolHelpers
{
    public sealed record CommandExecutionResult(
        bool Ok,
        int ExitCode,
        bool TimedOut,
        string Stdout,
        string Stderr,
        string? Error,
        string Command);

    public static bool IsCommandAllowed(CommandToolOptions options, string command)
    {
        if (options.AllowedCommands.Count == 0)
            return true;

        return options.AllowedCommands.Any(x =>
            string.Equals(x, command, StringComparison.OrdinalIgnoreCase));
    }

    public static async Task<CommandExecutionResult> RunAsync(
        string fileName,
        IReadOnlyList<string> args,
        CommandToolOptions options,
        string? cwd,
        int? timeoutSeconds,
        string? stdin,
        CancellationToken ct)
    {
        if (!IsCommandAllowed(options, fileName))
        {
            return new CommandExecutionResult(
                Ok: false,
                ExitCode: -1,
                TimedOut: false,
                Stdout: string.Empty,
                Stderr: string.Empty,
                Error: "command_not_allowed",
                Command: BuildCommandLine(fileName, args));
        }

        var workingDirectory = ResolveWorkingDirectory(options, cwd);
        if (workingDirectory == null)
        {
            return new CommandExecutionResult(
                Ok: false,
                ExitCode: -1,
                TimedOut: false,
                Stdout: string.Empty,
                Stderr: string.Empty,
                Error: "cwd_not_found",
                Command: BuildCommandLine(fileName, args));
        }

        var timeout = timeoutSeconds ?? options.TimeoutSeconds;
        var maxChars = Math.Max(256, options.MaxOutputChars);

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = stdin != null,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        foreach (var arg in args)
            process.StartInfo.ArgumentList.Add(arg);

        try
        {
            if (!process.Start())
            {
                return new CommandExecutionResult(
                    Ok: false,
                    ExitCode: -1,
                    TimedOut: false,
                    Stdout: string.Empty,
                    Stderr: string.Empty,
                    Error: "process_start_failed",
                    Command: BuildCommandLine(fileName, args));
            }
        }
        catch (Exception ex)
        {
            var error = ex is System.ComponentModel.Win32Exception
                ? "command_not_found"
                : ex.GetType().Name;
            return new CommandExecutionResult(
                Ok: false,
                ExitCode: -1,
                TimedOut: false,
                Stdout: string.Empty,
                Stderr: string.Empty,
                Error: error,
                Command: BuildCommandLine(fileName, args));
        }

        if (stdin != null)
        {
            await process.StandardInput.WriteAsync(stdin);
            await process.StandardInput.FlushAsync();
            process.StandardInput.Close();
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        if (timeout > 0)
            cts.CancelAfter(TimeSpan.FromSeconds(timeout));

        var stdoutTask = ReadStreamAsync(process.StandardOutput, maxChars, cts.Token);
        var stderrTask = ReadStreamAsync(process.StandardError, maxChars, cts.Token);

        var timedOut = false;
        try
        {
            await process.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            timedOut = true;
            TryKill(process);
        }

        var stdout = await SafeAwaitAsync(stdoutTask);
        var stderr = await SafeAwaitAsync(stderrTask);

        return new CommandExecutionResult(
            Ok: !timedOut && process.ExitCode == 0,
            ExitCode: timedOut ? -1 : process.ExitCode,
            TimedOut: timedOut,
            Stdout: stdout,
            Stderr: stderr,
            Error: timedOut ? "timeout" : null,
            Command: BuildCommandLine(fileName, args));
    }

    private static string? ResolveWorkingDirectory(CommandToolOptions options, string? cwd)
    {
        var root = (cwd ?? string.Empty).Trim();
        if (root.Length == 0)
            root = (options.WorkingDirectory ?? string.Empty).Trim();

        if (root.Length == 0)
            root = Directory.GetCurrentDirectory();

        try
        {
            root = Path.GetFullPath(root);
        }
        catch
        {
            return null;
        }

        return Directory.Exists(root) ? root : null;
    }

    private static async Task<string> ReadStreamAsync(
        StreamReader reader,
        int maxChars,
        CancellationToken ct)
    {
        var buffer = new char[4096];
        var sb = new System.Text.StringBuilder();

        while (sb.Length < maxChars)
        {
            var remaining = Math.Min(buffer.Length, maxChars - sb.Length);
            var read = await reader.ReadAsync(buffer.AsMemory(0, remaining), ct);
            if (read <= 0)
                break;
            sb.Append(buffer, 0, read);
        }

        return sb.ToString();
    }

    private static async Task<string> SafeAwaitAsync(Task<string> task)
    {
        try
        {
            return await task;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(true);
        }
        catch
        {
        }
    }

    private static string BuildCommandLine(string fileName, IReadOnlyList<string> args)
    {
        if (args.Count == 0)
            return fileName;

        return $"{fileName} {string.Join(" ", args)}";
    }
}
