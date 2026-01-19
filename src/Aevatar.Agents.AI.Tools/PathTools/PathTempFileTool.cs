using Aevatar.Agents.AI.Tool.Abstractions;
using Google.Protobuf;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.AI.Tools;

// ============================================================
//  PathTempFileTool
//
//  说明：
//  - 创建临时文件（可选写入内容）
//  - 文件必须落在允许的写路径下
// ============================================================
public sealed class PathTempFileTool : AevatarToolBase
{
    private readonly FileToolOptions _options;

    public PathTempFileTool(FileToolOptions options)
    {
        _options = options ?? FileToolOptions.Empty;
    }

    public override string Name => "path_tempfile";
    public override string Description => "Create a temporary file within allowed roots.";
    public override ToolCategory Category => ToolCategory.Utility;
    public override IList<string> Tags => new List<string> { "filesystem", "temp", "path" };

    public override ToolParameters CreateParameters()
    {
        return new ToolParameters
        {
            Items = new Dictionary<string, ToolParameter>
            {
                ["dir"] = new ToolParameter
                {
                    Type = "string",
                    Required = false,
                    Description = "Directory to create file (default: working directory)."
                },
                ["prefix"] = new ToolParameter
                {
                    Type = "string",
                    Required = false,
                    Description = "Filename prefix."
                },
                ["suffix"] = new ToolParameter
                {
                    Type = "string",
                    Required = false,
                    Description = "Filename suffix (e.g. .txt)."
                },
                ["content"] = new ToolParameter
                {
                    Type = "string",
                    Required = false,
                    Description = "Optional file content."
                }
            }
        };
    }

    public override Task<IMessage> ExecuteAsync(
        Dictionary<string, object> parameters,
        ToolContext context,
        ILogger? logger,
        CancellationToken cancellationToken = default)
    {
        var rawDir = parameters.TryGetValue("dir", out var dir) ? dir?.ToString() : null;
        var prefix = parameters.TryGetValue("prefix", out var p) ? p?.ToString() : string.Empty;
        var suffix = parameters.TryGetValue("suffix", out var s) ? s?.ToString() : string.Empty;
        var content = parameters.TryGetValue("content", out var c) ? c?.ToString() : null;

        var baseDir = string.IsNullOrWhiteSpace(rawDir) ? _options.WorkingDirectory : rawDir!;
        if (!FileToolHelpers.TryResolvePath(_options, baseDir, _options.WriteRoots, out var fullDir, out var reason))
        {
            return Task.FromResult<IMessage>(
                FileToolHelpers.ToStruct(new { ok = false, error = reason }));
        }

        if (!Directory.Exists(fullDir))
            Directory.CreateDirectory(fullDir);

        var name = $"{prefix}{Guid.NewGuid():N}{suffix}";
        // suffix is taken as-is; extension checks happen on full path.

        var path = Path.Combine(fullDir, name);
        if (!FileToolHelpers.IsWriteExtensionAllowed(_options, path))
        {
            return Task.FromResult<IMessage>(
                FileToolHelpers.ToStruct(new { ok = false, error = "extension_not_allowed", path }));
        }

        if (content != null)
            File.WriteAllText(path, content);
        else
            File.WriteAllText(path, string.Empty);

        return Task.FromResult<IMessage>(FileToolHelpers.ToStruct(new
        {
            ok = true,
            path
        }));
    }

    protected override bool RequiresInternalAccess() => true;
}
