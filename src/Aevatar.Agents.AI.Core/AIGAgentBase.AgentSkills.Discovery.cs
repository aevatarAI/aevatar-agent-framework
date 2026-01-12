using System.Text;
using System.Text.RegularExpressions;

namespace Aevatar.Agents.AI.Core;

public abstract partial class AIGAgentBase
{
    // ============================================================
    //  Agent Skills discovery (recursive)
    //
    //  NOTE:
    //  - AgentSkills spec: a skill is a folder containing SKILL.md
    //  - Some skill packs group skills by domain, e.g. root/domain/skill/SKILL.md
    //  - We support bounded recursion to keep discovery fast and predictable.
    // ============================================================
    private const int AgentSkillsDiscoveryMaxDepth = 3;

    private IReadOnlyList<AgentSkillDescriptor> DiscoverAgentSkills(
        IReadOnlyList<string> roots,
        CancellationToken cancellationToken)
    {
        var list = new List<AgentSkillDescriptor>();

        foreach (var root in roots)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!Directory.Exists(root))
                continue;

            foreach (var dir in EnumerateSkillCandidateDirectories(root, AgentSkillsDiscoveryMaxDepth, cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var skillFile = Path.Combine(dir, DefaultSkillEntryFileName);
                if (!File.Exists(skillFile))
                    continue;

                var folderName =
                    Path.GetFileName(dir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

                // Read front matter only (best-effort). If parsing fails, fall back to folder name.
                var frontMatter = TryReadSkillFrontMatter(skillFile, cancellationToken);
                var name = frontMatter?.Name ?? folderName;
                var desc = frontMatter?.Description ?? $"Agent skill at '{folderName}'";
                var allowedTools = frontMatter?.AllowedTools ?? Array.Empty<string>();

                // Discover dotnet tool files (explicit manifest marker only)
                var dotnetToolFiles = DiscoverDotNetToolFiles(dir, cancellationToken);

                list.Add(new AgentSkillDescriptor(
                    FolderName: folderName,
                    Name: name,
                    Description: desc,
                    AllowedTools: allowedTools,
                    DirectoryPath: dir,
                    SkillFilePath: skillFile,
                    DotNetToolFiles: dotnetToolFiles));
            }
        }

        // Stable ordering: by name
        return list
            .OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private IEnumerable<string> EnumerateSkillCandidateDirectories(
        string root,
        int maxDepth,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(root))
            yield break;

        maxDepth = Math.Clamp(maxDepth, 1, 10);

        // BFS: stable + bounded.
        var queue = new Queue<(string Dir, int Depth)>();

        IEnumerable<string> firstLevel;
        try
        {
            firstLevel = Directory.EnumerateDirectories(root);
        }
        catch (Exception ex)
        {
            LogAgentSkillsDebugOnce(
                $"enumerate_dirs::{root}",
                ex,
                "Failed to enumerate agent skills root '{Root}' (best-effort).",
                root);
            yield break;
        }

        foreach (var d in firstLevel.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (ShouldSkipSkillDiscoveryDirectory(d))
                continue;
            queue.Enqueue((d, 1));
        }

        while (queue.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var (dir, depth) = queue.Dequeue();
            yield return dir;

            if (depth >= maxDepth)
                continue;

            IEnumerable<string> children;
            try
            {
                children = Directory.EnumerateDirectories(dir);
            }
            catch (Exception ex)
            {
                LogAgentSkillsDebugOnce(
                    $"enumerate_dirs::{dir}",
                    ex,
                    "Failed to enumerate agent skill subdir '{Dir}' (best-effort).",
                    dir);
                continue;
            }

            foreach (var child in children.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (ShouldSkipSkillDiscoveryDirectory(child))
                    continue;
                queue.Enqueue((child, depth + 1));
            }
        }
    }

