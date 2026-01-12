# Design Document

## Overview

`learning/` 是一个 **AI Native 学习系统**：以“Notebook（学习专题）= 本地目录”为组织单元，把资料导入、问答、报告整理、专题百科、学习卡片（SRS）、测验、进度统计与 skills 生成统一在同一套会话/事件流之下。

MVP skeleton 的目标是先把 **端到端链路跑通**：

`Frontend (Tauri/React) → HTTP API → Session → Agent/Runtime → AG‑UI SSE (snapshot-first + live) → UI`

并在架构上为后续 7 大能力模块留好“可替换、可扩展”的边界。

---

## Steering Document Alignment

### Technical Standards (tech.md)

- **.NET 10 + Central Package Management**：所有新增依赖版本统一放在 `Directory.Packages.props`。
- **Protobuf‑First Contracts**：Agent State / Stream Event / Config 等跨边界类型必须由 `.proto` 生成（遵循 `AGENTS.md`）。
- **Runtime Agnostic**：业务逻辑写在 GAgent，运行时差异收敛在宿主/Actor 层；默认先用 Local runtime，后续可切 Orleans。
- **Observability**：遵循现有系统做法，提供 `/health` 与 `/api/info`，并在关键步骤发 AG‑UI `STEP_* / RUN_*`。

### Project Structure (structure.md)

遵循 monorepo 约定：为新系统创建根目录 `learning/`，内部结构与 `notebook/`、`trade/`、`novel/` 风格一致：

```
learning/
├── README.md
├── docs/
│   ├── ARCHITECTURE.md
│   ├── CONFIGURATION.md
│   └── DEVELOPMENT.md
├── start.sh
├── frontend/               # Tauri + Vite + React + TS
├── src/
│   ├── Aevatar.Learning/           # Core + Agents + domain services
│   └── Aevatar.Learning.Api/       # Minimal API host (AG-UI SSE + REST)
└── Aevatar.Learning.AppHost/       # Aspire AppHost (backend + frontend dev)
```

根目录新增解决方案入口：`aevatar-learning-system.slnx`（纳入系统项目与 docs）。

---

## Code Reuse Analysis

### Existing Components to Leverage

- **`src/Aevatar.Agents.AGUI/AgUiEvents.cs`**：标准 AG‑UI 事件模型（RUN/STEP/TEXT/STATE/MESSAGES/CUSTOM）。
- **`src/Aevatar.Agents.AGUI/AgUiBootstrap.cs`**：从 Agent `State.History` 收集 assistant 完整消息，生成 `MESSAGES_SNAPSHOT`（重连快照）。
- **`notebook/src/Aevatar.Notebook.Api/Sessions/NotebookSessionsApi.cs`**：
  - `GET /api/sessions/{id}/agui/events` 的 SSE 写法（headers + writer + snapshot-first + replay:false）。
  - `POST /api/sessions/{id}/input` 的 fire-and-forget run + per-session 串行锁模式。
- **`cognitive-mesh/Aevatar.AxiomReasoning/EventStreaming/AgUi/AxiomAgUiBootstrap.cs`**：快照构建与 laneId 设计（多 Actor/多 lane 分组）。
- **`cognitive-mesh/Aevatar.AxiomReasoning/EventStreaming/AgUi/AxiomAgUiEventStream.cs`**：业务事件 → AG‑UI 投影（RUN/STEP/TEXT/STATE + CUSTOM），以及 token stream 的“缓冲合并”优化策略。
- **`novel/dev.sh`**：一键启动脚本的 best-practice（端口清理、health wait、tauri/web 双模式、退出清理）。
- **`trade/Aevatar.Trade.AppHost/Program.cs`**：Aspire `AddProject + AddExecutable` 编排模式（固定 5173；注入 proxy target env）。
- **`novel/frontend/`**：Tauri 2 + Vite 的工程化组织与 `tauri.conf.json` 的 `connect-src` CSP 允许本地端口模式。

### Integration Points

- **LLMProviders**：复用 `Aevatar.Agents.AI.Abstractions.Configuration.LLMProvidersConfig` 的配置选择与 request-level override（参照 `notebook/README.md` 与 Notebook API 行为）。
- **Agent Runtime**：复用 `IGAgentActorManager` 与 Agent 生命周期；默认 Local，后续可通过配置切换 Orleans。
- **AG‑UI Client**：前端使用 `@agui/sdk` 对接 `GET /api/sessions/{id}/agui/events`。

---

## Architecture

### High-Level

