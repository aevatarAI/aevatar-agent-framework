using Aevatar.Agents.AI.Tool.Abstractions;
using Google.Protobuf;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.AI.Tools;

// ============================================================
//  PathMkdirTool
//
//  说明：
//  - 在允许的路径下创建目录
//  - 支持递归创建
// ============================================================
public sealed class PathMkdirTool : AevatarToolBase
{
    private readonly FileToolOptions _options;

    public PathMkdirTool(FileToolOptions options)
    {
        _options = options ?? FileToolOptions.Empty;
    }

    public override string Name => "path_mkdir";
    public override string Description => "Create a directory within allowed roots.";
    public override ToolCategory Category => ToolCategory.Utility;
    public override IList<string> Tags => new List<string> { "filesystem", "mkdir", "path" };

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
                    Description = "Directory path."
                },
                ["recursive"] = new ToolParameter
                {
                    Type = "boolean",
                    Required = false,
                    Description = "Create parent directories (default: true).",
                    DefaultValue = true
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

        if (!FileToolHelpers.TryResolvePath(_options, rawPath, _options.WriteRoots, out var full, out var reason))
        {
            return Task.FromResult<IMessage>(
                FileToolHelpers.ToStruct(new { ok = false, error = reason }));
        }

        if (File.Exists(full))
        {
            return Task.FromResult<IMessage>(
                FileToolHelpers.ToStruct(new { ok = false, error = "path_is_file", path = full }));
        }

        Directory.CreateDirectory(full);
        return Task.FromResult<IMessage>(FileToolHelpers.ToStruct(new
        {
            ok = true,
            path = full
        }));
    }

    protected override bool RequiresInternalAccess() => true;
}
