# Session Runtime Flow

## 端到端流程 (End-to-End)

1. **UI -> API**
   - Sessions UI 通过 `/api/chat/sessions/new` 创建 Session。
   - UI 使用 `/api/chat/sessions/{sessionId}/agui/events` 建立 SSE。

2. **API -> SessionRuntime**
   - `SessionRuntime.CreateSessionAsync` 启动 workflow，选择主角色。
   - `SessionRuntime.SendChatAsync` 组装 `ChatRequestEvent`，进入后台调度。
   - `SessionRuntime.RunWorkflowAsync` 执行 Cognitive Workflow（`workflow.run`）。

3. **Agent Bootstrap**
   - `AgentBootstrapper.EnsureInitializedAsync` 负责：
     - 选择 LLM provider（`LLMProvidersConfig.Default`）。
     - 应用 Role YAML（可选）。
     - 注册工具（核心/Memory/Web/Skills/MCP）。
   - 进度通过 `SESSION_STATUS` 输出到 AG-UI。

4. **Agent 执行**
   - `RoleAIGAgent.HandleChatRequestEvent` 执行聊天。
   - Streaming 产出 `ChatStreamChunkEvent`，结束时 `ChatResponseEvent`。

5. **AG-UI 投影**
   - `SessionAgUiStream` 订阅 agent stream：
     - `ChatStreamChunkEvent` -> `TEXT_MESSAGE_CONTENT`
     - `ChatResponseEvent` -> `TEXT_MESSAGE_END` + `RUN_FINISHED`
     - `ExecutionTraceEvent` -> `AgUiTraceProjector` 映射
     - Raw trace -> `CUSTOM: execution_trace_raw`
     - 状态 -> `CUSTOM: SESSION_STATUS`

6. **SSE 输出**
   - 先执行 bootstrap（`ISessionAgUiBootstrapper` → `MESSAGES_SNAPSHOT`）。
   - `AgUiSseWriter` 将 `AgUiEvent` 写入 SSE。
   - 前端 `app-sessions.js` 统一渲染 timeline + chat bubbles。

## 事件路径 (Event Path)

```
UI -> /api/chat/sessions/{id}/input
  -> SessionRuntime.SendChatAsync
    -> AgentBootstrapper.EnsureInitializedAsync
      -> RoleAIGAgent.HandleChatRequestEvent
        -> ChatStreamChunkEvent / ChatResponseEvent
          -> SessionAgUiStream
            -> AgUiSseWriter (SSE)
              -> UI timeline/messages
```

Workflow:

```
UI -> /api/chat/sessions/{id}/workflow/run
  -> SessionRuntime.RunWorkflowAsync
    -> SessionWorkflowRunner (CognitiveCoordinatorGAgent)
      -> ExecutionTraceEvent
        -> SessionAgUiStream
          -> AgUiSseWriter (SSE)
            -> UI timeline/messages
```

## 关键可观测事件 (Observability)

- `CUSTOM: SESSION_STATUS`
  - `init.plan / init.start / init.tools / init.done`
  - `dispatch.start / dispatch.done`
  - `run.error`
- `CUSTOM: execution_trace_raw`
  - 原始 ExecutionTraceEvent payload（phase/nodeId/fields）
- `CUSTOM: event.handler.*`
  - 来自 `AgUiTraceProjector` 的 handler start/end

## 常见问题 (FAQ)

- **dispatch.done 卡住？**
  - 已解耦 HTTP `CancellationToken`，dispatch 在后台执行。
- **看不到 ExecutionTrace？**
  - 确认开启 `ExecutionTraceProgressHook`，SSE 端会输出 `execution_trace_raw`。
