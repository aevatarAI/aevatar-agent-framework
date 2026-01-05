using System.Text.Json;
using Aevatar.Agents.AI.WithTool.Abstractions;
using Aevatar.Agents.AI.WithTool.Tools.CustomTools;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;

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

    private readonly object _agentSkillsLogLock = new();
    private readonly HashSet<string> _agentSkillsLogOnce = new(StringComparer.OrdinalIgnoreCase);

    private void LogAgentSkillsDebugOnce(string key, Exception ex, string message, params object[] args)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            Logger.LogDebug(ex, message, args);
            return;
        }

        lock (_agentSkillsLogLock)
        {
            if (!_agentSkillsLogOnce.Add(key))
                return;
        }

        Logger.LogDebug(ex, message, args);
    }

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
            foreach (var part in env.Split([';', ':'],
                         StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
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
            catch (Exception ex)
            {
                LogAgentSkillsDebugOnce(
                    $"invalid_root::{r}",
                    ex,
                    "Invalid agent skills root '{Root}' ignored (best-effort).",
                    r);
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
                        Description =
                            "If true, auto-register dotnet-file tools (.cs with /*aevatar_tool*/ manifest) under this skill folder"
                    },
                    ["max_chars"] = new()
                    {
                        Type = "integer",
                        Description = "Max characters of SKILL.md body to return (default 16000; may be capped by host policy)"
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

        // Default: align with Hook/Harness tool output budget to avoid duplicated "max chars" knobs.
        // NOTE: callers can still override per-call via tool parameter `max_chars`.
        var maxChars = HookOptions.MaxToolOutputChars;
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
                PublishEventCallback = executionContext.PublishEventCallback,
                PublishEventWithDirectionCallback = executionContext.PublishEventWithDirectionCallback,
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
                    var tool = await DotNetFileSkillTool.LoadFromFileAsync(toolFile, executionContext.Logger,
                        cancellationToken);
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
}