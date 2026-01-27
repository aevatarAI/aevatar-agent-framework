# Aevatar.Workshop Architecture

## 结构树 (Structure)

```
examples/Aevatar.Workshop/
├── RoleWorkspace/                 # 角色编排领域 (YAML + 层级 + AG-UI)
│   ├── WorkshopGroupAgUiHub.cs    # 组内 AG-UI Hub (snapshot + stream)
│   ├── WorkshopRoleAgent.cs       # RoleAIGAgent 派生，支持 self-handling
│   ├── WorkshopRoleWorkspace.cs   # 角色实例化 + Link/Unlink
│   └── WorkshopRoleWorkspaceEndpoints.cs # /api/roles* endpoints
├── wwwroot/                       # 前端 UI (Sessions / Role & Hierarchy / Tools / Workflow)
│   ├── app-core.js                # 全局状态 + 通用工具
│   ├── app-graph.js               # DAG/层级图渲染
│   ├── app-roles.js               # Role Workspace UI
│   ├── app-tools.js               # Tools/YAML Builder
│   ├── app-sessions.js            # Session Chat + SSE
│   ├── app-workflow.js            # Workflow Mesh UI
│   ├── app-nav.js                 # 顶部导航与 tabs
│   ├── app-init.js                # 事件绑定与启动流程
│   ├── config.html                # LLM Provider Config 页面
│   └── config.js                  # Config 页逻辑
├── WorkshopEventModules.cs        # YAML event modules (group agui / task)
├── WorkshopAgentYamlBootstrap.cs  # sisyphus / hermes YAML bootstrap
├── Program.cs                     # API + DI + SSE
└── README.md
```

## 架构决策 (Decisions)

1. **UI 顶部导航拆分**
   - Sessions、Role & Hierarchy、Tools & YAML、Workflow 逻辑互不混淆。
   - 目的：避免用户误解“所有页面都依赖 session”。

2. **Role Workspace 独立域**
   - 通过 `RoleWorkspace` 完成 YAML 发现、实例化、层级编排。
   - 通过 `WorkshopGroupAgUiHub` 聚合多 agent 的 AG-UI 流。

3. **Sisyphus 为根**
   - 用户始终与 Sisyphus 对话。
   - 由 Sisyphus 使用 `publish_event` 将任务路由到子角色。

4. **Hermes 角色创建**
   - Hermes 使用 `file_read` / `file_write` 写入 `~/.aevatar/agents`。
   - 新角色 YAML 必须显式包含工作区模块。

5. **层级操作统一入口**
   - 使用 `ActorHierarchyCoordinator` 建立 parent-child 关系，确保同步一致。

6. **Role 编排交互规则**
   - 右侧画布只负责 Link/Unlink：按顺序点击 Parent → Child。
   - 点击空白区域会清空选择，按钮随选择状态亮起/禁用。
   - 删除仅影响实例（Deactivate actor），不删除 YAML。

7. **Config 页面复用**
   - Sessions 页底部提供 Config 入口。
   - Config 页面复用 VibeResearching 的 LLM Provider UI 交互范式（列表 + 详情）。

## 开发规范 (Guidelines)

- Chat Streaming 必须遵循“ChatRequestEvent -> ChatStreamAsync -> ChatStreamChunkEvent”流程。
- Role YAML **必须**包含：
  - `workshop_group_agui`
  - `workshop_group_task`
  - `workshop_chat_trace` (可选但推荐)
- Sisyphus 需要在 YAML allowlist 中包含 `publish_event` 才能启用委派。
- Hermes 写入 YAML 时，遵循统一字段：`id / name / provider / tools / system_prompt / extensions.event_modules`。
- 委派展示：
  - Chat 中会插入 `system` 事件泡泡，提示 Sisyphus 的 publish_event 路由。
  - 同时写入 Delegation Log（Role 页面底部）。

## 变更日志 (Changelog)

- 2026-01-26: 引入 Role Workspace、Sisyphus/Hermes YAML、顶层导航重排、AG-UI 群组流。
