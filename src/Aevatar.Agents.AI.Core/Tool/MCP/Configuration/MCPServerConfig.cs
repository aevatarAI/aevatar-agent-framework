using System.Collections.Generic;

namespace Aevatar.Agents.AI.Tool.MCP.Configuration;

/// <summary>
/// Configuration for connecting to an MCP server.
/// Supports both remote servers (HTTP) and local servers (stdio via npx/uvx/docker).
/// </summary>
public class MCPServerConfig
{
    /// <summary>
    /// Server name for identification.
    /// </summary>
    public string Name { get; set; } = "MCP Server";

    /// <summary>
    /// Transport type (Stdio for local, Http for remote).
    /// </summary>
    public MCPTransportType TransportType { get; set; } = MCPTransportType.Stdio;

    // ===== Stdio Transport Configuration (for npx/uvx/docker) =====

    /// <summary>
    /// Command to execute (e.g., "npx", "uvx", "docker", "node").
    /// Required for Stdio transport.
    /// </summary>
    public string? Command { get; set; }

    /// <summary>
    /// Command-line arguments.
    /// Required for Stdio transport.
    /// </summary>
    public List<string>? Arguments { get; set; }

    /// <summary>
    /// Environment variables for the process.
    /// Optional for Stdio transport.
    /// </summary>
    public Dictionary<string, string>? Environment { get; set; }

    /// <summary>
    /// Working directory for the process.
    /// Optional for Stdio transport.
    /// </summary>
    public string? WorkingDirectory { get; set; }

    // ===== HTTP Transport Configuration (for remote servers) =====

    /// <summary>
    /// Server URL (e.g., "https://mcp.example.com").
    /// Required for Http transport.
    /// </summary>
    public string? ServerUrl { get; set; }

    /// <summary>
    /// Authentication token for HTTP requests.
    /// Optional for Http transport.
    /// </summary>
    public string? AuthToken { get; set; }

    /// <summary>
    /// Additional HTTP headers.
    /// Optional for Http transport.
    /// </summary>
    public Dictionary<string, string>? Headers { get; set; }

    // ===== Common Configuration =====

    /// <summary>
    /// Connection timeout in milliseconds.
    /// Default: 120000 (2 minutes) to allow for package installation/pulling.
    /// </summary>
    public int TimeoutMs { get; set; } = 120000;

    /// <summary>
    /// Maximum number of retry attempts.
    /// </summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>
    /// Whether to cache tool definitions.
    /// </summary>
    public bool CacheToolDefinitions { get; set; } = true;

    /// <summary>
    /// Cache expiration time in minutes.
    /// </summary>
    public int CacheExpirationMinutes { get; set; } = 60;

    /// <summary>
    /// Custom metadata for this server connection.
    /// </summary>
    public Dictionary<string, object>? Metadata { get; set; }

    // ===== Helper Methods =====

    /// <summary>
    /// Creates a configuration for a local MCP server via npx.
    /// </summary>
    public static MCPServerConfig CreateNpxConfig(
        string packageName,
        string? name = null,
        Dictionary<string, string>? env = null,
        List<string>? args = null)
    {
        var arguments = new List<string> { "-y", packageName };
        if (args != null) arguments.AddRange(args);

        return new MCPServerConfig
        {
            Name = name ?? $"MCP Server ({packageName})",
            TransportType = MCPTransportType.Stdio,
            Command = "npx",
            Arguments = arguments,
            Environment = env
        };
    }

    /// <summary>
    /// Creates a configuration for a local MCP server via uvx.
    /// </summary>
    public static MCPServerConfig CreateUvxConfig(
        string packageName,
        string? name = null,
        Dictionary<string, string>? env = null,
        List<string>? args = null)
    {
        var arguments = new List<string> { packageName };
        if (args != null) arguments.AddRange(args);

        return new MCPServerConfig
        {
            Name = name ?? $"MCP Server ({packageName})",
            TransportType = MCPTransportType.Stdio,
            Command = "uvx",
            Arguments = arguments,
            Environment = env
        };
    }

    /// <summary>
    /// Creates a configuration for a local MCP server via docker.
    /// </summary>
    public static MCPServerConfig CreateDockerConfig(
        string imageName,
        string? name = null,
        Dictionary<string, string>? env = null,
        List<string>? args = null)
    {
        var arguments = new List<string> { "run", "-i", "--rm" };

        // Pass environment variables to docker container
        if (env != null)
        {
            foreach (var kvp in env)
            {
                arguments.Add("-e");
                arguments.Add($"{kvp.Key}={kvp.Value}");
            }
        }

        arguments.Add(imageName);

        if (args != null) arguments.AddRange(args);

        return new MCPServerConfig
        {
            Name = name ?? $"MCP Server ({imageName})",
            TransportType = MCPTransportType.Stdio,
            Command = "docker",
            Arguments = arguments,
            Environment = null // Env vars are passed via -e flag to docker
        };
    }

    /// <summary>
    /// Creates a configuration for a generic local command.
    /// </summary>
    public static MCPServerConfig CreateStdioConfig(
        string command,
        List<string> args,
        string? name = null,
        Dictionary<string, string>? env = null,
        string? workingDirectory = null)
    {
        return new MCPServerConfig
        {
            Name = name ?? $"MCP Server ({command})",
            TransportType = MCPTransportType.Stdio,
            Command = command,
            Arguments = args,
            Environment = env,
            WorkingDirectory = workingDirectory
        };
    }

    /// <summary>
    /// Creates a configuration for a remote HTTP MCP server.
    /// </summary>
    public static MCPServerConfig CreateHttpConfig(
        string serverUrl,
        string? authToken = null,
        string? name = null,
        Dictionary<string, string>? headers = null)
    {
        return new MCPServerConfig
        {
            Name = name ?? $"MCP Server ({serverUrl})",
            TransportType = MCPTransportType.Http,
            ServerUrl = serverUrl,
            AuthToken = authToken,
            Headers = headers
        };
    }
}

/// <summary>
/// MCP transport type.
/// </summary>
public enum MCPTransportType
{
    /// <summary>
    /// Stdio transport for local servers (npx/uvx/docker).
    /// </summary>
    Stdio,

    /// <summary>
    /// HTTP transport for remote servers (SSE).
    /// </summary>
    Http,

    /// <summary>
    /// Custom stream transport (e.g. TCP, NamedPipe).
    /// </summary>
    Stream
}
