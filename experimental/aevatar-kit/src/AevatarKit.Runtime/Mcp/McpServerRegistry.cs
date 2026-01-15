using System.Collections.Concurrent;
using AevatarKit;
using Google.Protobuf.WellKnownTypes;

namespace AevatarKit.Runtime.Mcp;

public interface IMcpServerRegistry
{
    IReadOnlyList<McpServerDefinition> List();
    McpServerDefinition Create(CreateMcpServerRequest request);
    McpServerDefinition Update(string serverId, UpdateMcpServerRequest request);
}

/// <summary>
/// In-memory MCP server registry for MVP (management UI only).
/// No real connections are established in MVP.
/// </summary>
public sealed class InMemoryMcpServerRegistry : IMcpServerRegistry
{
    private readonly ConcurrentDictionary<string, McpServerDefinition> _servers = new();

    public InMemoryMcpServerRegistry()
    {
        // Seed example servers
        var now = Timestamp.FromDateTime(DateTime.UtcNow);

        var s1 = new McpServerDefinition
        {
            ServerId = Guid.NewGuid().ToString("N"),
            Name = "Local Toolpack (stdio)",
            Transport = McpServerTransport.Stdio,
            Command = "npx",
            Args = { "-y", "some-mcp-server" },
            Endpoint = "",
            Enabled = false,
            CreatedAt = now
        };

        var s2 = new McpServerDefinition
        {
            ServerId = Guid.NewGuid().ToString("N"),
            Name = "HTTP MCP Gateway",
            Transport = McpServerTransport.Http,
            Command = "",
            Endpoint = "http://localhost:8787/mcp",
            Enabled = false,
            CreatedAt = now
        };

        _servers[s1.ServerId] = s1;
        _servers[s2.ServerId] = s2;
    }

    public IReadOnlyList<McpServerDefinition> List()
    {
        return _servers.Values
            .OrderByDescending(s => s.CreatedAt?.ToDateTime() ?? DateTime.MinValue)
            .Select(s => s.Clone())
            .ToList();
    }

    public McpServerDefinition Create(CreateMcpServerRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var name = (request.Name ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("name is required");
        }

        var now = Timestamp.FromDateTime(DateTime.UtcNow);
        var s = new McpServerDefinition
        {
            ServerId = Guid.NewGuid().ToString("N"),
            Name = name,
            Transport = request.Transport,
            Command = request.Command ?? string.Empty,
            Endpoint = request.Endpoint ?? string.Empty,
            Enabled = request.Enabled,
            CreatedAt = now
        };
        s.Args.AddRange(request.Args);

        _servers[s.ServerId] = s;
        return s.Clone();
    }

    public McpServerDefinition Update(string serverId, UpdateMcpServerRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(serverId))
        {
            throw new ArgumentException("serverId is required");
        }

        var id = serverId.Trim();
        if (!_servers.TryGetValue(id, out var existing))
        {
            throw new KeyNotFoundException("server not found");
        }

        var name = (request.Name ?? existing.Name ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("name is required");
        }

        var s = existing.Clone();
        s.Name = name;
        s.Transport = request.Transport;
        s.Command = request.Command ?? string.Empty;
        s.Endpoint = request.Endpoint ?? string.Empty;
        s.Enabled = request.Enabled;

        s.Args.Clear();
        s.Args.AddRange(request.Args);

        _servers[id] = s;
        return s.Clone();
    }
}


