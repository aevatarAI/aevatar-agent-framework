# Aevatar.Agents.AI.Abstractions

AI 能力的契约与配置层：`LLMProviders` 配置、Chat/Tool 消息协议等。

## 职责
- 定义 AI 能力契约：LLMProviders 配置、Chat/Tool 消息协议。
- 用于跨边界的数据类型优先 Protobuf。

## 主要功能
- LLMProviders 配置模型。
- Chat/Tool 消息协议（历史 + 工具调用）。

## 公开 API 入口（自动扫描）
- `AevatarAIDefaults`
- `AevatarAIAgentConfiguration`
- `LLMProviderConfig`
- `LLMProvidersConfig`
- `LLMEmbeddingConfig`
- `ILLMProviderFactory`
- `LLMProviderFactoryBase`
- `ChatRequest`
- `AevatarParameterDefinition`
- `AevatarLLMProviderBase`

## 是否建议发布为 NuGet
- **建议**：是（建议作为独立 NuGet 包发布）。
- **原因**：属于基础能力模块，通常会被下游系统直接引用。
- **打包建议**：核心包保持轻依赖；数据库/Provider 等外部集成拆成独立可选包。

## 构建

```bash
dotnet build src/Aevatar.Agents.AI.Abstractions/Aevatar.Agents.AI.Abstractions.csproj
```

## 单元测试

- 测试覆盖面评审与入口请见 `docs/Tests.md`。

## 文档

- 项目文档：`docs/`
- 仓库级文档：仓库根目录 `docs/`

## 约束/约定

- 跨边界类型（State/Event/Config 等）必须使用 **Protocol Buffers** 定义。
