# Aevatar.Agents.Core

Aevatar Agent Framework 的核心实现层：Agent 基类、事件处理/路由、可观测性等。

## 职责
- 基于 Abstractions 提供 Agent 编程模型的核心实现。
- 提供事件处理发现/执行、事件路由等基础能力。
- 提供通用基础设施（观测、CQRS/EventSourcing 辅助等）。

## 主要功能
- 运行时无关设计（多运行时）。
- 事件驱动架构。
- 跨边界类型 Protobuf 优先。

## 公开 API 入口（自动扫描）
- `GAgentActorBase`
- `GAgentBase`
- `EventHandlerMetadata`
- `GAgentManager`
- `EventTypeInfo`
- `IEventHandlerDiscoverer`
- `ReflectionEventHandlerDiscoverer`
- `ProtobufEventTypeResolver`
- `IntervalSnapshotStrategy`
- `ISnapshotStrategy`

## 是否建议发布为 NuGet
- **建议**：是（建议作为独立 NuGet 包发布）。
- **原因**：属于基础能力模块，通常会被下游系统直接引用。
- **打包建议**：核心包保持轻依赖；数据库/Provider 等外部集成拆成独立可选包。

## 构建

```bash
dotnet build src/Aevatar.Agents.Core/Aevatar.Agents.Core.csproj
```

## 单元测试

- 测试覆盖面评审与入口请见 `docs/Tests.md`。

## 文档

- 项目文档：`docs/`
- 仓库级文档：仓库根目录 `docs/`

## 约束/约定

- 跨边界类型（State/Event/Config 等）必须使用 **Protocol Buffers** 定义。
