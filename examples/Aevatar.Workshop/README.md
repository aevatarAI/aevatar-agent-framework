# Aevatar.Workshop

一个 **全功能展示型 Web Demo**，用于演示 AIGAgentBase 与 RoleAIGAgent 的能力：

- **Chat + Streaming**：事件驱动聊天与流式输出
- **History + Compaction**：State.History + history_summary
- **MemoryStore**：Agent/Session 级别存储
- **Event Handlers**：方法级 `[EventHandler]` + YAML 模块级 `IEventModule`
- **AG-UI Timeline**：ExecutionTrace → AG‑UI 投影
- **MCP / Skills / dotnet-file**：能力开关与可见性展示
- **Role & Hierarchy**：Sisyphus 根角色 + 画布编排 + Hermes 角色创建
- **Agent YAML Builder**：通过 YAML 定义新 agent 并直接聊天
- **Workflow YAML**：通过 Cognitive Mesh DSL 生成 DAG 图

## ✅ 前置：配置 LLM API Key（推荐 Aevatar.Config）

1) 启动配置工具：

```bash
dotnet run --project apps/Aevatar.Config/Aevatar.Config.csproj
```

2) 打开：

- `http://localhost:6677`

3) 在 UI 中配置默认 provider 与 API key（写入 `~/.aevatar/secrets.json`）。

> Demo 会通过 `AddAevatarUserConfig()` 自动读取该 secrets。

## 运行

```bash
dotnet run --project examples/Aevatar.Workshop/Aevatar.Workshop.csproj
```

默认地址：`http://127.0.0.1:5691`

## UI 导航

- **Sessions**：Workflow Session 聊天 + History/Memory/Handlers。
- **Role & Hierarchy**：Sisyphus 统一入口 + YAML 角色实例化 + 层级编排。
- **Tools & YAML**：工具能力开关 + YAML Builder。
- **Workflow**：Cognitive Mesh DAG。

## Role & Hierarchy

- `~/.aevatar/agents` 角色 YAML 可视化编辑与保存。
- 通过 YAML 在画布上实例化角色，使用 `ActorHierarchyCoordinator` 编排层级关系。
- Sisyphus 作为根角色，用户始终与其对话；Sisyphus 可以 `publish_event` 委派给子角色。
- Hermes 角色负责创建新角色 YAML（需要 `file_read` / `file_write` 工具权限）。

## YAML Agent Builder

- 输入符合 `AgentYamlConfig` 的 YAML（`id` 必填）。
- 点击 **Save & Chat** 会写入 `~/.aevatar/agents/{id}.yaml` 并创建 Workflow Session。
- 会话使用 `RoleAIGAgent` + `IEventModule` 装配，可在 **Handlers** 面板查看模块列表。

## Workflow YAML (Cognitive Mesh)

- 输入 DSL YAML，点击 **Load Workflow**。
- 后端使用 `CognitiveDslCompiler` 解析并生成 **DAG Graph**，并通过 `IGAgentActorManager` 创建节点对应的 agents。
- YAML 保存到 `~/.aevatar/workflows/{name}.yaml`。

## Streaming UI（Chat）

- Chat 面板支持流式批量刷新与自动滚动保护。
- `stream_chunk_every_n` 可通过 Chat 面板的 **Stream batch** 设置，或在 `appsettings.json` 的 `Aevatar.Workshop:StreamChunkEveryN` 配置默认值。
- 连接就绪以 `MESSAGES_SNAPSHOT` 为准，`SSE_CONNECTED` 作为兜底。
- Best practices: `docs/SESSION_RUNTIME_STREAMING_AGUI_BEST_PRACTICES.md`.

## 重要说明

- 本 Demo 不绑定 `:5000` 端口。
- YAML 与 MemoryStore 均为 **best‑effort**，不影响主流程。
- Session 清理可通过 `Aevatar.Workshop:SessionIdleTimeoutMinutes` 与 `SessionCleanupIntervalSeconds` 调整。
- 架构说明见：`docs/ARCHITECTURE.md`。

