# Session Runtime Architecture

## 结构树 (Structure)

```
src/Aevatar.Agents.Sessions/Runtime/
├── AgentBootstrapper.cs        # LLM 初始化 + YAML 配置 + 状态上报
├── AgentMessageStreamResolver.cs # 统一解析 Agent message stream
├── SessionAgUiStream.cs        # AG-UI 投影 + SSE 事件输出
├── SessionRuntime.cs           # Session 生命周期 + 调度 + context 缓存
├── SessionRuntimeOptions.cs    # Runtime 默认参数
├── SessionWorkflowCatalog.cs   # Workflow 名称解析 + 单角色 workflow 生成
├── WorkflowMeshCompiler.cs     # YAML -> MeshDefinition 编译
├── WorkflowMeshService.cs      # Workflow graph + agent 实例化
└── docs/
    └── ARCHITECTURE.md
```

## 架构决策 (Decisions)

1. **Session 运行时下沉**
   - `SessionRuntime` 统一处理 Create/Send/Stream/cleanup。
   - Workshop 只做展示与 API wiring，避免业务逻辑重复。

2. **AG-UI 投影统一入口**
   - `SessionAgUiStream` 负责 ExecutionTrace -> AG-UI、Chat stream 等聚合。
   - 输出事件包含 `SESSION_STATUS`，供前端可观测。

3. **Agent 初始化集中化**
   - `AgentBootstrapper` 统一 LLM provider 初始化、YAML 应用与工具注册状态。
   - 减少多处重复配置逻辑。

4. **Workflow 解析归一**
   - `SessionWorkflowCatalog` 统一 workflow 名称/目录/模板。
   - `WorkflowMeshService` 提供 graph 快照与 agent 预热。

## 依赖关系 (Dependencies)

- `SessionRuntime` -> `CognitiveSessionService`, `AgentBootstrapper`, `SessionAgUiStream`
- `SessionAgUiStream` -> `AgUiTraceProjector`, `IMessageStream`
- `WorkflowMeshService` -> `WorkflowMeshCompiler`, `RoleAgentFactory`, `IGAgentActorManager`

## 变更日志 (Changelog)

- 2026-01-28: 新增 Runtime 模块，拆出 SessionRuntime/Stream/WorkflowMesh 能力。
