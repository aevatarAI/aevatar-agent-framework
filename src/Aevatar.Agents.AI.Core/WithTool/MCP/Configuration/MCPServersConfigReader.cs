using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;

namespace Aevatar.Agents.AI.WithTool.MCP.Configuration;

/// <summary>
/// Read Cursor-style MCP servers config from <see cref="IConfiguration"/> and convert it into runtime-ready
/// <see cref="MCPServerConfig"/> objects.
/// <para/>
/// 中文 + ASCII:
/// - Keep parsing logic in framework layer so apps don't re-implement MCP wiring.
/// - Follow Cursor's "mcpServers" map shape, but keep additional global knobs under "MCP".
/// - Best-effort: if config is missing or malformed, return empty list (caller can noop).
/// </summary>
public static class MCPServersConfigReader
{
    public sealed class Resolved
    {
        public string Source { get; init; } = "none";
        public bool AutoConnect { get; init; } = true;
        public bool NamespaceTools { get; init; } = true;
        public IReadOnlyList<ResolvedServer> Servers { get; init; } = Array.Empty<ResolvedServer>();
    }

    public sealed class ResolvedServer
    {
        public required string Key { get; init; }
        public required bool Enabled { get; init; }
        public required MCPServerConfig Config { get; init; }
        public string ToolNamePrefix { get; init; } = string.Empty;
    }

    /// <summary>
    /// Resolve MCP servers configuration.
    /// Supports:
    /// - "MCP:mcpServers" (recommended for Aevatar apps)
    /// - "mcpServers" (raw Cursor config file)
    /// </summary>
    public static Resolved Resolve(IConfiguration? configuration)
    {
        if (configuration == null)
            return new Resolved();

        // Prefer "MCP:mcpServers" (so we can also read MCP:autoConnect, MCP:namespaceTools).
        var mcpSection = configuration.GetSection(MCPServersOptions.SectionName);
        var serversSection = mcpSection.GetSection(MCPServersOptions.ServersKey);
        var source = "none";

        if (HasChildren(serversSection))
        {
            source = $"{MCPServersOptions.SectionName}:{MCPServersOptions.ServersKey}";
        }
        else
        {
            // Fallback: raw Cursor config file root.
            serversSection = configuration.GetSection(MCPServersOptions.ServersKey);
            if (HasChildren(serversSection))
                source = MCPServersOptions.ServersKey;
        }

        if (!HasChildren(serversSection))
            return new Resolved();

        var autoConnect = ReadBool(mcpSection["autoConnect"], defaultValue: true);
        var namespaceTools = ReadBool(mcpSection["namespaceTools"], defaultValue: true);

        var servers = new List<ResolvedServer>();
        foreach (var s in serversSection.GetChildren())
        {
            var key = (s.Key ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(key))
                continue;

            var enabled = ReadBool(s["enabled"], defaultValue: true);
            if (!enabled)
            {
                servers.Add(new ResolvedServer
                {
                    Key = key,
                    Enabled = false,
                    Config = new MCPServerConfig { Name = s["name"] ?? key },
                    ToolNamePrefix = string.Empty
                });
                continue;
            }

            var name = (s["name"] ?? key).Trim();
            var timeoutMs = ReadInt(s["timeoutMs"], defaultValue: 120000);

            // Cursor-style fields
            var url = (s["url"] ?? s["serverUrl"] ?? string.Empty).Trim();
            var command = (s["command"] ?? string.Empty).Trim();
            var transportHint = (s["transport"] ?? string.Empty).Trim(); // optional: "stdio" | "http"

            var cfg = TryBuildConfig(name, timeoutMs, url, command, s);
            if (cfg == null)
            {
                // best-effort: ignore malformed entries
                continue;
            }

            // tool name prefix
            var explicitPrefix = (s["toolNamePrefix"] ?? string.Empty).Trim();
            var prefix = !string.IsNullOrWhiteSpace(explicitPrefix)
                ? explicitPrefix
                : (namespaceTools ? BuildDefaultToolPrefix(key) : string.Empty);

            servers.Add(new ResolvedServer
            {
                Key = key,
                Enabled = true,
                Config = cfg,
                ToolNamePrefix = prefix
            });
        }

        return new Resolved
        {
            Source = source,
            AutoConnect = autoConnect,
            NamespaceTools = namespaceTools,
            Servers = servers
        };
    }

