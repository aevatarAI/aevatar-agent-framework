using System.Diagnostics;
using Aevatar.Agents.AI;
using Aevatar.Agents.AI.Abstractions;
using Aevatar.Agents.AI.Core;
using Aevatar.Agents.AI.Tool.Abstractions;
using Aevatar.Agents.AI.Tool.MCP;
using Aevatar.Agents.AI.Tool.MCP.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace MCPToolDemo;

/// <summary>
/// An agent that demonstrates MCP tool integration.
/// </summary>
public class MCPAgent : AIGAgentBase
{
    private readonly IConfiguration _configuration;

    public MCPAgent(IConfiguration configuration) : base()
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));

        // Base prompt; AIGAgentBase will append the available tool list automatically.
        SystemPrompt =
            "You are a helpful AI assistant.\n" +
            "When interacting with MCP servers (filesystem, Context7, etc.), prefer invoking the relevant tool before responding.";
    }

    protected override async Task RegisterToolsAsync(CancellationToken cancellationToken = default)
    {
        Logger?.LogInformation("🔧 Starting tool registration...");

        var context7Config = _configuration.GetSection("Context7");
        var context7McpUrl = context7Config["McpUrl"];
        var context7ApiUrl = context7Config["ApiUrl"];
        var context7ApiKey = context7Config["ApiKey"];

        // 0. Register local tool
        await RegisterToolAsync(new TimeTool(), Logger, cancellationToken);
        Logger?.LogInformation("✅ Registered local tool: TimeTool");

        // 1. Stdio (Docker) - Filesystem Server
        try
        {
            Logger?.LogInformation("🐳 Attempting to connect to Docker MCP Server...");
            var dockerConfig = MCPServerConfig.CreateDockerConfig(
                imageName: "mcp/server-filesystem:latest", // Ensure you have this image or similar
                args: new List<string> { "/tmp" },
                name: "Docker Filesystem MCP"
            );
            // Note: Docker execution requires docker installed and running
            var dockerClient = await MCPClientWrapper.CreateAsync(dockerConfig, Logger);
            await ToolManager.RegisterMCPServerAsync("docker-fs", dockerClient, dockerConfig, Logger);
            Logger?.LogInformation("✅ Registered Docker MCP Server");
        }
        catch (Exception ex)
        {
            Logger?.LogWarning("⚠️ Skipping Docker MCP demo: {Message}", ex.Message);
        }

        // 2. Custom Stream - Manual Process Management (Context7 docker)
        try
        {
            if (string.IsNullOrWhiteSpace(context7ApiKey))
            {
                Logger?.LogInformation("ℹ️ Context7 API key not configured, skipping stream-based demo.");
            }
            else
            {
                Logger?.LogInformation("🔄 Attempting to connect to Stream-based MCP Server (via Context7 docker)...");

                var processStartInfo = new ProcessStartInfo
                {
                    FileName = "docker",
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                processStartInfo.ArgumentList.Add("run");
                processStartInfo.ArgumentList.Add("-i");
                processStartInfo.ArgumentList.Add("--rm");
                processStartInfo.ArgumentList.Add("context7-mcp");
                processStartInfo.Environment["CONTEXT7_API_KEY"] = context7ApiKey;
                if (!string.IsNullOrWhiteSpace(context7ApiUrl))
                {
                    processStartInfo.Environment["CONTEXT7_API_URL"] = context7ApiUrl;
                }

                var process = Process.Start(processStartInfo);
                if (process != null)
                {
                    if (process.HasExited)
                    {
                        var errorOutput = process.StandardError?.ReadToEnd() ?? "No error output";
                        throw new InvalidOperationException(
                            $"Stream MCP docker exited immediately. Output: {errorOutput}");
                    }

                    var streamConfig = new MCPServerConfig
                    {
                        Name = "Context7 Stream MCP",
                        TransportType = MCPTransportType.Stream,
                        TimeoutMs = 10000 // Short timeout for demo
                    };

                    var stdoutStream = process.StandardOutput.BaseStream;
                    var stdinStream = process.StandardInput.BaseStream;

                    Logger?.LogDebug("Process stdout readable: {Readable}, stdin writable: {Writable}",
                        stdoutStream.CanRead,
                        stdinStream.CanWrite);

                    if (!stdinStream.CanWrite)
                    {
                        var errorOutput = process.HasExited
                            ? process.StandardError?.ReadToEnd() ?? "No error output"
                            : "Process still running";
                        throw new InvalidOperationException(
                            $"Stream MCP stdin stream is not writable. Details: {errorOutput}");
                    }

                    var streamClient = await MCPClientWrapper.CreateFromStreamsAsync(
                        stdoutStream,
                        stdinStream,
                        streamConfig,
                        Logger
                    );

                    await ToolManager.RegisterMCPServerAsync("stream-context7", streamClient, streamConfig, Logger);
                    Logger?.LogInformation("✅ Registered Context7 Stream MCP Server");
                }
            }
        }
        catch (Exception ex)
        {
            Logger?.LogWarning("⚠️ Skipping Stream MCP demo: {Message}", ex.Message);
        }

        // 3. Context7 HTTP MCP Server
        try
        {
            if (!string.IsNullOrWhiteSpace(context7McpUrl) && !string.IsNullOrWhiteSpace(context7ApiKey))
            {
                Logger?.LogInformation("🌐 Attempting to connect to Context7 MCP Server...");
                
                var httpConfig = MCPServerConfig.CreateHttpConfig(
                    serverUrl: context7McpUrl,
                    name: "Context7 MCP",
                    authToken: context7ApiKey
                );
                
                var httpClient = await MCPClientWrapper.CreateAsync(httpConfig, Logger);
                await ToolManager.RegisterMCPServerAsync("context7", httpClient, httpConfig, Logger);
                Logger?.LogInformation("✅ Registered Context7 MCP Server");
            }
            else
            {
                Logger?.LogInformation("ℹ️ Context7 configuration not found, skipping HTTP MCP demo.");
            }
        }
        catch (Exception ex)
        {
            Logger?.LogWarning("⚠️ Skipping Context7 MCP demo: {Message}", ex.Message);
        }

        var tools = await GetRegisteredToolsAsync();
        Logger?.LogInformation("🎉 Tool registration complete! Total tools: {Count}", tools.Count);
    }
}
