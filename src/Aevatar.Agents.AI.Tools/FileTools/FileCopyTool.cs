using Aevatar.Agents.AI.Tool.Abstractions;
using Google.Protobuf;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.AI.Tools;

public sealed class FileCopyTool : AevatarToolBase
{
    private readonly FileToolOptions _options;

    public FileCopyTool(FileToolOptions options)
    {
        _options = options ?? FileToolOptions.Empty;
    }

    public override string Name => "path_copy";
    public override string Description => "Copy a file within allowed roots.";
    public override ToolCategory Category => ToolCategory.Utility;
    public override IList<string> Tags => new List<string> { "file", "copy", "filesystem" };

    public override ToolParameters CreateParameters()
    {
        return new ToolParameters
        {
            Items = new Dictionary<string, ToolParameter>
            {
                ["source"] = new ToolParameter
                {
                    Type = "string",
                    Required = true,
                    Description = "Source file path."
                },
                ["dest"] = new ToolParameter
                {
                    Type = "string",
                    Required = true,
                    Description = "Destination file path."
                },
                ["overwrite"] = new ToolParameter
                {
                    Type = "boolean",
                    Required = false,
                    Description = "Overwrite if destination exists (default: false).",
                    DefaultValue = false
                }
            },
            Required = new[] { "source", "dest" }
        };
    }

    public override Task<IMessage> ExecuteAsync(
        Dictionary<string, object> parameters,
        ToolContext context,
        ILogger? logger,
        CancellationToken cancellationToken = default)
    {
        if (!FileToolHelpers.TryGetString(parameters, "source", out var sourceRaw))
        {
            return Task.FromResult<IMessage>(
                FileToolHelpers.ToStruct(new { ok = false, error = "source_required" }));
        }

        if (!FileToolHelpers.TryGetString(parameters, "dest", out var destRaw))
        {
            return Task.FromResult<IMessage>(
                FileToolHelpers.ToStruct(new { ok = false, error = "dest_required" }));
        }

        if (!FileToolHelpers.TryResolvePath(_options, sourceRaw, _options.ReadRoots, out var source, out var reason))
        {
            return Task.FromResult<IMessage>(
                FileToolHelpers.ToStruct(new { ok = false, error = reason }));
        }

        if (!File.Exists(source))
        {
            return Task.FromResult<IMessage>(
                FileToolHelpers.ToStruct(new { ok = false, error = "file_not_found", path = source }));
        }

        if (!FileToolHelpers.TryResolvePath(_options, destRaw, _options.WriteRoots, out var dest, out var destReason))
        {
            return Task.FromResult<IMessage>(
                FileToolHelpers.ToStruct(new { ok = false, error = destReason }));
        }

        if (!FileToolHelpers.IsWriteExtensionAllowed(_options, dest))
        {
            return Task.FromResult<IMessage>(
                FileToolHelpers.ToStruct(new { ok = false, error = "extension_not_allowed", path = dest }));
        }

        if (Directory.Exists(dest))
        {
            return Task.FromResult<IMessage>(
                FileToolHelpers.ToStruct(new { ok = false, error = "dest_is_directory", path = dest }));
        }

        var overwrite = parameters.TryGetValue("overwrite", out var o) && FileToolHelpers.TryGetBool(o);
        var allowOverwrite = _options.AllowOverwrite || overwrite;
        if (File.Exists(dest) && !allowOverwrite)
        {
            return Task.FromResult<IMessage>(
                FileToolHelpers.ToStruct(new { ok = false, error = "file_exists", path = dest }));
        }

        var dir = Path.GetDirectoryName(dest);
        if (!string.IsNullOrWhiteSpace(dir))
            Directory.CreateDirectory(dir);

        File.Copy(source, dest, allowOverwrite);
        return Task.FromResult<IMessage>(FileToolHelpers.ToStruct(new
        {
            ok = true,
            source,
            dest,
            overwritten = File.Exists(dest) && allowOverwrite
        }));
    }

    protected override bool RequiresInternalAccess() => true;
    protected override bool IsDangerous() => true;
}
