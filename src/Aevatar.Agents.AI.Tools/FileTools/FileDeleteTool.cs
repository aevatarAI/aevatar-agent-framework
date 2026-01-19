using Aevatar.Agents.AI.Tool.Abstractions;
using Google.Protobuf;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.AI.Tools;

public sealed class FileDeleteTool : AevatarToolBase
{
    private readonly FileToolOptions _options;

    public FileDeleteTool(FileToolOptions options)
    {
        _options = options ?? FileToolOptions.Empty;
    }

    public override string Name => "file_delete";
    public override string Description => "Delete a file within allowed roots.";
    public override ToolCategory Category => ToolCategory.Utility;
    public override IList<string> Tags => new List<string> { "file", "delete", "filesystem" };

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
                    Description = "File path (absolute or relative to working directory)."
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
            return Task.FromResult<IMessage>(FileToolHelpers.ToStruct(new { ok = false, error = "path_required" }));

        if (!FileToolHelpers.TryResolvePath(_options, rawPath, _options.WriteRoots, out var full, out var reason))
            return Task.FromResult<IMessage>(FileToolHelpers.ToStruct(new { ok = false, error = reason }));

        if (!FileToolHelpers.IsWriteExtensionAllowed(_options, full))
        {
            return Task.FromResult<IMessage>(
                FileToolHelpers.ToStruct(new { ok = false, error = "extension_not_allowed", path = full }));
        }

        if (Directory.Exists(full))
        {
            return Task.FromResult<IMessage>(
                FileToolHelpers.ToStruct(new { ok = false, error = "is_directory", path = full }));
        }

        if (!File.Exists(full))
        {
            return Task.FromResult<IMessage>(
                FileToolHelpers.ToStruct(new { ok = false, error = "file_not_found", path = full }));
        }

        File.Delete(full);
        return Task.FromResult<IMessage>(FileToolHelpers.ToStruct(new { ok = true, path = full }));
    }

    protected override bool RequiresInternalAccess() => true;
    protected override bool IsDangerous() => true;
}
