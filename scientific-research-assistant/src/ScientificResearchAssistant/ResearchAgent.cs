using Aevatar.Agents.AI.Abstractions;
using Aevatar.Agents.AI.Core;
using Aevatar.Agents.AI.WithTool.Abstractions;
using Microsoft.Extensions.Logging;
using ScientificResearchAssistant.Streaming;

namespace ScientificResearchAssistant;

public class ResearchAgent : AIGAgentBase
{
    public ResearchAgent()
    {
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
            "- Never fabricate tool results. Prefer executable verification when possible.";
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
