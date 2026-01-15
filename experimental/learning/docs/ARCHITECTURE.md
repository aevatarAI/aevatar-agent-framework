# Aevatar.Learning — Architecture

## 系统目标

`experimental/learning/` 是一个 **AI Native 学习系统**：以 “Notebook（学习专题）= 本地目录” 为组织单元，把 **资料导入 → 上下文构建 → 问答 → 报告整理 → 专题百科 → 学习卡片（SRS）→ 测验 → 进度统计 → skills 生成** 串成可复用闭环。

核心约束：
- **没有 AI（LLMProviders）就不成立**：问答/报告/百科/测验/卡片生成/skills 生成均依赖 LLM。
- **AG‑UI SSE** 作为前后端实时通信协议：断线重连采用 **snapshot-first**（不依赖 replay）。
- **禁止使用 `:5000`**：默认后端 `:5678`，前端 `:5173`。
- **跨边界类型必须 Protobuf**：Agent State / Stream Event / Config / EventSourcing Event 不允许用手写 POCO。

## 目录结构（目标形态）

```
experimental/learning/
├── README.md
├── boot.sh
├── docs/
│   ├── ARCHITECTURE.md
│   ├── CONFIGURATION.md
│   └── DEVELOPMENT.md
├── frontend/                        # Tauri + React + Vite + TS
├── src/
│   ├── Aevatar.Learning/            # Core（Agents/Domain/Protos）
│   └── Aevatar.Learning.Api/        # API Host（REST + AG-UI SSE）
└── Aevatar.Learning.AppHost/        # Aspire 编排（后端 + 前端 web dev）
```

## 模块边界与职责

### Core：`experimental/learning/src/Aevatar.Learning/`

目标：把学习系统的“业务真相源”收敛在清晰边界内，避免 API 层堆 if/else。

- **`Protos/*`**：学习系统领域契约（State/Events/可选 Config），全部 Protobuf。
- **`Agents/LearningNotebookAgent`**：最小可运行 Agent；承载会话内的 AI 执行入口与 `State.History`（用于 AG‑UI `MESSAGES_SNAPSHOT`）。
- **`Notebooks/*`**：Notebook ⇄ 目录映射（默认根目录 + 子目录结构统一）。
- **`Sources/*`**：资料导入与读取（写入 notebook 目录的 `sources/`）。
- **`Context/*`**：Notebook context 构建（有界预算、可追溯 `sourceId` 标记）。
- **`Chat/*`**：问答服务（LLMProviders 可配置 + streaming）。
- **`Reports/*`**：报告生成与版本化持久化（写入 `reports/`）。
- **`Encyclopedia/*`**：专题百科构建/查询（写入 `encyclopedia/`，返回结构化结果）。
- **`Cards/*`**：学习卡片与 SRS 调度（写入 `cards/`）。
- **`Quiz/*`**：测验生成/判分/记录（写入 `quizzes/`）。
- **`Skills/*`**：skills 生成与版本管理（写入 `skills/`）。
- **`Progress/*`**：进度统计聚合（供 Notebook 首页展示）。

### API Host：`experimental/learning/src/Aevatar.Learning.Api/`

目标：对外提供最小可运行 API，并把执行过程投影成 AG‑UI 事件流。

- **基础诊断**
  - `GET /health`
  - `GET /api/info`（返回非敏感诊断：默认 provider、model、endpoint、timeout、runtimeType、version）
- **AG‑UI 会话（threadId=sessionId）**
  - `POST /api/sessions`
  - `POST /api/sessions/{id}/input`
  - `GET  /api/sessions/{id}/agui/events`（SSE，snapshot-first）
- **SSE fan-out**：使用 `Aevatar.Agents.Cognitive.Streaming.BroadcastEventHub` 进行多订阅广播
- **Notebook & 资料 & 学习能力（按 notebookId 作用域）**
  - `POST /api/notebooks` / `GET /api/notebooks` / `GET /api/notebooks/{id}`
  - `POST /api/notebooks/{id}/sources/*`、`GET /api/notebooks/{id}/sources/*`
  - `POST /api/notebooks/{id}/reports:generate`、`GET /api/notebooks/{id}/reports/*`
  - `POST /api/notebooks/{id}/encyclopedia:*`
  - `GET/POST /api/notebooks/{id}/cards:*`
  - `POST /api/notebooks/{id}/quiz:*`
  - `POST /api/notebooks/{id}/skills:*`

约定：
- 后端 JSON 序列化为 **camelCase**（与 AG‑UI 约定一致）。
- SSE 连接设置 `Cache-Control: no-store`，并关闭代理缓冲（`X-Accel-Buffering: no`）。

### Frontend：`experimental/learning/frontend/`

目标：桌面端优先（Tauri），同时保留 Web dev 模式便于开发与调试。

- **AG‑UI 客户端**：使用 `@agui/sdk` 连接 SSE，消费：
  - `MESSAGES_SNAPSHOT`（快照恢复）
  - `TEXT_MESSAGE_*`（流式输出）
  - `RUN_*` / `STEP_*`（运行与步骤生命周期）
  - `CUSTOM` / `STATE_*`（应用扩展事件与可视化状态）

为什么选择 Tauri：
- Notebook 绑定本地目录，天然需要文件系统交互、权限与离线资产管理。
- 桌面端更适合“长期学习工作台”形态（多面板、快捷键、跨窗口管理）。

### Aspire AppHost：`experimental/learning/Aevatar.Learning.AppHost/`

目标：一键联调（后端 + 前端 web dev），并在 Aspire Dashboard 中看到两端端点与日志。

说明：
- AppHost 负责编排 `dev:web`（Vite）更可靠；`tauri dev` 更适合用 `experimental/learning/boot.sh --tauri` 在本机启动。

## 关键数据流

### 1) Notebook → Session → AG‑UI SSE（推荐路径）

1. 前端创建/选择 Notebook（对应真实目录）。
2. 前端 `POST /api/sessions` 创建 session。
3. 前端连接 SSE：`GET /api/sessions/{id}/agui/events`
   - 服务端先发 `MESSAGES_SNAPSHOT`（必要）
   - 可选发 `STATE_SNAPSHOT`（例如 progressSummary/百科构建进度）
   - 再进入 live stream（`TEXT_MESSAGE_*` / `RUN_*` / `STEP_*` / `CUSTOM`）
4. 前端 `POST /api/sessions/{id}/input` 提交输入：
   - 服务端执行一次 run，推送 `RUN_STARTED → STEP_STARTED → TEXT_MESSAGE_* → STEP_FINISHED → RUN_FINISHED`
   - 失败路径推送 `RUN_ERROR`（不抛异常中断 SSE）

### 2) snapshot-first（为什么必须）

token/进度事件可能极多，断线后 replay 会卡死 UI。  
因此重连只需要快照恢复“最新状态”，而不是重放全部中间事件。

## 设计权衡

- **目录即真相源**：每个 Notebook 的资料与产出放在自己的目录下，便于迁移、备份与审计；代价是需要明确的目录结构与权限处理。
- **协议投影（业务事件 → AG‑UI）**：业务不绑定 UI，AG‑UI 只是边界层投影；代价是需要维护一套事件映射与 CUSTOM 扩展命名规范。


