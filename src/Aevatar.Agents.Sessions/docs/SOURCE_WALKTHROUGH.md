# Session Runtime 源码导读

> 目标：把“现在怎么实现的”从入口到细节串起来，读代码更高效。

## 一眼看懂（从入口开始）

1. **应用入口**
   - `examples/Aevatar.Workshop/Program.cs`
   - 关键：`AddAevatarSessionRuntime` + `AddAevatarSessionTooling` + `MapSessionUiEndpoints`

2. **API 路由**
   - `src/Aevatar.Agents.Sessions/Endpoints/SessionUiEndpoints.cs`
   - 重点：`/api/chat/sessions/*` 与 SSE `/agui/events` 的输出逻辑

3. **Session Runtime 主干**
   - `src/Aevatar.Agents.Sessions/Runtime/SessionRuntime.cs`
   - 重点：`CreateSessionAsync`、`SendChatAsync`、`GetSessionStreamAsync`

4. **AG-UI 投影**
   - `src/Aevatar.Agents.Sessions/Runtime/SessionAgUiStream.cs`
   - 重点：Chat/Trace/ToolCall 事件 -> AG-UI 事件的映射

5. **Agent 初始化**
   - `src/Aevatar.Agents.Sessions/Runtime/AgentBootstrapper.cs`
   - 重点：Provider 选择、YAML 应用、工具注册与 `SESSION_STATUS` 输出

## 关键模块（职责清单）

### SessionRuntime（主干）
- 文件：`src/Aevatar.Agents.Sessions/Runtime/SessionRuntime.cs`
- 职责：
  - Session 创建（workflow YAML -> roles -> SessionState）
  - Chat 调度（后台执行，解耦 HTTP 生命周期）
  - Stream 绑定（`SessionAgUiStream`）
  - Context 缓存与 idle 清理

### SessionAgUiStream（投影层）
- 文件：`src/Aevatar.Agents.Sessions/Runtime/SessionAgUiStream.cs`
- 职责：
  - 订阅 agent stream
  - ChatStreamChunk/ChatResponse -> TEXT_MESSAGE_* / RUN_* 事件
  - ExecutionTrace -> `AgUiTraceProjector` + `CUSTOM: execution_trace_raw`
  - `CUSTOM: SESSION_STATUS` 统一输出

### AgentBootstrapper（初始化/配置）
- 文件：`src/Aevatar.Agents.Sessions/Runtime/AgentBootstrapper.cs`
- 职责：
  - 选择 LLM provider
  - 应用 Role YAML
  - 注册工具与状态上报

### Workflow 相关
- `SessionWorkflowCatalog`：`src/Aevatar.Agents.Sessions/Runtime/SessionWorkflowCatalog.cs`
  - workflow 名称解析 + 单 role workflow 生成
- `WorkflowMeshCompiler`：`src/Aevatar.Agents.Sessions/Runtime/WorkflowMeshCompiler.cs`
  - YAML -> MeshDefinition
- `WorkflowMeshService`：`src/Aevatar.Agents.Sessions/Runtime/WorkflowMeshService.cs`
  - Graph 快照 + agent 预热

### Tooling 下沉
- `AgentToolCatalog`：`src/Aevatar.Agents.Tooling/Catalog/AgentToolCatalog.cs`
  - 统一工具列表 / DotNetTool 扫描
- `AgentToolingOptions`：`src/Aevatar.Agents.Tooling/Options/AgentToolingOptions.cs`

### Role Workspace（独立模块）
- `RoleWorkspaceService`：`src/Aevatar.Agents.Workspaces/Core/RoleWorkspaceService.cs`
  - 角色实例化 + Link/Unlink + Role Chat 路由
- `RoleWorkspaceApiEndpoints`：`src/Aevatar.Agents.Workspaces/Endpoints/RoleWorkspaceApiEndpoints.cs`
  - `/api/roles/*` API 与 Role SSE

### SSE 输出
- `AgUiSseWriter`：`src/Aevatar.Agents.AGUI/AgUiSseWriter.cs`
  - 统一写入 `AgUiEvent` 到 SSE

## 关键链路（读懂“消息怎么流动”）

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

## 事件与契约

- `ChatRequestEvent`：触发请求
- `ChatStreamChunkEvent`：streaming delta
- `ChatResponseEvent`：最终回复
- `ExecutionTraceEvent`：trace 原始事件
- `CUSTOM: SESSION_STATUS`：后端状态/步骤提示
- `CUSTOM: execution_trace_raw`：原始 trace payload

AG-UI 投影入口：`src/Aevatar.Agents.AGUI/AgUiTraceProjector.cs`

## 配置入口

- `SessionRuntimeOptions`：`src/Aevatar.Agents.Sessions/Runtime/SessionRuntimeOptions.cs`
- `AgentToolingOptions`：`src/Aevatar.Agents.Tooling/Options/AgentToolingOptions.cs`
- `CognitiveSessionOptions`：`src/Aevatar.Agents.Sessions/CognitiveSessionOptions.cs`

Workshop 默认绑定：
- `examples/Aevatar.Workshop/Program.cs`

## 测试位置

- `test/Aevatar.Agents.Sessions.Tests/SessionRuntimeTests.cs`
- `test/Aevatar.Agents.Sessions.Tests/SessionWorkflowCatalogTests.cs`
- `test/Aevatar.Agents.Sessions.Tests/WorkflowMeshServiceTests.cs`
- `test/Aevatar.Agents.Sessions.Tests/SessionToolCatalogTests.cs` (AgentToolCatalog coverage)

## 推荐阅读顺序（省时间）

1. `SessionUiEndpoints.cs`
2. `SessionRuntime.cs`
3. `SessionAgUiStream.cs`
4. `AgentBootstrapper.cs`
5. `SessionWorkflowCatalog.cs` + `WorkflowMeshService.cs`
6. `AgentToolCatalog.cs`
7. `RoleWorkspaceApiEndpoints.cs`
8. 测试用例（验证行为）

## 相关文档

- 流程图：`docs/SESSION_RUNTIME_FLOW.md`
- 前端对接：`docs/SESSION_API_FRONTEND.md`
