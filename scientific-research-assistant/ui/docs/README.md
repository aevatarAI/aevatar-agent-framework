## Shared UI (`ui/src`) 说明

本目录是 **Scientific Research Assistant** 的共享前端 UI（Web 前端与 Obsidian 插件共用同一套 React 组件）。

### 路由约定（Query Router）

`SraWorkbenchApp` 使用轻量 query router（避免引入复杂路由依赖）：

- `?view=files&session={sessionId}&path={relativePath?}`：Files 页面（需要 loopback sidecar 才启用 Files API）
- `?view=dag&session={sessionId}`：DAG 页面（兼容 `view=graph`）
- `?session={sessionId}`：预选 session（用于从外部链接直接打开某个 session）

### Graph 页面数据源

Graph 页面/面板主要走两类后端 API：

- **兼容 DAG 视图**
  - `GET /api/sessions/{sessionId}/dag`：渲染图节点/边（依赖 -> 被依赖：`from -> to`）
  - `GET /api/sessions/{sessionId}/dag/{nodeId}/explain`：依赖解释（provable/missing/cycle）
- **KnowledgeGraph 增强**
  - `GET /api/sessions/{sessionId}/graph/{nodeId}/chain`：知识链 Markdown（用于 UI 侧 “Knowledge chain”）


