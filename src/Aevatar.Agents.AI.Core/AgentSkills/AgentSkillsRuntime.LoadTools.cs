using Aevatar.Agents.AI.Tool.Abstractions;
using Aevatar.Agents.AI.Tool.Tools.CustomTools;
using Google.Protobuf;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.AI.Core;

internal sealed partial class AgentSkillsRuntime
{
    internal async Task<IMessage> ExecuteSkillsListToolAsync(
        bool enabled,
        string agentType,
        Dictionary<string, object> parameters,
        ToolExecutionContext? executionContext,
        CancellationToken cancellationToken)
    {
        await Task.CompletedTask;

        var roots = GetEffectiveRoots();
        var skills = DiscoverAgentSkills(roots, cancellationToken);
        var maxResults = ClampInt(parameters.GetValueOrDefault("max_results"), fallback: 30, min: 1, max: 200);

        var returned = skills
            .Take(maxResults)
            .Select(s => new
            {
                name = s.Name,
                description = s.Description,
                allowedTools = s.AllowedTools,
                path = s.DirectoryPath,
                hasDotNetTools = s.DotNetToolFiles.Count > 0
            })
            .ToList();

        return ToStruct(new
        {
            success = true,
            enabled,
            roots,
            count = skills.Count,
            returned = returned.Count,
            truncated = returned.Count < skills.Count,
            skills = returned,
            note = returned.Count < skills.Count
                ? "Output truncated. Prefer find_helpful_skills for task-driven work, or use list_skills for full inventory (debug)."
                : null
        });
    }

    internal async Task<IMessage> ExecuteSkillsLoadToolAsync(
        string agentType,
        Dictionary<string, object> parameters,
        ToolExecutionContext? executionContext,
        int defaultMaxChars,
        bool defaultRegisterTools,
        CancellationToken cancellationToken)
    {
        var name = parameters.TryGetValue("name", out var nameObj)
            ? nameObj?.ToString()
            : null;

        if (string.IsNullOrWhiteSpace(name))
            return ToStruct(new { success = false, error = "Parameter 'name' is required." });

        if (executionContext?.ToolManager == null)
            return ToStruct(new { success = false, error = "ToolExecutionContext.ToolManager not provided." });

        var registerTools = defaultRegisterTools;
        if (parameters.TryGetValue("register_tools", out var rt))
        {
            if (rt is bool b) registerTools = b;
            else if (bool.TryParse(rt?.ToString(), out var parsed)) registerTools = parsed;
        }

        // Default: align with Hook/Harness tool output budget to avoid duplicated "max chars" knobs.
        // NOTE: callers can still override per-call via tool parameter `max_chars`.
        var maxChars = defaultMaxChars;
        if (parameters.TryGetValue("max_chars", out var mc))
        {
            if (mc is int i) maxChars = i;
            else if (int.TryParse(mc?.ToString(), out var parsed)) maxChars = parsed;
        }

        maxChars = Math.Clamp(maxChars, 1_000, 128_000);

        var roots = GetEffectiveRoots();
        var skills = DiscoverAgentSkills(roots, cancellationToken);
        var match = skills.FirstOrDefault(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                    ?? skills.FirstOrDefault(s => s.FolderName.Equals(name, StringComparison.OrdinalIgnoreCase));

        if (match == null)
        {
            return ToStruct(new
            {
                success = false,
                error = $"Skill '{name}' not found.",
                availableCount = skills.Count,
                available = skills.Select(s => s.Name).Take(50).ToArray(),
                hint = "Use find_helpful_skills to search, or list_skills for full inventory (debug)."
            });
        }

        var skillMarkdown = await ReadAllTextWithLimitAsync(match.SkillFilePath, maxChars, cancellationToken);
        var parsedSkill = ParseSkillMarkdown(skillMarkdown);

        var registered = new List<string>();
        var skipped = new List<object>();

        if (registerTools && match.DotNetToolFiles.Count > 0)
        {
            // Dotnet-file tool import == local code execution. Guard it explicitly.
            // We keep skills_load non-dangerous for usability, but importing executable tools still requires opt-in.
            if (executionContext.AllowDangerousTools == false)
            {
                foreach (var toolFile in match.DotNetToolFiles)
                {
                    skipped.Add(new
                    {
                        file = toolFile,
                        error =
                            "Dotnet-file tool import is disabled by policy (AllowDangerousTools=false). Set AllowDangerousTools=true to enable register_tools."
                    });
                }

                registerTools = false;
            }
        }

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

        return ToStruct(new
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
        });
    }
}


