using System.Text;
using System.Text.RegularExpressions;
using Aevatar.Agents.AI.Tool.Abstractions;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.AI.Core;

public abstract partial class AIGAgentBase
{
    // ============================================================
    //  Agent Skills: dynamic search + document read (no eager load)
    //
    //  Goal:
    //  - Avoid dumping a full skill inventory into LLM context every turn.
    //  - Provide a "search first" tool that returns only a few candidates.
    //  - Provide pattern-based document reading (scripts/references/assets) without executing code.
    // ============================================================

    private async Task RegisterAgentSkillsDynamicReadToolsAsync(CancellationToken cancellationToken = default)
    {
        if (!EnableAgentSkills)
            return;

        EnsureToolManagerInitialized();

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
        // Some models occasionally hallucinate this tool name. Keep an alias so we fail soft.
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

        // list_skills (debug/exploration)
        var listTool = new ToolDefinition
        {
            Name = "list_skills",
            Description =
                "List the full inventory of available skills (debug/exploration). For task-driven work, prefer find_helpful_skills.",
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
                        Description = "Glob pattern relative to skill root, e.g. 'scripts/*.py' or 'references/**/*.md'"
                    },
                    ["max_files"] = new()
                    {
                        Type = "integer",
                        Required = false,
                        Description = "Max files to return (default: 6; range 1..20)",
                        DefaultValue = 6
                    },
                    ["max_chars_per_file"] = new()
                    {
                        Type = "integer",
                        Required = false,
                        Description = "Max chars per file (default: 8000; range 200..50000)",
                        DefaultValue = 8000
                    },
                    ["total_max_chars"] = new()
                    {
                        Type = "integer",
                        Required = false,
                        Description = "Total output char budget for all files combined (default: 16000; range 1000..200000)",
                        DefaultValue = 16000
                    },
                    ["restrict_to_resource_dirs"] = new()
                    {
                        Type = "boolean",
                        Required = false,
                        Description = "If true, only allow reading under scripts/, references/, assets/ (default: true).",
                        DefaultValue = true
                    }
                },
                Required = new[] { "name", "pattern" }
            },
            ExecuteAsync = async (parameters, executionContext, ct) =>
                await ExecuteReadSkillDocumentToolAsync(parameters, executionContext, ct)
        };

        await ToolManager.RegisterToolAsync(findTool, cancellationToken);
        await ToolManager.RegisterToolAsync(findAllsAliasTool, cancellationToken);
        await ToolManager.RegisterToolAsync(listTool, cancellationToken);
        await ToolManager.RegisterToolAsync(readDocTool, cancellationToken);
    }

    private async Task<IMessage> ExecuteListSkillsToolAsync(
        ToolExecutionContext? executionContext,
        CancellationToken cancellationToken)
    {
        return await AgentSkillsRuntime.ExecuteListSkillsToolAsync(executionContext, cancellationToken);
    }

    private async Task<IMessage> ExecuteFindHelpfulSkillsToolAsync(
        Dictionary<string, object> parameters,
        ToolExecutionContext? executionContext,
        CancellationToken cancellationToken)
    {
        return await AgentSkillsRuntime.ExecuteFindHelpfulSkillsToolAsync(parameters, executionContext, cancellationToken);
    }

    private async Task<IMessage> ExecuteReadSkillDocumentToolAsync(
        Dictionary<string, object> parameters,
        ToolExecutionContext? executionContext,
        CancellationToken cancellationToken)
    {
        return await AgentSkillsRuntime.ExecuteReadSkillDocumentToolAsync(parameters, executionContext, cancellationToken);
    }

    // NOTE:
    // Helper functions for skill search and file reading were moved into AgentSkillsRuntime to keep AIGAgentBase thin.
}


