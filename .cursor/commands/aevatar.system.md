---
description: Aevatar System Scaffold Protocol - Create a new AI system folder (backend+frontend) using AG-UI + configurable LLMProviders + Aspire AppHost + start.sh + docs
---

## User Input

```text
$ARGUMENTS
```

你将收到一个“xx AI 系统”的需求描述（在 `$ARGUMENTS` 中）。你的任务是在本仓库根目录下 **创建一个新的系统子目录**，并用 `src/` 下的 Aevatar Agent Framework 构建该系统的“最小可运行版本（MVP skeleton）”，同时把文档与启动/编排脚手架补齐。

## Core Rules (Hard Requirements)

### 0) Repository Policies

- **禁止使用 5000 端口**：仓库内任何服务/示例/文档/默认配置都不要绑定或示例化 `:5000`。默认后端优先 `:5678`，前端 Vite `:5173`。
- **Protobuf 铁律**：任何跨边界类型必须用 Protobuf 定义（Agent State / Event Messages / EventSourcing Events / Configuration Objects）。严禁用 C# POCO 作为 State/Event/Config。

### 1) Root-level System Directory

- 在仓库根目录创建一个新子目录：命名风格参考 `novel/`、`notebook/`、`trade/`，要求：
  - 全小写、言简意赅、建议 kebab-case
  - 如用户未指定名称：你给出 2~3 个候选并选一个最合适的落地

### 2) Required Structure (Must exist)

在 `<system>/` 下必须包含：

- `<system>/README.md`：一页说明系统构成、如何运行、关键配置入口
- `<system>/docs/`：必须详细说明架构/功能/实现/运行方式/配置说明（不是空壳）
- `<system>/start.sh`：一键同时启动前后端
- `<system>/frontend/`：前端工程（默认 React + Vite + TS；如需求更适合桌面端可选 Tauri，但必须在 docs 解释原因）
- `<system>/src/`：.NET 项目（至少 Core + Api）
- `<system>/<SystemName>.AppHost/`：Aspire AppHost（同时启动前后端）
- 根目录新增 `aevatar-<system>-system.slnx`：格式参考 `aevatar-trade-system.slnx`，把该系统项目与 docs 文件纳入

### 3) Frontend/Backend Communication via AG-UI (Mandatory)

- 后端必须提供标准 SSE 端点：`GET /api/sessions/{id}/agui/events`
- **重连策略：快照优先**（不要依赖 replay）：
  - 连接时先发 `MESSAGES_SNAPSHOT`（必要）
  - `STATE_SNAPSHOT`（可选，若系统有可视化状态/图/进度）
  - 然后进入 live stream（token/step/state delta）
- 事件类型必须使用框架 `src/Aevatar.Agents.AGUI/AgUiEvents.cs`
- 后端 JSON 序列化使用 camelCase（与 AG-UI 约定一致）
- 前端必须使用 `@agui/sdk` 对接该 SSE

### 4) LLM Provider Must Be Configurable (Never hardcode)

- Provider 配置必须走 `LLMProviders`（见 `Aevatar.Agents.AI.Abstractions.Configuration.LLMProvidersConfig`）
- 默认 provider 从 `LLMProviders:Default` 选取
- 允许请求级覆盖（例如创建 session 时传 `providerName`）
- Secrets 必须支持 **全局 user secrets（加密，推荐）+ 项目级覆盖**：
  - **全局（推荐）**：`~/.aevatar/secrets.json`（加密；用 `src/Aevatar.Agents.SecretsCli` 写入；可用 `AEVATAR_SECRETS_PATH/AEVATAR_SECRETS_DIR` 覆盖）
  - **项目级（可选）**：`appsettings.secrets.json`（gitignored，用于覆盖/团队模板），并提供 `.example` 文件
  - 后端启动时配置源顺序建议：`appsettings.json` → `AddAevatarUserSecrets()` → `appsettings.secrets.json` → 环境变量

### 5) Aspire AppHost (Mandatory)

- AppHost 项目命名为 `*.AppHost`
- 使用 Aspire 同时启动后端与前端：
  - 后端：`builder.AddProject<Projects.<System>_Api>(...)`
  - 前端：`builder.AddExecutable(..., "npm", <frontendDir>, "run", "dev")`
  - 参考 `trade/Aevatar.Trade.AppHost/Program.cs` 的 `AddProject + AddExecutable + WithHttpEndpoint` 模式
- 不要引入不必要的 NuGet；版本按 `Directory.Packages.props` 统一管理

### 6) start.sh (Mandatory)

