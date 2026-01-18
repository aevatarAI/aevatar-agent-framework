using System.Text.RegularExpressions;
using Aevatar.Agents.AI.Tool.Abstractions;
using Google.Protobuf;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.AI.Tools;

public sealed class GrepTool : AevatarToolBase
{
    private readonly FileToolOptions _options;

    public GrepTool(FileToolOptions options)
    {
        _options = options ?? FileToolOptions.Empty;
    }

    public override string Name => "grep";
    public override string Description => "Search text files with a regex or literal pattern.";
    public override ToolCategory Category => ToolCategory.Utility;
    public override IList<string> Tags => new List<string> { "search", "grep", "filesystem" };

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
                    Description = "Regex pattern or literal text."
                },
                ["path"] = new ToolParameter
                {
                    Type = "string",
                    Required = false,
                    Description = "File or directory to search."
                },
                ["glob"] = new ToolParameter
                {
                    Type = "string",
                    Required = false,
                    Description = "Optional glob filter for file names."
                },
                ["ignore_case"] = new ToolParameter
                {
                    Type = "boolean",
                    Required = false,
                    Description = "Case-insensitive search."
                },
                ["literal"] = new ToolParameter
                {
                    Type = "boolean",
                    Required = false,
                    Description = "Treat pattern as literal."
                },
                ["max_results"] = new ToolParameter
                {
                    Type = "integer",
                    Required = false,
                    Description = "Max results (default: 200).",
                    DefaultValue = 200,
                    Minimum = 1,
                    Maximum = 2000
                },
                ["max_file_kb"] = new ToolParameter
                {
                    Type = "integer",
                    Required = false,
                    Description = "Skip files larger than this size (KB).",
                    DefaultValue = 512,
                    Minimum = 1,
                    Maximum = 10240
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

        var rawPath = parameters.TryGetValue("path", out var raw) ? raw?.ToString() : null;
        var glob = parameters.TryGetValue("glob", out var g) ? g?.ToString() : null;
        var ignoreCase = parameters.TryGetValue("ignore_case", out var ic) && FileToolHelpers.TryGetBool(ic);
        var literal = parameters.TryGetValue("literal", out var lit) && FileToolHelpers.TryGetBool(lit);
        var maxResults = FileToolHelpers.ClampInt(parameters.GetValueOrDefault("max_results"), 200, 1, 2000);
        var maxFileKb = FileToolHelpers.ClampInt(parameters.GetValueOrDefault("max_file_kb"), 512, 1, 10240);

        if (!TryResolveRoot(rawPath, out var rootPath, out var targetFile, out var error))
        {
            return Task.FromResult<IMessage>(FileToolHelpers.ToStruct(new { ok = false, error }));
        }

        var regex = BuildRegex(pattern, literal, ignoreCase);
        var globRegex = string.IsNullOrWhiteSpace(glob) ? null : SearchToolHelpers.BuildGlobRegex(glob!);

        var results = new List<object>();
        if (targetFile != null)
        {
            ScanFile(targetFile, regex, results, maxResults, maxFileKb);
        }
        else
        {
            foreach (var file in Directory.EnumerateFiles(rootPath!, "*", SearchOption.AllDirectories))
            {
                if (globRegex != null)
                {
                    var rel = Path.GetRelativePath(rootPath!, file);
                    var normalized = SearchToolHelpers.NormalizePathForMatch(rel);
                    if (!globRegex.IsMatch(normalized))
                        continue;
                }

                if (!ScanFile(file, regex, results, maxResults, maxFileKb))
                    break;
            }
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

    private bool TryResolveRoot(
        string? rawPath,
        out string? rootPath,
        out string? filePath,
        out string error)
    {
        rootPath = null;
        filePath = null;
        error = string.Empty;

        var candidate = string.IsNullOrWhiteSpace(rawPath) ? _options.WorkingDirectory : rawPath!;
        if (!FileToolHelpers.TryResolvePath(_options, candidate, _options.ReadRoots, out var full, out var reason))
        {
            error = reason;
            return false;
        }

        if (File.Exists(full))
        {
            filePath = full;
            rootPath = Path.GetDirectoryName(full);
            return true;
        }

        if (Directory.Exists(full))
        {
            rootPath = full;
            return true;
        }

        error = "path_not_found";
        return false;
    }

    private static Regex BuildRegex(string pattern, bool literal, bool ignoreCase)
    {
        var text = literal ? Regex.Escape(pattern) : pattern;
        var options = RegexOptions.CultureInvariant;
        if (ignoreCase)
            options |= RegexOptions.IgnoreCase;
        return new Regex(text, options);
    }

    private static bool ScanFile(
        string file,
        Regex regex,
        List<object> results,
        int maxResults,
        int maxFileKb)
    {
        try
        {
            var info = new FileInfo(file);
            if (info.Length > maxFileKb * 1024L)
                return true;

            var lineNo = 0;
            foreach (var line in File.ReadLines(file))
            {
                lineNo++;
                var match = regex.Match(line);
                if (!match.Success)
                    continue;

                results.Add(new
                {
                    path = file,
                    line = lineNo,
                    column = match.Index + 1,
                    text = line
                });

                if (results.Count >= maxResults)
                    return false;
            }

            return true;
        }
        catch
        {
            return true;
        }
    }
}
