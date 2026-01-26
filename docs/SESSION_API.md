## Session API（Cognitive Workflow）

目标：为 App 提供“按 workflow 启动会话、查询 Agent 状态与历史、聚合 session 级记忆/trace”的统一入口。

---

### 1) 依赖注入

```csharp
// 注册 Cognitive workflow registry（会加载内置与目录内 YAML）
services.AddCognitiveAgents();

services.AddAevatarCognitiveSessions(options =>
{
    options.DefaultProviderName = "deepseek";
    options.DefaultWorkerCount = 5;
    options.EnableSessionMemory = true;
    options.EnableAgentMemory = false;
    options.RegisterAllWorkflows = true;
});
```

### 2) Minimal API

```csharp
app.MapAevatarSessionApi();
```

所有请求/响应均为 `application/x-protobuf`（二进制 Protobuf）。

---

### 3) 关键接口

- **POST** `/api/sessions`  
  启动会话（`StartSessionRequest`），基于 workflow `name` 执行。
- **GET** `/api/sessions/{sessionId}`  
  获取 `SessionState`（含 coordinator/worker/agent id 与 status）。
- **GET** `/api/sessions/{sessionId}/agents`  
  获取 AgentId 列表。
- **GET** `/api/sessions/{sessionId}/agents/states`  
  获取所有 Agent 的 `AevatarAIAgentState`（可选 history）。
- **GET** `/api/sessions/{sessionId}/agents/histories`  
  直接返回 `State.History` 视图。
- **GET** `/api/sessions/{sessionId}/memory/session`  
  Session 级 Memory（scope=session）。
- **GET** `/api/sessions/{sessionId}/memory/agents/{agentId}`  
  Agent 级 Memory（scope=private_agent）。
- **GET** `/api/sessions/{sessionId}/memory/resources`  
  Memory 资源列表（可选 `scope_type`）。
- **GET** `/api/sessions/{sessionId}/trace`  
  读取 `ExecutionTrace`（如果配置了 `IExecutionTraceStore`）。
- **GET** `/api/workflows`  
  列出可用 workflow（来自 `IWorkflowRegistry`）。
- **GET** `/api/sessions`  
  列出 session 资源（依赖 `IMemoryStore` 的 session scope）。

---

### 4) 持久化与多级数据

- `SessionState`：通过 `IStateStore<SessionState>` 持久化。
- `State.History`：由各 Agent 自行维护（Cognitive 默认开启，窗口化）。
- `MemoryStore`：
  - `scope=session`：全会话聚合记忆  
  - `scope=private_agent`：单 Agent 记忆
- `ExecutionTraceStore`：可选，提供完整工作流 trace。

> 注意：Session 级 Memory 依赖 `EnableSessionMemoryStoreAppend`，启动会话时由 Session API 自动配置。
