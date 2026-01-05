# Aevatar.Agents.Runtime

多个运行时（Local/Orleans/ProtoActor）共用的 Runtime 抽象与基础设施。

## 职责
- 为各 Runtime 适配层提供共用的 Runtime 抽象与基础设施。
- 承载通用的生命周期/订阅等基础实现。

## 主要功能
- 多运行时共享的 runtime 基础设施。

## 公开 API 入口（自动扫描）
- `GAgentActorFactoryBase`

## 是否建议发布为 NuGet
- **建议**：是（建议作为独立 NuGet 包发布）。
- **原因**：属于基础能力模块，通常会被下游系统直接引用。
- **打包建议**：核心包保持轻依赖；数据库/Provider 等外部集成拆成独立可选包。

## 构建

```bash
dotnet build src/Aevatar.Agents.Runtime/Aevatar.Agents.Runtime.csproj
```

## 单元测试

- 测试覆盖面评审与入口请见 `docs/Tests.md`。

## 文档

- 项目文档：`docs/`
- 仓库级文档：仓库根目录 `docs/`

## 约束/约定

- 跨边界类型（State/Event/Config 等）必须使用 **Protocol Buffers** 定义。
