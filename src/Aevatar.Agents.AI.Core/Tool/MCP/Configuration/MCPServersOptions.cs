using System.Collections.Generic;

namespace Aevatar.Agents.AI.Tool.MCP.Configuration;

/// <summary>
/// Cursor-style MCP servers configuration.
/// <para/>
/// JSON shape (Cursor-like):
/// <code>
/// {
///   "MCP": {
///     "autoConnect": true,
///     "namespaceTools": true,
///     "mcpServers": {
///       "scientific-skills": { "url": "https://...", "timeoutMs": 300000 }
///     }
///   }
/// }
/// </code>
/// <para/>
/// Also supported (raw Cursor config file):
/// <code>
/// { "mcpServers": { "github": { "command": "npx", "args": ["-y", "..."] } } }
/// </code>
/// </summary>
public sealed class MCPServersOptions
{
    public const string SectionName = "MCP";
    public const string ServersKey = "mcpServers";

    /// <summary>
    /// Whether framework should auto-connect and register MCP servers at tool initialization time.
    /// Default: true (best-effort; if not configured, it's a no-op).
    /// </summary>
    public bool AutoConnect { get; set; } = true;

    /// <summary>
    /// Minimum interval (seconds) between MCP reconnect attempts triggered by chat/session activity.
    /// <para/>
    /// Default: 30 seconds.
    /// <para/>
    /// Notes:
    /// - Set to 0 to disable throttling (more aggressive; may spam when server is down).
    /// </summary>
    public int RetryMinIntervalSeconds { get; set; } = 30;

    /// <summary>
    /// Whether to namespace MCP tool names with a stable prefix.
    /// <para/>
    /// WHY:
    /// - MCP tool names are only unique per-server.
    /// - When multiple servers are configured, collisions are possible.
    /// - Namespacing makes collisions impossible and makes provenance explicit.
    /// <para/>
    /// Default prefix format: <c>mcp__{serverKey}__{toolName}</c>
    /// </summary>
    public bool NamespaceTools { get; set; } = true;

    /// <summary>
    /// Cursor-style server map: key is server alias, value is server config.
    /// </summary>
    public Dictionary<string, MCPServerEntryOptions> McpServers { get; set; } = new();
}

/// <summary>
/// A single MCP server entry in Cursor-style configuration.
/// </summary>
public sealed class MCPServerEntryOptions
{
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Optional display name (purely informational).
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// Optional transport hint ("stdio" / "http").
    /// Usually inferred from fields (<c>url</c> vs <c>command</c>).
    /// </summary>
    public string? Transport { get; set; }

    // ---- Stdio transport (Cursor-style) ----
    public string? Command { get; set; }
    public List<string>? Args { get; set; }
    public Dictionary<string, string>? Env { get; set; }
    public string? Cwd { get; set; }

    // ---- HTTP transport (Cursor-style) ----
    public string? Url { get; set; }
    public string? AuthToken { get; set; }
    public Dictionary<string, string>? Headers { get; set; }

    // ---- Common ----
    public int? TimeoutMs { get; set; }

    /// <summary>
    /// Optional override for tool name prefix.
    /// If not set, framework uses <c>mcp__{serverKey}__</c> when <see cref="MCPServersOptions.NamespaceTools"/> is true.
    /// </summary>
    public string? ToolNamePrefix { get; set; }
}


