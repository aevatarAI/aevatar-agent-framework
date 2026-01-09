using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Aevatar.Agents.Cognitive.Primitives;
using Aevatar.Agents.Cognitive.Template;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.Cognitive.Execution;

// ============================================================
//  WorkspaceCodeSearchExecutor
//
//  Responsibility:
//  - Search workspace code for a pattern with strict bounds
//  - Prefer ripgrep (rg) for performance; fallback to managed scan
//
//  Output (Protobuf-Value friendly Dictionary):
//  {
//    ok: bool,
//    pattern: string,
//    matches: [ { path: string, line: int, excerpt: string } ],
//    truncated: bool,
//    error: string?
//  }
// ============================================================

public sealed class WorkspaceCodeSearchExecutor
{
    private const int DefaultMaxResults = 50;
    private const int DefaultContextLines = 2;
    private const int DefaultMaxTotalChars = 60_000;

    private const int MaxResultsHardCap = 500;
    private const int MaxContextHardCap = 8;
    private const int MaxExcerptCharsPerMatch = 1200;

    private static readonly string[] DefaultSkipDirs =
    [
        ".git", "bin", "obj", "node_modules", ".spec-workflow"
    ];

    private readonly TemplateEngine _templateEngine;
    private readonly ILogger _logger;

    public WorkspaceCodeSearchExecutor(TemplateEngine templateEngine, ILogger logger)
    {
        _templateEngine = templateEngine;
        _logger = logger;
    }

    public PrimitiveResult Execute(StepDefinition step, Dictionary<string, object> variables, string workspaceRoot)
    {
        var patternTemplate = step.Parameters.GetValueOrDefault("pattern")?.ToString() ?? "";
        if (string.IsNullOrWhiteSpace(patternTemplate))
        {
            return PrimitiveResult.Ok(new Dictionary<string, object?>
            {
                ["ok"] = false,
                ["pattern"] = "",
                ["matches"] = new List<object>(),
                ["truncated"] = false,
                ["error"] = "missing_required_param:pattern"
            });
        }

        var pattern = _templateEngine.Render(patternTemplate, variables).Replace("\r", "").Trim();
        if (string.IsNullOrWhiteSpace(pattern))
        {
            return PrimitiveResult.Ok(new Dictionary<string, object?>
            {
                ["ok"] = false,
                ["pattern"] = "",
                ["matches"] = new List<object>(),
                ["truncated"] = false,
                ["error"] = "pattern_empty"
            }) with
            {
                UserPrompt = "workspace_code_search: (empty pattern)"
            };
        }

        var glob = RenderOptional(step.Parameters.GetValueOrDefault("glob"), variables);
        var fileType = RenderOptional(step.Parameters.GetValueOrDefault("file_type"), variables);

        var maxResults = ResolveInt(step.Parameters.GetValueOrDefault("max_results"), variables, DefaultMaxResults);
        maxResults = Math.Clamp(maxResults, 1, MaxResultsHardCap);

        var contextLines = ResolveInt(step.Parameters.GetValueOrDefault("context_lines"), variables, DefaultContextLines);
        contextLines = Math.Clamp(contextLines, 0, MaxContextHardCap);

        var maxTotalChars = ResolveInt(step.Parameters.GetValueOrDefault("max_total_chars"), variables, DefaultMaxTotalChars);
        maxTotalChars = Math.Clamp(maxTotalChars, 1_000, 500_000);

        var matches = new List<object>(capacity: Math.Min(maxResults, 64));
        var truncated = false;

        // Try rg first (fast path)
        if (!TrySearchWithRipgrep(
                workspaceRoot,
                pattern,
                glob,
                fileType,
                maxResults,
                contextLines,
                maxTotalChars,
                matches,
                out truncated,
                out var rgError))
        {
            // Fallback: managed scan (bounded)
            if (!TrySearchWithManagedScan(
                    workspaceRoot,
                    pattern,
                    glob,
                    fileType,
                    maxResults,
                    contextLines,
                    maxTotalChars,
                    matches,
                    out truncated,
                    out var scanError))
            {
                return PrimitiveResult.Ok(new Dictionary<string, object?>
                {
                    ["ok"] = false,
                    ["pattern"] = pattern,
                    ["matches"] = matches,
                    ["truncated"] = false,
                    ["error"] = Bound(string.Join(" | ", new[] { rgError, scanError }.Where(x => !string.IsNullOrWhiteSpace(x))), 400)
                }) with
                {
                    UserPrompt = $"workspace_code_search: {pattern}"
                };
            }
        }

        return PrimitiveResult.Ok(new Dictionary<string, object?>
        {
            ["ok"] = true,
            ["pattern"] = pattern,
            ["matches"] = matches,
            ["truncated"] = truncated
        }) with
        {
            UserPrompt = $"workspace_code_search: {pattern}"
        };
    }

