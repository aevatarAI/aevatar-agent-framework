using System.Text;
using System.Text.RegularExpressions;
using Aevatar.Agents.AI.Tool.Abstractions;
using Google.Protobuf;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.AI.Core;

internal sealed partial class AgentSkillsRuntime
{
    internal async Task<IMessage> ExecuteReadSkillDocumentToolAsync(
        Dictionary<string, object> parameters,
        ToolExecutionContext? executionContext,
        CancellationToken cancellationToken)
    {
        var name = parameters.GetValueOrDefault("name")?.ToString();
        var pattern = parameters.GetValueOrDefault("pattern")?.ToString();

        if (string.IsNullOrWhiteSpace(name))
            return ToStruct(new { success = false, error = "Parameter 'name' is required." });
        if (string.IsNullOrWhiteSpace(pattern))
            return ToStruct(new { success = false, error = "Parameter 'pattern' is required." });

        var maxFiles = ClampInt(parameters.GetValueOrDefault("max_files"), fallback: 6, min: 1, max: 20);
        var maxCharsPerFile = ClampInt(parameters.GetValueOrDefault("max_chars_per_file"), fallback: 8000, min: 200, max: 50_000);
        var totalMaxChars = ClampInt(parameters.GetValueOrDefault("total_max_chars"), fallback: 16_000, min: 1_000, max: 200_000);
        var restrictToResourceDirs = TryGetBool(parameters.GetValueOrDefault("restrict_to_resource_dirs"), fallback: true);

        var match = FindSkillByName(name.Trim(), cancellationToken);
        if (match == null)
            return ToStruct(new { success = false, error = $"Skill '{name}' not found." });

        var skillDir = Path.GetFullPath(match.DirectoryPath);
        var rawPattern = NormalizeRel(pattern!);

        // Fast path: no glob => single file
        if (!LooksLikeGlob(rawPattern))
        {
            if (restrictToResourceDirs && !IsUnderResourceDirs(rawPattern))
                return ToStruct(new { success = false, error = "Path is outside scripts/references/assets (restricted by policy)." });

            var file = ResolvePathUnderRoot(skillDir, rawPattern);
            if (file == null)
                return ToStruct(new { success = false, error = "Invalid path (path traversal denied)." });
            if (!File.Exists(file))
                return ToStruct(new { success = false, error = $"File not found: {rawPattern}", skill = match.Name });

            var content = await ReadAllTextWithLimitAsync(file, Math.Min(maxCharsPerFile, totalMaxChars), cancellationToken);
            long? sizeBytes = null;
            try { sizeBytes = new FileInfo(file).Length; } catch { /* ignore */ }

            return ToStruct(new
            {
                success = true,
                name = match.Name,
                pattern = rawPattern,
                matched = 1,
                returned = 1,
                files = new[]
                {
                    new
                    {
                        path = rawPattern,
                        sizeBytes,
                        truncated = content.Length >= Math.Min(maxCharsPerFile, totalMaxChars),
                        content
                    }
                }
            });
        }

        // Glob path: enumerate + match
        IEnumerable<string> allFiles;
        try
        {
            allFiles = Directory.EnumerateFiles(skillDir, "*", SearchOption.AllDirectories);
        }
        catch (Exception ex)
        {
            _owner.InternalLogger.LogDebug(ex, "Failed to enumerate skill files (best-effort).");
            return ToStruct(new { success = false, error = ex.Message });
        }

        var matcher = BuildGlobRegex(rawPattern);
        var selected = new List<(string Rel, string Full)>();

        foreach (var f in allFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var rel = NormalizeRel(Path.GetRelativePath(skillDir, f));

            if (restrictToResourceDirs && !IsUnderResourceDirs(rel))
                continue;

            if (matcher.IsMatch(rel))
            {
                selected.Add((rel, f));
            }
        }

        selected = selected
            .OrderBy(x => x.Rel, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var files = new List<object>();
        var skipped = new List<object>();
        var returned = 0;
        var usedChars = 0;
        var truncatedAny = false;

        foreach (var (rel, full) in selected)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (returned >= maxFiles)
            {
                truncatedAny = true;
                break;
            }

            // crude binary check: only read small UTF-8 text-ish files
            if (LooksLikeBinaryByExtension(rel))
            {
                skipped.Add(new { path = rel, reason = "binary_or_unsupported_extension" });
                continue;
            }

            var remaining = totalMaxChars - usedChars;
            if (remaining <= 0)
            {
                truncatedAny = true;
                break;
            }

            var perFileBudget = Math.Min(maxCharsPerFile, remaining);

            string content;
            try
            {
                content = await ReadAllTextWithLimitAsync(full, perFileBudget, cancellationToken);
            }
            catch (Exception ex)
            {
                skipped.Add(new { path = rel, reason = ex.Message });
                continue;
            }

            long? sizeBytes = null;
            try { sizeBytes = new FileInfo(full).Length; } catch { /* ignore */ }

            usedChars += content.Length;
            returned++;

            files.Add(new
            {
                path = rel,
                sizeBytes,
                truncated = content.Length >= perFileBudget,
                content
            });
        }

        return ToStruct(new
        {
            success = true,
            name = match.Name,
            pattern = rawPattern,
            matched = selected.Count,
            returned,
            truncated = truncatedAny,
            totalChars = usedChars,
            files,
            skipped
        });
    }

    private static bool LooksLikeGlob(string pattern)
        => pattern.Contains('*') || pattern.Contains('?');

    private static string NormalizeRel(string rel)
        => rel.Replace('\\', '/').TrimStart('/');

    private static bool IsUnderResourceDirs(string rel)
        => rel.StartsWith("scripts/", StringComparison.OrdinalIgnoreCase) ||
           rel.StartsWith("references/", StringComparison.OrdinalIgnoreCase) ||
           rel.StartsWith("assets/", StringComparison.OrdinalIgnoreCase);

    private static bool LooksLikeBinaryByExtension(string rel)
    {
        var ext = Path.GetExtension(rel);
        if (string.IsNullOrWhiteSpace(ext)) return false;
        ext = ext.ToLowerInvariant();

        return ext is ".png" or ".jpg" or ".jpeg" or ".gif" or ".webp" or ".pdf" or ".zip" or ".gz" or ".tar" or ".7z" or ".mp4" or ".mov";
    }

    private static Regex BuildGlobRegex(string pattern)
    {
        // Simple glob:
        // - ** matches any chars including '/'
        // - * matches any chars except '/'
        // - ? matches one char except '/'
        var sb = new StringBuilder();
        sb.Append("^");

        for (var i = 0; i < pattern.Length; i++)
        {
            var c = pattern[i];

            if (c == '*')
            {
                var isDouble = i + 1 < pattern.Length && pattern[i + 1] == '*';
                if (isDouble)
                {
                    sb.Append(".*");
                    i++;
                }
                else
                {
                    sb.Append("[^/]*");
                }
                continue;
            }

            if (c == '?')
            {
                sb.Append("[^/]");
                continue;
            }

            sb.Append(Regex.Escape(c.ToString()));
        }

        sb.Append("$");
        return new Regex(sb.ToString(), RegexOptions.Compiled | RegexOptions.IgnoreCase);
    }
}


