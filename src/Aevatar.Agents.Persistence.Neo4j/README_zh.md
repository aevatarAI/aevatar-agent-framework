# Aevatar.Agents.Persistence.Neo4j

Neo4j 图/记忆图持久化适配（Graph / MemoryGraph）。

## 职责
- 提供 State / Memory / Graph 等持久化适配实现。
- 对外隐藏存储实现细节，遵循框架抽象。
- 将外部依赖隔离在可选包中，避免污染核心包。

## 主要功能
- Neo4j 图/记忆图持久化实现。
- 按 provider 隔离依赖与实现（可选安装）。
- 通过 DI 配置驱动接入。
- 可按需选择存储组合。

## 公开 API 入口（自动扫描）
- `Neo4jPersistenceOptions`
- `INeo4jDriverFactory`
- `Neo4jDriverFactory`
- `INeo4jSessionFactory`
- `Neo4jSessionFactory`
- `Neo4jServiceCollectionExtensions`
- `INeo4jClient`
- `Neo4jClient`

## 是否建议发布为 NuGet
- **建议**：可选。
- **原因**：属于适配/集成模块；发布为独立 NuGet 包可让使用方按需引入，避免拉入不必要依赖。
- **打包建议**：外部依赖尽量隔离在本包内，避免污染 Core。

## 构建

```bash
dotnet build src/Aevatar.Agents.Persistence.Neo4j/Aevatar.Agents.Persistence.Neo4j.csproj
```

## 单元测试

- 测试覆盖面评审与入口请见 `docs/Tests.md`。

## 文档

- 项目文档：`docs/`
- 仓库级文档：仓库根目录 `docs/`

## 约束/约定

- 跨边界类型（State/Event/Config 等）必须使用 **Protocol Buffers** 定义。
