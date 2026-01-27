# Chat Streaming UI 最佳实践（参考 VibeResearching）

> 说明：VibeResearching 并非“最终最佳实践”，但它在 **SSE + AG-UI** 的 streaming 展示上已经具备高可用与高性能特征。下面给出可迁移的最佳实践清单，并补齐更通用的 UI 设计要点。

## 适用范围

- **协议**：AG-UI (`TEXT_MESSAGE_START/CONTENT/END`) + SSE `text/event-stream`
- **目标**：让用户稳定看到“逐步生成”的体验，同时避免 UI 卡顿与滚动抖动

## 参考实现（VibeResearching 的可迁移要点）

- **Snapshot-first**：SSE 连接后先下发 `MESSAGES_SNAPSHOT`，再进入 live stream，避免依赖 replay。
- **高频更新隔离**：使用独立 store / 轻量 state 存放 streaming 文本，减少全局重绘。
- **滚动节流**：auto-scroll 使用 token 计数变化触发，配合节流防止 layout thrash。

## 最佳实践清单（UI + SSE）

1. **Snapshot-first + 无 replay**
   - 连接建立 → 先接收 `MESSAGES_SNAPSHOT` → 再接 live stream。
   - 刷新/重连时能立即恢复已生成内容，避免空白 UI。

2. **流式状态隔离**
   - 把 streaming 文本放在独立 store 或 per-message buffer，避免全局 re-render。
   - UI 只订阅必要的 messageId，减少高频更新扩散。

3. **微批量刷新（Micro-batching）**
   - 不要每个 token 都触发 DOM 更新。
   - 用 `requestAnimationFrame`/`setTimeout` 合并多次 delta，再统一渲染。

4. **渲染策略：streaming 用纯文本，结束再 Markdown**
   - streaming 期间使用 `textContent`（快、稳）。
   - `TEXT_MESSAGE_END` 后再做 Markdown 渲染。

5. **自动滚动策略**
   - 仅当用户在底部附近才 auto-scroll。
   - 使用节流或 token 计数变化触发滚动（避免每次 delta 都滚）。

6. **视觉反馈**
   - streaming 状态使用光标闪烁/高亮边框等轻量动效。
   - 展示 token 计数或进度条有助于“可预期感”。

7. **断连与降级**
   - `EventSource` 断连时展示状态并允许自动重连。
   - 如果没有 `TEXT_MESSAGE_CONTENT`（仅最终响应），用 fallback streaming 分片补体验。

## 关键配置：stream_chunk_every_n（后端 → UI 节流）

可以在 `@ai_messages.proto (26)` 设置 `stream_chunk_every_n`，用来控制前端一次输出多少 token/chunk。

在 `src/Aevatar.Agents.AI.Core/ai_messages.proto` 的 `ChatRequestEvent` 中定义：

```
int32 stream_chunk_every_n = 8;
```

- 语义：**每 N 个 token/chunk 才触发一次 `ChatStreamChunkEvent`**。
- 作用：降低前端刷新频率，控制 UI 重绘成本。
- 建议值：`4 ~ 8` 通常更平衡；`1` 最实时但 UI 压力更大。

> 在 Workshop 中已开放 `stream_chunk_every_n` 的 UI 配置入口，可直接调节前端一帧输出节奏。

## Workshop 的落地改进（对齐以上最佳实践）

- 引入 streaming micro-batch（`requestAnimationFrame` 合并刷新）。
- 自动滚动仅在“接近底部”时触发，防止用户阅读被打断。
- Chat 面板支持设置 `stream_chunk_every_n`，并在后端透传到 `ChatRequestEvent`。

