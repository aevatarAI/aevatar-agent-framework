# Integration Guide: Claude Scientific Skills (MCP)

This guide explains how to connect the `claude-scientific-skills` toolset via **Model Context Protocol (MCP)**.

**Repository**: `https://github.com/K-Dense-AI/claude-scientific-skills`

## 1) Overview

In Aevatar, MCP servers are configured **at the host/framework boundary** (Cursor-style `mcpServers`) and are **auto-connected best-effort** by `AIGAgentBase` during tool initialization.

## 2) Configuration (Cursor-style `mcpServers`)

Location (recommended):
- `scientific-research-assistant/src/ScientificResearchAssistant.Api/appsettings.json` → `MCP:mcpServers`

Key ideas:
- Each MCP server lives under a stable key (server alias).
- Tools are namespaced by default to avoid collisions: `mcp__{serverKey}__{toolName}`.
- If MCP is misconfigured or unreachable, the app still starts; MCP is best-effort.

### Option A: Hosted server (zero setup)

```json
{
  "MCP": {
    "autoConnect": true,
    "namespaceTools": true,
    "mcpServers": {
      "scientific-skills": {
        "name": "Scientific Skills (Hosted)",
        "url": "https://mcp.k-dense.ai/claude-scientific-skills/mcp",
        "timeoutMs": 300000
      }
    }
  }
}
```

### Option B: Local Docker (privacy)

```json
{
  "MCP": {
    "autoConnect": true,
    "namespaceTools": true,
    "mcpServers": {
      "scientific-skills-docker": {
        "name": "Scientific Skills (Docker)",
        "command": "docker",
        "args": ["run", "-i", "--rm", "ghcr.io/k-dense-ai/claude-scientific-skills:latest"],
        "timeoutMs": 300000
      }
    }
  }
}
```

## 3) Troubleshooting

- **Connection refused / 5xx**: check your `url` reachability (or Docker runtime if using `command: docker`).
- **Tool timeout**: increase the server’s `timeoutMs`.
- **Tool name collisions**: keep `namespaceTools=true` (default).
