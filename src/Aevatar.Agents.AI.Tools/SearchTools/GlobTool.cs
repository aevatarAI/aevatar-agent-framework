using Aevatar.Agents.AI.Tool.Abstractions;
using Google.Protobuf;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.AI.Tools;

public sealed class GlobTool : AevatarToolBase
{
    private readonly FileToolOptions _options;

    public GlobTool(FileToolOptions options)
    {
        _options = options ?? FileToolOptions.Empty;
    }

    public override string Name => "glob";
    public override string Description => "List files by glob pattern within allowed roots.";
    public override ToolCategory Category => ToolCategory.Utility;
    public override IList<string> Tags => new List<string> { "search", "glob", "filesystem" };

    public override ToolParameters CreateParameters()
    {
        return new ToolParameters
        {
            Items = new Dictionary<string, ToolParameter>
            {
                ["pattern"] = new ToolParameter
                {
                    Type = "string",
                    Required = true,
                    Description = "Glob pattern (supports * ? and **)."
                },
                ["root"] = new ToolParameter
                {
                    Type = "string",
                    Required = false,
                    Description = "Search root directory."
                },
                ["max_results"] = new ToolParameter
                {
                    Type = "integer",
                    Required = false,
                    Description = "Max results (default: 200).",
                    DefaultValue = 200,
                    Minimum = 1,
                    Maximum = 2000
                }
            },
            Required = new[] { "pattern" }
        };
    }

    public override Task<IMessage> ExecuteAsync(
        Dictionary<string, object> parameters,
        ToolContext context,
        ILogger? logger,
        CancellationToken cancellationToken = default)
    {
        if (!FileToolHelpers.TryGetString(parameters, "pattern", out var pattern))
            return Task.FromResult<IMessage>(FileToolHelpers.ToStruct(new { ok = false, error = "pattern_required" }));

        var rawRoot = parameters.TryGetValue("root", out var root) ? root?.ToString() : null;
        var maxResults = FileToolHelpers.ClampInt(parameters.GetValueOrDefault("max_results"), 200, 1, 2000);

        if (!TryResolveRoot(rawRoot, out var rootPath, out var error))
        {
            return Task.FromResult<IMessage>(FileToolHelpers.ToStruct(new { ok = false, error }));
        }

        var regex = SearchToolHelpers.BuildGlobRegex(pattern);
        var results = new List<string>();

        foreach (var file in Directory.EnumerateFiles(rootPath, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(rootPath, file);
            var normalized = SearchToolHelpers.NormalizePathForMatch(relative);
            if (!regex.IsMatch(normalized))
                continue;

            results.Add(file);
            if (results.Count >= maxResults)
                break;
        }

        return Task.FromResult<IMessage>(FileToolHelpers.ToStruct(new
        {
            ok = true,
            root = rootPath,
            matches = results,
            truncated = results.Count >= maxResults
        }));
    }

    protected override bool RequiresInternalAccess() => true;

    private bool TryResolveRoot(string? rawRoot, out string rootPath, out string error)
    {
        rootPath = string.Empty;
        error = string.Empty;

        var candidate = string.IsNullOrWhiteSpace(rawRoot) ? _options.WorkingDirectory : rawRoot!;
        if (!FileToolHelpers.TryResolvePath(_options, candidate, _options.ReadRoots, out var full, out var reason))
        {
            error = reason;
            return false;
        }

        if (!Directory.Exists(full))
        {
            error = "root_not_found";
            return false;
        }

        rootPath = full;
        return true;
    }
}
