# Aevatar.Workshop Architecture

## 结构树 (Structure)

```
examples/Aevatar.Workshop/
├── wwwroot/                       # 前端 UI (Sessions / Role / Hierarchy / Workflow)
│   ├── app-core.js                # 全局状态 + 通用工具
│   ├── app-graph.js               # DAG/层级图渲染
│   ├── app-roles.js               # Role 定义 + Role Chat + Hierarchy 交互
│   ├── app-tools.js               # Role Tools/YAML Builder
│   ├── app-sessions.js            # Session Chat + SSE
│   ├── app-workflow.js            # Workflow Mesh UI
│   ├── app-nav.js                 # 顶部导航与 tabs
│   ├── app-init.js                # 事件绑定与启动流程
│   ├── config.html                # LLM Provider Config 页面
│   └── config.js                  # Config 页逻辑
├── Program.cs                     # API + DI + SSE
├── WorkshopApiModels.cs           # LLM Config DTO
├── appsettings.json               # 演示配置
└── README.md
```

## 架构决策 (Decisions)

1. **UI 顶部导航拆分**
   - Sessions、Role、Hierarchy、Workflow 独立分区。
   - Role 负责定义/实例化 + Chat，Hierarchy 专注 Parent-Child 编排。

2. **Session 运行时下沉到框架**
   - Workshop 使用 `SessionRuntime` + `SessionUiEndpoints` 处理 Session/AG-UI 流。
   - `SessionAgUiStream` 负责 trace + stream 投影与状态上报。

3. **Workflow Catalog 统一职责**
   - `SessionWorkflowCatalog` 负责名称解析、目录选择、单角色 workflow 生成。
   - Session Runtime 只关心“启动哪个 workflow”。

4. **消息流解析归一**
   - `AgentMessageStreamResolver` 统一获取消息流。
   - 支持外部 provider，缺省回退本地 registry。

5. **Tooling 下沉**
   - `AgentToolCatalog` 统一工具列表/注册逻辑。
   - Workshop 只负责调用与展示。

6. **Session 生命周期清理**
   - `SessionCleanupService` 定期清理 idle Session Stream。
   - 避免订阅泄露与长期占用内存。

7. **清理策略可配置**
   - 通过 `SessionIdleTimeoutMinutes` / `SessionCleanupIntervalSeconds` 控制节奏。
   - 后台服务定期触发清理。

8. **Role Workspace 独立模块**
   - `Aevatar.Agents.Workspaces` 负责角色编排/AG-UI 聚合/Role API。
   - Workshop 仅挂载 `MapRoleWorkspaceEndpoints`。

9. **Sisyphus 为根**
   - Root 仍默认为 Sisyphus，但用户可直接与当前 Role 实例对话。
   - Sisyphus 通过 `publish_event` 可路由到子角色。

10. **Hermes 角色创建**
   - Hermes 使用 `file_read` / `file_write` 写入 `~/.aevatar/agents`。
   - 新角色 YAML 必须显式包含工作区模块。

11. **层级操作统一入口**
   - 使用 `ActorHierarchyCoordinator` 建立 parent-child 关系，确保同步一致。

12. **Hierarchy 编排交互规则**
   - 画布只负责 Link/Unlink：按顺序点击 Parent → Child。
   - 点击空白区域会清空选择，按钮随选择状态亮起/禁用。
   - 删除仅影响实例（Deactivate actor），不删除 YAML。

13. **Config 页面复用**
   - Sessions 页底部提供 Config 入口。
   - Config 页面复用 VibeResearching 的 LLM Provider UI 交互范式（列表 + 详情）。

## 开发规范 (Guidelines)

- Chat Streaming 必须遵循“ChatRequestEvent -> ChatStreamAsync -> ChatStreamChunkEvent”流程。
- Role YAML **必须**包含：
  - `workspace_group_agui`
  - `workspace_group_task`
  - `workspace_chat_trace` (可选但推荐)
- Sisyphus 需要在 YAML allowlist 中包含 `publish_event` 才能启用委派。
- Hermes 写入 YAML 时，遵循统一字段：`id / name / provider / tools / system_prompt / extensions.event_modules`。
- 委派展示：
  - Chat 中会插入 `system` 事件泡泡，提示 publish_event 路由。
  - 同时写入 Delegation Log（Hierarchy 页面右侧）。

## 交互流程 (Role Chat)

- Role 页面右侧为可折叠 Chat Drawer，绑定当前选中 Role 实例。
- 保存 YAML 会触发 **重建实例 + 清空 Role Chat**（不保留旧消息）。
- Role Chat SSE：`/api/roles/{role}/agui/events` 仅推送该 role 的消息。

## 变更日志 (Changelog)

- 2026-01-26: 引入 Role Workspace、Sisyphus/Hermes YAML、顶层导航重排、AG-UI 群组流。
- 2026-01-27: Role/Hierarchy 拆分，Role Chat Drawer + Role YAML 重建实例。
- 2026-01-27: Sessions 切换到 Aevatar.Agents.Sessions，新增 Session Runtime + Endpoints。
- 2026-01-27: 抽出 Workflow Catalog，减少 Session Runtime 责任。
- 2026-01-27: 引入 Stream Resolver，弱化对 Local runtime 的依赖。
- 2026-01-27: Session Stream 增加订阅释放与 idle 清理。
- 2026-01-27: 增加清理后台服务与可配置参数。
- 2026-01-28: SessionRuntime/Stream/Tooling 下沉到框架，Workshop 变薄。
- 2026-01-28: Role Workspace 下沉为 `Aevatar.Agents.Workspaces`，模块名统一为 `workspace_*`。