# Aevatar.Agents.Runtime.Local

本地（进程内）运行时：用于开发/测试，反馈最快。

## 职责
- 为特定 Actor 系统提供运行时适配，使同一 Agent 代码可在该运行时运行。
- 把框架的 stream/订阅语义桥接到运行时机制。
- 处理生命周期、激活、以及父子关系等运行时行为。

## 主要功能
- 纯内存运行时，依赖最少、反馈最快。
- 适合开发与单元测试。

## 公开 API 入口（自动扫描）
- `LocalGAgentActor`
- `LocalStreamNotFoundHandler`
- `LocalGAgentActorManager`
- `LocalMessageStreamRegistry`
- `LocalMessageStream`
- `LocalGAgentActorFactory`
- `LocalSubscriptionManager`
- `AevatarBuilderExtensions`
- `ServiceCollectionExtensions`

## 是否建议发布为 NuGet
- **建议**：可选。
- **原因**：属于适配/集成模块；发布为独立 NuGet 包可让使用方按需引入，避免拉入不必要依赖。
- **打包建议**：外部依赖尽量隔离在本包内，避免污染 Core。

## 构建

```bash
dotnet build src/Aevatar.Agents.Runtime.Local/Aevatar.Agents.Runtime.Local.csproj
```

## 单元测试

- 测试覆盖面评审与入口请见 `docs/Tests.md`。

## 文档

- 项目文档：`docs/`
- 仓库级文档：仓库根目录 `docs/`

## 约束/约定

- 跨边界类型（State/Event/Config 等）必须使用 **Protocol Buffers** 定义。
