# Chat Streaming 宪法（强制流程）

版本: v1
适用范围: 所有基于 Aevatar Agent Framework 的聊天类 App（含演示与生产）

## 0. 核心定义

- **GAgent**: 业务逻辑主体，必须运行在 Actor 包装器内。
- **GAgentActor**: 运行时承载与事件路由。
- **ChatRequestEvent**: 前端发起的聊天请求事件（Protobuf）。
- **ChatStreamChunkEvent**: 流式增量事件（Protobuf）。
- **ChatResponseEvent**: 最终响应事件（Protobuf）。
- **AG-UI**: 前端统一消费的事件协议（SSE 输出）。

## 1. 不可违背的铁律

1) **所有聊天必须由 Event Handler 触发**  
   - 任何聊天请求只能通过 `ChatRequestEvent` 进入系统。
   - 严禁绕过 Event Handler 直接调用 LLM。

2) **Agent 必须由 GAgent Actor Factory 创建**  
   - 只能通过 `IGAgentFactory` / `IGAgentActorManager` 创建并激活。
   - 严禁 `new` 一个 Agent 然后直接调用。

3) **跨边界类型必须用 Protobuf**  
   - Chat 请求、流式增量、最终响应与状态都必须是 Protobuf。

## 2. 强制执行流程（从前端到 UI）

```
Frontend
  ↓ (HTTP) ChatRequestEvent
Session/API
  ↓ (PublishEventAsync)
GAgentActor
  ↓
[EventHandler] HandleChatRequestEvent
  ↓ ChatStreamAsync
  ↓ ChatStreamChunkEvent (N token/chunk)
SessionStore.HandleAssistantChunk
  ↓ AG-UI TEXT_MESSAGE_CONTENT
SSE /agui/events
  ↓
Frontend streaming UI
```

收尾流程（必须）：

```
ChatResponseEvent
  ↓
SessionStore.HandleChatResponseAsync
  ↓ AG-UI TEXT_MESSAGE_END + 消息快照更新
```

## 3. 事件契约（必备字段与职责）

- `ChatRequestEvent`
  - 必填: `request_id`, `message`, `timestamp`
  - 可选: `stream_chunk_every_n`（控制 chunk 频率）

- `ChatStreamChunkEvent`
  - 必填: `request_id`, `content`, `chunk_index`, `timestamp`

- `ChatResponseEvent`
  - 必填: `request_id`, `content`, `tokens_used`

## 4. 端到端职责分配

- **Frontend**
  - 只发 `ChatRequestEvent`（HTTP 或 RPC 封装均可）
  - 只消费 AG-UI（SSE）

- **Session/API**
  - 从请求体构造 `ChatRequestEvent`
  - 通过 `PublishEventAsync` 投递给 Actor

- **Agent**
  - Event Handler 触发 `ChatStreamAsync`
  - 按 `stream_chunk_every_n` 控制 chunk 输出

- **SessionStore / Projector**
  - 订阅 `ChatStreamChunkEvent` → `TEXT_MESSAGE_CONTENT`
  - 订阅 `ChatResponseEvent` → `TEXT_MESSAGE_END` + snapshot

## 5. UI 流式渲染规范（强制）

1) **Micro-batching**
   - 必须合并多次 delta，再刷新 DOM（`requestAnimationFrame` 或 `setTimeout`）。

2) **streaming 状态只用纯文本**
   - `textContent` 更新，避免 Markdown 解析开销。
   - `TEXT_MESSAGE_END` 后再转 Markdown。

3) **自动滚动保护**
   - 仅当用户在底部附近才 auto-scroll。

4) **Snapshot-first**
   - SSE connect 先接 `MESSAGES_SNAPSHOT`，再进入 live stream。

## 6. 配置规范（stream_chunk_every_n）

- 定义位置: `src/Aevatar.Agents.AI.Core/ai_messages.proto`
  - `@ai_messages.proto (26) stream_chunk_every_n`
- 语义: 每 N 个 token/chunk 才触发一次 `ChatStreamChunkEvent`
- 默认建议: 4 ~ 8（更平衡）
- UI 可覆写: 前端可在请求中设置该值，但必须有上限保护

## 7. 降级与容错（允许，但必须显式）

- 若未产生 `ChatStreamChunkEvent`：
  - 允许在 `ChatResponseEvent` 中做 fallback streaming（分片补体验）
  - 仍必须发 `TEXT_MESSAGE_END`

## 8. 禁止事项（硬性禁止）

- 跳过 `ChatRequestEvent`，直接调用 LLM
- 绕过 Actor Factory 创建 Agent
- 前端直连内部消息流（必须经由 AG-UI）
- 非 Protobuf 类型跨边界传输

## 9. 最小合规清单（App 交付必检）

- [ ] 前端发送 `ChatRequestEvent`
- [ ] Agent 由 Actor Factory 创建
- [ ] `HandleChatRequestEvent` → `ChatStreamAsync`
- [ ] `ChatStreamChunkEvent` → `SessionStore.HandleAssistantChunk`
- [ ] `TEXT_MESSAGE_CONTENT` 与 `TEXT_MESSAGE_END` 正常输出
- [ ] UI 使用 micro-batching + snapshot-first
- [ ] `stream_chunk_every_n` 可配置且被限制范围

