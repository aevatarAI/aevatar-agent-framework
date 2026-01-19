using Aevatar.Agents.AI.Tool.Abstractions;
using Google.Protobuf;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.AI.Tools;

// ============================================================
//  PathRemoveTreeTool
//
//  说明：
//  - 删除目录树（危险操作）
//  - 仅允许在白名单路径下执行
// ============================================================
public sealed class PathRemoveTreeTool : AevatarToolBase
{
    private readonly FileToolOptions _options;

    public PathRemoveTreeTool(FileToolOptions options)
    {
        _options = options ?? FileToolOptions.Empty;
    }

    public override string Name => "path_remove_tree";
    public override string Description => "Remove a directory tree within allowed roots.";
    public override ToolCategory Category => ToolCategory.Utility;
    public override IList<string> Tags => new List<string> { "filesystem", "delete", "path" };

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

        if (!Directory.Exists(full))
        {
            return Task.FromResult<IMessage>(
                FileToolHelpers.ToStruct(new { ok = false, error = "not_directory", path = full }));
        }

        Directory.Delete(full, recursive: true);
        return Task.FromResult<IMessage>(FileToolHelpers.ToStruct(new { ok = true, path = full }));
    }

    protected override bool RequiresInternalAccess() => true;
    protected override bool IsDangerous() => true;
}
