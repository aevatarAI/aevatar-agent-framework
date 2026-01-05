using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Aevatar.Agents.AI.WithTool.Abstractions;
using Aevatar.Agents.AI.WithTool.Messages;
using Aevatar.Agents.AI.WithTool.Tools.CustomTools;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;

namespace Aevatar.Agents.AI.Core;

public abstract partial class AIGAgentBase
{
    // ============================================================
    //  Agent Skills (agentskills.io) integration
    //
    //  Approach:
    //  - Skill is a directory containing SKILL.md (YAML front matter + instruction body) and script/resource files
    //  - AIGAgentBase enables LLM to "load on demand" skills via built-in tools (avoiding stuffing all instructions into system prompt)
    //  - Optional: Automatically import dotnet-file tools in skill directory on load (C# single file + /*aevatar_tool*/ manifest)
    // ============================================================

    private const string DefaultSkillEntryFileName = "SKILL.md";
    private const string AgentSkillsRootsEnv = "AEVATAR_AGENT_SKILLS_DIRS";

    private readonly object _agentSkillsLock = new();
    private readonly List<string> _agentSkillsRoots = [];

    /// <summary>
    /// Enable Agent Skills tools (<c>skills_list</c>/<c>skills_load</c>).
    /// Default: false (avoid exposing filesystem reads by default).
    /// </summary>
    public bool EnableAgentSkills { get; set; }

    /// <summary>
    /// Auto-register dotnet-file tools (.cs + /*aevatar_tool*/ manifest) when loading a skill.
    /// Default: true.
    /// </summary>
    public bool AgentSkillsAutoRegisterDotNetFileTools { get; set; } = true;

    /// <summary>
    /// Add a root directory that contains skill folders.
    /// </summary>
    public void AddAgentSkillsRoot(string rootDirectory)
    {
        if (string.IsNullOrWhiteSpace(rootDirectory))
            return;

        lock (_agentSkillsLock)
        {
            _agentSkillsRoots.Add(rootDirectory.Trim());
        }
    }

    /// <summary>
    /// Replace skill roots and (optionally) enable the feature, then (re)register tools.
    /// </summary>
    public async Task ConfigureAgentSkillsAsync(
        IEnumerable<string> roots,
        bool enable = true,
        CancellationToken cancellationToken = default)
    {
        lock (_agentSkillsLock)
        {
            _agentSkillsRoots.Clear();
            foreach (var r in roots)
            {
                if (!string.IsNullOrWhiteSpace(r))
                {
                    _agentSkillsRoots.Add(r.Trim());
                }
            }
        }

        EnableAgentSkills = enable;

        // If tools already initialized, we want the new tools to become visible immediately.
        await InitializeToolsAsync(cancellationToken);
        await RegisterAgentSkillsToolsAsync(cancellationToken);
    }

