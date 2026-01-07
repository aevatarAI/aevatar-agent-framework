using Aevatar.Agents.AI.Core;
using Aevatar.Agents.AI.WithTool.MCP;
using Aevatar.Agents.AI.WithTool.MCP.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SkillsMCPUnifiedDemo.Tools;

namespace SkillsMCPUnifiedDemo;

/// <summary>
/// A single agent that demonstrates:
/// - dotnet-file tools (skills/*.cs)
/// - Agent Skills (agent_skills/**/SKILL.md) via skills_list/skills_load
/// - MCP tools (docker filesystem / context7) best-effort
/// </summary>
public sealed class UnifiedAgent : AIGAgentBase
{
    private readonly IConfiguration _configuration;

    public UnifiedAgent(IConfiguration configuration)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));

        // Agent Skills
        EnableAgentSkills = true;
        AddAgentSkillsRoot(Path.Combine(AppContext.BaseDirectory, "agent_skills"));

        SystemPrompt =
            "You are a helpful AI assistant.\n" +
            "If a relevant Agent Skill exists, you MUST call find_helpful_skills first, then skills_load before acting.\n" +
            "For runtime/system information, prefer dotnet-file tools.\n" +
            "For external context, prefer MCP tools when available.";
    }

    public override Task<string> GetDescriptionAsync() => Task.FromResult("SkillsMCPUnifiedDemoAgent");

    protected override async Task RegisterToolsAsync(CancellationToken cancellationToken = default)
    {
        await base.RegisterToolsAsync(cancellationToken);

        // Local (in-proc) AI tools
        await RegisterToolAsync(new TextStatsTool(), cancellationToken: cancellationToken);
        await RegisterToolAsync(new JsonPrettifyTool(), cancellationToken: cancellationToken);
        await RegisterToolAsync(new SlugifyTool(), cancellationToken: cancellationToken);

        // file-based tools (skills/*.cs + skills/*.py)
        // - Only files that contain the aevatar_tool manifest marker will be registered.
        // - This keeps the demo clean: drop a tool file into /skills and it becomes available automatically.
        var skillsDir = Path.Combine(AppContext.BaseDirectory, "skills");
        await RegisterFileSkillsFromDirectoryAsync(
            skillsDir,
            includeDotNet: true,
            includePython: true,
            searchOption: SearchOption.TopDirectoryOnly,
            maxFilesPerType: 64,
            requireManifestMarker: true,
            cancellationToken: cancellationToken);

        // MCP servers (best-effort)
        await RegisterMcpServersBestEffortAsync(cancellationToken);
    }

    private async Task RegisterMcpServersBestEffortAsync(CancellationToken cancellationToken)
    {
        // 1) Docker filesystem MCP (optional)
        try
        {
            // NOTE:
            // - We mount AEVATAR_DEMO_ROOT (or current dir) into the container so the filesystem MCP is actually useful.
            // - If docker is not installed, we'll just skip (best-effort).
            var hostRoot = Environment.GetEnvironmentVariable("AEVATAR_DEMO_ROOT");
            if (string.IsNullOrWhiteSpace(hostRoot))
            {
                hostRoot = Directory.GetCurrentDirectory();
            }

            var dockerArgs = new List<string>
            {
                "run", "-i", "--rm",
                "-v", $"{hostRoot}:/workspace",
                "mcp/server-filesystem:latest",
                "/workspace"
            };

            var dockerConfig = MCPServerConfig.CreateStdioConfig(
                command: "docker",
                args: dockerArgs,
                name: "Docker Filesystem MCP");

            var dockerClient = await MCPClientWrapper.CreateAsync(dockerConfig, Logger, cancellationToken);
            await ToolManager.RegisterMCPServerAsync("docker-fs", dockerClient, dockerConfig, Logger, cancellationToken);
        }
        catch (Exception ex)
        {
            Logger?.LogWarning("Skipping Docker filesystem MCP (optional): {Message}", ex.Message);
        }

        // 2) Context7 HTTP MCP (optional)
        try
        {
            var context7 = _configuration.GetSection("Context7");
            var mcpUrl = context7["McpUrl"];
            var apiKey = context7["ApiKey"];
            var apiUrl = context7["ApiUrl"]; // optional, may be used by some deployments

            if (string.IsNullOrWhiteSpace(mcpUrl) || string.IsNullOrWhiteSpace(apiKey))
            {
                Logger?.LogInformation("Context7 not configured, skipping Context7 MCP.");
                return;
            }

            var headers = new Dictionary<string, string>();
            if (!string.IsNullOrWhiteSpace(apiUrl))
            {
                headers["CONTEXT7_API_URL"] = apiUrl;
            }

            var httpConfig = MCPServerConfig.CreateHttpConfig(
                serverUrl: mcpUrl,
                authToken: apiKey,
                name: "Context7 MCP",
                headers: headers.Count > 0 ? headers : null
            );

            var httpClient = await MCPClientWrapper.CreateAsync(httpConfig, Logger, cancellationToken);
            await ToolManager.RegisterMCPServerAsync("context7", httpClient, httpConfig, Logger, cancellationToken);
        }
        catch (Exception ex)
        {
            Logger?.LogWarning("Skipping Context7 MCP (optional): {Message}", ex.Message);
        }

        // 3) GitHub MCP via npx (optional)
        // Set env var GITHUB_TOKEN to enable.
        try
        {
            var token = Environment.GetEnvironmentVariable("GITHUB_TOKEN");
            if (string.IsNullOrWhiteSpace(token))
            {
                Logger?.LogInformation("GITHUB_TOKEN not set, skipping GitHub MCP.");
                return;
            }

            var env = new Dictionary<string, string> { ["GITHUB_TOKEN"] = token };
            var githubConfig = MCPServerConfig.CreateNpxConfig(
                packageName: "@modelcontextprotocol/server-github",
                name: "GitHub MCP",
                env: env);

            var githubClient = await MCPClientWrapper.CreateAsync(githubConfig, Logger, cancellationToken);
            await ToolManager.RegisterMCPServerAsync("github", githubClient, githubConfig, Logger, cancellationToken);
        }
        catch (Exception ex)
        {
            Logger?.LogWarning("Skipping GitHub MCP (optional): {Message}", ex.Message);
        }
    }
}

