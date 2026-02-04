using Aevatar.Agents.AI.Abstractions;
using Aevatar.Agents.AI.Core;
using Aevatar.Agents.AI.Tool.Abstractions;
using Microsoft.Extensions.Logging;
using Aevatar.Agents.Cognitive.Streaming;
using VibeResearching.Vibe.Tools;

namespace VibeResearching;

public class ResearchAgent : AIGAgentBase
{
    private readonly IVibeGraphAccess? _graphAccess;

    public ResearchAgent(IVibeGraphAccess? graphAccess = null)
    {
        _graphAccess = graphAccess;

        // Default: keep chat stateful + compacted (bounded) so AG-UI can build snapshots on reconnect.
        EnableChatHistoryInState = true;
        EnableChatHistoryCompaction = true;
        ChatHistoryMaxMessages = 40;
        ChatHistorySummaryMaxChars = 6000;

        SystemPrompt =
            "You are an advanced Scientific Research Assistant.\n" +
            "\n" +
            "You may have two kinds of capabilities:\n" +
            "1) Agent Skills (SKILL.md) on disk (e.g. 'Claude Scientific Skills' pack).\n" +
            "   - For domain-specific tasks, you MUST call find_helpful_skills first (avoid dumping the full inventory into context).\n" +
            "   - Then call skills_load on the best candidate.\n" +
            "   - Skills may bundle scripts/references/assets.\n" +
            "     Use read_skill_document (pattern-based) to load only the documents you need.\n" +
            "     Use skills_run_python to execute scripts when needed (dangerous).\n" +
            "2) MCP tools (optional): MCP servers may be configured (Cursor-style mcpServers) and auto-connected.\n" +
            "\n" +
            "Rules:\n" +
            "- Always cite your sources when performing literature reviews.\n" +
            "- When analyzing data, explain your methodology clearly.\n" +
            "- If a tool fails, explain why and suggest alternatives.\n" +
            "- Never fabricate tool results. Prefer executable verification when possible.\n" +
            "\n" +
            "Planning:\n" +
            "- The research plan (milestones) is fixed at session start. Focus on executing the current milestone.\n" +
            "- Use get_research_plan to view the plan. Use update_plan_status to mark progress.\n" +
            "- If the user intent is ambiguous, ask a clarification question instead of writing.";
    }

    protected override IAevatarToolManager CreateToolManager()
    {
        // Wrap default tool manager to emit tool progress events into current AG-UI stream (best-effort).
        return new ResearchToolManager(base.CreateToolManager());
    }

    protected override async Task RegisterToolsAsync(CancellationToken cancellationToken = default)
    {
        Logger.LogInformation("🧪 Initializing Scientific Research Agent Tools...");

        // Keep framework built-ins (state query / event publisher / memory / skills tools, etc.)
        await base.RegisterToolsAsync(cancellationToken);

        // Plan tools: Only register read/status tools, NOT create tool.
        // Plan nodes are created during brief generation (milestones), not during research execution.
        // This ensures milestones remain fixed once the session starts.
        if (_graphAccess != null)
        {
            // Disabled: CreatePlanTool - we don't want agents creating new Plan nodes during execution
            // await RegisterToolAsync(new CreatePlanTool(_graphAccess), cancellationToken: cancellationToken);
            await RegisterToolAsync(new UpdatePlanStatusTool(_graphAccess), cancellationToken: cancellationToken);
            await RegisterToolAsync(new GetPlanTool(_graphAccess), cancellationToken: cancellationToken);
        }

        // Log available tools
        var tools = await GetRegisteredToolsAsync();
        Logger.LogInformation("📚 Available Tools: {Count}", tools.Count);
        if (Logger.IsEnabled(LogLevel.Debug))
        {
            foreach (var tool in tools)
            {
                Logger.LogDebug(" - {Name}: {Description}", tool.Name, tool.Description);
            }
        }
    }

    /// <summary>
    /// Ensure tools are initialized (may contact MCP server) and return the registered tool list.
    /// </summary>
    public async Task<IReadOnlyList<ToolDefinition>> ListToolsAsync(CancellationToken ct = default)
    {
        await InitializeToolsAsync(ct);
        return await GetRegisteredToolsAsync();
    }
}
