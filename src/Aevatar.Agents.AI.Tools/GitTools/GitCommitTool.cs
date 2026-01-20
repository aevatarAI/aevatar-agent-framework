using Aevatar.Agents.AI.Tool.Abstractions;
using Google.Protobuf;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.AI.Tools;

public sealed class GitCommitTool : AevatarToolBase
{
    private readonly CommandToolOptions _options;

    public GitCommitTool(CommandToolOptions options)
    {
        _options = options ?? CommandToolOptions.Empty;
    }

    public override string Name => "git_commit";
    public override string Description => "Create a git commit.";
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
                ["message"] = new ToolParameter
                {
                    Type = "string",
                    Required = true,
                    Description = "Commit message."
                },
                ["all"] = new ToolParameter
                {
                    Type = "boolean",
                    Required = false,
                    Description = "Stage all modified files."
                },
                ["amend"] = new ToolParameter
                {
                    Type = "boolean",
                    Required = false,
                    Description = "Amend previous commit."
                }
            },
            Required = new[] { "message" }
        };
    }

    public override async Task<IMessage> ExecuteAsync(
        Dictionary<string, object> parameters,
        ToolContext context,
        ILogger? logger,
        CancellationToken cancellationToken = default)
    {
        if (!FileToolHelpers.TryGetString(parameters, "message", out var message))
            return FileToolHelpers.ToStruct(new { ok = false, error = "message_required" });

        var cwd = parameters.TryGetValue("cwd", out var raw) ? raw?.ToString() : null;
        var all = parameters.TryGetValue("all", out var a) && FileToolHelpers.TryGetBool(a);
        var amend = parameters.TryGetValue("amend", out var am) && FileToolHelpers.TryGetBool(am);

        var args = new List<string> { "commit" };
        if (all)
            args.Add("-a");
        if (amend)
            args.Add("--amend");
        args.Add("-m");
        args.Add(message);

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

    protected override bool IsDangerous() => true;
}
