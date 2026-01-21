using Aevatar.Agents.AI.Tool.Abstractions;
using Google.Protobuf;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.AI.Tools;

public sealed class LspTool : AevatarToolBase
{
    private readonly CommandToolOptions _options;

    public LspTool(CommandToolOptions options)
    {
        _options = options ?? CommandToolOptions.Empty;
    }

    public override string Name => "lsp";
    public override string Description => "Invoke an external language server command (best-effort).";
    public override ToolCategory Category => ToolCategory.Utility;
    public override IList<string> Tags => new List<string> { "lsp", "process", "exec" };

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
                    Description = "Command to run (e.g., language-server binary)."
                },
                ["args"] = new ToolParameter
                {
                    Type = "array",
                    Required = false,
                    Description = "Command arguments.",
                    Items = new ToolParameter { Type = "string" }
                },
                ["stdin"] = new ToolParameter
                {
                    Type = "string",
                    Required = false,
                    Description = "Optional stdin payload."
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

        var args = new List<string>();
        if (parameters.TryGetValue("args", out var raw) && raw is IEnumerable<object> list)
        {
            foreach (var item in list)
            {
                var text = item?.ToString() ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(text))
                    args.Add(text.Trim());
            }
        }

        var stdin = parameters.TryGetValue("stdin", out var input) ? input?.ToString() : null;
        var cwd = parameters.TryGetValue("cwd", out var rawCwd) ? rawCwd?.ToString() : null;
        var timeout = parameters.TryGetValue("timeout_sec", out var t)
            ? FileToolHelpers.ClampInt(t, _options.TimeoutSeconds, 1, 3600)
            : (int?)null;

        var result = await CommandToolHelpers.RunAsync(command, args, _options, cwd, timeout, stdin, cancellationToken);
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
}
