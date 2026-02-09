# Aevatar.Agents.Workspaces Architecture

## 结构树 (Structure)

```
src/Aevatar.Agents.Workspaces/
├── Core/
│   ├── RoleWorkspaceService.cs     # 角色实例化 + Link/Unlink + Chat 路由
│   ├── RoleWorkspaceAgent.cs       # RoleAIGAgent 派生 (self-handling + publish_event)
│   └── RoleWorkspaceOptions.cs     # Workspace 运行参数
├── Endpoints/
│   ├── RoleWorkspaceApiEndpoints.cs # /api/roles* endpoints (JSON + SSE)
│   └── RoleWorkspaceApiModels.cs    # Role API DTO
├── Events/
│   └── RoleWorkspaceEventModules.cs # workspace_* event modules
├── Hubs/
│   └── RoleAgUiHub.cs              # 多角色 AG-UI 聚合
├── Models/
│   └── RoleWorkspaceModels.cs      # RoleGraph/Instance 快照
├── Bootstrap/
│   └── RoleWorkspaceYamlBootstrap.cs # Root/Hermes YAML bootstrap
├── Messages/
│   └── workspace_messages.proto    # WorkspacePing/WorkspacePong
├── docs/
│   └── ARCHITECTURE.md
├── Aevatar.Agents.Workspaces.csproj
└── ServiceCollectionExtensions.cs  # AddAevatarRoleWorkspace
```

## 职责边界 (Responsibilities)

- **RoleWorkspaceService**：管理角色实例、层级关系、Chat 路由与 AG-UI 消息汇聚。
- **RoleAgUiHub**：聚合多角色的 AG-UI 事件流（snapshot + streaming）。
- **RoleWorkspaceEventModules**：提供 `workspace_*` 事件模块（group_agui / group_task / chat_trace / ping）。
- **RoleWorkspaceApiEndpoints**：对外提供 Role 管理与 Role Chat 的 HTTP 接口。
- **RoleWorkspaceYamlBootstrap**：生成默认 Root/Hermes YAML（演示可用）。

## 关键约定 (Conventions)

- 角色事件模块名统一为 `workspace_*`。
- 委派日志事件：`CUSTOM: WORKSPACE_DELEGATION`。
- Ping 事件：`WorkspacePingEvent` / `WorkspacePongEvent`（Protobuf）。

## 依赖关系 (Dependencies)

- **Aevatar.Agents.AI.Core**：RoleAIGAgent 与 Event Modules。
- **Aevatar.Agents.AGUI**：AG-UI 事件 + SSE writer。
- **Aevatar.Agents.Cognitive.Streaming**：BroadcastEventHub。
- **Aevatar.Agents.Tooling**：工具扫描/注册能力（AgentToolCatalog）。
