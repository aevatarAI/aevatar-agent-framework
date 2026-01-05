# Aevatar.Agents.CreativeReasoning

创意推理模块：为更高层认知 Agent 提供能力组件。

## 职责
- 提供认知推理相关模块，构建在核心 Agent 能力之上。
- 沉淀可复用的推理工具与模式。

## 主要功能
- 基于框架的推理工具与模块。
- 更高层的认知组件沉淀。

## 公开 API 入口（自动扫描）
- `UoTServiceCollectionExtensions`
- `UoTResult`
- `UoTResultTrace`
- `TUoTResult`
- `TUoTResultTrace`
- `UoTExecutionTraceExtensions`
- `UoTMode`
- `UoTOptions`
- `UoTProgress`
- `UoTPhase`

## 是否建议发布为 NuGet
- **建议**：可选。
- **原因**：属于适配/集成模块；发布为独立 NuGet 包可让使用方按需引入，避免拉入不必要依赖。
- **打包建议**：外部依赖尽量隔离在本包内，避免污染 Core。

## 构建

```bash
dotnet build src/Aevatar.Agents.CreativeReasoning/Aevatar.Agents.CreativeReasoning.csproj
```

## 单元测试

- 测试覆盖面评审与入口请见 `docs/Tests.md`。

## 文档

- 项目文档：`docs/`
- 仓库级文档：仓库根目录 `docs/`

## 约束/约定

- 跨边界类型（State/Event/Config 等）必须使用 **Protocol Buffers** 定义。
