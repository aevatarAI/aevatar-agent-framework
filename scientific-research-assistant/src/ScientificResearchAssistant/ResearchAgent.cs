using Aevatar.Agents.AI.Abstractions;
using Aevatar.Agents.AI.Core;
using Aevatar.Agents.AI.WithTool.Abstractions;
using Aevatar.Agents.AI.WithTool.MCP;
using Aevatar.Agents.AI.WithTool.MCP.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ScientificResearchAssistant.Streaming;

namespace ScientificResearchAssistant;

public class ResearchAgent : AIGAgentBase
{
    private const string DefaultMcpServerUrl = "https://mcp.k-dense.ai/claude-scientific-skills/mcp";
    private readonly Microsoft.Extensions.Configuration.IConfiguration _configuration;

    public ResearchAgent(Microsoft.Extensions.Configuration.IConfiguration configuration)
    {
        _configuration = configuration;

        // Default: keep chat stateful + compacted (bounded) so AG-UI can build snapshots on reconnect.
        EnableChatHistoryInState = true;
        EnableChatHistoryCompaction = true;
        ChatHistoryMaxMessages = 40;
        ChatHistorySummaryMaxChars = 6000;

        SystemPrompt = "You are an advanced Scientific Research Assistant.\n" +
                       "You have access to a wide range of scientific tools via the Claude Scientific Skills MCP server.\n" +
                       "Use these tools to assist with literature search, bioinformatics analysis, chemical informatics, and more.\n" +
                       "Always cite your sources when performing literature reviews.\n" +
                       "When analyzing data, explain your methodology clearly.\n" +
                       "If a tool fails, explain why and suggest alternatives.";
    }

    protected override IAevatarToolManager CreateToolManager()
    {
        // Wrap default tool manager to emit tool progress events into current AG-UI stream (best-effort).
        return new ResearchToolManager(base.CreateToolManager());
    }

    /// <summary>
    /// Reconnect MCP server and re-register tools (best-effort).
    /// <para/>
    /// WHY:
    /// - MCP HTTP server can be flaky at startup.
    /// - Tool initialization runs once per agent lifetime; if MCP failed once, we need a manual retry hook.
    /// </summary>
    public async Task<(bool Ok, string? Error)> ReconnectMcpAsync(CancellationToken ct = default)
    {
        try
        {
            await InitializeToolsAsync(ct);

            var ok = await TryRegisterScientificSkillsAsync(ct);
            await RefreshToolCachesAsync(ct);

            return ok
                ? (true, null)
                : (false, "MCP reconnect failed (see server logs for details).");
        }
        catch (Exception ex)
        {
            Logger?.LogWarning(ex, "MCP reconnect failed: {Message}", ex.Message);
            return (false, ex.Message);
        }
    }

    private async Task<bool> TryRegisterScientificSkillsAsync(CancellationToken cancellationToken)
    {
        try
        {
            var mcpSection = _configuration.GetSection("MCP");
            var type = mcpSection["Type"] ?? "Http";
            var timeoutVal = mcpSection.GetValue<int?>("RequestTimeoutMs") ?? 300000;

            MCPServerConfig config;
            if (type.Equals("Docker", StringComparison.OrdinalIgnoreCase))
            {
                var image = mcpSection["DockerImage"] ?? "ghcr.io/k-dense-ai/claude-scientific-skills:latest";
                Logger?.LogInformation("🐳 Configuring Docker MCP Server: {Image}", image);
                config = MCPServerConfig.CreateDockerConfig(image, "Scientific Skills (Docker)");
            }
            else
            {
                var url = mcpSection["HttpUrl"] ?? DefaultMcpServerUrl;
                Logger?.LogInformation("🌐 Configuring HTTP MCP Server: {Url}", url);
                config = MCPServerConfig.CreateHttpConfig(url, name: "Scientific Skills (HTTP)");
            }

            config.TimeoutMs = timeoutVal;

            var client = await MCPClientWrapper.CreateAsync(config, Logger, cancellationToken);
            await ToolManager.RegisterMCPServerAsync("scientific-skills", client, config, Logger, cancellationToken);

            Logger?.LogInformation("✅ Successfully registered Claude Scientific Skills!");
            return true;
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "❌ Failed to register scientific skills. Check configuration and connectivity.");
            return false;
        }
    }

    protected override async Task RegisterToolsAsync(CancellationToken cancellationToken = default)
    {
        Logger?.LogInformation("🧪 Initializing Scientific Research Agent Tools...");

        // Keep framework built-ins (state query / event publisher / memory / skills tools, etc.)
        await base.RegisterToolsAsync(cancellationToken);

        await TryRegisterScientificSkillsAsync(cancellationToken);
        
        // Log available tools
        var tools = await GetRegisteredToolsAsync();
        Logger?.LogInformation("📚 Available Tools: {Count}", tools.Count);
        if (Logger?.IsEnabled(LogLevel.Debug) == true)
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
