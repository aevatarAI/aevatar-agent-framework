using Aevatar.Agents.AI.Tool.Abstractions;
using Google.Protobuf;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.AI.Tools;

public sealed class FileReadTool : AevatarToolBase
{
    private readonly FileToolOptions _options;

    public FileReadTool(FileToolOptions options)
    {
        _options = options ?? FileToolOptions.Empty;
    }

    public override string Name => "file_read";
    public override string Description => "Read a UTF-8 text file within allowed roots.";
    public override ToolCategory Category => ToolCategory.Utility;
    public override IList<string> Tags => new List<string> { "file", "read", "filesystem" };

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
                ["max_chars"] = new ToolParameter
                {
                    Type = "integer",
                    Required = false,
                    Description = "Max characters to read (default: 12000).",
                    DefaultValue = 12000,
                    Minimum = 256,
                    Maximum = 200_000
                }
            },
            Required = new[] { "path" }
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

        var maxChars = FileToolHelpers.ClampInt(parameters.GetValueOrDefault("max_chars"), _options.MaxReadChars, 256, 200_000);
        if (!FileToolHelpers.TryResolvePath(_options, rawPath, _options.ReadRoots, out var full, out var reason))
            return FileToolHelpers.ToStruct(new { ok = false, error = reason });

        if (!File.Exists(full))
            return FileToolHelpers.ToStruct(new { ok = false, error = "file_not_found", path = full });

        var (content, truncated) = await FileToolHelpers.ReadTextAsync(full, maxChars, cancellationToken);
        return FileToolHelpers.ToStruct(new
        {
            ok = true,
            path = full,
            content,
            truncated
        });
    }

    protected override bool RequiresInternalAccess() => true;
}
