using Aevatar.Agents.AI.Tool.Abstractions;
using Google.Protobuf;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.AI.Tools;

public sealed class PathExistsTool : AevatarToolBase
{
    private readonly FileToolOptions _options;

    public PathExistsTool(FileToolOptions options)
    {
        _options = options ?? FileToolOptions.Empty;
    }

    public override string Name => "path_exists";
    public override string Description => "Check whether a path exists (file or directory) within allowed roots.";
    public override ToolCategory Category => ToolCategory.Utility;
    public override IList<string> Tags => new List<string> { "filesystem", "path", "exists" };

    public override ToolParameters CreateParameters()
    {
        return new ToolParameters
        {
            Items = new Dictionary<string, ToolParameter>
            {
                ["path"] = new ToolParameter
                {
                    Type = "string",
                    Required = true,
                    Description = "File or directory path."
                }
            },
            Required = new[] { "path" }
        };
    }

    public override Task<IMessage> ExecuteAsync(
        Dictionary<string, object> parameters,
        ToolContext context,
        ILogger? logger,
        CancellationToken cancellationToken = default)
    {
        if (!FileToolHelpers.TryGetString(parameters, "path", out var rawPath))
        {
            return Task.FromResult<IMessage>(
                FileToolHelpers.ToStruct(new { ok = false, error = "path_required" }));
        }

        if (!FileToolHelpers.TryResolvePath(_options, rawPath, _options.ReadRoots, out var full, out var reason))
        {
            return Task.FromResult<IMessage>(
                FileToolHelpers.ToStruct(new { ok = false, error = reason }));
        }

        var isFile = File.Exists(full);
        var isDir = Directory.Exists(full);
        return Task.FromResult<IMessage>(FileToolHelpers.ToStruct(new
        {
            ok = true,
            path = full,
            exists = isFile || isDir,
            is_file = isFile,
            is_dir = isDir
        }));
    }

    protected override bool RequiresInternalAccess() => true;
}
