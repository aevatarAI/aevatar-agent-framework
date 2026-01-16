using System.Text.Json;
using Aevatar.Agents.AI.Tool.Abstractions;
using Aevatar.Agents.AI.Tool.Tools.CustomTools;
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

    private AgentSkillsRuntime? _agentSkillsRuntime;

    private AgentSkillsRuntime AgentSkillsRuntime => _agentSkillsRuntime ??= new AgentSkillsRuntime(this);

    private void LogAgentSkillsDebugOnce(string key, Exception ex, string message, params object[] args)
        => AgentSkillsRuntime.LogDebugOnce(key, ex, message, args);

    /// <summary>
    /// Enable Agent Skills tools (<c>skills_list</c>/<c>skills_load</c>).
    /// Default: true (favor usability; disable in derived agents if you need tighter safety).
    /// </summary>
    public bool EnableAgentSkills { get; set; } = true;

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
        AgentSkillsRuntime.AddRoot(rootDirectory);
    }

    /// <summary>
    /// Replace skill roots and (optionally) enable the feature, then (re)register tools.
    /// </summary>
    public async Task ConfigureAgentSkillsAsync(
        IEnumerable<string> roots,
        bool enable = true,
        CancellationToken cancellationToken = default)
    {
        AgentSkillsRuntime.ReplaceRoots(roots);

        EnableAgentSkills = enable;

        // If tools already initialized, we want the new tools to become visible immediately.
        await InitializeToolsAsync(cancellationToken);
        await RegisterAgentSkillsToolsAsync(cancellationToken);
    }

    private IReadOnlyList<string> GetEffectiveAgentSkillsRoots()
    {
        return AgentSkillsRuntime.GetEffectiveRoots();
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
            Description =
                "List available Agent Skills (legacy). Prefer find_helpful_skills for task-driven work to avoid large outputs.",
            Category = ToolCategory.Core,
            Version = "1.0.0",
            Tags = new List<string> { "skills", "agent-skills", "filesystem", "discovery" },
            Parameters = new ToolParameters
            {
                Items = new Dictionary<string, ToolParameter>
                {
                    ["max_results"] = new()
                    {
                        Type = "integer",
                        Required = false,
                        Description = "Max skills to return (default: 30; range 1..200).",
                        DefaultValue = 30
                    }
                }
            },
            RequiresInternalAccess = true,
            // NOTE:
            // - Skill discovery is gated by EnableAgentSkills (default true).
            // - Keep it as an internal tool, but not "dangerous" so users don't need to flip AllowDangerousTools just to list skills.
            IsDangerous = false,
            CanBeOverridden = true,
            ExecuteAsync = async (parameters, executionContext, ct) =>
                await ExecuteSkillsListToolAsync(agentType, parameters, executionContext, ct)
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
            // NOTE:
            // - Loading SKILL.md is gated by EnableAgentSkills (default true).
            // - Dotnet-file tool import remains guarded by AllowDangerousTools at execution time.
            IsDangerous = false,
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
                            "If true, auto-register dotnet-file tools (.cs with /*aevatar_tool*/ manifest) under this skill folder. Requires AllowDangerousTools=true."
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
        await RegisterAgentSkillsResourceToolsAsync(cancellationToken);
        await RegisterAgentSkillsDynamicReadToolsAsync(cancellationToken);

        await RefreshToolCachesAsync(cancellationToken);
    }

    private async Task<IMessage> ExecuteSkillsListToolAsync(
        string agentType,
        Dictionary<string, object> parameters,
        ToolExecutionContext? executionContext,
        CancellationToken cancellationToken)
    {
        return await AgentSkillsRuntime.ExecuteSkillsListToolAsync(
            EnableAgentSkills,
            agentType,
            parameters,
            executionContext,
            cancellationToken);
    }

    private async Task<IMessage> ExecuteSkillsLoadToolAsync(
        string agentType,
        Dictionary<string, object> parameters,
        ToolExecutionContext? executionContext,
        CancellationToken cancellationToken)
    {
        return await AgentSkillsRuntime.ExecuteSkillsLoadToolAsync(
            agentType,
            parameters,
            executionContext,
            HookOptions.MaxToolOutputChars,
            AgentSkillsAutoRegisterDotNetFileTools,
            cancellationToken);
    }
}