```mermaid
flowchart TD
    UI[Tauri UI (React)] -->|POST /api/sessions| API
    UI -->|POST /api/sessions/{id}/input| API
    UI -->|SSE /api/sessions/{id}/agui/events| SSE

    API[Aevatar.Learning.Api] --> SM[SessionManager]
    API --> NB[NotebookDirectoryStore]
    API --> RT[LearningRuntime]

    RT --> AG[LearningNotebookAgent<br/>(GAgentBase&lt;State&gt;)]
    AG -->|internal protobuf events| HUB[Session EventHub]
    HUB --> SSE

    SSE -->|snapshot-first: MESSAGES_SNAPSHOT (+STATE_SNAPSHOT opt)| UI
```

### Modular Design Principles

- **边界优先**：Notebook/Sources/Encyclopedia/Cards/Quiz/Skills 均以“服务 + Protobuf 契约”抽象，不在 API 里堆 if/else。
- **协议投影**：业务事件不直接绑定 UI；通过 “EventStream → AG‑UI” 投影层输出标准事件 + CUSTOM 扩展。
- **快照优先**：重连不依赖 replay（避免 token/progress 爆炸），连接即发送 `MESSAGES_SNAPSHOT`，必要时再发 `STATE_SNAPSHOT`。

---

## Components and Interfaces

### `Aevatar.Learning.Api` (HTTP Host)

- **Purpose**：对外 HTTP API + AG‑UI SSE，负责 session 生命周期与前后端联调入口。
- **Interfaces**
  - `GET /health`
  - `GET /api/info`（非敏感诊断：provider/model/endpoint/timeout/runtime/version）
  - `POST /api/sessions`（可选 `providerName` 覆盖）
  - `POST /api/sessions/{id}/input`
  - `GET /api/sessions/{id}/agui/events`（snapshot-first + live）
- **Dependencies**
  - `LLMProvidersConfig`（默认 provider + override）
  - `LearningRuntime`（Agent 获取/创建）
  - `SessionManager`（会话状态与事件 hub）
  - `NotebookDirectoryStore`（Notebook ⇄ 目录映射；MVP 可先走默认根目录）
- **Reuses**
  - `NotebookSessionsApi` 的 SSE + run 模式

### `SessionManager`

- **Purpose**：管理 session（threadId=sessionId）、provider override、run 序号、并发锁、事件 hub（`BroadcastEventHub<AgUiEvent>`）。
- **Interfaces**
  - `Create(providerName?) -> session`
  - `TryGet(sessionId) -> session`
  - `SubscribeAgUi(sessionId, replay:false) -> IAsyncEnumerable<AgUiEvent>`
  - `NextRunSeq()`（生成 runId）

### `NotebookDirectoryStore` + `NotebookWorkspace`

- **Purpose**：Notebook 的元数据与目录组织；负责创建/打开/校验目录可写性；`NotebookWorkspace` 统一拼接 `sources/ reports/ encyclopedia/ cards/ quizzes/ skills/` 等子目录。
- **Interfaces（MVP）**
  - `CreateNotebook(displayName, rootPath?) -> notebookId + absolutePath`
  - `GetNotebook(notebookId) -> metadata`
  - `ListNotebooks()`
- **Notes**
  - 默认根目录建议：`~/AevatarLearning/`（可用 env 覆盖：`LEARNING_NOTEBOOK_ROOT`）。
  - 后续可接入 Tauri 选择目录并把路径传给后端创建。

### `Aevatar.Learning` (Core + Agents)

#### `LearningNotebookAgent`

- **Purpose**：承载 AI Native 核心：基于 Notebook 资料执行问答/报告/百科/卡片/测验/skills 生成；把执行过程映射为 AG‑UI（RUN/STEP/TEXT/STATE/CUSTOM）。
- **State**：必须是 Protobuf 生成类型（例如 `LearningNotebookState`），至少包含：
  - `History`（滑窗消息；用于快照与连续对话）
  - `SelectedNotebookId`
  - `SrsStats`（卡片/测验统计的聚合视图，MVP 可先为空）
- **Events**：内部业务事件必须 Protobuf（例如 `LearningUserInputEvent`、`LearningNotebookCreatedEvent` 等），并由投影层转换为 AG‑UI 事件输出。

#### `LearningAgUiProjection`（边界层）

- **Purpose**：把 Agent/业务事件流投影为 AG‑UI 事件流：
  - 连接时：发 `MESSAGES_SNAPSHOT`（通过 `AgUiBootstrap` 或应用自建 bootstrap）
  - live：按 `NotebookSessionsApi` 模式输出 `TextMessage* / Step* / Run* / CustomEvent / (StateSnapshot/Delta)`
