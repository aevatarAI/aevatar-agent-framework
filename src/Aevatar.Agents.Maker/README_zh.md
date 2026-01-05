# Aevatar.Agents.Maker

MAKER 框架模块：用于构建/编排 agent/skills。

## 职责
- 提供 MAKER 框架组件，用于构建与编排 agent/skills。
- 提供与 Aevatar runtime 的集成扩展点。

## 主要功能
- skills/tools 编排能力。
- 构建 agent 系统的组合模式。

## 公开 API 入口（自动扫描）
- `MakerServiceCollectionExtensions`
- `TaskCheckpoint`
- `ExecutionCheckpoint`
- `RecoveryResult`
- `ICheckpointStore`
- `InMemoryCheckpointStore`
- `FileCheckpointStore`
- `TaskCheckpointManager`
- `ExecutionMode`
- `ContextIsolationMode`

## 是否建议发布为 NuGet
- **建议**：可选。
- **原因**：属于适配/集成模块；发布为独立 NuGet 包可让使用方按需引入，避免拉入不必要依赖。
- **打包建议**：外部依赖尽量隔离在本包内，避免污染 Core。

## 构建

```bash
dotnet build src/Aevatar.Agents.Maker/Aevatar.Agents.Maker.csproj
```

## 单元测试

- 测试覆盖面评审与入口请见 `docs/Tests.md`。

## 文档

- 项目文档：`docs/`
- 仓库级文档：仓库根目录 `docs/`

## 约束/约定

- 跨边界类型（State/Event/Config 等）必须使用 **Protocol Buffers** 定义。
