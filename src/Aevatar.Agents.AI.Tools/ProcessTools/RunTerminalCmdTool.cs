using System.Diagnostics;
using Aevatar.Agents.AI.Tool.Abstractions;
using Google.Protobuf;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.AI.Tools;

// ============================================================
//  RunTerminalCmdTool
//
//  说明：
//  - 兼容 Cursor 风格工具名 run_terminal_cmd
//  - 支持后台执行（best-effort）
// ============================================================
public sealed class RunTerminalCmdTool : AevatarToolBase
{
    private readonly CommandToolOptions _options;

    public RunTerminalCmdTool(CommandToolOptions options)
    {
        _options = options ?? CommandToolOptions.Empty;
    }

    public override string Name => "run_terminal_cmd";
    public override string Description => "Run a terminal command (optionally in background).";
    public override ToolCategory Category => ToolCategory.Utility;
    public override IList<string> Tags => new List<string> { "terminal", "process", "exec" };

    public override ToolParameters CreateParameters()
    {
        return new ToolParameters
        {
            Items = new Dictionary<string, ToolParameter>
            {
                ["command"] = new ToolParameter
                {
                    Type = "string",
                    Required = true,
                    Description = "Command to execute."
                },
                ["cwd"] = new ToolParameter
                {
                    Type = "string",
                    Required = false,
                    Description = "Working directory."
                },
                ["timeout_sec"] = new ToolParameter
                {
                    Type = "integer",
                    Required = false,
                    Description = "Timeout in seconds."
                },
                ["is_background"] = new ToolParameter
                {
                    Type = "boolean",
                    Required = false,
                    Description = "Run in background."
                }
            },
            Required = new[] { "command" }
        };
    }

    public override async Task<IMessage> ExecuteAsync(
        Dictionary<string, object> parameters,
        ToolContext context,
        ILogger? logger,
        CancellationToken cancellationToken = default)
    {
        if (!FileToolHelpers.TryGetString(parameters, "command", out var command))
            return FileToolHelpers.ToStruct(new { ok = false, error = "command_required" });

        var cwd = parameters.TryGetValue("cwd", out var raw) ? raw?.ToString() : null;
        var timeout = parameters.TryGetValue("timeout_sec", out var t)
            ? FileToolHelpers.ClampInt(t, _options.TimeoutSeconds, 1, 3600)
            : (int?)null;
        var background = parameters.TryGetValue("is_background", out var bg) && FileToolHelpers.TryGetBool(bg);

        if (background)
        {
            var (shell, args) = BuildShell(command);
            if (!CommandToolHelpers.IsCommandAllowed(_options, shell))
            {
                return FileToolHelpers.ToStruct(new { ok = false, error = "command_not_allowed" });
            }

            var workingDirectory = ResolveWorkingDirectory(cwd);
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = shell,
                    WorkingDirectory = workingDirectory,
                    RedirectStandardOutput = false,
                    RedirectStandardError = false,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            foreach (var arg in args)
                process.StartInfo.ArgumentList.Add(arg);

            try
            {
                process.Start();
                return FileToolHelpers.ToStruct(new
                {
                    ok = true,
                    pid = process.Id,
                    command,
                    background = true
                });
            }
            catch (Exception ex)
            {
                return FileToolHelpers.ToStruct(new { ok = false, error = ex.GetType().Name });
            }
        }

        var (fileName, cmdArgs) = BuildShell(command);
        var result = await CommandToolHelpers.RunAsync(fileName, cmdArgs, _options, cwd, timeout, null, cancellationToken);
        return FileToolHelpers.ToStruct(new
        {
            ok = result.Ok,
            exit_code = result.ExitCode,
            timed_out = result.TimedOut,
            stdout = result.Stdout,
            stderr = result.Stderr,
            error = result.Error,
            command = result.Command
        });
    }

    protected override bool IsDangerous() => true;

    private static (string FileName, IReadOnlyList<string> Args) BuildShell(string command)
    {
        if (OperatingSystem.IsWindows())
            return ("cmd.exe", new[] { "/c", command });

        var shell = File.Exists("/bin/bash") ? "/bin/bash" : "bash";
        return (shell, new[] { "-lc", command });
    }

    private string ResolveWorkingDirectory(string? cwd)
    {
        var root = (cwd ?? string.Empty).Trim();
        if (root.Length == 0)
            root = (_options.WorkingDirectory ?? string.Empty).Trim();
        if (root.Length == 0)
            root = Directory.GetCurrentDirectory();
        try
        {
            root = Path.GetFullPath(root);
        }
        catch
        {
        }
        return root;
    }
}