脚本行为参考 `novel/dev.sh`（稳健、可维护）：

- 默认端口：后端 5678、前端 5173（可通过环境变量覆盖）
- 可选 kill 端口（macOS/Linux 用 lsof），并在退出时清理子进程
- 等待 `http://localhost:<backend>/health` OK 后再启动前端
- 注入前端需要的 env（例如 `VITE_API_BASE_URL` 或 `VITE_<SYSTEM>_API_URL`）

## Reference Patterns (You MUST reuse these)

- **AG-UI 规范与最佳实践**：`docs/AGUI_INTEGRATION_GUIDE.md`
- **AG-UI 代码参考**：`cognitive-mesh/Aevatar.AxiomReasoning/AgUi/*`
  - `AxiomAgUiBootstrap`：构建快照（messages/status/state）
  - `AxiomAgUiEventStream`：把业务事件流投影成 AG-UI 事件（RUN/STEP/TEXT/STATE + CUSTOM）
- **LLMProviders 配置模式**：`notebook/README.md` + `src/Aevatar.Agents.AI.*`
- **User Secrets（全局加密密钥）**：`src/Aevatar.Agents.Core/docs/UserSecrets.md`
- **Aspire 编排前后端**：`trade/Aevatar.Trade.AppHost/Program.cs`
- **一键启动脚本**：`novel/dev.sh`

## Backend Minimal Deliverables (Must be runnable)

后端需实现一个最小“会话/线程”模型：`threadId=sessionId`，用于 AG-UI 事件关联。最小接口集合：

- `GET  /health`
- `GET  /api/info`
  - 返回非敏感诊断：默认 provider、model、endpoint、timeout、runtimeType、以及系统版本等
- `POST /api/sessions`
  - 创建 session/thread，返回 sessionId；可选 `providerName`
- `POST /api/sessions/{id}/input`
  - 提交用户输入，触发一次 run（产生 AG-UI 事件）
- `GET  /api/sessions/{id}/agui/events`
  - AG-UI SSE：先快照后 live

最小 Agent 要求：

- 至少一个可运行 Agent（使用 Aevatar 框架）
- Agent State / Events / Config（如有）必须 Protobuf
- 不要删除任何已有测试；如新增测试，覆盖 happy path + error path

## Frontend Minimal Deliverables (Must be runnable)

前端默认采用 React + Vite + TypeScript（除非需求强烈指向桌面端/离线能力，需要 Tauri）：

- 必须安装并使用 `@agui/sdk`（按 `docs/AGUI_INTEGRATION_GUIDE.md`）
- UI 最小包含：
  - Session 创建/选择
  - 输入框（发送 user input）
  - 消息区（消费 `MESSAGES_SNAPSHOT` + `TEXT_MESSAGE_*`）
  - 状态区（消费 `STATE_SNAPSHOT/STATE_DELTA`；若暂时无复杂状态，可先展示 JSON）
- 后端地址不得写死：
  - 方案 A：Vite 同源 proxy（推荐）
  - 方案 B：使用 `VITE_*` env 注入 baseUrl

## Documentation Requirements (docs/ must be real)

至少创建并填写：

- `<system>/docs/ARCHITECTURE.md`
  - 模块边界（Core/Api/Agents/Frontend/AppHost）
  - 事件流（用户输入 → Agent 执行 → AG-UI SSE）
  - AG-UI 数据流与重连策略（快照优先）
  - 运行方式（start.sh / AppHost / 分别启动）
  - 关键权衡（为何选择该前端栈/为何该事件模型）
- `<system>/docs/CONFIGURATION.md`
  - `LLMProviders` 配置（含 user secrets + 项目级 secrets 示例与优先级）
  - 运行时（Local/Orleans）如何切换（如支持）
  - 端口与 env 变量
- `<system>/docs/DEVELOPMENT.md`
  - 本地开发、调试路径、常见问题排查

## Execution Protocol (What you do when invoked)

1) 解析 `$ARGUMENTS`，用 5~10 行复述系统目标与关键需求。
2) 若需求不完整：最多问 3~6 个“最关键澄清问题”（优先：数据源/隐私/部署约束/是否桌面端/是否多用户/是否需要持久化）。
3) 直接落地创建目录与工程（不要只给方案不写代码）。
4) 严格按本文件的硬性要求补齐：AG-UI SSE、LLMProviders 配置、Aspire AppHost、start.sh、docs、slnx。
5) 最后输出：
   - `start.sh` 使用方式
   - AppHost 启动方式
   - 分别启动方式
   - 访问地址（明确端口；不得出现 5000）


