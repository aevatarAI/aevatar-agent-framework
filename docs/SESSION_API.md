## Session API（Workflow YAML → Roles）

目标：为 App 提供“按 workflow YAML 启动会话、加载角色 agents、查询状态/历史”的统一入口。

---

### 1) 依赖注入

```csharp
// 注册 Session 服务（workflow YAML 会在启动时解析）。
services.AddAevatarCognitiveSessions(options =>
{
    options.WorkflowsDirectory = "~/.aevatar/workflows";
    options.LazyLoadRoles = true;
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
  启动会话（`StartSessionRequest`），加载 workflow YAML 并解析角色节点。
  - `workflow_name` 可是文件路径，或目录中的名字（自动补 `.yaml/.yml/.json`）。
- **GET** `/api/sessions/{sessionId}`  
  获取 `SessionState`（含 workflow_path 与 roles）。
- **GET** `/api/sessions/{sessionId}/agents`  
  获取角色列表（`SessionRole`）。
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
  读取 `ExecutionTrace`（如果配置了 `IExecutionTraceStore` 并提供 `execution_id` tag）。
- **GET** `/api/workflows`  
  列出可用 workflow（来自 workflows 目录）。
- **GET** `/api/sessions`  
  列出 session 资源（依赖 `IMemoryStore` 的 session scope）。

---

### 4) 持久化与多级数据

- `SessionState`：通过 `IStateStore<SessionState>` 持久化。
- `SessionRole.loaded`：lazy 模式下为 false，首次读取 state/histories 会触发加载。
- `MemoryStore`：
  - `scope=session`：全会话聚合记忆  
  - `scope=private_agent`：单 Agent 记忆
- `ExecutionTraceStore`：可选，提供 trace 读取能力（需 tags 中存在 execution_id）。
