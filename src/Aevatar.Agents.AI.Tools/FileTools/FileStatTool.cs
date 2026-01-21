using Aevatar.Agents.AI.Tool.Abstractions;
using Google.Protobuf;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.AI.Tools;

public sealed class FileStatTool : AevatarToolBase
{
    private readonly FileToolOptions _options;

    public FileStatTool(FileToolOptions options)
    {
        _options = options ?? FileToolOptions.Empty;
    }

    public override string Name => "file_stat";
    public override string Description => "Get file or directory metadata.";
    public override ToolCategory Category => ToolCategory.Utility;
    public override IList<string> Tags => new List<string> { "filesystem", "stat" };

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

        if (File.Exists(full))
        {
            var info = new FileInfo(full);
            return Task.FromResult<IMessage>(FileToolHelpers.ToStruct(new
            {
                ok = true,
                path = full,
                is_dir = false,
                size_bytes = info.Length,
                last_write_utc = info.LastWriteTimeUtc.ToString("O"),
                last_write_local = info.LastWriteTime.ToString("O")
            }));
        }

        if (Directory.Exists(full))
        {
            var info = new DirectoryInfo(full);
            return Task.FromResult<IMessage>(FileToolHelpers.ToStruct(new
            {
                ok = true,
                path = full,
                is_dir = true,
                size_bytes = (long?)null,
                last_write_utc = info.LastWriteTimeUtc.ToString("O"),
                last_write_local = info.LastWriteTime.ToString("O")
            }));
        }

        return Task.FromResult<IMessage>(
            FileToolHelpers.ToStruct(new { ok = false, error = "path_not_found", path = full }));
    }

    protected override bool RequiresInternalAccess() => true;
}
