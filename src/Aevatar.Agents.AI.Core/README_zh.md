# Aevatar.Agents.AI.Core

AI Agent 的核心实现：`AIGAgentBase`、工具调用（tool calling）、hooks、memory 集成等。

## 职责
- 提供 AI Agent 基类（AIGAgentBase）与 LLM 请求构建逻辑。
- 实现 tool calling 循环、流式输出与安全护栏。
- 集成 memory 分层与 hooks。

## 主要功能
- AIGAgentBase（chat + streaming）。
- tool calling 循环与安全控制。
- hooks 管线与 memory 集成。

## 公开 API 入口（自动扫描）
- `AIGAgentBase`
- `ConversationHistoryManager`
- `AIGAgentFactory`
- `IAIAgentEmbeddingFactory`
- `ToolArgumentsJson`
- `AIGAgentKeys`
- `LLMResponseParser`
- `AevatarAgentHookContext`
- `struct`
- `AevatarAgentHookPipeline`

## 是否建议发布为 NuGet
- **建议**：是（建议作为独立 NuGet 包发布）。
- **原因**：属于基础能力模块，通常会被下游系统直接引用。
- **打包建议**：核心包保持轻依赖；数据库/Provider 等外部集成拆成独立可选包。

## 构建

```bash
dotnet build src/Aevatar.Agents.AI.Core/Aevatar.Agents.AI.Core.csproj
```

## 单元测试

- 测试覆盖面评审与入口请见 `docs/Tests.md`。

## 文档

- 项目文档：`docs/`
- 仓库级文档：仓库根目录 `docs/`

## 约束/约定

- 跨边界类型（State/Event/Config 等）必须使用 **Protocol Buffers** 定义。
