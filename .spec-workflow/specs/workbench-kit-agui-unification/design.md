# Design Document

## Overview

本 spec 的设计目标很单纯：把 “能消失的分支” 贯彻到工程结构里，让 **Aevatar Workbench（可选官方 kit）** 的最小闭环更像一个可复用的 SDK，而不是一堆散落的 demo 代码。

我们做两件事（对应两个坏味道）：

1) **统一前端 AG-UI Client 的 SSOT**
- 把仓库里重复的 `@agui/sdk` 本地 shim 收敛为一个共享实现，供 `learning/` 与 `scientific-research-assistant/` 等前端复用。

2) **统一 AevatarKit 的 Run 流式协议为 AG-UI**
- `aevatar-kit` 的 Run SSE 目前输出自定义事件（`event: step` + 自定义字段），与 AG-UI 的 `type` 协议不兼容，导致前端 SDK 无法复用。
- 目标是：Run SSE 输出 **AG-UI JSON 事件流（`data: { type: ... }`）**，并遵循 **snapshot-first**（先 `MESSAGES_SNAPSHOT`/`STATE_SNAPSHOT`，再增量事件）。

边界声明：
- **Workbench 作为可选官方 kit**：实现集中在 `aevatar-kit/` 与少量共享前端工具，不侵入 Aevatar Core 的稳定 API。
- **不做 Task/Board 域模型**：先把 Graph Editor/Run 跑通，后续再叠任务面板。

## Steering Document Alignment

### Technical Standards (tech.md)

- **.NET 10**：AevatarKit API 继续使用 ASP.NET Core 10.0。
- **Protobuf-First**：
  - AevatarKit 的 CRUD/存储契约（Graph/Run/Memory/MCP）继续使用 `aevatarkit_messages.proto`（已存在）。
  - AG-UI 是 UI 事件流协议，当前仓库已有 `Aevatar.Agents.AGUI` 的 C# event model（JSON 输出）。本 spec **不引入新的“跨 Actor/持久化边界”的非 Protobuf 类型**。
- **Port Policy**：不引入/示例化 `:5000`；本 spec 的示例端口保持现有（如 `5777` / `5678` / `5173`）。

### Project Structure (structure.md)

- **共享前端代码**放入既有 `packages/` 目录下（该目录已存在），避免再造顶层目录。
- **协议投影逻辑**（Kit internal events → AG-UI）放在 `aevatar-kit/src/AevatarKit.Api/` 内的独立文件夹/文件中，避免塞进 `Program.cs` 的 endpoint lambda 里。
- 每个文件职责单一、保持短小，避免在一个文件里同时做：读写 run registry + mapping + SSE 编码。

## Code Reuse Analysis

### Existing Components to Leverage

- **`src/Aevatar.Agents.AGUI/AgUiEvents.cs`**：AG-UI 标准事件类型（`RUN_*` / `STEP_*` / `TEXT_MESSAGE_*` / `STATE_*` / `CUSTOM` / Tool events）。
- **`cognitive-mesh/Aevatar.AxiomReasoning/EventStreaming/AgUi/AxiomAgUiEventStream.cs`**：协议投影的最佳实践样例：
  - snapshot-first（`MESSAGES_SNAPSHOT` + `STATE_SNAPSHOT`）
  - 标准事件 + `CUSTOM` 扩展并存（平滑 UI 迁移）
  - streaming delta 合并策略（降低 SSE 事件数量）
- **`aevatar-kit/src/AevatarKit.Runtime/*`**：现有 Run/Graph/Memory/MCP 的 in-memory registry，可作为 M0/MVP 的数据面。
- **`aevatar-kit/frontend/app.js`**：现有 Timeline UI 与 Run replay 逻辑（将调整为消费 AG-UI）。

### Integration Points

- **AevatarKit API**：
  - `/api/runs/{runId}/events`（现有）将输出 AG-UI SSE（或新增 `/api/runs/{runId}/agui/events` 作为别名）。
  - `GET /api/runs/{runId}`（回放）保持 Protobuf JSON 输出不变。
- **Frontends**：
  - `learning/frontend` 与 `scientific-research-assistant/frontend` 通过 Vite alias + TS paths 统一指向共享 `@agui/sdk` shim。

## Architecture

核心思想：**一个协议（AG-UI）+ 两类消费者（Kit UI / 其他系统 UI）+ 一个共享 client shim（离线友好）**。

```mermaid
graph TD
  subgraph Frontends
    L[learning frontend] -->|import @agui/sdk| SDK[(shared agui sdk shim)]
    S[scientific-research-assistant frontend] -->|import @agui/sdk| SDK
    K[aevatar-kit frontend (no build)] -->|EventSource + JSON type dispatch| SSE
  end

  subgraph Backend
    SSE[/AevatarKit.Api SSE endpoint\n/api/runs/{id}/events/] -->|data: {type: ...}| K
    SSE -->|data: {type: ...}| SDK
    M[RunRegistry + RunEngine] --> MAP[Kit Run → AG-UI mapper] --> SSE
  end
```

### Modular Design Principles

- **单一职责**：
  - “run 事件 → AG-UI 事件”的映射是一个纯函数/小组件；
  - “SSE 写出”是一个独立 helper；
  - endpoint 只负责 orchestration（取 run、写 headers、调用 mapper、循环输出）。
