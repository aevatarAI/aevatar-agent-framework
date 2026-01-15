# Cognitive Streaming

本目录提供 **跨应用可复用的流式广播工具**，用于 SSE/AG‑UI 事件 fan‑out。

## 组件

- `BroadcastEventHub<T>`：多订阅广播（pub‑sub），可选 replay buffer。

## 适用场景

- 多个 SSE 连接同时订阅同一会话事件流
- 需要“快照优先、replay 可控”的流式输出通道

## 约束

- 事件结构保持外部协议稳定（AG‑UI / NDJSON 由上层投影）
- 禁止在 Publish/Subscribe 中抛异常（边界层必须 best‑effort）