    private static bool ShouldSkipSkillDiscoveryDirectory(string dirPath)
    {
        if (string.IsNullOrWhiteSpace(dirPath))
            return true;

        var name = Path.GetFileName(dirPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.IsNullOrWhiteSpace(name))
            return true;

        // Skip hidden dirs and common build artifacts.
        if (name.StartsWith(".", StringComparison.Ordinal))
            return true;

        return string.Equals(name, "bin", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(name, "obj", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(name, "node_modules", StringComparison.OrdinalIgnoreCase) ||
               // Resource folders inside a skill: not skill containers.
               string.Equals(name, "scripts", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(name, "references", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(name, "assets", StringComparison.OrdinalIgnoreCase);
    }

    private IReadOnlyList<string> DiscoverDotNetToolFiles(string skillDir, CancellationToken cancellationToken)
    {
        var results = new List<string>();

        // Keep it bounded; skills should be small.
        const int MaxFiles = 32;

        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(skillDir, "*.cs", SearchOption.AllDirectories);
        }
        catch (Exception ex)
        {
            LogAgentSkillsDebugOnce(
                $"enumerate_files::{skillDir}",
                ex,
                "Failed to enumerate dotnet tool files under skill dir '{SkillDir}' (best-effort).",
                skillDir);
            return results;
        }

        foreach (var f in files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (results.Count >= MaxFiles)
                break;

            if (!LooksLikeAevatarDotNetToolFile(f))
                continue;

            results.Add(f);
        }

        return results;
    }

    private static bool LooksLikeAevatarDotNetToolFile(string filePath)
    {
        try
        {
            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var max = (int)Math.Min(16 * 1024, fs.Length);
            if (max <= 0) return false;

            var buf = new byte[max];
            var read = fs.Read(buf, 0, max);
            if (read <= 0) return false;

            var head = Encoding.UTF8.GetString(buf, 0, read);
            return head.Contains("/*aevatar_tool", StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }

    private static AgentSkillFrontMatter? TryReadSkillFrontMatter(string skillFile, CancellationToken cancellationToken)
    {
        // Only need the first part for YAML; keep it small.
        var text = ReadAllTextWithLimit(skillFile, maxChars: 32_000, cancellationToken);
        var parsed = ParseSkillMarkdown(text);
        return parsed.FrontMatter;
    }

    private static SkillMarkdown ParseSkillMarkdown(string markdown)
    {
        if (string.IsNullOrEmpty(markdown))
            return new SkillMarkdown(null, string.Empty);

        var m = SkillFrontMatterRegex.Match(markdown);
        if (!m.Success)
            return new SkillMarkdown(null, markdown);

        var yaml = m.Groups["yaml"].Value;
        var body = m.Groups["body"].Value;
        var fm = ParseFrontMatterYaml(yaml);
        return new SkillMarkdown(fm, body);
    }

    private static readonly Regex SkillFrontMatterRegex = new(
        @"\A---\s*\r?\n(?<yaml>[\s\S]*?)\r?\n---\s*\r?\n(?<body>[\s\S]*)\z",
        RegexOptions.Compiled);

    private static AgentSkillFrontMatter? ParseFrontMatterYaml(string yaml)
    {
        if (string.IsNullOrWhiteSpace(yaml))
            return null;

        // Minimal YAML subset parser:
        // - key: value
        // - key: |
        //   indented block...
        // - key:
        //   - item
        var scalars = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var lists = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        string? modeKey = null;
        var mode = YamlMode.None;
        var block = new StringBuilder();
        List<string>? currentList = null;

        foreach (var raw in yaml.Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw;

            // Skip comments
            if (line.TrimStart().StartsWith("#", StringComparison.Ordinal))
                continue;

            if (mode == YamlMode.Block)
            {
                if (IsYamlKeyLine(line))
                {
                    if (modeKey != null)
                        scalars[modeKey] = block.ToString().TrimEnd();

                    modeKey = null;
                    mode = YamlMode.None;
                    block.Clear();
                    // fallthrough to parse this line as a new key
                }
                else
                {
                    block.AppendLine(TrimYamlIndent(line));
                    continue;
                }
            }

            if (mode == YamlMode.List)
            {
                if (IsYamlKeyLine(line))
                {
                    modeKey = null;
                    mode = YamlMode.None;
                    currentList = null;
                    // fallthrough to parse this line as a new key
                }
                else
                {
                    var t = line.Trim();
                    if (t.StartsWith("- ", StringComparison.Ordinal))
                    {
                        currentList?.Add(t[2..].Trim());
                    }

                    continue;
                }
            }

            var idx = line.IndexOf(':');
            if (idx <= 0)
                continue;

            var key = line[..idx].Trim();
            var value = line[(idx + 1)..].TrimStart();

            if (string.IsNullOrWhiteSpace(key))
                continue;

            if (value is "|" or ">")
            {
                modeKey = key;
                mode = YamlMode.Block;
                block.Clear();
                continue;
            }

            if (string.IsNullOrWhiteSpace(value))
            {
                modeKey = key;
                mode = YamlMode.List;
                currentList = new List<string>();
                lists[key] = currentList;
                continue;
            }

            scalars[key] = UnquoteYamlScalar(value);
        }

        if (mode == YamlMode.Block && modeKey != null)
        {
            scalars[modeKey] = block.ToString().TrimEnd();
        }

        var name = scalars.TryGetValue("name", out var n) ? n : null;
        var desc = scalars.TryGetValue("description", out var d) ? d : null;

        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(desc))
            return null;

        var allowedTools = lists.TryGetValue("allowed-tools", out var at)
            ? at.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()).ToArray()
            : Array.Empty<string>();

        return new AgentSkillFrontMatter(name.Trim(), desc.Trim(), allowedTools);
    }

    private static bool IsYamlKeyLine(string line)
    {
        // Key must start at column 0: "key:"
        if (string.IsNullOrWhiteSpace(line))
            return false;

        if (char.IsWhiteSpace(line[0]))
            return false;

        var idx = line.IndexOf(':');
        return idx > 0;
    }

    private static string TrimYamlIndent(string line)
    {
        // YAML block content is typically indented by 2 spaces.
        if (line.StartsWith("  ", StringComparison.Ordinal))
            return line[2..];
        return line.TrimStart();
    }

    private static string UnquoteYamlScalar(string value)
    {
        var v = value.Trim();
        if (v.Length >= 2 &&
            ((v[0] == '"' && v[^1] == '"') || (v[0] == '\'' && v[^1] == '\'')))
        {
            return v[1..^1];
        }

        return v;
    }

    private static string ReadAllTextWithLimit(string path, int maxChars, CancellationToken cancellationToken)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var reader = new StreamReader(fs, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);

        var sb = new StringBuilder(Math.Min(maxChars, 16 * 1024));
        var buf = new char[4096];
        while (sb.Length < maxChars)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var toRead = Math.Min(buf.Length, maxChars - sb.Length);
            var read = reader.Read(buf, 0, toRead);
            if (read <= 0)
                break;

            sb.Append(buf, 0, read);
        }

        return sb.ToString();
    }

    private static async Task<string> ReadAllTextWithLimitAsync(string path, int maxChars,
        CancellationToken cancellationToken)
    {
        await using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var reader = new StreamReader(fs, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);

        var sb = new StringBuilder(Math.Min(maxChars, 16 * 1024));
        var buf = new char[4096];
        while (sb.Length < maxChars)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var toRead = Math.Min(buf.Length, maxChars - sb.Length);
            var read = await reader.ReadAsync(buf.AsMemory(0, toRead), cancellationToken);
            if (read <= 0)
                break;

            sb.Append(buf, 0, read);
        }

        return sb.ToString();
    }

    private sealed record AgentSkillDescriptor(
        string FolderName,
        string Name,
        string Description,
        IReadOnlyList<string> AllowedTools,
        string DirectoryPath,
        string SkillFilePath,
        IReadOnlyList<string> DotNetToolFiles);

    private sealed record AgentSkillFrontMatter(
        string Name,
        string Description,
        IReadOnlyList<string> AllowedTools);

    private sealed record SkillMarkdown(
        AgentSkillFrontMatter? FrontMatter,
        string Body);

    private enum YamlMode
    {
        None,
        Block,
        List
    }
}