using Aevatar.Agents.AI.Tool.Abstractions;
using Google.Protobuf;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.AI.Tools;

// ============================================================
//  ReadLintsTool
//
//  说明：
//  - 兼容 Cursor 风格工具名 read_lints
//  - 默认运行 "dotnet build"，可自定义 command
// ============================================================
public sealed class ReadLintsTool : AevatarToolBase
{
    private readonly CommandToolOptions _options;

    public ReadLintsTool(CommandToolOptions options)
    {
        _options = options ?? CommandToolOptions.Empty;
    }

    public override string Name => "read_lints";
    public override string Description => "Run a lint/build command and return output.";
    public override ToolCategory Category => ToolCategory.Utility;
    public override IList<string> Tags => new List<string> { "lint", "diagnostics", "build" };

    public override ToolParameters CreateParameters()
    {
        return new ToolParameters
        {
            Items = new Dictionary<string, ToolParameter>
            {
                ["command"] = new ToolParameter
                {
                    Type = "string",
                    Required = false,
                    Description = "Command to run (default: dotnet build)."
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
                }
            }
        };
    }

    public override async Task<IMessage> ExecuteAsync(
        Dictionary<string, object> parameters,
        ToolContext context,
        ILogger? logger,
        CancellationToken cancellationToken = default)
    {
        var command = parameters.TryGetValue("command", out var raw) ? raw?.ToString() : null;
        if (string.IsNullOrWhiteSpace(command))
            command = "dotnet build";

        var cwd = parameters.TryGetValue("cwd", out var c) ? c?.ToString() : null;
        var timeout = parameters.TryGetValue("timeout_sec", out var t)
            ? FileToolHelpers.ClampInt(t, _options.TimeoutSeconds, 1, 3600)
            : (int?)null;

        var (shell, args) = BuildShell(command);
        var result = await CommandToolHelpers.RunAsync(shell, args, _options, cwd, timeout, null, cancellationToken);
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
}
