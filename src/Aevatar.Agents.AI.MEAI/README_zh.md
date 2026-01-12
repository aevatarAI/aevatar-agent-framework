# Aevatar.Agents.AI.MEAI

基于 Microsoft.Extensions.AI 的 Provider 集成：对接 OpenAI-compatible/DeepSeek，支持流式输出。

## 职责
- 提供具体的 LLM Provider 适配实现（基于 AI.Abstractions/Core）。
- 把框架消息/工具调用协议映射到具体 Provider（含 streaming）。

## 主要功能
- 具体 Provider 的 chat + streaming 适配。
- tool calling 协议护栏。

## 公开 API 入口（自动扫描）
- `MEAILLMProviderFactory`
- `MEAIEmbeddingFactory`
- `MEAILLMProvider`
- `LLMTelemetry`
- `ServiceCollectionExtensions`

## 是否建议发布为 NuGet
- **建议**：可选。
- **原因**：属于适配/集成模块；发布为独立 NuGet 包可让使用方按需引入，避免拉入不必要依赖。
- **打包建议**：外部依赖尽量隔离在本包内，避免污染 Core。

## 构建

```bash
dotnet build src/Aevatar.Agents.AI.MEAI/Aevatar.Agents.AI.MEAI.csproj
```

## 单元测试

- 测试覆盖面评审与入口请见 `docs/Tests.md`。

## 文档

- 项目文档：`docs/`
- 仓库级文档：仓库根目录 `docs/`

## 约束/约定

- 跨边界类型（State/Event/Config 等）必须使用 **Protocol Buffers** 定义。
