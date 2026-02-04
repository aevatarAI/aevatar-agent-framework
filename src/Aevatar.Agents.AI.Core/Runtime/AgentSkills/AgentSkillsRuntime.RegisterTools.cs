using Aevatar.Agents.AI.Tool.Abstractions;
using Aevatar.Agents.AI.Tool.Tools.CustomTools;
using Google.Protobuf;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.AI.Core;

internal sealed partial class AgentSkillsRuntime
{
    internal async Task RegisterToolsAsync(
        bool enabled,
        string agentType,
        IAevatarToolManager toolManager,
        ILogger logger,
        int defaultMaxToolOutputChars,
        bool defaultRegisterToolsOnLoad,
        CancellationToken cancellationToken)
    {
        if (!enabled)
            return;

        ArgumentNullException.ThrowIfNull(toolManager);
        ArgumentNullException.ThrowIfNull(logger);

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
            IsDangerous = false,
            CanBeOverridden = true,
            ExecuteAsync = async (parameters, executionContext, ct) =>
                await ExecuteSkillsListToolAsync(enabled, agentType, parameters, executionContext, ct)
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
                await ExecuteSkillsLoadToolAsync(
                    agentType,
                    parameters,
                    executionContext,
                    defaultMaxChars: defaultMaxToolOutputChars,
                    defaultRegisterTools: defaultRegisterToolsOnLoad,
                    ct)
        };

        await toolManager.RegisterToolAsync(listTool, cancellationToken);
        await toolManager.RegisterToolAsync(loadTool, cancellationToken);

