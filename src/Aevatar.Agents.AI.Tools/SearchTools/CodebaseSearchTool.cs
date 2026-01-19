using System.Text.RegularExpressions;
using Aevatar.Agents.AI.Tool.Abstractions;
using Google.Protobuf;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.AI.Tools;

// ============================================================
//  CodebaseSearchTool
//
//  说明：
//  - 兼容 Cursor 风格工具名 codebase_search
//  - 语义能力为 best-effort：基于关键词与简单评分
// ============================================================
public sealed class CodebaseSearchTool : AevatarToolBase
{
    private readonly FileToolOptions _options;

    public CodebaseSearchTool(FileToolOptions options)
    {
        _options = options ?? FileToolOptions.Empty;
    }

    public override string Name => "codebase_search";
    public override string Description => "Best-effort codebase search using keyword matching.";
    public override ToolCategory Category => ToolCategory.Utility;
    public override IList<string> Tags => new List<string> { "search", "codebase", "filesystem" };

    public override ToolParameters CreateParameters()
    {
        return new ToolParameters
        {
            Items = new Dictionary<string, ToolParameter>
            {
                ["query"] = new ToolParameter
                {
                    Type = "string",
                    Required = true,
                    Description = "Search query."
                },
                ["root"] = new ToolParameter
                {
                    Type = "string",
                    Required = false,
                    Description = "Search root directory."
                },
                ["glob"] = new ToolParameter
                {
                    Type = "string",
                    Required = false,
                    Description = "Optional glob filter for file names."
                },
                ["max_results"] = new ToolParameter
                {
                    Type = "integer",
                    Required = false,
                    Description = "Max results (default: 20).",
                    DefaultValue = 20,
                    Minimum = 1,
                    Maximum = 200
                },
                ["max_files"] = new ToolParameter
                {
                    Type = "integer",
                    Required = false,
                    Description = "Max files to scan (default: 2000).",
                    DefaultValue = 2000,
                    Minimum = 100,
                    Maximum = 20000
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
            Required = new[] { "query" }
        };
    }

    public override Task<IMessage> ExecuteAsync(
        Dictionary<string, object> parameters,
        ToolContext context,
        ILogger? logger,
        CancellationToken cancellationToken = default)
    {
        if (!FileToolHelpers.TryGetString(parameters, "query", out var query))
        {
            return Task.FromResult<IMessage>(
                FileToolHelpers.ToStruct(new { ok = false, error = "query_required" }));
        }

        var rawRoot = parameters.TryGetValue("root", out var root) ? root?.ToString() : null;
        var glob = parameters.TryGetValue("glob", out var g) ? g?.ToString() : null;
        var maxResults = FileToolHelpers.ClampInt(parameters.GetValueOrDefault("max_results"), 20, 1, 200);
        var maxFiles = FileToolHelpers.ClampInt(parameters.GetValueOrDefault("max_files"), 2000, 100, 20000);
        var maxFileKb = FileToolHelpers.ClampInt(parameters.GetValueOrDefault("max_file_kb"), 512, 1, 10240);

        if (!TryResolveRoot(rawRoot, out var rootPath, out var error))
        {
            return Task.FromResult<IMessage>(
                FileToolHelpers.ToStruct(new { ok = false, error }));
        }

        var tokens = ExtractTokens(query);
        if (tokens.Count == 0)
        {
            return Task.FromResult<IMessage>(
                FileToolHelpers.ToStruct(new { ok = false, error = "query_empty" }));
        }

        var globRegex = string.IsNullOrWhiteSpace(glob) ? null : SearchToolHelpers.BuildGlobRegex(glob!);
        var results = new List<SearchHit>();
        var scanned = 0;

        foreach (var file in Directory.EnumerateFiles(rootPath, "*", SearchOption.AllDirectories))
        {
            if (globRegex != null)
            {
                var rel = Path.GetRelativePath(rootPath, file);
                var normalized = SearchToolHelpers.NormalizePathForMatch(rel);
                if (!globRegex.IsMatch(normalized))
                    continue;
            }

            scanned++;
            if (scanned > maxFiles)
                break;

            if (!TryReadFileScore(file, tokens, maxFileKb, out var score, out var line, out var text))
                continue;

            results.Add(new SearchHit(file, score, line, text));
        }

        var ordered = results
            .OrderByDescending(x => x.Score)
            .Take(maxResults)
            .ToList();

        return Task.FromResult<IMessage>(FileToolHelpers.ToStruct(new
        {
            ok = true,
            root = rootPath,
            query,
            tokens,
            results = ordered,
            truncated = scanned > maxFiles || results.Count > maxResults
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

    private static List<string> ExtractTokens(string query)
    {
        var tokens = Regex.Matches(query.ToLowerInvariant(), "[a-z0-9_]+")
            .Select(m => m.Value)
            .Where(t => t.Length > 1)
            .Distinct()
            .ToList();
        return tokens;
    }

    private static bool TryReadFileScore(
        string file,
        IReadOnlyList<string> tokens,
        int maxFileKb,
        out int score,
        out int line,
        out string text)
    {
        score = 0;
        line = 0;
        text = string.Empty;

        try
        {
            var info = new FileInfo(file);
            if (info.Length > maxFileKb * 1024L)
                return false;

            var lineNo = 0;
            foreach (var raw in File.ReadLines(file))
            {
                lineNo++;
                var lower = raw.ToLowerInvariant();
                var hits = 0;
                foreach (var token in tokens)
                {
                    var idx = lower.IndexOf(token, StringComparison.Ordinal);
                    while (idx >= 0)
                    {
                        hits++;
                        idx = lower.IndexOf(token, idx + token.Length, StringComparison.Ordinal);
                    }
                }

                if (hits > 0 && score == 0)
                {
                    line = lineNo;
                    text = raw;
                }

                score += hits;
            }

            return score > 0;
        }
        catch
        {
            return false;
        }
    }

    private sealed record SearchHit(string Path, int Score, int Line, string Text);
}
