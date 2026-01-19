using Aevatar.Agents.AI.Tool.Abstractions;
using Google.Protobuf;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.AI.Tools;

// ============================================================
//  ApplyPatchTool
//
//  说明：
//  - 兼容 Cursor 风格工具名 apply_patch
//  - 通过 git apply / patch best-effort 方式应用补丁
// ============================================================
public sealed class ApplyPatchTool : AevatarToolBase
{
    private readonly CommandToolOptions _options;

    public ApplyPatchTool(CommandToolOptions options)
    {
        _options = options ?? CommandToolOptions.Empty;
    }

    public override string Name => "apply_patch";
    public override string Description => "Apply a unified diff patch to files.";
    public override ToolCategory Category => ToolCategory.Utility;
    public override IList<string> Tags => new List<string> { "patch", "diff", "filesystem" };

    public override ToolParameters CreateParameters()
    {
        return new ToolParameters
        {
            Items = new Dictionary<string, ToolParameter>
            {
                ["patch"] = new ToolParameter
                {
                    Type = "string",
                    Required = true,
                    Description = "Unified diff patch content."
                },
                ["cwd"] = new ToolParameter
                {
                    Type = "string",
                    Required = false,
                    Description = "Working directory."
                },
                ["strip"] = new ToolParameter
                {
                    Type = "integer",
                    Required = false,
                    Description = "Strip path components (-p).",
                    DefaultValue = 0,
                    Minimum = 0,
                    Maximum = 10
                },
                ["timeout_sec"] = new ToolParameter
                {
                    Type = "integer",
                    Required = false,
                    Description = "Timeout in seconds."
                }
            },
            Required = new[] { "patch" }
        };
    }

    public override async Task<IMessage> ExecuteAsync(
        Dictionary<string, object> parameters,
        ToolContext context,
        ILogger? logger,
        CancellationToken cancellationToken = default)
    {
        if (!FileToolHelpers.TryGetString(parameters, "patch", out var patch))
            return FileToolHelpers.ToStruct(new { ok = false, error = "patch_required" });

        var cwd = parameters.TryGetValue("cwd", out var raw) ? raw?.ToString() : null;
        var strip = FileToolHelpers.ClampInt(parameters.GetValueOrDefault("strip"), 0, 0, 10);
        var timeout = parameters.TryGetValue("timeout_sec", out var t)
            ? FileToolHelpers.ClampInt(t, _options.TimeoutSeconds, 1, 3600)
            : (int?)null;

        var gitArgs = new List<string>
        {
            "apply",
            "--whitespace=nowarn",
            "--recount",
            "--unsafe-paths"
        };
        if (strip > 0)
            gitArgs.Add($"-p{strip}");
        gitArgs.Add("-");

        var result = await CommandToolHelpers.RunAsync("git", gitArgs, _options, cwd, timeout, patch, cancellationToken);
        if (!result.Ok && string.Equals(result.Error, "command_not_found", StringComparison.OrdinalIgnoreCase))
        {
            var patchArgs = new List<string> { "-s" };
            if (strip > 0)
                patchArgs.Add($"-p{strip}");
            result = await CommandToolHelpers.RunAsync("patch", patchArgs, _options, cwd, timeout, patch, cancellationToken);
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
    protected override bool IsDangerous() => true;
}
