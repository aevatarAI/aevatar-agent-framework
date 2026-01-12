# Aevatar.Agents.AGUI

AG-UI 协议集成：标准事件 + snapshot-first SSE，用于构建可视化 UI。

## 职责
- 提供 AG-UI 事件流协议与辅助工具（snapshot-first + 标准事件）。
- 帮助 API Host 以 SSE 方式把 Agent 运行过程推送到前端。

## 主要功能
- AG-UI 标准事件类型。
- snapshot-first 重连策略辅助。
- SSE 友好的 JSON 默认。

## 公开 API 入口（自动扫描）
- `AgUiEvent`
- `RunStartedEvent`
- `RunFinishedEvent`
- `RunErrorEvent`
- `StepStartedEvent`
- `StepFinishedEvent`
- `TextMessageStartEvent`
- `TextMessageContentEvent`
- `TextMessageEndEvent`
- `StateSnapshotEvent`

## 是否建议发布为 NuGet
- **建议**：是（建议作为独立 NuGet 包发布）。
- **原因**：属于基础能力模块，通常会被下游系统直接引用。
- **打包建议**：核心包保持轻依赖；数据库/Provider 等外部集成拆成独立可选包。

## 构建

```bash
dotnet build src/Aevatar.Agents.AGUI/Aevatar.Agents.AGUI.csproj
```

## 单元测试

- 测试覆盖面评审与入口请见 `docs/Tests.md`。

## 文档

- 项目文档：`docs/`
- 仓库级文档：仓库根目录 `docs/`

## 约束/约定

- 跨边界类型（State/Event/Config 等）必须使用 **Protocol Buffers** 定义。
