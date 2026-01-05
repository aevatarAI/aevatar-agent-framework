## 架构总览（Scientific Research Assistant）

### 模块边界

- **`frontend/`**：React + Vite + Tailwind，使用 **AG-UI SDK** 订阅后端 SSE，渲染消息流、run/step、tool 调用面板。
- **`src/ScientificResearchAssistant.Api/`**：ASP.NET Core API（**AG-UI**），提供 session API + `/agui/events` SSE（快照优先）。
- **`src/ScientificResearchAssistant/`**：`ResearchAgent`（基于 `AIGAgentBase`），注册 Claude Scientific Skills 的 MCP 工具并执行 tool loop。
- **`src/ScientificResearchAssistant/Vibe/*`**：vibe researching 多智能体角色：
  - `VibePlannerAgent`：生成研究计划（假设/未知/验证路径）
  - `VibeReasonerAgent`：基于 materials（sources）推理（可选 `python_exec` 验证）
- **`materials/`**：本地 grounding 输入（NotebookLM 风格 sources），由后端读取并注入 LLM 上下文。

### 事件流（用户输入 → Agent 执行 → AG-UI SSE）

1. **前端创建会话**：`POST /api/sessions` → 得到 `sessionId`
2. **前端订阅 AG-UI**：`GET /api/sessions/{id}/agui/events`
   - 先发送 `MESSAGES_SNAPSHOT`（快照优先）
   - 再进入 live stream（RUN/STEP/TEXT/CUSTOM）
3. **前端提交输入**：`POST /api/sessions/{id}/input`
   - body: `{ "message": "...", "mode": "chat" | "vibe" }`（默认 `chat`）
4. **后端触发 run（fire-and-forget）**：
   - 发送 `RUN_STARTED`
   - 发送 `STEP_STARTED(chat)` 或 `STEP_STARTED(vibe.*)`
   - 发送 `TEXT_MESSAGE_*`（user + assistant streaming）
   - vibe 模式会额外发送 `STATE_SNAPSHOT`（workspace：materials/进度等）
   - tool calling 期间发送 `CUSTOM`（tool_start/tool_end）
   - 结束时发送 `STEP_FINISHED(chat)` → `RUN_FINISHED`（失败则 `RUN_ERROR`）

### AG-UI 重连策略（快照优先）

- **不依赖 replay**：SSE 重连时不重放 token/tool spam。
- **连接即快照**：
  - `MESSAGES_SNAPSHOT` 来自后端 `ResearchSession` 的 **server-side message log**（兼容 multi-agent）。
  - `STATE_SNAPSHOT`（workspace）用于展示 materials / 运行阶段等可视化状态。
  - 额外发送 `CUSTOM`：
    - `aevatar.scientific.session`：会话元信息
    - `aevatar.scientific.tools_snapshot`：工具列表（含 MCP 标记）

### Vibe Researching（materials → 多智能体推论）

vibe 模式的最小闭环（MVP）：

- `vibe.materials`：读取 `materials/` 下的 sources（任意子目录的 `.md/.txt`），构建 bounded context
- `vibe.plan`：`VibePlannerAgent` 输出研究计划（可执行步骤）
- `vibe.reason`：`VibeReasonerAgent` 进行推理与引用；如启用 Python，可用 `python_exec` 做计算验证

### Tool 调用可视化（MCP skills）

核心思路：**不改 tool 本身**，只在边界层做“协议投影”。

- `ResearchAgent` 使用 `ResearchToolManager` 包装工具执行：
  - 在 `ExecuteToolAsync` 前后通过 `AsyncLocal` sink 发出 tool_start/tool_end
- API 在执行一次 run 时把 `ResearchStreamEventContext.Current` 绑定为 `AgUiResearchStreamEventSink`：
  - 输出为 `CUSTOM` 事件：
    - `aevatar.scientific.tool_start`
    - `aevatar.scientific.tool_end`
  - 并标注 `isMcp`，让 UI 可以明确区分 Claude Scientific Skills 的调用