- **扩展优先用 CUSTOM**：保持协议标准化，同时允许 Kit UI 继续展示 step status/output/error 等 richer 字段。

## Components and Interfaces

### Component 1 — Shared `@agui/sdk` shim (TypeScript)

- **Purpose:** 为 monorepo 内的多个前端提供一致的 `AgUiClient`（EventSource + handler dispatch），避免重复实现与漂移。
- **Interfaces:**
  - `class AgUiClient { constructor(url, options?); on(type, handler); close(); }`
- **Dependencies:** 浏览器 `EventSource`。
- **Reuses:** 复用现有 `learning/frontend/src/lib/agui-sdk.ts` 与 `scientific-research-assistant/frontend/src/lib/agui-sdk.ts` 的共同实现（合并为 SSOT）。
- **Planned location:**
  - `packages/agui-sdk/src/index.ts`（单文件，保持最小 surface）
  - 两个前端通过：
    - Vite `resolve.alias['@agui/sdk']` 指向该文件
    - TS `compilerOptions.paths` 指向该文件（删除本地 `.d.ts` 重复声明）

### Component 2 — AevatarKit AG-UI Event Stream (C#)

- **Purpose:** 把 AevatarKit 的 Run 内部事件（当前为 `AevatarKit.WorkflowStepEvent`）投影为 AG-UI SSE 输出。
- **Interfaces:**
  - `BuildInitialSnapshot(run) -> IEnumerable<AgUiEvent>`：输出 `MESSAGES_SNAPSHOT`（必选）+ `STATE_SNAPSHOT`（可选）+ `RUN_STARTED`（可选）。
  - `MapStepEvent(run, WorkflowStepEvent evt) -> IEnumerable<AgUiEvent>`：输出 `STEP_*`、`TEXT_MESSAGE_*`、`CUSTOM(aevatarkit.step_update)` 等。
- **Dependencies:**
  - `Aevatar.Agents.AGUI`（事件类型）
  - `IRunRegistry`（取 run）
- **Reuses:**
  - 参考 `AxiomAgUiEventStream` 的：snapshot-first、best-effort、streaming delta 合并策略。

### Component 3 — AevatarKit SSE Endpoint (C#)

- **Purpose:** 提供稳定的 AG-UI SSE 输出端点，处理连接/断开与 flush。
- **Interfaces:**
  - `GET /api/runs/{runId}/events`（输出 `data: { ... }\n\n`）
- **Dependencies:** `IRunRegistry`、mapper、`System.Text.Json`。
- **Reuses:** 参考 `AxiomReasoning` 的 SSE 写法（无 `event:` 行，全部走默认 message）。

### Component 4 — AevatarKit Frontend Timeline (no build)

- **Purpose:** 继续保持“无构建步骤”的体验，同时消费 AG-UI 事件流。
- **Approach:** 使用原生 `EventSource` 的 `onmessage`：
  - 解析 JSON
  - 以 `evt.type` 分发处理
  - 对 `CUSTOM` 的 `aevatarkit.step_update` 做兼容渲染（statusBadge/输出/错误）

## Data Models

### Shared AG-UI Client

- `AgUiClient`: 同现有 shim（EventSource + `evt.type` dispatch）。

### Kit Custom Event Payload (AG-UI CUSTOM)

用于 Kit UI 的 richer timeline（不影响通用 AG-UI 客户端）：

- `CUSTOM`:
  - `name`: `"aevatarkit.step_update"`
  - `value`:
    - `runId: string`
    - `stepId: string`
    - `stepName: string`
    - `status: "RUNNING" | "STREAMING" | "COMPLETED" | "FAILED"`
    - `outputChunk?: string`
    - `error?: string`

## Error Handling

### Error Scenarios

1. **Unknown runId**
   - **Handling:** SSE endpoint returns HTTP 404 and a short text body.
   - **User Impact:** UI 显示 “run not found”，并停止连接。

2. **Mapper exception / bad event**
   - **Handling:** mapper best-effort：吞掉异常并继续输出后续事件；必要时发 `RUN_ERROR`。
   - **User Impact:** 运行不被中断；UI 至多缺一条事件或显示 run error。

3. **Client disconnect**
   - **Handling:** 写 SSE 时使用 `RequestAborted`，断开即退出循环；不写异常日志噪音。
   - **User Impact:** 重新连接后 snapshot-first 恢复。

## Testing Strategy

### Unit Testing

- 针对 mapper：
  - 初始 snapshot 顺序：`MESSAGES_SNAPSHOT` MUST be first。
  - `WorkflowStepEvent` 状态变化映射：
    - 首次 RUNNING → `STEP_STARTED`
    - STREAMING → `TEXT_MESSAGE_*`（start/content/end 的约束）
    - COMPLETED/FAILED → `STEP_FINISHED` + `CUSTOM step_update`

### Integration Testing

- `dotnet run` 启动 `AevatarKit.Api`：
  - 创建/保存 graph
  - start run
  - 打开 SSE 并观察事件为 AG-UI（包含 `type` 字段）
  - 刷新页面，确认 snapshot-first 立即恢复（无需 replay SSE）

### End-to-End Testing

- 人工 E2E（MVP）：
  - Run history replay 仍可用（GET `/api/runs/{id}`）
  - Memory 页可通过 runId 查看 `run:{runId}` entries


