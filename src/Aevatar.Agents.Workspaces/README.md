# Aevatar.Agents.Workspaces

角色工作区（Role Workspace）模块，用于**编排多角色 Agent**、维护角色关系图，
并向外提供统一的聊天与 AG-UI 事件流能力。

## 主要职责
- 角色实例生命周期：创建、重建、删除。
- 角色层级关系：Link / Unlink 形成角色图。
- Chat 路由：对 root 或指定 role 发送请求并输出流式响应。
- AG-UI 聚合：统一收敛多角色消息与流式片段。
- Workspace 事件模块：`workspace_*` 事件体系。

## 关键组成
- `Core/RoleWorkspaceService`：角色实例与关系图管理、Chat 路由。
- `Core/RoleWorkspaceAgent`：RoleAIGAgent 派生，支持 self-handling + publish_event。
- `Core/RoleWorkspaceOptions`：运行参数（root、prompt、temperature、snapshot 等）。
- `Hubs/RoleAgUiHub`：AG-UI 事件聚合与快照。
- `Events/RoleWorkspaceEventModules`：workspace_chat_trace / workspace_group_agui / workspace_group_task / workspace_ping。
- `Endpoints/RoleWorkspaceApiEndpoints`：HTTP + SSE 接口。
- `Bootstrap/RoleWorkspaceYamlBootstrap`：默认 root/hermes YAML 生成（可选）。
- `Messages/workspace_messages.proto`：Ping/Pong 事件契约。

## 快速接入

```csharp
builder.Services.AddAevatarRoleWorkspace(options =>
{
    options.RootRole = "sisyphus";
    options.EnableAgentYaml = true;
});

// 可选：生成默认角色 YAML
RoleWorkspaceYamlBootstrap.EnsureDefaultRoleYaml(builder.Services
    .BuildServiceProvider()
    .GetRequiredService<IOptions<RoleWorkspaceOptions>>().Value);

app.MapRoleWorkspaceEndpoints();
```

## 主要接口（HTTP/SSE）
- `GET /api/roles`：角色与实例列表
- `GET /api/roles/graph`：角色关系图
- `POST /api/roles/instances`：创建/确保角色实例
- `POST /api/roles/link` / `POST /api/roles/unlink`：关系管理
- `POST /api/roles/chat`：向 root 发送 chat
- `POST /api/roles/{role}/chat`：向指定 role 发送 chat
- `GET /api/roles/agui/events`：全局 AG-UI SSE
- `GET /api/roles/{role}/agui/events`：单角色 AG-UI SSE

## 依赖关系
- `Aevatar.Agents.AI.Core`：RoleAIGAgent 与事件模块
- `Aevatar.Agents.AGUI`：AG-UI 事件与 SSE writer
- `Aevatar.Agents.Cognitive.Streaming`：BroadcastEventHub
- `Aevatar.Agents.Tooling`：工具扫描/注册

## 相关文档
- `docs/ARCHITECTURE.md`

