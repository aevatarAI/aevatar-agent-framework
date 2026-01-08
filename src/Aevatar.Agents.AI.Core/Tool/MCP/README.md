# MCP Tool Integration for Aevatar

Official Model Context Protocol (MCP) integration for Aevatar's tool system.

## Overview

This project integrates the **official MCP C# SDK** (`ModelContextProtocol.Core`) into Aevatar, enabling agents to use external tools from MCP servers. Supports both **local servers** (via npx/uvx) and remote servers (HTTP).

## Quick Start

### 1. Install Package

```bash
# MCP tool integration is included in Aevatar.Agents.AI.Core
dotnet add package Aevatar.Agents.AI.Core --version 1.0.0-alpha
```

### 2. Register MCP Tools

```csharp
using Aevatar.Agents.AI.Tool.MCP;

public class MyAgent : AIGAgentBase<MyState>
{
    protected override async Task RegisterToolsAsync(CancellationToken cancellationToken = default)
    {
        // Option 1: Via npx (simplest)
        await ToolManager.RegisterMCPServerViaNpxAsync(
            "@modelcontextprotocol/server-github"
        );

        // Option 2: Via uvx
        await ToolManager.RegisterMCPServerViaUvxAsync(
            "mcp-server-github"
        );

        // Option 3: Manual client creation (advanced)
        var config = MCPServerConfig.CreateNpxConfig("@modelcontextprotocol/server-github");
        var client = await MCPClientWrapper.CreateAsync(config);
        await ToolManager.RegisterMCPServerAsync("github", client);
    }
}
```

### 3. Use MCP Tools

Once registered, MCP tools are available alongside local tools:

```csharp
// Agent can now use GitHub MCP tools like:
// - create_issue
// - list_repositories
// - create_pull_request
// etc.
```

## Configuration

### Stdio Transport (Local Servers)

For local MCP servers via npx/uvx:

```csharp
var config = new MCPServerConfig
{
    TransportType = MCPTransportType.Stdio,
    Command = "npx",
    Arguments = ["-y", "@modelcontextprotocol/server-github"],
    Name = "GitHub MCP Server",
    Environment = new Dictionary<string, string>
    {
        ["GITHUB_TOKEN"] = "your-token"
    }
};
```

**Helper Methods**:
```csharp
// For npx
var config = MCPServerConfig.CreateNpxConfig(
    "@modelcontextprotocol/server-github",
    "GitHub Server"
);

// For uvx
var config = MCPServerConfig.CreateUvxConfig(
    "mcp-server-github",
    "GitHub Server"
);
```

### HTTP Transport (Remote Servers)

For remote MCP servers (coming soon):

```csharp
var config = MCPServerConfig.CreateHttpConfig(
    "https://mcp.example.com",
    authToken: "your-token",
    name: "Remote MCP Server"
);
```

## Extension Methods

### RegisterMCPServerViaNpxAsync

Simplest way to register an MCP server via npx:

```csharp
await ToolManager.RegisterMCPServerViaNpxAsync(
    "@modelcontextprotocol/server-github",
    serverName: "GitHub",
    logger: logger
);
```

### RegisterMCPServerViaUvxAsync

Register an MCP server via uvx (Python):

```csharp
await ToolManager.RegisterMCPServerViaUvxAsync(
    "mcp-server-github",
    serverName: "GitHub",
    logger: logger
);
```

### RegisterMCPServerAsync

Advanced registration with custom client:

```csharp
var config = MCPServerConfig.CreateNpxConfig("@modelcontextprotocol/server-github");
var client = await MCPClientWrapper.CreateAsync(config, logger);

await ToolManager.RegisterMCPServerAsync(
    "github",
    client,
    config,
    logger
);
```

## Architecture

```
AIGAgentBase
    ↓
AevatarToolManager (统一管理)
    ├── Local Tools
    ├── MCP Tools ← MCPToolAdapter
    └── Custom Tools
         ↓
    MCPClientWrapper
         ↓
    Official McpClient (ModelContextProtocol.Core)
         ↓
    MCP Server (stdio/http)
```

## Components

### MCPClientWrapper

Wraps the official `McpClient` from ModelContextProtocol.Core SDK.

**Features**:
- Stdio transport support (npx/uvx)
- Tool discovery (`ListToolsAsync`)
- Tool execution (`CallToolAsync`)
- Automatic disposal

### MCPToolAdapter

Converts MCP tools to Aevatar's `ToolDefinition` format.

**Features**:
- Schema conversion (MCP → Aevatar)
- Execution delegation
- Error handling
- Metadata preservation

### MCPServerConfig

Configuration for MCP server connections.

**Properties**:
- `TransportType`: Stdio or Http
- `Command`: "npx", "uvx", etc.
- `Arguments`: Package name and args
- `Environment`: Environment variables
- `ServerUrl`: For HTTP transport

## Examples

### GitHub MCP Server

```csharp
protected override async Task RegisterToolsAsync()
{
    await ToolManager.RegisterMCPServerViaNpxAsync(
        "@modelcontextprotocol/server-github"
    );
    
    // Now can use: create_issue, list_repositories, etc.
}
```

### Multiple MCP Servers

```csharp
protected override async Task RegisterToolsAsync()
{
    // GitHub
    await ToolManager.RegisterMCPServerViaNpxAsync(
        "@modelcontextprotocol/server-github"
    );
    
    // Slack
    await ToolManager.RegisterMCPServerViaNpxAsync(
        "@modelcontextprotocol/server-slack"
    );
    
    // Notion
    await ToolManager.RegisterMCPServerViaNpxAsync(
        "@modelcontextprotocol/server-notion"
    );
}
```

### With Environment Variables

```csharp
var config = new MCPServerConfig
{
    TransportType = MCPTransportType.Stdio,
    Command = "npx",
    Arguments = ["-y", "@modelcontextprotocol/server-github"],
    Environment = new Dictionary<string, string>
    {
        ["GITHUB_TOKEN"] = Environment.GetEnvironmentVariable("GITHUB_TOKEN")!
    }
};

var client = await MCPClientWrapper.CreateAsync(config);
await ToolManager.RegisterMCPServerAsync("github", client);
```

## Benefits

✅ **Official SDK**: Uses maintained, standard-compliant implementation  
✅ **Stdio Support**: Works with npx/uvx local servers  
✅ **Unified Management**: MCP tools and local tools in same manager  
✅ **Easy Integration**: Simple extension methods  
✅ **Type-Safe**: Full C# type safety  
✅ **Flexible**: Support for multiple servers  
✅ **Future-Proof**: Automatic SDK updates

## Dependencies

- **ModelContextProtocol.Core** (v0.4.0-preview.3): Official MCP SDK
- **Aevatar.Agents.AI.Core**: Provides the tool system + MCP integration (namespace remains `Aevatar.Agents.AI.Tool.*`)

## References

- [Model Context Protocol](https://modelcontextprotocol.io/)
- [MCP C# SDK](https://github.com/modelcontextprotocol/csharp-sdk)
- [MCP Servers](https://github.com/modelcontextprotocol/servers)
