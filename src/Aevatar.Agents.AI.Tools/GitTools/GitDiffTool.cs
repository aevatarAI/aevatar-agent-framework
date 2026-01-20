using Aevatar.Agents.AI.Tool.Abstractions;
using Google.Protobuf;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.AI.Tools;

public sealed class GitDiffTool : AevatarToolBase
{
    private readonly CommandToolOptions _options;

    public GitDiffTool(CommandToolOptions options)
    {
        _options = options ?? CommandToolOptions.Empty;
    }

    public override string Name => "git_diff";
    public override string Description => "Run git diff.";
    public override ToolCategory Category => ToolCategory.Utility;
    public override IList<string> Tags => new List<string> { "git", "vcs" };

    public override ToolParameters CreateParameters()
    {
        return new ToolParameters
        {
            Items = new Dictionary<string, ToolParameter>
            {
                ["cwd"] = new ToolParameter
                {
                    Type = "string",
                    Required = false,
                    Description = "Repository root."
                },
                ["staged"] = new ToolParameter
                {
                    Type = "boolean",
                    Required = false,
                    Description = "Diff staged changes."
                },
                ["path"] = new ToolParameter
                {
                    Type = "string",
                    Required = false,
                    Description = "Optional path to diff."
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
        var cwd = parameters.TryGetValue("cwd", out var raw) ? raw?.ToString() : null;
        var staged = parameters.TryGetValue("staged", out var s) && FileToolHelpers.TryGetBool(s);
        var path = parameters.TryGetValue("path", out var p) ? p?.ToString() : null;

        var args = new List<string> { "diff" };
        if (staged)
            args.Add("--staged");
        if (!string.IsNullOrWhiteSpace(path))
        {
            args.Add("--");
            args.Add(path!.Trim());
        }

        var result = await CommandToolHelpers.RunAsync("git", args, _options, cwd, null, null, cancellationToken);
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
}
