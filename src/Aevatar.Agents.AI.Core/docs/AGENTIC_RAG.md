# Agentic RAG (Framework) — `AgenticRagGAgent`

本文件描述 Aevatar 框架层的 Agentic RAG 基座：`AgenticRagGAgent`。

## 1. 这是什么（Aevatar 语境）

**Agentic RAG** 是一个由框架控制的、有界的多步循环：

> Plan → Retrieve → Synthesize → Critique → Stop/Continue

在 Aevatar 里，我们把它实现为**单个 Agent 内部的“角色化策略”**（Planner/Retriever/Synthesizer/Critic），而不是强制拆成多个 Actor。你可以：
- **单 Agent 内部扮演四个角色**（默认）
- 按需替换其中某一环（只换 Retriever 或只换 Critic）
- 未来如果需要并行/权限边界，再拆成多 Agent（不在本 spec 范围）

## 2. 核心约束（铁律）

- **跨边界类型必须 Protobuf**：State / Event / Config / Evidence / Citation 都在 `.proto` 中定义  
  - 见：`src/Aevatar.Agents.AI.Core/Messages/agentic_rag_messages.proto`
- **有界循环**：轮次、证据数量、snippet 长度、trace 输出必须有上限；避免无限循环与上下文爆炸。
- **安全默认值**：不泄露 secrets；不把大段原文塞进 state/event。
- **端口策略**：仓库内示例/默认配置禁止使用 `:5000`（如需示例监听端口，优先 `:5678` 且可配置）。

## 3. 你会得到什么（MVP 能力）

- `AgenticRagGAgent.AnswerAsync(...)`：一次请求内跑有界 loop，并返回：
  - `Answer`（文本）
  - `Evidence`（`RagEvidenceSummary` 列表，带 `RagCitation`）
  - `Diagnostics`（strings only，小而可读）
  - 结束原因（`RagStopReason`：Succeeded/NoEvidence/BudgetExceeded/Cancelled/Failed）
- 默认 Retriever：`MemoryStoreAgenticRagRetriever`
  - semantic-first（`IMemoryVectorIndex`，需要 query embedding）
  - lexical fallback（`IMemoryStore.SearchAsync`）
- **可选 ExecutionTrace 导出**（best-effort）：当 `enable_execution_trace=true` 且注入了 `IExecutionTraceStore` 时，自动保存一个有界 trace bundle。

## 4. 快速开始（最小用法）

### 4.1 继承一个 Agent

你可以直接继承 `AgenticRagGAgent`，并（按需）覆盖策略工厂：

- `CreateRetriever()`：接入你自己的检索后端（向量库/搜索/DB/Web…）
- `CreatePlanner()/CreateSynthesizer()/CreateCritic()`：替换 loop 的关键决策

默认实现是 **deterministic**（不调用 LLM），方便先把工程管线跑通；真正要做“智能规划/批判”，建议在你的策略实现里调用 LLM（推荐通过 `GenerateLLMWithHooksAsync` 走一致治理，默认不附加 tools）。

### 4.2 调用 `AnswerAsync`

`AnswerAsync` 的输入是进程内模型：
- `AgenticRagRequest`：query + 可选 scope/memoryId/budget override
- `RagBudget`：每次调用的预算覆盖（0 表示不覆盖，使用 `AgenticRagConfig` 默认/配置）

输出为：
- `AgenticRagResponse`：answer + evidence + diagnostics

## 5. Evidence / Citation（引用怎么表示）

跨边界的证据/引用在 `agentic_rag_messages.proto`：
- `RagEvidenceSummary`：`snippet`（有界）+ `citation` + `score` + `tags`
- `RagCitation`：
  - `memory_entry`（`MemoryEntryCitation`：memory_id/entry_id/scope）
  - `uri`（`UriCitation`：uri + range）

注意：
- **不在 state/event 里存大段原文**：引用应当是“指针”，snippet 仅用于展示与 debug。
- `tags/diagnostics` 都是 **string-only**，不要塞 JSON blob，不要塞 secrets。

## 6. ExecutionTrace（可观测 / 可回放）

当 `AgenticRagConfig.enable_execution_trace=true` 且 `IExecutionTraceStore` 可用时：
- 每次 `AnswerAsync` 会 best-effort 写出一个 `ExecutionTrace`（`execution_id = runId`）
- Trace 树结构：root → iterations → plan/retrieve/synthesize/critique（有界输出）

实现位置：
- `src/Aevatar.Agents.AI.Core/AgenticRag/AgenticRagExecutionTraceExporter.cs`

## 7. 扩展点建议（不引入坏味道）

- **先替换 Retriever**：让数据源对接正确（最常见的工程工作）。
- **再替换 Critic**：把“缺证据就打回”的规则写清楚（减少幻觉）。
- **最后替换 Planner/Synthesizer**：引入 LLM 时要控制预算与可观测性（hooks、trace、日志）。

好品味提醒：
- 能消失的分支永远比能写对的分支更优雅：优先通过设计把“特殊情况”变成统一路径（例如统一的 budget/stop reason）。

## 8. 相关文件索引

- Contracts（Protobuf）：`src/Aevatar.Agents.AI.Core/Messages/agentic_rag_messages.proto`
- Loop 基座：`src/Aevatar.Agents.AI.Core/AgenticRag/AgenticRagGAgent.cs`
- 默认 Retriever：`src/Aevatar.Agents.AI.Core/AgenticRag/MemoryStoreAgenticRagRetriever.cs`
- ExecutionTrace 导出：`src/Aevatar.Agents.AI.Core/AgenticRag/AgenticRagExecutionTraceExporter.cs`
- 单测（loop）：`test/Aevatar.Agents.AI.Core.Tests/AgenticRag/AgenticRagLoopTests.cs`
- 单测（retriever）：`test/Aevatar.Agents.AI.Core.Tests/AgenticRag/MemoryStoreAgenticRagRetrieverTests.cs`


