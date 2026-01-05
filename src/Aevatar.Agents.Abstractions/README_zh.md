# Aevatar.Agents.Abstractions

Aevatar Agent Framework 的**运行时无关**契约层：接口、Attribute、以及跨边界的 Protobuf 消息定义。

## 职责
- 定义 Agent/Actor 的核心契约：接口、Attribute、Envelope 等。
- 提供跨边界数据的 Protobuf Schema（State / Event / Config）。
- 保持运行时无关：同一套 Agent 代码可运行在多种 Runtime 上。

## 主要功能
- 运行时无关设计（多运行时）。
- 事件驱动架构。
- 跨边界类型 Protobuf 优先。

## 公开 API 入口（自动扫描）
- `ResourceContext`
- `ResourceMetadata`
- `StreamingOptions`
- `IStreamNotFoundHandler`
- `IStateGAgent`
- `IGAgentActorManager`
- `ActorHealthStatus`
- `ActorManagerStatistics`
- `IEventDeduplicator`
- `DeduplicationStatistics`

## 是否建议发布为 NuGet
- **建议**：是（建议作为独立 NuGet 包发布）。
- **原因**：属于基础能力模块，通常会被下游系统直接引用。
- **打包建议**：核心包保持轻依赖；数据库/Provider 等外部集成拆成独立可选包。

## 构建

```bash
dotnet build src/Aevatar.Agents.Abstractions/Aevatar.Agents.Abstractions.csproj
```

## 单元测试

- 测试覆盖面评审与入口请见 `docs/Tests.md`。

## 文档

- 项目文档：`docs/`
- 仓库级文档：仓库根目录 `docs/`

## 约束/约定

- 跨边界类型（State/Event/Config 等）必须使用 **Protocol Buffers** 定义。
