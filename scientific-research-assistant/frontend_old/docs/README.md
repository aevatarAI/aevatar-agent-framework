# Frontend Structure（clean split）

目标：保持 **streaming chat 的性能**（token append 不刷新整页），同时让 `App.tsx` 可维护。

## 目录结构

```
frontend/src/
  App.tsx                          # 纯 view：布局 + 组装 panels/components

  app/
    useAppController.ts            # 单入口 controller hook（聚合状态/行为）
    messageStore.ts                # MessageStore：按 message 粒度订阅，避免全量 rerender
    jsonPatch.ts                   # STATE_DELTA 的最小 patch 应用（best-effort）
    skillsSync.ts                  # /api/skills/sync 轮询逻辑（拆出去减小 controller）
    uiBootstrap.ts                 # 刷新重连 bootstrap：meta/tools/run-steps 快照应用
    messageId.ts                   # messageId 解析与 agent display name

  components/
    ChatMessageRow.tsx             # 单条消息渲染（含 streaming/plainText → markdown 切换）
    DagGraphView.tsx               # DAG mini graph view（被 DagPanel 复用）

  panels/
    GoalsPanel.tsx                 # Goals 编辑/保存
    DagPanel.tsx                   # DAG 列表 + explain + graph（可新页打开）
    TracePanel.tsx                 # trace 预览
    BriefPanel.tsx                 # brief 卡片
    DeliveryCenterPanel.tsx        # delivery center 卡片
    ComputePanel.tsx               # compute 决策卡（MVP）
    Composer.tsx                   # 输入框（支持 toAgents + uploads）
    ApiKeyModal.tsx                # (Web host) iframe 集成 @aevatar/secrets-ui（示范复用）

  pages/
    FilesPage.tsx                  # Session 文件管理页（tree + editor + directory guide）
```

## 关键点

- **性能**：`MessageStore` + `ChatMessageRow` 用 `useSyncExternalStore` 做“按消息订阅”，streaming 只刷新对应行。
- **约束**：`panels/` 保持 ≤ 8 文件；通用/可复用组件放 `components/`；状态与副作用集中在 `app/`。

## Secrets UI 复用（示范）

- Web host 的 `ApiKeyModal.tsx` 通过 iframe 打开 `public/secrets-ui/index.html`
- `public/secrets-ui/*` 来自 `packages/aevatar-secrets-ui/dist`（静态可复用 UI）


