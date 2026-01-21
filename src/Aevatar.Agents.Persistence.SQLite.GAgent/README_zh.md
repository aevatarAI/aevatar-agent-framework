# Aevatar.Agents.Persistence.SQLite.GAgent

SQLite 持久化适配（State/Config/EventRouter），面向 GAgent 使用。

## 职责
- 提供 State/Config/EventRouter 的持久化适配实现。
- 对外隐藏存储实现细节，遵循框架抽象。
- 将外部依赖隔离在可选包中，避免污染核心包。

## 主要功能
- SQLite 持久化实现（State/Config/EventRouter）。
- 按 provider 隔离依赖与实现（可选安装）。
- 通过 DI 配置驱动接入。
- 可按需选择存储组合。

## 公开 API 入口（自动扫描）
- `SQLiteStateStore`
- `SQLiteConfigStore`
- `SQLiteEventRouterStore`
- `SQLiteGAgentServiceCollectionExtensions`

## 构建

```bash
dotnet build src/Aevatar.Agents.Persistence.SQLite.GAgent/Aevatar.Agents.Persistence.SQLite.GAgent.csproj
```

## 文档

- 项目文档：`docs/`
- 仓库级文档：仓库根目录 `docs/`

## 约束/约定

- 跨边界类型（State/Event/Config 等）必须使用 **Protocol Buffers** 定义。
