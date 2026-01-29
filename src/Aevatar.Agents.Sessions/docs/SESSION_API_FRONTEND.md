# Session API 前端对接指南

本文说明如何在浏览器或前端应用中调用 Session API。

## 流程概览
- 端到端流程请见 `docs/SESSION_RUNTIME_FLOW.md`。

## 协议与 Content-Type
- 所有请求/响应均为 **二进制 Protobuf**。
- `Content-Type` 必须为 `application/x-protobuf`。
- 响应体是原始字节流，需要用 Protobuf 解码。

## Proto 协议
生成客户端代码：
- `src/Aevatar.Agents.Sessions/session_messages.proto`

依赖（需一起编译）：
- `src/Aevatar.Agents.AI.Abstractions/ai_abstractions_messages.proto`
- `src/Aevatar.Agents.Abstractions/memory.proto`
- `src/Aevatar.Agents.Abstractions/execution_trace.proto`
- `google/protobuf/timestamp.proto`

## 接口列表

### Workflows
- **GET** `/api/workflows` -> `WorkflowListResponse`

### Sessions
- **POST** `/api/sessions` -> `StartSessionRequest` / `StartSessionResponse`
  - 必填：`workflow_name`（文件路径或 workflows 目录中的名字）。
  - 可选：`session_id`。
- **GET** `/api/sessions` -> `SessionMemoryResourcesResponse`
  - 依赖服务端已注册 `IMemoryStore`。
  - Query：`limit`（默认 200，范围 1-2000）。
- **GET** `/api/sessions/{sessionId}` -> `SessionState`
- **GET** `/api/sessions/{sessionId}/agents` -> `SessionAgentsResponse`（包含 `SessionRole` 列表）
- **GET** `/api/sessions/{sessionId}/agents/states` -> `SessionAgentStatesResponse`
  - Query：`include_history`（默认 true），`history_limit`（默认 50，范围 1-500）。
- **GET** `/api/sessions/{sessionId}/agents/histories` -> `SessionAgentHistoriesResponse`
  - Query：`history_limit`（默认 50，范围 1-500）。

### Memory
- **GET** `/api/sessions/{sessionId}/memory/session` -> `SessionMemoryEntriesResponse`
  - Query：`limit`（默认 200，范围 1-2000）。
- **GET** `/api/sessions/{sessionId}/memory/agents/{agentId}` -> `SessionMemoryEntriesResponse`
  - Query：`limit`（默认 200，范围 1-2000）。
- **GET** `/api/sessions/{sessionId}/memory/resources` -> `SessionMemoryResourcesResponse`
  - Query：`scope_type`（默认 `Session`），`limit`（默认 200，范围 1-2000）。
  - `scope_type` 不区分大小写，允许：
    `Unspecified`, `PrivateAgent`, `Session`, `Run`, `Execution`, `Graph`, `Tenant`。

### Trace
- **GET** `/api/sessions/{sessionId}/trace` -> `SessionTraceResponse`
  - 依赖服务端已注册 `IExecutionTraceStore` 且 `SessionState.tags` 包含 `execution_id`。

## 错误处理
- `404 Not Found`：session/agent 不存在，或服务未配置可选存储（memory/trace）。
- 非 2xx 表示校验或服务端错误，请先处理状态码再尝试解码响应体。

## 示例（TypeScript + @bufbuild/protobuf）

```ts
import { create, fromBinary, toBinary } from "@bufbuild/protobuf";
import {
  StartSessionRequestSchema,
  StartSessionResponseSchema,
  SessionStateSchema,
} from "./gen/session_messages_pb";

export async function startSession() {
  const req = create(StartSessionRequestSchema, {
    workflowName: "workspace_mesh",
  });

  const res = await fetch("/api/sessions", {
    method: "POST",
    headers: { "content-type": "application/x-protobuf" },
    body: toBinary(StartSessionRequestSchema, req),
  });
  if (!res.ok) throw new Error(`StartSession failed: ${res.status}`);

  const bytes = new Uint8Array(await res.arrayBuffer());
  return fromBinary(StartSessionResponseSchema, bytes);
}

export async function getSessionState(sessionId: string) {
  const res = await fetch(`/api/sessions/${sessionId}`);
  if (!res.ok) throw new Error(`GetSessionState failed: ${res.status}`);
  const bytes = new Uint8Array(await res.arrayBuffer());
  return fromBinary(SessionStateSchema, bytes);
}
```

## Tips
- 历史记录可能较大，建议用 `include_history=false` 或降低 `history_limit`。
- Memory/Trace 接口属于可选能力，未接入时可能返回 404。
