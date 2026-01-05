# Aevatar.Agents.Runtime.Orleans.MongoDB

Orleans 运行时扩展：提供 MongoDB 相关的持久化/存储能力。

## 职责
- 为特定 Actor 系统提供运行时适配，使同一 Agent 代码可在该运行时运行。
- 把框架的 stream/订阅语义桥接到运行时机制。
- 处理生命周期、激活、以及父子关系等运行时行为。

## 主要功能
- Orleans 分布式运行时（Virtual Actor）。
- stream/订阅语义映射到 Orleans streaming。

## 公开 API 入口（自动扫描）
- `MongoEventRepositoryOptions`
- `EventDocument`
- `MongoEventRepository`

## 是否建议发布为 NuGet
- **建议**：可选。
- **原因**：属于适配/集成模块；发布为独立 NuGet 包可让使用方按需引入，避免拉入不必要依赖。
- **打包建议**：外部依赖尽量隔离在本包内，避免污染 Core。

## 构建

```bash
dotnet build src/Aevatar.Agents.Runtime.Orleans.MongoDB/Aevatar.Agents.Runtime.Orleans.MongoDB.csproj
```

## 单元测试

- 测试覆盖面评审与入口请见 `docs/Tests.md`。

## 文档

- 项目文档：`docs/`
- 仓库级文档：仓库根目录 `docs/`

## 约束/约定

- 跨边界类型（State/Event/Config 等）必须使用 **Protocol Buffers** 定义。
