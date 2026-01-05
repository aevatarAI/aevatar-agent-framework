# Aevatar.Agents.Runtime.ProtoActor

Proto.Actor 运行时适配：偏高吞吐、低开销 Actor 执行。

## 职责
- 为特定 Actor 系统提供运行时适配，使同一 Agent 代码可在该运行时运行。
- 把框架的 stream/订阅语义桥接到运行时机制。
- 处理生命周期、激活、以及父子关系等运行时行为。

## 主要功能
- Proto.Actor 高吞吐运行时适配。
- 显式 Actor 生命周期映射。

## 公开 API 入口（自动扫描）
- `ProtoActorMessageStream`
- `ProtoActorGAgentActorFactory`
- `ProtoActorGAgentActor`
- `AgentActor`
- `SetGAgentActor`
- `HandleEventMessage`
- `ProtoActorMessageStreamRegistry`
- `ProtoActorGAgentActorManager`
- `ProtoActorSubscriptionManager`
- `AevatarBuilderExtensions`

## 是否建议发布为 NuGet
- **建议**：可选。
- **原因**：属于适配/集成模块；发布为独立 NuGet 包可让使用方按需引入，避免拉入不必要依赖。
- **打包建议**：外部依赖尽量隔离在本包内，避免污染 Core。

## 构建

```bash
dotnet build src/Aevatar.Agents.Runtime.ProtoActor/Aevatar.Agents.Runtime.ProtoActor.csproj
```

## 单元测试

- 测试覆盖面评审与入口请见 `docs/Tests.md`。

## 文档

- 项目文档：`docs/`
- 仓库级文档：仓库根目录 `docs/`

## 约束/约定

- 跨边界类型（State/Event/Config 等）必须使用 **Protocol Buffers** 定义。
