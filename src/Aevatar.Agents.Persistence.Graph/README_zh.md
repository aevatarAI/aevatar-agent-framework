# Aevatar.Agents.Persistence.Graph

图持久化相关的抽象与工具（Graph persistence abstractions）。

## 职责
- 提供 State / Memory / Graph 等持久化适配实现。
- 对外隐藏存储实现细节，遵循框架抽象。
- 将外部依赖隔离在可选包中，避免污染核心包。

## 主要功能
- 按 provider 隔离依赖与实现（可选安装）。
- 通过 DI 配置驱动接入。
- 可按需选择存储组合。

## 公开 API 入口（自动扫描）
- `IGraphCompiler`
- `GraphClient`
- `IGraphExecutor`
- `struct`
- `IGraphClient`
- `ReadNode`
- `CreateNode`
- `UpdateNode`
- `DeleteNode`
- `QueryNodes`

## 是否建议发布为 NuGet
- **建议**：可选。
- **原因**：属于适配/集成模块；发布为独立 NuGet 包可让使用方按需引入，避免拉入不必要依赖。
- **打包建议**：外部依赖尽量隔离在本包内，避免污染 Core。

## 构建

```bash
dotnet build src/Aevatar.Agents.Persistence.Graph/Aevatar.Agents.Persistence.Graph.csproj
```

## 单元测试

- 测试覆盖面评审与入口请见 `docs/Tests.md`。

## 文档

- 项目文档：`docs/`
- 仓库级文档：仓库根目录 `docs/`

## 约束/约定

- 跨边界类型（State/Event/Config 等）必须使用 **Protocol Buffers** 定义。
