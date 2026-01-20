# AIGAgentBase — 全面指南

> 范围：`AIGAgentBase` 及其 partial 文件（Chat/Tools/History/Memory/MCP/AgentSkills/Telemetry/YAML）。
> 目标：给出“能用 + 可扩展 + 可审计”的统一认知。

## 1) 适用范围与约束

- **跨边界类型必须 Protobuf**：`AevatarAIAgentState` / `AevatarAIAgentConfig` 都是 Protobuf 类型，派生状态/事件也必须保持该约束。
- **初始化必须显式**：调用 `InitializeAsync(...)` 完成配置与 provider 装载后才能使用（否则 `EnsureInitialized` 会抛错）。
- **best-effort 默认**：MCP / WebSearch / Memory / AgentSkills 等属于可选能力，不应阻断主流程。

## 2) 快速开始（最小可用）

```csharp
public class MyAgent : AIGAgentBase
{
    public override Task<string> GetDescriptionAsync()
        => Task.FromResult("MyAgent");
}

var agent = new MyAgent();
await agent.InitializeAsync("openai-gpt4");
var response = await agent.ChatAsync(new ChatRequest { Message = "hi" });
```

> 说明：业务端通常由 `AIGAgentFactory` 负责实例化与注入依赖（LLMProviderFactory / ToolManager / 配置等）。

## 3) 生命周期与初始化

- **OnActivateAsync**：派生类应 **base-first**（符合仓库规范）。
- **InitializeAsync(...)**：
  - 支持 **providerName** 与 **providerConfig** 两种入口。
  - 内部通过 `_initializationSemaphore` 保证并发安全。
  - 使用 `StateProtectionContext.BeginInitializationScope()` 允许初始化期修改 `State/Config`。
- **EnsureInitialized()**：所有对外能力（Chat/Stream/Tools）入口都会调用，避免未初始化状态。

## 4) 配置与 SystemPrompt

- `ConfigAI(...)`：默认填充 `AevatarAIDefaults`，派生类可 override 覆盖默认值。
- `SystemPrompt`：可直接设置，实际 prompt 会与工具指令块合并（工具系统已内置）。

## 5) LLM Provider

- **Provider 工厂**：`LLMProviderFactory` 通过注入提供。
- **创建入口**：
  - `CreateLLMProviderFromFactoryAsync(...)`
  - `CreateLLMProviderFromConfigAsync(...)`
- **配置 & 观测**：
  - `ActiveProviderConfig` 记录当前 provider 配置。
  - `GetProviderAndModelForTelemetry()` 统一提供 provider/model 标识。

## 6) Embeddings（可选）

- `EmbeddingFactory` 可注入，成功后生成 `IEmbeddingGenerator`。
- `BuildDefaultEmbeddingOptions()`：默认使用 provider 或 embeddings 子配置的 model/dimensions。
- **失败语义**：初始化失败只记录日志，不影响主流程。

## 7) Chat / Streaming 主流程

### ChatAsync（简化流程）

1. `EnsureInitialized()`
2. `CompactChatHistoryIfNeededAsync(...)`
3. `InitializeToolsAsync(...)`
4. `TryReconnectMcpOnChatAsync(...)`
5. `BuildLLMRequest(...)`
6. `GenerateLLMWithHooksAsync(...)`
7. `ExecuteToolCallLoopAsync(...)`（如有 function call）
8. 历史 / MemoryStore 追加（best-effort）
9. 发布 `ChatResponseEvent` 与 `AIDecisionEvent`

### ChatStreamAsync

- **流式工具策略**：`StreamingToolCalls`（`StreamingToolCallMode`）可 override。
- **错误日志策略**：`LogChatStreamTokenReadException(...)` 可 override。

### 内部 runtime（行为不变）

- **LlmRequestRuntime**：负责 `BuildLLMRequest(...)` 的组装（system prompt + history + allowlist + tools）。
- **ToolingRuntime**：负责工具系统初始化/缓存/loop 与 allowlist 执行防线。
- **Runtime Contexts**：Tooling 侧拆为 `ToolingInitContext` / `ToolingLoopContext`，LLM 侧为 `LlmRequestRuntimeContext`，按职责分层。

## 8) 工具系统（Tooling）

### 内置工具（默认注册）

- `StateQueryTool`
- `EventPublisherTool`
- `AevatarMemorySearchTool`
- `WebSearch`（best-effort）
- `AgentSkills`（默认启用）
- `MCP`（best-effort）

### 安全开关

- `AllowInternalTools`：控制内部访问类工具可见/可执行。
- `AllowDangerousTools`：控制危险/需确认工具可见/可执行。

### Allowlist（双层）