        await RegisterResourceToolsAsync(enabled, toolManager, defaultMaxToolOutputChars, cancellationToken);
        await RegisterDynamicReadToolsAsync(enabled, toolManager, defaultMaxToolOutputChars, cancellationToken);
    }

    private async Task RegisterResourceToolsAsync(
        bool enabled,
        IAevatarToolManager toolManager,
        int defaultMaxToolOutputChars,
        CancellationToken cancellationToken)
    {
        if (!enabled)
            return;

        // skills_files
        var filesTool = new ToolDefinition
        {
            Name = "skills_files",
            Description = "List files/directories under a specific Agent Skill folder (e.g., scripts/, references/, assets/).",
            Category = ToolCategory.Core,
            Version = "1.0.0",
            Tags = new List<string> { "skills", "agent-skills", "filesystem", "resources", "discovery" },
            RequiresInternalAccess = true,
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
                    ["dir"] = new()
                    {
                        Type = "string",
                        Description = "Relative directory inside the skill folder to list (default: '.')",
                        Required = false,
                        DefaultValue = "."
                    },
                    ["recursive"] = new()
                    {
                        Type = "boolean",
                        Description = "If true, list recursively (default: false)",
                        Required = false,
                        DefaultValue = false
                    },
                    ["max_entries"] = new()
                    {
                        Type = "integer",
                        Description = "Max entries to return (default: 200; range 1..2000)",
                        Required = false,
                        DefaultValue = 200
                    }
                },
                Required = new[] { "name" }
            },
            ExecuteAsync = async (parameters, executionContext, ct) =>
                await ExecuteSkillsFilesToolAsync(parameters, executionContext, ct)
        };

        // skills_read_file
        var readFileTool = new ToolDefinition
        {
            Name = "skills_read_file",
            Description = "Read a text file inside an Agent Skill folder (e.g., scripts/*.py, references/*.md).",
            Category = ToolCategory.Core,
            Version = "1.0.0",
            Tags = new List<string> { "skills", "agent-skills", "filesystem", "resources", "read" },
            RequiresInternalAccess = true,
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
                    ["path"] = new()
                    {
                        Type = "string",
                        Required = true,
                        Description = "Relative file path inside the skill folder"
                    },
                    ["max_chars"] = new()
                    {
                        Type = "integer",
                        Required = false,
                        Description = "Max characters to return (default: uses host tool output budget)",
                        DefaultValue = defaultMaxToolOutputChars
                    }
                },
                Required = new[] { "name", "path" }
            },
            ExecuteAsync = async (parameters, executionContext, ct) =>
                await ExecuteSkillsReadFileToolAsync(parameters, executionContext, defaultMaxToolOutputChars, ct)
        };

        // skills_run_python
        var runPythonTool = new ToolDefinition
        {
            Name = "skills_run_python",
            Description = "Run a python script inside a skill folder (dangerous).",
            Category = ToolCategory.Core,
            Version = "1.0.0",
            Tags = new List<string> { "skills", "agent-skills", "scripts", "python", "execute" },
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
                    ["script"] = new()
                    {
                        Type = "string",
                        Required = true,
                        Description = "Relative script path inside the skill folder (e.g., scripts/foo.py)"
                    },
                    ["args"] = new()
                    {
                        Type = "array",
                        Required = false,
                        Description = "Command args (array of strings)"
                    },
                    ["input"] = new()
                    {
                        Type = "string",
                        Required = false,
                        Description = "Optional stdin content"
                    },
                    ["timeoutMs"] = new()
                    {
                        Type = "integer",
                        Required = false,
                        Description = "Timeout in ms (default: 120000; range 1000..600000)",
                        DefaultValue = 120000
                    },
                    ["max_output_chars"] = new()
                    {
                        Type = "integer",
                        Required = false,
                        Description = "Max characters to return from stdout/stderr (default: uses host tool output budget)",
                        DefaultValue = defaultMaxToolOutputChars
                    },
                    ["restrictToScriptsDir"] = new()
                    {
                        Type = "boolean",
                        Required = false,
                        Description = "If true, only allow executing files under 'scripts/' (default: true).",
                        DefaultValue = true
                    }
                },
                Required = new[] { "name", "script" }
            },
            ExecuteAsync = async (parameters, executionContext, ct) =>
                await ExecuteSkillsRunPythonToolAsync(parameters, executionContext, defaultMaxToolOutputChars, ct)
        };

        await toolManager.RegisterToolAsync(filesTool, cancellationToken);
        await toolManager.RegisterToolAsync(readFileTool, cancellationToken);
        await toolManager.RegisterToolAsync(runPythonTool, cancellationToken);
    }

    private async Task RegisterDynamicReadToolsAsync(
        bool enabled,
        IAevatarToolManager toolManager,
        int defaultMaxToolOutputChars,
        CancellationToken cancellationToken)
    {
        if (!enabled)
            return;

        // find_helpful_skills
        var findTool = new ToolDefinition
        {
            Name = "find_helpful_skills",
            Description =
                "Search Agent Skills by query and return ranked candidates with brief guidance. Uses embeddings when configured; otherwise falls back to lexical matching. Prefer this over listing all skills.",
            Category = ToolCategory.Core,
            Version = "1.0.0",
            Tags = new List<string> { "skills", "agent-skills", "search", "semantic", "discovery" },
            RequiresInternalAccess = true,
            IsDangerous = false,
            CanBeOverridden = true,
            Parameters = new ToolParameters
            {
                Items = new Dictionary<string, ToolParameter>
                {
                    ["query"] = new()
                    {
                        Type = "string",
                        Required = true,
                        Description = "User request or keywords to search for"
                    },
                    ["max_results"] = new()
                    {
                        Type = "integer",
                        Required = false,
                        Description = "Max candidates to return (default: 5; range 1..10)",
                        DefaultValue = 5
                    },
                    ["max_chars_per_skill"] = new()
                    {
                        Type = "integer",
                        Required = false,
                        Description = "Max chars of guidance snippet per skill (default: 1200; range 200..6000)",
                        DefaultValue = 1200
                    }
                },
                Required = new[] { "query" }
            },
            ExecuteAsync = async (parameters, executionContext, ct) =>
                await ExecuteFindHelpfulSkillsToolAsync(parameters, executionContext, ct)
        };

        // find_helpful_alls (alias)
        var findAllsAliasTool = new ToolDefinition
        {
            Name = "find_helpful_alls",
            Description = "Alias of find_helpful_skills (kept for robustness).",
            Category = ToolCategory.Core,
            Version = "1.0.0",
            Tags = new List<string> { "skills", "agent-skills", "search", "semantic", "alias" },
            RequiresInternalAccess = true,
            IsDangerous = false,
            CanBeOverridden = true,
            Parameters = findTool.Parameters,
            ExecuteAsync = async (parameters, executionContext, ct) =>
                await ExecuteFindHelpfulSkillsToolAsync(parameters, executionContext, ct)
        };

        // list_skills (debug)
        var listTool = new ToolDefinition
        {
            Name = "list_skills",
            Description = "List the full inventory of available skills (debug/exploration). For task-driven work, prefer find_helpful_skills.",
            Category = ToolCategory.Core,
            Version = "1.0.0",
            Tags = new List<string> { "skills", "agent-skills", "filesystem", "inventory" },
            RequiresInternalAccess = true,
            IsDangerous = false,
            CanBeOverridden = true,
            Parameters = new ToolParameters(),
            ExecuteAsync = async (_, executionContext, ct) =>
                await ExecuteListSkillsToolAsync(executionContext, ct)
        };

        // read_skill_document
        var readDocTool = new ToolDefinition
        {
            Name = "read_skill_document",
            Description =
                "Read text documents inside a skill folder by pattern (e.g., scripts/*.py, references/**/*.md). Never executes code.",
            Category = ToolCategory.Core,
            Version = "1.0.0",
            Tags = new List<string> { "skills", "agent-skills", "filesystem", "read", "glob" },
            RequiresInternalAccess = true,
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
                    ["pattern"] = new()
                    {
                        Type = "string",
                        Required = true,
                        Description = "Glob pattern to match documents relative to the skill folder"
                    },
                    ["max_files"] = new()
                    {
                        Type = "integer",
                        Required = false,
                        Description = "Max files to read (default: 10; range 1..50)",
                        DefaultValue = 10
                    },
                    ["max_chars_per_file"] = new()
                    {
                        Type = "integer",
                        Required = false,
                        Description = "Max chars per file (default: uses host tool output budget)",
                        DefaultValue = defaultMaxToolOutputChars
                    }
                },
                Required = new[] { "name", "pattern" }
            },
            ExecuteAsync = async (parameters, executionContext, ct) =>
                await ExecuteReadSkillDocumentToolAsync(parameters, executionContext, ct)
        };

        await toolManager.RegisterToolAsync(findTool, cancellationToken);
        await toolManager.RegisterToolAsync(findAllsAliasTool, cancellationToken);
        await toolManager.RegisterToolAsync(listTool, cancellationToken);
        await toolManager.RegisterToolAsync(readDocTool, cancellationToken);
    }
}