- **Notes**
  - token streaming 采用类似 `AxiomAgUiEventStream` 的“delta 缓冲合并”（降低 SSE event 数量）。

### `learning/frontend` (Tauri + React + @agui/sdk)

- **Purpose**：桌面学习工作台。MVP 先实现标准 AG‑UI 客户端 UI：Session 创建/选择、输入框、消息区、状态区（JSON）。
- **Key UX (MVP)**
  - 左侧：Notebook 列表（后续扩展 sources/卡片/测验入口）
  - 中间：Chat（AG‑UI messages + streaming）
  - 右侧：State/Stats（先展示 JSON；后续做卡片/测验/百科面板）
- **Tauri Details**
  - 参考 `novel/frontend/src-tauri/tauri.conf.json`：`connect-src` 允许 `http://localhost:*` / `127.0.0.1:*`（用于 dev 连接后端 5678 与 Vite 5173）。

### `Aevatar.Learning.AppHost` (Aspire)

- **Purpose**：本地一键编排后端与前端 dev server。
- **Plan**
  - 后端：`builder.AddProject<Projects.Aevatar_Learning_Api>(...)`
  - 前端：`builder.AddExecutable(..., "npm", <frontendDir>, "run", "dev:web")` 固定 5173（避免端口漂移）
  - Desktop（tauri dev）建议通过 `learning/start.sh --tauri` 启动（避免 AppHost 在无 GUI 环境启动失败）

---

## Data Models

> 说明：跨边界（Agent State / Stream Events / Config）必须 Protobuf；HTTP DTO 可沿用 Notebook 的最小 JSON 输入输出风格。

### Protobuf (Core)

（示意，字段可在实现阶段细化）

```
message LearningNotebookState {
  string notebook_id = 1;
  repeated AevatarChatMessage history = 2; // 复用框架 AI state 结构或嵌套类型
  LearningProgressSummary progress = 3;
}

message LearningProgressSummary {
  int32 due_cards = 1;
  int32 new_cards = 2;
  int32 total_sources = 3;
  int32 quizzes_taken = 4;
}

message LearningUserInputEvent {
  string session_id = 1;
  string run_id = 2;
  string message = 3;
}
```

### HTTP DTO (Api)

- `POST /api/sessions`：`{ providerName?: string }`
- `POST /api/sessions/{id}/input`：`{ message: string, requestId?: string, providerName?: string, notebookId?: string }`

---

## Error Handling

### Error Scenarios

1. **LLMProviders 未配置 / 默认 provider 不存在**
   - **Handling**：`/api/info` 返回可定位的诊断；`/api/sessions/{id}/input` 返回 503/400 并在 AG‑UI 发 `RUN_ERROR`（code=`LEARNING_PROVIDER_NOT_CONFIGURED`）。
   - **User Impact**：UI 显示“需要配置 provider”的明确提示与入口链接（docs/CONFIGURATION）。

2. **Notebook 目录不可写/不存在**
   - **Handling**：创建/打开时返回 400/404；同时在 UI 提示选择不同目录或检查权限。
   - **User Impact**：不会进入损坏状态；已有数据不受影响。

3. **SSE 中断/重连**
   - **Handling**：严格 snapshot-first：重连立即发 `MESSAGES_SNAPSHOT`（必要）+ 可选 `STATE_SNAPSHOT`，live 订阅 `replay:false`。
   - **User Impact**：刷新即刻恢复最新消息与状态，不等待大量 replay。

---

## Testing Strategy

### Unit Testing

- `AgUiBootstrap`/消息快照：参考 `test/Aevatar.Agents.AGUI.Tests/*` 的覆盖方式，保证 messageId 稳定与去重。
- `NotebookDirectoryStore`：目录创建/权限错误/路径规范化。
- `SessionManager`：runId 生成、并发锁（串行执行）、provider override。

### Integration Testing

- 后端 API：`POST /api/sessions` → `POST /input` → `GET /agui/events`（验证：
  - 首包是 `MESSAGES_SNAPSHOT`
  - 后续包含 `RUN_STARTED/STEP_STARTED/TEXT_MESSAGE_*`）

### End-to-End Testing

- Web dev 模式（Vite）：用浏览器跑通最小 UI。
- Desktop dev（Tauri）：在本机环境验证 `connect-src` 与 SSE 工作正常（不在 CI 强制）。