    private bool TrySearchWithRipgrep(
        string workspaceRoot,
        string pattern,
        string? glob,
        string? fileType,
        int maxResults,
        int contextLines,
        int maxTotalChars,
        List<object> matches,
        out bool truncated,
        out string? error)
    {
        truncated = false;
        error = null;

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "rg",
                WorkingDirectory = workspaceRoot,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            // Stable output for parsing
            psi.ArgumentList.Add("--no-heading");
            psi.ArgumentList.Add("--line-number");
            psi.ArgumentList.Add("--color");
            psi.ArgumentList.Add("never");

            if (!string.IsNullOrWhiteSpace(glob))
            {
                psi.ArgumentList.Add("--glob");
                psi.ArgumentList.Add(glob!);
            }

            if (!string.IsNullOrWhiteSpace(fileType))
            {
                // NOTE: rg --type expects a known type name (e.g., "cs").
                // If unknown, rg will error and we'll fallback to managed scan.
                psi.ArgumentList.Add("--type");
                psi.ArgumentList.Add(fileType!);
            }

            psi.ArgumentList.Add(pattern);
            psi.ArgumentList.Add("."); // search within workspace root

            using var process = Process.Start(psi);
            if (process == null)
            {
                error = "rg_start_failed";
                return false;
            }

            var outChars = 0;

            // Read stdout lines (bounded). We don't await indefinitely; stop when we collected enough.
            while (!process.StandardOutput.EndOfStream)
            {
                var line = process.StandardOutput.ReadLine();
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                if (!TryParseRgLine(line, out var relPath, out var lineNo))
                    continue;

                // Normalize path: strip leading "./"
                relPath = relPath.StartsWith("./", StringComparison.Ordinal) ? relPath[2..] : relPath;

                // Defense-in-depth: ensure returned path stays within root
                if (!WorkspacePathGuard.TryResolvePathWithinRoot(workspaceRoot, relPath, out var fullPath, out _))
                    continue;

                var excerpt = contextLines > 0
                    ? BuildExcerpt(fullPath!, lineNo, contextLines, MaxExcerptCharsPerMatch)
                    : ExtractMatchText(line, MaxExcerptCharsPerMatch);

                var item = new Dictionary<string, object?>
                {
                    ["path"] = relPath,
                    ["line"] = lineNo,
                    ["excerpt"] = excerpt
                };
                matches.Add(item);

                outChars += excerpt.Length;
                if (matches.Count >= maxResults || outChars >= maxTotalChars)
                {
                    truncated = true;
                    TryKill(process);
                    break;
                }
            }

            // Drain stderr (best-effort) for diagnostics if exit != 0
            process.WaitForExit(2000);
            if (process.ExitCode == 2 && matches.Count == 0)
            {
                var err = process.StandardError.ReadToEnd();
                error = string.IsNullOrWhiteSpace(err) ? $"rg_exit:{process.ExitCode}" : Bound(err.Replace("\r", "").Trim(), 200);
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            // Most commonly: Win32Exception (rg not found)
            error = ex.Message;
            return false;
        }
    }

