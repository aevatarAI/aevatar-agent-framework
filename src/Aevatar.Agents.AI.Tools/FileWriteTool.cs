using Aevatar.Agents.AI.Tool.Abstractions;
using Google.Protobuf;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.AI.Tools;

public sealed class FileWriteTool : AevatarToolBase
{
    private readonly FileToolOptions _options;

    public FileWriteTool(FileToolOptions options)
    {
        _options = options ?? FileToolOptions.Empty;
    }

    public override string Name => "file_write";
    public override string Description => "Write a text file within allowed roots.";
    public override ToolCategory Category => ToolCategory.Utility;
    public override IList<string> Tags => new List<string> { "file", "write", "filesystem" };

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
                },
                ["content"] = new ToolParameter
                {
                    Type = "string",
                    Required = true,
                    Description = "File content to write."
                },
                ["overwrite"] = new ToolParameter
                {
                    Type = "boolean",
                    Required = false,
                    Description = "Overwrite if file exists (default: false).",
                    DefaultValue = false
                }
            },
            Required = new[] { "path", "content" }
        };
    }

    public override async Task<IMessage> ExecuteAsync(
        Dictionary<string, object> parameters,
        ToolContext context,
        ILogger? logger,
        CancellationToken cancellationToken = default)
    {
        if (!FileToolHelpers.TryGetString(parameters, "path", out var rawPath))
            return FileToolHelpers.ToStruct(new { ok = false, error = "path_required" });
        if (!FileToolHelpers.TryGetString(parameters, "content", out var content))
            return FileToolHelpers.ToStruct(new { ok = false, error = "content_required" });

        var overwrite = FileToolHelpers.TryGetBool(parameters.GetValueOrDefault("overwrite"));
        if (!FileToolHelpers.TryResolvePath(_options, rawPath, _options.WriteRoots, out var full, out var reason))
            return FileToolHelpers.ToStruct(new { ok = false, error = reason });

        if (!FileToolHelpers.IsWriteExtensionAllowed(_options, full))
            return FileToolHelpers.ToStruct(new { ok = false, error = "extension_not_allowed", path = full });

        var exists = File.Exists(full);
        var allowOverwrite = _options.AllowOverwrite || overwrite;
        if (exists && !allowOverwrite)
            return FileToolHelpers.ToStruct(new { ok = false, error = "file_exists", path = full });

        var dir = Path.GetDirectoryName(full);
        if (!string.IsNullOrWhiteSpace(dir))
            Directory.CreateDirectory(dir);

        await File.WriteAllTextAsync(full, content, cancellationToken);
        return FileToolHelpers.ToStruct(new
        {
            ok = true,
            path = full,
            bytes = System.Text.Encoding.UTF8.GetByteCount(content),
            overwritten = exists
        });
    }

    protected override bool RequiresInternalAccess() => true;
}
