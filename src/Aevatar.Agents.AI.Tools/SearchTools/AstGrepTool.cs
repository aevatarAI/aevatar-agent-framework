using Aevatar.Agents.AI.Tool.Abstractions;
using Google.Protobuf;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.AI.Tools;

public sealed class AstGrepTool : AevatarToolBase
{
    private readonly CommandToolOptions _options;

    public AstGrepTool(CommandToolOptions options)
    {
        _options = options ?? CommandToolOptions.Empty;
    }

    public override string Name => "ast-grep";
    public override string Description => "Run ast-grep (sg) search if available.";
    public override ToolCategory Category => ToolCategory.Utility;
    public override IList<string> Tags => new List<string> { "search", "ast", "code" };

    public override ToolParameters CreateParameters()
    {
        return new ToolParameters
        {
            Items = new Dictionary<string, ToolParameter>
            {
                ["pattern"] = new ToolParameter
                {
                    Type = "string",
                    Required = true,
                    Description = "AST pattern."
                },
                ["path"] = new ToolParameter
                {
                    Type = "string",
                    Required = false,
                    Description = "Path to search (default: working directory)."
                },
                ["language"] = new ToolParameter
                {
                    Type = "string",
                    Required = false,
                    Description = "Language (e.g., ts, cs, py)."
                },
                ["json"] = new ToolParameter
                {
                    Type = "boolean",
                    Required = false,
                    Description = "Return JSON output."
                }
            },
            Required = new[] { "pattern" }
        };
    }

    public override async Task<IMessage> ExecuteAsync(
        Dictionary<string, object> parameters,
        ToolContext context,
        ILogger? logger,
        CancellationToken cancellationToken = default)
    {
        if (!FileToolHelpers.TryGetString(parameters, "pattern", out var pattern))
            return FileToolHelpers.ToStruct(new { ok = false, error = "pattern_required" });

        var path = parameters.TryGetValue("path", out var raw) ? raw?.ToString() : null;
        var language = parameters.TryGetValue("language", out var lang) ? lang?.ToString() : null;
        var json = parameters.TryGetValue("json", out var j) && FileToolHelpers.TryGetBool(j);

        var args = new List<string> { "-p", pattern };
        if (!string.IsNullOrWhiteSpace(language))
        {
            args.Add("-l");
            args.Add(language!.Trim());
        }

        if (json)
            args.Add("--json");

        if (!string.IsNullOrWhiteSpace(path))
            args.Add(path!.Trim());

        var result = await RunAstGrepAsync("ast-grep", args, path, cancellationToken);
        if (!result.Ok && string.Equals(result.Error, "command_not_found", StringComparison.OrdinalIgnoreCase))
        {
            result = await RunAstGrepAsync("sg", args, path, cancellationToken);
        }

        return FileToolHelpers.ToStruct(new
        {
            ok = result.Ok,
            exit_code = result.ExitCode,
            stdout = result.Stdout,
            stderr = result.Stderr,
            error = result.Error,
            command = result.Command
        });
    }

    protected override bool RequiresInternalAccess() => true;

    private Task<CommandToolHelpers.CommandExecutionResult> RunAstGrepAsync(
        string command,
        IReadOnlyList<string> args,
        string? path,
        CancellationToken ct)
    {
        return CommandToolHelpers.RunAsync(command, args, _options, path, null, null, ct);
    }
}
