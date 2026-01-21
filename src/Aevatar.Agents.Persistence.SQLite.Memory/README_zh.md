# Aevatar.Agents.Persistence.SQLite.Memory

SQLite AI Memory 适配（IMemoryStore / IMemoryVectorIndex）。

## 职责
- 提供 AI Memory 的 SQLite 持久化实现。
- 对外隐藏存储实现细节，遵循框架抽象。
- 将外部依赖隔离在可选包中，避免污染核心包。

## 主要功能
- SQLite 持久化实现（IMemoryStore + IMemoryVectorIndex）。
- 向量检索采用进程内余弦相似度（brute-force）。
- 通过 DI 配置驱动接入。

## 公开 API 入口（自动扫描）
- `SQLiteMemoryStore`
- `SQLiteMemoryVectorIndex`
- `SQLiteMemoryServiceCollectionExtensions`

## 构建

```bash
dotnet build src/Aevatar.Agents.Persistence.SQLite.Memory/Aevatar.Agents.Persistence.SQLite.Memory.csproj
```

## 文档

- 项目文档：`docs/`
- 仓库级文档：仓库根目录 `docs/`

## 约束/约定

- 跨边界类型（State/Event/Config 等）必须使用 **Protocol Buffers** 定义。
