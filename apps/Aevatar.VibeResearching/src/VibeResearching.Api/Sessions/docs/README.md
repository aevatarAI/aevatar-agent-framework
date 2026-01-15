# Sessions Module（后端：会话 + SSE）

本目录提供 **会话管理（in-memory）** 与 **AG-UI SSE** 的入口，并补齐“刷新不丢 UI”的工作痕迹落盘能力。

## 文件结构

```
Sessions/
  ResearchSessionManager.cs            # In-memory session registry + server-side message log
  ResearchSessionsApi.cs               # Minimal APIs + SSE /agui/events
  ResearchRunExecutor.cs               # 执行单次 run（chat / vibe）
  ResearchApiDtos.cs                   # API DTOs
  ResearchWorkspaceState.cs            # STATE_SNAPSHOT payload (UI workspace inspector)

  SessionUiSnapshotStore.cs            # artifacts/ui/ui_snapshot.json + runs/{runId}/ui_events.jsonl
  SessionUiTraceRecorder.cs            # 订阅 session.Events，把 STEP/TOOL/META/MESSAGE_END 落盘

  # Session file manager (local-only)
  - /api/sessions/{id}/files/tree      # 目录树（bounded）
  - /api/sessions/{id}/files?path=...  # 读取文件（text-like, size-limited）
  - PUT /api/sessions/{id}/files       # 保存文件（atomic write, text-like, size-limited）
```

## 关键约束

- **SSE 不 replay**：`Aevatar.Agents.Cognitive.Streaming.BroadcastEventHub(replayBufferSize=0)`，因此浏览器刷新会丢失 client-only 的展示（如 step/system 消息、tool 卡片）。
- **File-backed UI snapshot**：`SessionUiSnapshotStore` 持久化 `ui_snapshot.json`，SSE connect 时注入 bootstrap：
  - `aevatar.vibe.message_meta_snapshot`
  - `aevatar.ui.tools_snapshot`
  - `aevatar.ui.run_steps_snapshot`
- **best-effort**：任何落盘失败不得影响 run（只记日志/忽略）。


