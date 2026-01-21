# Vibe Researching Frontend Session API

面向前端对接的会话与 agent 数据查询接口（JSON）。

## 前置条件

- 会话元信息可持久化：
  - **MongoDB / SQLite**：使用 `IVibeSessionStore` + `IStateStore<>`（推荐）。
  - **无 DB**：回退到文件索引 `workspace/.data/vibe_sessions.json`（仅会话元信息）。
- 只有 agent 通过 `IGAgentActorFactory.CreateGAgentActorAsync(...)` 创建并写入状态时，才能读取到 state/history。
- `VibeAgentBase` / `ResearchAgent` 默认开启 `EnableChatHistoryInState`，因此 history 会进入 state。

## 1) 会话列表

`GET /api/sessions`

响应示例：

```json
{
  "count": 2,
  "sessions": [
    {
      "sessionId": "9b1f...e9",
      "createdAt": "2026-01-21T08:00:00.0000000Z",
      "providerName": "default"
    }
  ]
}
```

## 2) 会话 Agent 列表（持久化）

`GET /api/sessions/{sessionId}/agents`

响应示例：

```json
{
  "ok": true,
  "sessionId": "9b1f...e9",
  "providerName": "default",
  "dagId": "global",
  "coordinatorId": "sra-9b1f...-main-default",
  "workerIds": [
    "sra-9b1f...-planner-default",
    "sra-9b1f...-reasoner-default"
  ],
  "agentIds": [
    "sra-9b1f...-main-default",
    "sra-9b1f...-planner-default",
    "sra-9b1f...-reasoner-default"
  ],
  "createdAt": "2026-01-21T08:00:00.0000000Z",
  "updatedAt": "2026-01-21T08:05:00.0000000Z"
}
```

## 0) 可用 Workflow 列表

`GET /api/workflows`

响应示例：

```json
[
  "vibe_researching",
  "maker"
]
```

> 说明：`agentIds` / `workerIds` 会在对应 agent 被创建后追加写入持久化（DB 或文件索引）。

## 3) 会话内所有 Agent 的 State（可选 history）

`GET /api/sessions/{sessionId}/agents/states?includeHistory=false&historyLimit=50`

响应示例：

```json
{
  "ok": true,
  "sessionId": "9b1f...e9",
  "agents": [
    { "agentId": "sra-...-planner-default", "state": { "history": [] } }
  ],
  "missing": [
    "sra-...-dag_builder-default"
  ]
}
```

参数：
- `includeHistory`: 是否包含 history（默认 false）
- `historyLimit`: history 最大条数（默认 50）

> 注意：若未配置 DB，`IStateStore<>` 默认为内存实现，重启后 state/history 会丢失。

## 4) 单个 Agent 的 History

`GET /api/sessions/{sessionId}/agents/{agentId}/history?limit=50`

响应示例：

```json
{
  "ok": true,
  "sessionId": "9b1f...e9",
  "agentId": "sra-...-planner-default",
  "history": [
    { "role": "user", "content": "..." },
    { "role": "assistant", "content": "..." }
  ]
}
```

## 常见问题

- **如果返回 state not found**：该 agent 尚未写入 state（尚未执行过对话）。
- **如果返回 session store not configured**：请先配置 MongoDB/SQLite；否则只能使用文件索引模式（不支持 state/history 持久化）。
