## 架构总览（Scientific Research Assistant）

### 模块边界

- **`frontend/`**：React + Vite + Tailwind，使用 **AG-UI SDK** 订阅后端 SSE，渲染消息流、run/step、tool 调用面板。
- **`src/ScientificResearchAssistant.Api/`**：ASP.NET Core API（**AG-UI**），提供 session API + `/agui/events` SSE（快照优先）。
- **`src/ScientificResearchAssistant/`**：`ResearchAgent`（基于 `AIGAgentBase`），注册 Claude Scientific Skills 的 MCP 工具并执行 tool loop。

### 事件流（用户输入 → Agent 执行 → AG-UI SSE）

1. **前端创建会话**：`POST /api/sessions` → 得到 `sessionId`
2. **前端订阅 AG-UI**：`GET /api/sessions/{id}/agui/events`
   - 先发送 `MESSAGES_SNAPSHOT`（快照优先）
   - 再进入 live stream（RUN/STEP/TEXT/CUSTOM）
3. **前端提交输入**：`POST /api/sessions/{id}/input`
4. **后端触发 run（fire-and-forget）**：
   - 发送 `RUN_STARTED`
   - 发送 `STEP_STARTED(chat)`
   - 发送 `TEXT_MESSAGE_*`（user + assistant streaming）
   - tool calling 期间发送 `CUSTOM`（tool_start/tool_end）
   - 结束时发送 `STEP_FINISHED(chat)` → `RUN_FINISHED`（失败则 `RUN_ERROR`）

### AG-UI 重连策略（快照优先）

- **不依赖 replay**：SSE 重连时不重放 token/tool spam。
- **连接即快照**：
  - `MESSAGES_SNAPSHOT` 来自 `AIGAgentBase.State.History`（已开启 bounded history + compaction）。
  - 额外发送 `CUSTOM`：
    - `aevatar.scientific.session`：会话元信息
    - `aevatar.scientific.tools_snapshot`：工具列表（含 MCP 标记）

### Tool 调用可视化（MCP skills）

核心思路：**不改 tool 本身**，只在边界层做“协议投影”。

- `ResearchAgent` 使用 `ResearchToolManager` 包装工具执行：
  - 在 `ExecuteToolAsync` 前后通过 `AsyncLocal` sink 发出 tool_start/tool_end
- API 在执行一次 run 时把 `ResearchStreamEventContext.Current` 绑定为 `AgUiResearchStreamEventSink`：
  - 输出为 `CUSTOM` 事件：
    - `aevatar.scientific.tool_start`
    - `aevatar.scientific.tool_end`
  - 并标注 `isMcp`，让 UI 可以明确区分 Claude Scientific Skills 的调用


