# Aevatar Platform

> 面向多领域的 **Agent OS / Workbench**。CLI/TUI 体验对标 OpenCode，能力不局限编程场景。

## 功能概览

- **CLI/TUI**：默认进入 TUI，支持 `/` 命令、`!` shell、`@` 文件引用与流式输出。
- **Profiles/Packs**：通过 `--profile` 切换领域（coding/vibe/worldbuilding），底层装配 workflow + roles + tools policy。
- **DSL 工作流**：基于 Cognitive Mesh DSL 的 JSON/YAML workflow 编排。
- **会话存储**：文件事件溯源（JSONL + meta 索引），支持 list/show/export/import。
- **工具策略**：危险工具默认关闭；命令/路径 allowlist 与超时控制。
- **Server/Attach**：HTTP + SSE（snapshot-first）后端，支持 attach/web。

## 环境要求

- .NET 10 SDK

## 快速开始

### 构建

```bash
dotnet build platform/src/Aevatar.Platform.slnx
```

### 运行 CLI / TUI

```bash
# 默认启动 TUI
dotnet run --project platform/src/Aevatar.Platform.Cli

# 单次任务（目前为占位 stub）
dotnet run --project platform/src/Aevatar.Platform.Cli -- -c "hello"
```

### 启动 Server

```bash
# 默认 http://127.0.0.1:5678 (严禁 5000)
dotnet run --project platform/src/Aevatar.Platform.Server

# 自定义 host/port
AEVATAR_SERVER_HOST=0.0.0.0 AEVATAR_SERVER_PORT=5678 \
dotnet run --project platform/src/Aevatar.Platform.Server
```

### Attach

```bash
dotnet run --project platform/src/Aevatar.Platform.Cli -- attach --url http://127.0.0.1:5678
```

> 注意：部分命令目前为 MVP 占位，后续任务会补全。

## 配置目录

默认目录：`~/.aevatar/`

```
~/.aevatar/
├── config.json
├── secrets.json
├── agents/        # role YAML
├── skills/        # 可选
├── tools/         # dotnet-file 工具插件
├── workflows/     # DSL JSON/YAML
├── mcp/
└── sessions/      # 会话事件 (JSONL + meta.json)
```

支持环境变量覆盖：

- `AEVATAR_CONFIG_DIR`
- `AEVATAR_CONFIG`
- `AEVATAR_SECRETS_PATH`
- `AEVATAR_SECRETS_DIR`

## TUI 使用（MVP）

- `/help`：命令帮助
- `/sessions` 或 `/sessions show <id>`
- `/workflow <name>` / `/profile <name>`
- `/editor`：使用 `EDITOR` 或 `config.json` 里的 `ui.editor`
- `!<cmd>`：执行 shell（受 allowlist + timeout 约束）
- `@<file>`：附件（支持模糊匹配）

## DSL（示例）

```json
{
  "dsl_version": "0.1",
  "goal": { "name": "demo" },
  "strategy": "cot",
  "budget": { "max_steps": 3, "token_limit": 500 },
  "nodes": [
    { "id": "planner", "type": "DivergentAgent" },
    { "id": "reviewer", "type": "ConvergentAgent" }
  ],
  "edges": [
    { "from": "planner", "to": "reviewer", "channel": "upstream_output" }
  ],
  "constraints": []
}
```

> `node.type` 必须是允许的类型：默认内置类型 + `~/.aevatar/agents/*.yaml` 中声明的角色。

## 常用 CLI

```bash
aevatar --workflow hermes
aevatar --profile coding
aevatar sessions list
aevatar sessions show <id>
aevatar export <id> <file>
aevatar import <file>
```

## Server API（摘要）

- `GET /api/sessions`
- `GET /api/sessions/{id}`
- `POST /api/sessions`
- `GET /api/sessions/{id}/events`
- `POST /api/sessions/{id}/messages`
- `GET /api/sessions/{id}/stream` (SSE, snapshot-first)

鉴权（可选）：

- `AEVATAR_SERVER_AUTH_TOKEN`  
  - `Authorization: Bearer <token>` 或 `X-API-Key: <token>`

## 注意事项

- **端口禁用**：禁止使用 `:5000`，默认 `5678`。
- **Protobuf 铁律**：跨边界类型必须为 Protobuf。
- **MVP 限制**：Workflow 执行目前为 stub；部分 CLI 命令为占位。


