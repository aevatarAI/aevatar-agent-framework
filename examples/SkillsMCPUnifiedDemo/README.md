# Skills + MCP Unified Demo

这个 demo 把四件事串在一起：

1) **dotnet-file skills**：把 `skills/*.cs` 作为 Tool（通过 `.NET 10` 的 `dotnet run --file` 执行）
2) **Agent Skills (SKILL.md)**：通过 `skills_list/skills_load` 让 LLM 按需加载 skill（并支持 `allowed-tools` 硬约束）
3) **MCP (Model Context Protocol)**：把 MCP server 的 tools 注册进 Agent（可选：docker filesystem / Context7）
4) **In-proc AI tools**：用 C# 类直接注册工具（`text_stats/json_prettify/slugify`）

## 运行

```bash
dotnet run --project examples/SkillsMCPUnifiedDemo/SkillsMCPUnifiedDemo.csproj
```

## 配置真实 LLM

在 `examples/SkillsMCPUnifiedDemo/appsettings.secrets.json` 写入：

```json
{
  "LLMProviders": {
    "providers": {
      "deepseek": {
        "apiKey": "YOUR_API_KEY_HERE"
      }
    }
  }
}
```

## MCP（可选）

- **Docker Filesystem MCP**：需要本机安装并启动 docker（demo 会 try/catch，没装也能跑，只是少 MCP tools）
  - demo 会把 `AEVATAR_DEMO_ROOT`（默认运行时 CWD）挂载到容器 `/workspace`
- **Context7 MCP**：在 `appsettings.secrets.json` 里配置 `Context7.ApiKey` / `Context7.McpUrl`
- **GitHub MCP（可选）**：设置环境变量 `GITHUB_TOKEN`，demo 会通过 `npx` 启动 `@modelcontextprotocol/server-github`

## 包含的示例 skills

- `agent_skills/time-helper/SKILL.md`：`allowed-tools: [get_time]`（演示 allowlist）
- `agent_skills/env-helper/SKILL.md`：`allowed-tools: [get_env]`（演示读取环境变量）
- `agent_skills/file-reader/SKILL.md`：`allowed-tools: [file_read]`（读文件片段）
- `agent_skills/file-searcher/SKILL.md`：`allowed-tools: [file_search]`（grep）
- `agent_skills/text-analyzer/SKILL.md`：`allowed-tools: [text_stats]`（本地 AI tool）
- `agent_skills/json-pretty/SKILL.md`：`allowed-tools: [json_prettify]`（本地 AI tool）
- `agent_skills/slugify-helper/SKILL.md`：`allowed-tools: [slugify]`（本地 AI tool）
- `agent_skills/context7-docs/SKILL.md`：`allowed-tools: [resolve-library-id, get-library-docs]`（MCP：Context7）
- `agent_skills/mcp-filesystem-browse/SKILL.md`：MCP filesystem（docker-fs，没写 allowlist，避免不同版本 tool 名差异）

## 包含的 tools

- **dotnet-file tools（`skills/*.cs`）**：
  - `get_time` / `system_info` / `get_env` / `file_read` / `file_search`
- **in-proc tools（C#）**：
  - `text_stats` / `json_prettify` / `slugify`