    private static MCPServerConfig? TryBuildConfig(
        string name,
        int timeoutMs,
        string url,
        string command,
        IConfigurationSection section)
    {
        // 1) HTTP (SSE)
        if (!string.IsNullOrWhiteSpace(url) ||
            string.Equals(section["transport"], "http", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(url))
                return null;

            var headers = ReadDictionary(section.GetSection("headers"));
            var token = (section["authToken"] ?? section["token"] ?? string.Empty).Trim();

            var cfg = MCPServerConfig.CreateHttpConfig(
                serverUrl: url,
                authToken: string.IsNullOrWhiteSpace(token) ? null : token,
                name: name,
                headers: headers.Count > 0 ? headers : null);

            cfg.TimeoutMs = Math.Clamp(timeoutMs, 1000, 10 * 60_000);
            return cfg;
        }

        // 2) Stdio
        if (!string.IsNullOrWhiteSpace(command) ||
            string.Equals(section["transport"], "stdio", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(command))
                return null;

            var args = ReadStringList(section.GetSection("args"));
            if (args.Count == 0)
            {
                // Support alias "arguments" (non-cursor but common)
                args = ReadStringList(section.GetSection("arguments"));
            }

            var env = ReadDictionary(section.GetSection("env"));
            if (env.Count == 0)
            {
                // Support alias "environment" (non-cursor but common)
                env = ReadDictionary(section.GetSection("environment"));
            }

            var cwd = (section["cwd"] ?? section["workingDirectory"] ?? string.Empty).Trim();

            var cfg = MCPServerConfig.CreateStdioConfig(
                command: command,
                args: args,
                name: name,
                env: env.Count > 0 ? env : null,
                workingDirectory: string.IsNullOrWhiteSpace(cwd) ? null : cwd);

            cfg.TimeoutMs = Math.Clamp(timeoutMs, 1000, 10 * 60_000);
            return cfg;
        }

        return null;
    }

    // ------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------

    private static bool HasChildren(IConfigurationSection section) => section.GetChildren().Any();

    private static bool ReadBool(string? raw, bool defaultValue)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return defaultValue;

        return bool.TryParse(raw, out var v) ? v : defaultValue;
    }

    private static int ReadInt(string? raw, int defaultValue)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return defaultValue;

        return int.TryParse(raw, out var v) ? v : defaultValue;
    }

    private static List<string> ReadStringList(IConfigurationSection section)
    {
        var list = new List<string>();
        foreach (var child in section.GetChildren())
        {
            var v = (child.Value ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(v))
                list.Add(v);
        }

        return list;
    }

    private static Dictionary<string, string> ReadDictionary(IConfigurationSection section)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var child in section.GetChildren())
        {
            var k = (child.Key ?? string.Empty).Trim();
            var v = (child.Value ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(k) || string.IsNullOrWhiteSpace(v))
                continue;
            dict[k] = v;
        }

        return dict;
    }

    private static readonly Regex UnsafeChars = new(@"[^a-zA-Z0-9_-]+", RegexOptions.Compiled);

    private static string BuildDefaultToolPrefix(string serverKey)
    {
        // Keep function-name friendly: OpenAI-like providers often restrict to [a-zA-Z0-9_-].
        var safe = UnsafeChars.Replace(serverKey.Trim(), "_");
        if (string.IsNullOrWhiteSpace(safe))
            safe = "server";
        return $"mcp__{safe}__";
    }
}


