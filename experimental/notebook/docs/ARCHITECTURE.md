# Aevatar.Notebook — Architecture

## 系统目标

NotebookLM-lite：在一个 Notebook 应用里，把 **Sources（资料）→ Context（上下文构建）→ Chat（问答）→ Report（报告）** 串成可复用的闭环，并尽可能复用 Aevatar Agent Framework 的 Memory 体系。

## 目录结构（规范化后）

```
experimental/notebook/
├── README.md
├── boot.sh                          # ✅ 一键启动（API + legacy UI）
├── docs/                             # ✅ 文档镜像（本目录）
│   ├── ARCHITECTURE.md
│   ├── CONFIGURATION.md
│   └── DEVELOPMENT.md
├── src/
│   ├── Aevatar.Notebook/             # ✅ Core（Agent/Context/Tools/Trace/Proto）
│   └── Aevatar.Notebook.Api/         # ✅ API Host（serves legacy UI + NDJSON + AG-UI SSE）
└── Aevatar.Notebook.AppHost/         # ✅ Aspire 编排（同时起前后端）
```

## 核心模块与职责

### Core：`src/Aevatar.Notebook/`

- **`Agents/NotebookAgent`**：基于 `AIGAgentBase`，启用 `State.History` 并把 `notebook_context` 注入 system prompt。
- **`Context/*`**：从 MemoryStore/VectorIndex 构建“覆盖优先 + Top‑K 相关”上下文，确保每次调用都 grounded。
- **`Tools/*`**：Notebook 专用 tools（list/get sources、retrieve chunks、get/generate report、get execution graph）。
- **`Tracing/*`**：把一次 chat/report 生成过程投影成 `ExecutionTrace`（可进一步被 MemoryGraph 投影）。
- **`Streaming/*`**：Tool calling 的进度事件通过 AsyncLocal sink 做边界层投影（NDJSON 或 AG‑UI）。
- **`Protos/*`**：Notebook 领域契约（sources/chunks/reports/context slices），用于跨边界/可回放的数据结构。

### API Host：`src/Aevatar.Notebook.Api/`

- **NDJSON streaming（legacy）**
  - `POST /api/chat/stream`
  - `POST /api/report/stream`
- **AG‑UI（标准化）**
  - `POST /api/sessions`
  - `POST /api/sessions/{id}/input`
  - `GET  /api/sessions/{id}/agui/events`（SSE）
  - 断线重连：**snapshot-first**（先 `MESSAGES_SNAPSHOT` 再 live events）
- **SSE fan-out**：使用 `Aevatar.Agents.Cognitive.Streaming.BroadcastEventHub` 进行多订阅广播

## 关键数据流

### 1) AG‑UI 会话（推荐）

1. 前端 `POST /api/sessions` 创建 session（thread）。
2. 前端连接 SSE：`GET /api/sessions/{id}/agui/events`
   - 服务端先发 `MESSAGES_SNAPSHOT`（来自 `AIGAgentBase.State.History`）
   - 再进入 live stream（`TEXT_MESSAGE_*` / `CUSTOM` / `RUN_*` / `STEP_*`）
3. 前端 `POST /api/sessions/{id}/input` 提交输入：
   - 服务端开始 run，推送：
     - `RUN_STARTED`
     - `STEP_STARTED(chat)`
     - `TEXT_MESSAGE_*`（user + assistant streaming）
     - tool calling 进度：`CUSTOM aevatar.notebook.tool_start|tool_end`
     - `STEP_FINISHED(chat)`
     - `RUN_FINISHED` / `RUN_ERROR`

### 2) NDJSON streaming（legacy）

保持旧 UI 可用：每行一个 JSON 对象，适合快速 MVP，但不利于“跨系统复用前端”，因此新增 AG‑UI 作为标准化对接层。

## 设计权衡

- **快照优先 vs replay**：token/进度事件可能极多，重连 replay 会卡死 UI；因此采用“快照优先”。
- **session → agent 映射**：每个 session 对应一个 `NotebookAgent`（`NotebookRuntime` 内维护），让 `State.History` 天然隔离，避免多会话串台。