    private IReadOnlyList<string> GetEffectiveAgentSkillsRoots()
    {
        var roots = new List<string>();

        lock (_agentSkillsLock)
        {
            roots.AddRange(_agentSkillsRoots);
        }

        var env = Environment.GetEnvironmentVariable(AgentSkillsRootsEnv);
        if (!string.IsNullOrWhiteSpace(env))
        {
            // Support both ';' and ':' for convenience across shells.
            foreach (var part in env.Split([';', ':'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (!string.IsNullOrWhiteSpace(part))
                {
                    roots.Add(part.Trim());
                }
            }
        }

        // Normalize + dedupe
        var unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();
        foreach (var r in roots)
        {
            try
            {
                var full = Path.GetFullPath(r);
                if (Directory.Exists(full) && unique.Add(full))
                {
                    result.Add(full);
                }
            }
            catch
            {
                // Ignore invalid paths (best-effort).
            }
        }

        return result;
    }

    private async Task RegisterAgentSkillsToolsAsync(CancellationToken cancellationToken = default)
    {
        // Disabled -> don't expose tools to the model.
        if (!EnableAgentSkills)
            return;

        EnsureToolManagerInitialized();

        var agentType = GetType().Name;

        // skills_list
        var listTool = new ToolDefinition
        {
            Name = "skills_list",
            Description = "List available Agent Skills (folders containing SKILL.md) from configured roots",
            Category = ToolCategory.Core,
            Version = "1.0.0",
            Tags = new List<string> { "skills", "agent-skills", "filesystem", "discovery" },
            Parameters = new ToolParameters(),
            RequiresInternalAccess = true,
            IsDangerous = true,
            CanBeOverridden = true,
            ExecuteAsync = async (_, executionContext, ct) =>
                await ExecuteSkillsListToolAsync(agentType, executionContext, ct)
        };

        // skills_load
        var loadTool = new ToolDefinition
        {
            Name = "skills_load",
            Description =
                "Load a specific Agent Skill by name; returns SKILL.md content and optionally registers dotnet-file tools found inside the skill folder",
            Category = ToolCategory.Core,
            Version = "1.0.0",
            Tags = new List<string> { "skills", "agent-skills", "filesystem", "prompt", "import" },
            RequiresInternalAccess = true,
            IsDangerous = true,
            CanBeOverridden = true,
            Parameters = new ToolParameters
            {
                Items = new Dictionary<string, ToolParameter>
                {
                    ["name"] = new()
                    {
                        Type = "string",
                        Required = true,
                        Description = "Skill name (from SKILL.md front matter 'name') or folder name"
                    },
                    ["register_tools"] = new()
                    {
                        Type = "boolean",
                        Description = "If true, auto-register dotnet-file tools (.cs with /*aevatar_tool*/ manifest) under this skill folder"
                    },
                    ["max_chars"] = new()
                    {
                        Type = "integer",
                        Description = "Max characters of SKILL.md body to return (default 16000)"
                    }
                },
                Required = new[] { "name" }
            },
            ExecuteAsync = async (parameters, executionContext, ct) =>
                await ExecuteSkillsLoadToolAsync(agentType, parameters, executionContext, ct)
        };

        await ToolManager.RegisterToolAsync(listTool, cancellationToken);
        await ToolManager.RegisterToolAsync(loadTool, cancellationToken);

        await RefreshToolCachesAsync(cancellationToken);
    }

    private async Task<IMessage> ExecuteSkillsListToolAsync(
        string agentType,
        ToolExecutionContext? executionContext,
        CancellationToken cancellationToken)
    {
        await Task.CompletedTask;

        var roots = GetEffectiveAgentSkillsRoots();
        var skills = DiscoverAgentSkills(roots, cancellationToken);

        var result = new
        {
            success = true,
            enabled = EnableAgentSkills,
            roots,
            count = skills.Count,
            skills = skills.Select(s => new
            {
                name = s.Name,
                description = s.Description,
                allowedTools = s.AllowedTools,
                path = s.DirectoryPath,
                hasDotNetTools = s.DotNetToolFiles.Count > 0
            })
        };

        return JsonParser.Default.Parse<Struct>(JsonSerializer.Serialize(result));
    }

    private async Task<IMessage> ExecuteSkillsLoadToolAsync(
        string agentType,
        Dictionary<string, object> parameters,
        ToolExecutionContext? executionContext,
        CancellationToken cancellationToken)
    {
        var name = parameters.TryGetValue("name", out var nameObj)
            ? nameObj?.ToString()
            : null;

        if (string.IsNullOrWhiteSpace(name))
        {
            var bad = new { success = false, error = "Parameter 'name' is required." };
            return JsonParser.Default.Parse<Struct>(JsonSerializer.Serialize(bad));
        }

        if (executionContext?.ToolManager == null)
        {
            var bad = new { success = false, error = "ToolExecutionContext.ToolManager not provided." };
            return JsonParser.Default.Parse<Struct>(JsonSerializer.Serialize(bad));
        }

        var registerTools = AgentSkillsAutoRegisterDotNetFileTools;
        if (parameters.TryGetValue("register_tools", out var rt))
        {
            if (rt is bool b) registerTools = b;
            else if (bool.TryParse(rt?.ToString(), out var parsed)) registerTools = parsed;
        }

        var maxChars = 16_000;
        if (parameters.TryGetValue("max_chars", out var mc))
        {
            if (mc is int i) maxChars = i;
            else if (int.TryParse(mc?.ToString(), out var parsed)) maxChars = parsed;
        }

        maxChars = Math.Clamp(maxChars, 1_000, 128_000);

        var roots = GetEffectiveAgentSkillsRoots();
        var skills = DiscoverAgentSkills(roots, cancellationToken);
        var match = skills.FirstOrDefault(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                    ?? skills.FirstOrDefault(s => s.FolderName.Equals(name, StringComparison.OrdinalIgnoreCase));

        if (match == null)
        {
            var notFound = new
            {
                success = false,
                error = $"Skill '{name}' not found.",
                available = skills.Select(s => s.Name).ToArray()
            };
            return JsonParser.Default.Parse<Struct>(JsonSerializer.Serialize(notFound));
        }

        var skillMarkdown = await ReadAllTextWithLimitAsync(match.SkillFilePath, maxChars, cancellationToken);
        var parsedSkill = ParseSkillMarkdown(skillMarkdown);

        var registered = new List<string>();
        var skipped = new List<object>();

        if (registerTools && match.DotNetToolFiles.Count > 0)
        {
            var baseToolContext = new ToolContext
            {
                AgentId = executionContext.AgentId,
                AgentType = agentType,
                Memory = executionContext.Memory,
                PublishEventCallback = executionContext.PublishEventCallback,
                GetSessionIdCallback = executionContext.GetSessionId != null
                    ? () => executionContext.GetSessionId()
                    : null,
                Logger = executionContext.Logger
            };

            foreach (var toolFile in match.DotNetToolFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    var tool = await DotNetFileSkillTool.LoadFromFileAsync(toolFile, executionContext.Logger, cancellationToken);
                    var def = tool.CreateToolDefinition(baseToolContext, executionContext.Logger);
                    await executionContext.ToolManager.RegisterToolAsync(def, cancellationToken);
                    registered.Add(tool.Name);
                }
                catch (Exception ex)
                {
                    skipped.Add(new { file = toolFile, error = ex.Message });
                }
            }
        }

        var result = new
        {
            success = true,
            name = match.Name,
            description = match.Description,
            allowedTools = match.AllowedTools,
            path = match.DirectoryPath,
            markdown = parsedSkill.Body.Length > maxChars ? parsedSkill.Body[..maxChars] : parsedSkill.Body,
            dotnetToolFiles = match.DotNetToolFiles,
            registeredTools = registered,
            skipped
        };

        return JsonParser.Default.Parse<Struct>(JsonSerializer.Serialize(result));
    }

    private static IReadOnlyList<AgentSkillDescriptor> DiscoverAgentSkills(
        IReadOnlyList<string> roots,
        CancellationToken cancellationToken)
    {
        var list = new List<AgentSkillDescriptor>();

        foreach (var root in roots)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!Directory.Exists(root))
                continue;

            IEnumerable<string> dirs;
            try
            {
                dirs = Directory.EnumerateDirectories(root);
            }
            catch
            {
                continue;
            }

            foreach (var dir in dirs)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var skillFile = Path.Combine(dir, DefaultSkillEntryFileName);
                if (!File.Exists(skillFile))
                    continue;

                var folderName = Path.GetFileName(dir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

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

    private static IReadOnlyList<string> DiscoverDotNetToolFiles(string skillDir, CancellationToken cancellationToken)
    {
        var results = new List<string>();

        // Keep it bounded; skills should be small.
        const int MaxFiles = 32;

        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(skillDir, "*.cs", SearchOption.AllDirectories);
        }
        catch
        {
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

    private static async Task<string> ReadAllTextWithLimitAsync(string path, int maxChars, CancellationToken cancellationToken)
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