    private bool TrySearchWithManagedScan(
        string workspaceRoot,
        string pattern,
        string? glob,
        string? fileType,
        int maxResults,
        int contextLines,
        int maxTotalChars,
        List<object> matches,
        out bool truncated,
        out string? error)
    {
        truncated = false;
        error = null;

        Regex? regex = null;
        try
        {
            regex = new Regex(pattern, RegexOptions.Compiled);
        }
        catch (Exception ex)
        {
            error = $"invalid_regex: {ex.Message}";
            return false;
        }

        var outChars = 0;

        try
        {
            foreach (var file in EnumerateFilesBounded(workspaceRoot, glob, fileType))
            {
                if (!WorkspacePathGuard.TryResolvePathWithinRoot(workspaceRoot, file, out var fullPath, out _))
                    continue;

                if (!TryScanSingleFile(fullPath!, file, regex, contextLines, maxResults, maxTotalChars, matches, ref outChars, out var hitTruncated))
                    continue;

                if (hitTruncated)
                {
                    truncated = true;
                    break;
                }

                if (matches.Count >= maxResults || outChars >= maxTotalChars)
                {
                    truncated = true;
                    break;
                }
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "[workspace_code_search] managed scan failed.");
            error = ex.Message;
            return false;
        }
    }

    private IEnumerable<string> EnumerateFilesBounded(string root, string? glob, string? fileType)
    {
        // Minimal filtering: file_type as extension fallback; glob supports "*.ext" only.
        var ext = NormalizeExtension(fileType) ?? NormalizeGlobExtension(glob);

        var stack = new Stack<string>();
        stack.Push(root);

        while (stack.Count > 0)
        {
            var dir = stack.Pop();
            IEnumerable<string> subDirs;
            IEnumerable<string> files;

            try
            {
                subDirs = Directory.EnumerateDirectories(dir);
                files = Directory.EnumerateFiles(dir);
            }
            catch
            {
                continue;
            }

            foreach (var sd in subDirs)
            {
                var name = Path.GetFileName(sd);
                if (DefaultSkipDirs.Contains(name, StringComparer.OrdinalIgnoreCase))
                    continue;
                stack.Push(sd);
            }

            foreach (var f in files)
            {
                if (ext != null && !f.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
                    continue;

                // Yield relative path (so downstream stays stable)
                var rel = Path.GetRelativePath(root, f).Replace('\\', '/');
                yield return rel;
            }
        }
    }

    private static bool TryScanSingleFile(
        string fullPath,
        string relPath,
        Regex regex,
        int contextLines,
        int maxResults,
        int maxTotalChars,
        List<object> matches,
        ref int outChars,
        out bool truncated)
    {
        truncated = false;

        // Small, bounded reading; skip gigantic files.
        var fi = new FileInfo(fullPath);
        if (!fi.Exists) return false;
        if (fi.Length > 2L * 1024 * 1024) return false; // 2MB cap for managed fallback

        string[] lines;
        try
        {
            lines = File.ReadAllLines(fullPath);
        }
        catch
        {
            return false;
        }

        for (var i = 0; i < lines.Length; i++)
        {
            if (!regex.IsMatch(lines[i]))
                continue;

            var lineNo = i + 1;
            var excerpt = BuildExcerptFromLines(lines, lineNo, contextLines, MaxExcerptCharsPerMatch);
            matches.Add(new Dictionary<string, object?>
            {
                ["path"] = relPath,
                ["line"] = lineNo,
                ["excerpt"] = excerpt
            });

            outChars += excerpt.Length;

            if (matches.Count >= maxResults || outChars >= maxTotalChars)
            {
                truncated = true;
                return true;
            }
        }

        return true;
    }

    private static string BuildExcerpt(string fullPath, int lineNo, int contextLines, int maxChars)
    {
        try
        {
            // NOTE: ReadAllLines is acceptable here because:
            // - called at most maxResults times
            // - file sizes are expected to be small (code files)
            var lines = File.ReadAllLines(fullPath);
            if (lines.Length == 0) return string.Empty;

            var idx = Math.Clamp(lineNo - 1, 0, lines.Length - 1);
            var start = Math.Max(0, idx - contextLines);
            var end = Math.Min(lines.Length - 1, idx + contextLines);

            var sb = new StringBuilder();
            for (var i = start; i <= end; i++)
            {
                var prefix = (i + 1 == lineNo) ? ">" : " ";
                sb.Append(prefix).Append(i + 1).Append(": ").Append(lines[i]).Append('\n');
                if (sb.Length >= maxChars) break;
            }

            return Bound(sb.ToString().TrimEnd('\n'), maxChars);
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string BuildExcerptFromLines(string[] lines, int lineNo, int contextLines, int maxChars)
    {
        if (lines.Length == 0) return string.Empty;

        var idx = Math.Clamp(lineNo - 1, 0, lines.Length - 1);
        var start = Math.Max(0, idx - contextLines);
        var end = Math.Min(lines.Length - 1, idx + contextLines);

        var sb = new StringBuilder();
        for (var i = start; i <= end; i++)
        {
            var prefix = (i + 1 == lineNo) ? ">" : " ";
            sb.Append(prefix).Append(i + 1).Append(": ").Append(lines[i]).Append('\n');
            if (sb.Length >= maxChars) break;
        }

        return Bound(sb.ToString().TrimEnd('\n'), maxChars);
    }

    private static bool TryParseRgLine(string line, out string path, out int lineNo)
    {
        // Expected: path:line:match...
        path = string.Empty;
        lineNo = 0;

        var i1 = line.IndexOf(':');
        if (i1 <= 0) return false;

        var i2 = line.IndexOf(':', i1 + 1);
        if (i2 <= i1 + 1) return false;

        path = line[..i1];
        var lineStr = line[(i1 + 1)..i2];
        if (!int.TryParse(lineStr, out lineNo)) return false;

        return true;
    }

    private static string ExtractMatchText(string rgLine, int maxChars)
    {
        // Extract substring after "path:line:"
        var i1 = rgLine.IndexOf(':');
        if (i1 <= 0) return Bound(rgLine, maxChars);
        var i2 = rgLine.IndexOf(':', i1 + 1);
        if (i2 <= i1 + 1) return Bound(rgLine, maxChars);
        return Bound(rgLine[(i2 + 1)..].Trim(), maxChars);
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch
        {
            // best-effort
        }
    }

    private int ResolveInt(object? value, Dictionary<string, object> vars, int defaultValue)
    {
        if (value == null) return defaultValue;
        return value switch
        {
            int i => i,
            long l => (int)l,
            double d => (int)d,
            float f => (int)f,
            string s when int.TryParse(s, out var parsed) => parsed,
            string s => ConvertToInt(_templateEngine.Evaluate(s, vars), defaultValue),
            _ => defaultValue
        };
    }

    private static int ConvertToInt(object? value, int defaultValue) => value switch
    {
        int i => i,
        long l => (int)l,
        double d => (int)d,
        float f => (int)f,
        decimal m => (int)m,
        string s when int.TryParse(s, out var parsed) => parsed,
        _ => defaultValue
    };

    private string? RenderOptional(object? value, Dictionary<string, object> vars)
    {
        if (value == null) return null;
        var s = value.ToString();
        if (string.IsNullOrWhiteSpace(s)) return null;
        var rendered = _templateEngine.Render(s!, vars).Replace("\r", "").Trim();
        return string.IsNullOrWhiteSpace(rendered) ? null : rendered;
    }

    private static string? NormalizeExtension(string? fileType)
    {
        if (string.IsNullOrWhiteSpace(fileType)) return null;
        var ft = fileType.Trim();

        // If looks like ".cs" or "cs", treat as extension for fallback scan.
        if (ft.StartsWith(".", StringComparison.Ordinal))
            return ft;
        if (ft.All(ch => char.IsLetterOrDigit(ch)))
            return "." + ft;

        return null;
    }

    private static string? NormalizeGlobExtension(string? glob)
    {
        if (string.IsNullOrWhiteSpace(glob)) return null;
        var g = glob.Trim();
        if (!g.StartsWith("*.", StringComparison.Ordinal)) return null;
        var ext = g[1..]; // ".cs"
        return ext.Length >= 2 ? ext : null;
    }

    private static string Bound(string? s, int maxChars)
    {
        if (string.IsNullOrEmpty(s)) return string.Empty;
        if (s.Length <= maxChars) return s;
        return s[..maxChars] + "...(truncated)";
    }
}