- **基线 allowlist**：`SetFixedToolAllowlist(...)`，应用于每次 BuildLLMRequest。
- **请求级 allowlist**：`AIGAgentKeys.ToolAllowlist`（可按请求注入）。
- **防御性执行检查**：`ExecuteAllowedToolAsync(...)` 会再次验证策略。

### 缓存与一致性

- `_registeredToolsCache` / `_functionDefinitionsCache` 使用“**替换引用**”保持并发安全。
- **语义**：缓存是“最终一致”，刷新期间可能读到旧值。
- 若需强一致性，需引入锁/版本戳（注意性能与死锁风险）。

## 9) YAML 工具策略（统一入口）

YAML 应用由 `AgentYamlConfigApplier` 驱动，并通过以下 hook 统一策略：

- `BuildYamlToolPolicy(...)`
- `BuildToolAllowlistFromYaml(...)`
- `GetYamlSkillToolNames()`
- `GetYamlDefaultSkillRoots()`
- `GetYamlDangerousToolNames()`
- `ShouldEnableDangerousToolsFromYaml(...)`
- `LogYamlToolPolicyDecision(...)`

详见：`./YAML_TOOL_POLICY.md`

### 审计字段（默认）

- `allowlist_count` / `allowlist_hash` / `allowlist_sample`
- `dangerous_names_count` / `enable_dangerous`
- `skills_count` / `tools_count`

## 10) AgentSkills（agentskills.io）

- **开关**：`EnableAgentSkills`（默认 true）
- **根目录**：`AddAgentSkillsRoot(...)` / `ConfigureAgentSkillsAsync(...)`
- **工具**：`skills_list` / `skills_load` / `read_skill_document` / `skills_run_python` 等
- **安全**：危险工具仍受 `AllowDangerousTools` 约束

## 11) MCP（Model Context Protocol）

- **开关**：`EnableMcpServers`（默认 true）
- **重试**：`McpRetryOnEachChat` + `McpRetryMinInterval`
- **配置来源**：`MCP:mcpServers`（Cursor 风格）
- **手动重连**：`ReconnectMcpAsync(...)`

## 12) Memory Store / Vector Index

- **开关**：
  - `EnableMemoryStoreAppend`
  - `EnableMemoryVectorIndexAppend`
- **Scope**：
  - `MemoryStoreScopeType`（默认 PrivateAgent）
  - `MemoryStoreScopeIdOverride` / `MemoryIdOverride`
- **best-effort**：失败不会阻断 Chat/Stream。

## 13) History（Layer 1 / 2）

- `EnableChatHistoryInState`：把对话写入 `State.History`
- `EnableChatHistoryCompaction`：滚动摘要写入 `State.Context["history_summary"]`
- `ChatHistoryMaxMessages` / `ChatHistorySummaryMaxChars` 可调
- **注意**：`RepeatedField<T>` 非线程安全，锁由 `HistoryRuntime` 管理。

## 14) Hooks & Harness

Hook/Harness 提供 LLM/Tool 生命周期的横切治理能力（限流、策略、输出截断等）。
此外补齐会话级阶段：`OnSessionStart` / `OnStop` / `OnSessionEnd`。
详见：`../HOOKS_HARNESS.md`

## 15) Telemetry / Observability

`LlmCallInstrumentationScope` 统一封装：

- Stopwatch
- Activity / logging scope
- 完成/失败记录

## 16) Thread-safety 与 best-effort 语义

- **并发保护**：`_initializationSemaphore` / `_toolInitSemaphore`
- **best-effort 覆盖**：WebSearch / MCP / Memory / AgentSkills / IO
- **一致性声明**：工具缓存与 skills 发现属于最终一致，非强一致。

## 17) 常用扩展点（protected virtual）

- `ConfigAI(...)`
- `CreateLLMProviderFromFactoryAsync(...)`
- `CreateLLMProviderFromConfigAsync(...)`
- `BuildLLMRequest(...)`
- `RegisterToolsAsync(...)`
- `BuildToolAllowlistFromYaml(...)`
- `GetYamlDefaultSkillRoots()`
- `GetYamlDangerousToolNames()`
- `GetSkillToolsAutoIncludedFromYaml(...)`
- `StreamingToolCalls` / `LogChatStreamTokenReadException(...)`
- `AppendChatMemoryAsync(...)` / `BuildMemoryScope(...)`

## 18) 常见误用

- **忘记初始化**：未调用 `InitializeAsync` 即调用 `ChatAsync` → 直接抛异常。
- **滥用危险工具**：未关闭 `AllowDangerousTools` 直接暴露 Python / MCP side-effect。
- **无界历史**：开启 `EnableChatHistoryInState` 但未启用 compaction → 状态膨胀。


