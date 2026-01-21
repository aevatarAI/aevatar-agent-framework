using Aevatar.Agents.AI.Tool.Abstractions;
using Google.Protobuf;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.AI.Tools;

public sealed class DirListTool : AevatarToolBase
{
    private readonly FileToolOptions _options;

    public DirListTool(FileToolOptions options)
    {
        _options = options ?? FileToolOptions.Empty;
    }

    public override string Name => "dir_list";
    public override string Description => "List directory entries within allowed roots.";
    public override ToolCategory Category => ToolCategory.Utility;
    public override IList<string> Tags => new List<string> { "filesystem", "list", "dir" };

    public override ToolParameters CreateParameters()
    {
        return new ToolParameters
        {
            Items = new Dictionary<string, ToolParameter>
            {
                ["path"] = new ToolParameter
                {
                    Type = "string",
                    Required = false,
                    Description = "Directory path (default: working directory)."
                },
                ["recursive"] = new ToolParameter
                {
                    Type = "boolean",
                    Required = false,
                    Description = "List recursively."
                },
                ["include_files"] = new ToolParameter
                {
                    Type = "boolean",
                    Required = false,
                    Description = "Include files (default: true)."
                },
                ["include_dirs"] = new ToolParameter
                {
                    Type = "boolean",
                    Required = false,
                    Description = "Include directories (default: true)."
                },
                ["max_results"] = new ToolParameter
                {
                    Type = "integer",
                    Required = false,
                    Description = "Max results (default: 200).",
                    DefaultValue = 200,
                    Minimum = 1,
                    Maximum = 5000
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
        var rawPath = parameters.TryGetValue("path", out var raw) ? raw?.ToString() : null;
        var recursive = parameters.TryGetValue("recursive", out var r) && FileToolHelpers.TryGetBool(r);
        var includeFiles = !parameters.TryGetValue("include_files", out var f) || FileToolHelpers.TryGetBool(f);
        var includeDirs = !parameters.TryGetValue("include_dirs", out var d) || FileToolHelpers.TryGetBool(d);
        var maxResults = FileToolHelpers.ClampInt(parameters.GetValueOrDefault("max_results"), 200, 1, 5000);

        var candidate = string.IsNullOrWhiteSpace(rawPath) ? _options.WorkingDirectory : rawPath!;
        if (!FileToolHelpers.TryResolvePath(_options, candidate, _options.ReadRoots, out var full, out var reason))
        {
            return Task.FromResult<IMessage>(
                FileToolHelpers.ToStruct(new { ok = false, error = reason }));
        }

        if (!Directory.Exists(full))
        {
            return Task.FromResult<IMessage>(
                FileToolHelpers.ToStruct(new { ok = false, error = "not_directory", path = full }));
        }

        var results = new List<object>();
        var option = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        foreach (var entry in Directory.EnumerateFileSystemEntries(full, "*", option))
        {
            var isDir = Directory.Exists(entry);
            if (isDir && !includeDirs)
                continue;
            if (!isDir && !includeFiles)
                continue;

            results.Add(new
            {
                path = entry,
                is_dir = isDir
            });

            if (results.Count >= maxResults)
                break;
        }

        return Task.FromResult<IMessage>(FileToolHelpers.ToStruct(new
        {
            ok = true,
            root = full,
            entries = results,
            truncated = results.Count >= maxResults
        }));
    }

    protected override bool RequiresInternalAccess() => true;
}
