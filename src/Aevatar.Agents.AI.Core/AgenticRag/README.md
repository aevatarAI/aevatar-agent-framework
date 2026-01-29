# Agentic RAG（AgenticRag）

本目录提供 Aevatar AI 代理使用的框架级 Agentic RAG 循环。
它在单个 Agent 内部执行一个**有界且可控**的
plan -> retrieve -> synthesize -> critique 循环，并发布带证据与诊断信息的
Protobuf Answer Event。

## 它做什么

- 在 `AgenticRagGAgent.AnswerAsync(...)` 中运行有界的多步 RAG 循环。
- 对输出进行安全约束（证据数量、snippet 长度、上下文长度均有上限）。
- 四个角色以接口隔离，可分别替换与扩展。
- 发布 `AgenticRagAnswerEvent`（Protobuf）供跨运行时消费。
- 可选导出有界 `ExecutionTrace`（启用且注入时 best-effort）。

## 关键组成

- `AgenticRagGAgent.cs`
  - Loop 基座与安全默认值。
  - 默认 planner/synthesizer/critic 为 deterministic（不调用 LLM）。
  - 默认 retriever 为 `MemoryStoreAgenticRagRetriever`。
- `AgenticRagModels.cs`
  - 进程内 request/response 模型。
  - 非跨运行时契约（跨边界在 `.proto` 中定义）。
- `IAgenticRagPlanner`, `IAgenticRagRetriever`, `IAgenticRagSynthesizer`, `IAgenticRagCritic`
  - 四个角色的策略接口。
- `MemoryStoreAgenticRagRetriever.cs`
  - 默认检索器：语义优先 + 词法回退。
- `AgenticRagExecutionTraceExporter.cs`
  - 有界 ExecutionTrace 导出工具（best-effort）。
- 合约（Protobuf）
  - `src/Aevatar.Agents.AI.Core/Messages/agentic_rag_messages.proto`

## 核心流程

1. **Plan**：planner 决定检索请求或提前停止。
2. **Retrieve**：retriever 返回带引用的有界证据摘要。
3. **Synthesize**：synthesizer 基于证据生成草稿答案。
4. **Critique**：critic 判断通过/失败并给出缺口。
5. **Stop/Continue**：成功、无证据、预算耗尽、取消或失败时终止。

停止原因通过 `RagStopReason` 返回，并记录在 diagnostics 中。

## 如何使用

### 1) 继承 Agent 并替换策略

```csharp
public sealed class MyAgent : AgenticRagGAgent
{
    protected override IAgenticRagRetriever CreateRetriever()
        => new MyRetriever();

    protected override IAgenticRagPlanner CreatePlanner()
        => new MyPlanner();

    protected override IAgenticRagSynthesizer CreateSynthesizer()
        => new MySynthesizer();

    protected override IAgenticRagCritic CreateCritic()
        => new MyCritic();
}
```

### 2) 初始化并调用 `AnswerAsync`

```csharp
var actor = await actorFactory.CreateGAgentActorAsync<MyAgent>();
var agent = (MyAgent)actor.GetAgent();

// 确保 AIGAgentBase 已初始化（AgenticRagGAgent 需要）
await agent.InitializeAsync("your-llm-provider-name");

var response = await agent.AnswerAsync(new AgenticRagRequest
{
    RequestId = Guid.NewGuid().ToString("N"),
    Query = "你的问题",
    Budget = new RagBudget
    {
        MaxIterations = 3,
        MaxEvidenceItems = 8,
        MaxEvidenceChars = 800
    }
});
```

### 3) 使用输出

`AgenticRagResponse` 包含：
- `Answer`（string）
- `Evidence`（`RagEvidenceSummary` 列表，带引用）
- `StopReason`（`RagStopReason`）
- `Iterations`（执行轮次数）
- `Diagnostics`（string-only 键值）

同时 Agent 会发布跨边界的 `AgenticRagAnswerEvent`（Protobuf）。

## 配置与预算

`AgenticRagConfig`（Protobuf）提供默认值：
- `max_iterations`, `max_evidence_items`, `max_evidence_chars`
- `max_context_chars`, `call_timeout_ms`
- `enable_execution_trace`
- `scope_type`, `scope_id`, `memory_id`（默认检索范围）

每次调用的覆盖写在 `RagBudget`（进程内）。0 表示不覆盖（使用默认）。
`AnswerAsync` 会对预算做安全 clamp。

## 默认检索行为

`MemoryStoreAgenticRagRetriever`：
- 提供 query embedding 时使用 `IMemoryVectorIndex`（语义检索）。
- 否则回退到 `IMemoryStore.SearchAsync`（词法检索）。
- 基于 query 构建有界 snippet，并按引用去重证据。

证据 tags 为 string-only，并包含 source 与 ranking 信息。

## ExecutionTrace（可选）

当 `AgenticRagConfig.enable_execution_trace = true` 且 DI 注入了
`IExecutionTraceStore` 时，每次运行会 best-effort 导出一个有界
`ExecutionTrace` 树。导出失败不会影响主链路。

## 合约（Protobuf）

跨边界 state/event 定义在：
`src/Aevatar.Agents.AI.Core/Messages/agentic_rag_messages.proto`

核心合约：
- `AgenticRagState`, `AgenticRagConfig`
- `RagEvidenceSummary`, `RagCitation`（含 `MemoryEntryCitation`, `UriCitation`）
- `AgenticRagAnswerEvent`

## 示例与测试

- 示例：`examples/AgenticRagDemo/`
- Loop 测试：`test/Aevatar.Agents.AI.Core.Tests/AgenticRag/AgenticRagLoopTests.cs`
- Retriever 测试：`test/Aevatar.Agents.AI.Core.Tests/AgenticRag/MemoryStoreAgenticRagRetrieverTests.cs`

# Agentic RAG (AgenticRag)

This folder provides the framework-level Agentic RAG loop used by Aevatar AI agents.
It runs a bounded, deterministic plan -> retrieve -> synthesize -> critique loop inside
a single agent and publishes a Protobuf answer event with evidence and diagnostics.

## What it does

- Runs a bounded, multi-step RAG loop in `AgenticRagGAgent.AnswerAsync(...)`.
- Keeps outputs safe and bounded (evidence count, snippet length, context length).
- Separates strategy roles behind interfaces so you can swap parts independently.
- Publishes `AgenticRagAnswerEvent` (Protobuf) for cross-runtime consumption.
- Optionally exports a bounded `ExecutionTrace` if enabled and injected.

## Key pieces

- `AgenticRagGAgent.cs`
  - Base agent implementing the loop and safe defaults.
  - Default planner/synthesizer/critic are deterministic (no LLM calls).
  - Default retriever is `MemoryStoreAgenticRagRetriever`.
- `AgenticRagModels.cs`
  - In-process request/response models for the loop.
  - Not cross-runtime contracts (those live in `.proto`).
- `IAgenticRagPlanner`, `IAgenticRagRetriever`, `IAgenticRagSynthesizer`, `IAgenticRagCritic`
  - Strategy interfaces for the four roles.
- `MemoryStoreAgenticRagRetriever.cs`
  - Default retriever using `IMemoryVectorIndex` (semantic) with lexical fallback.
- `AgenticRagExecutionTraceExporter.cs`
  - Best-effort export of bounded `ExecutionTrace` trees.
- Contracts (Protobuf)
  - `src/Aevatar.Agents.AI.Core/Messages/agentic_rag_messages.proto`

## Core flow

1. **Plan**: planner decides retrieval requests or early stop.
2. **Retrieve**: retriever returns bounded evidence summaries with citations.
3. **Synthesize**: synthesizer drafts an answer from evidence.
4. **Critique**: critic decides pass/fail and provides gaps.
5. **Stop/Continue**: loop ends on success, no evidence, budget exhausted, cancel, or failure.

Stop reason is reported via `RagStopReason` and echoed in response diagnostics.

## How to use

### 1) Derive your agent and swap strategies

```csharp
public sealed class MyAgent : AgenticRagGAgent
{
    protected override IAgenticRagRetriever CreateRetriever()
        => new MyRetriever();

    protected override IAgenticRagPlanner CreatePlanner()
        => new MyPlanner();

    protected override IAgenticRagSynthesizer CreateSynthesizer()
        => new MySynthesizer();

    protected override IAgenticRagCritic CreateCritic()
        => new MyCritic();
}
```

### 2) Initialize and call `AnswerAsync`

```csharp
var actor = await actorFactory.CreateGAgentActorAsync<MyAgent>();
var agent = (MyAgent)actor.GetAgent();

// Ensure AIGAgentBase is initialized (required by AgenticRagGAgent).
await agent.InitializeAsync("your-llm-provider-name");

var response = await agent.AnswerAsync(new AgenticRagRequest
{
    RequestId = Guid.NewGuid().ToString("N"),
    Query = "Your question",
    Budget = new RagBudget
    {
        MaxIterations = 3,
        MaxEvidenceItems = 8,
        MaxEvidenceChars = 800
    }
});
```

### 3) Use the output

`AgenticRagResponse` includes:
- `Answer` (string)
- `Evidence` (`RagEvidenceSummary` list with citations)
- `StopReason` (`RagStopReason`)
- `Iterations` (executed loop count)
- `Diagnostics` (string-only key/value)

The agent also publishes a cross-boundary `AgenticRagAnswerEvent` (Protobuf).

## Configuration and budgets

`AgenticRagConfig` (Protobuf) provides defaults:
- `max_iterations`, `max_evidence_items`, `max_evidence_chars`
- `max_context_chars`, `call_timeout_ms`
- `enable_execution_trace`
- `scope_type`, `scope_id`, `memory_id` (default retrieval scope)

Per-call overrides are in `RagBudget` (in-process). Zero values mean "use defaults."
`AnswerAsync` clamps budgets to safe bounds.

## Retrieval behavior (default)

`MemoryStoreAgenticRagRetriever`:
- Uses `IMemoryVectorIndex` when query embedding is provided.
- Falls back to `IMemoryStore.SearchAsync` for lexical search.
- Builds bounded snippets around the query and deduplicates evidence by citation.

Evidence tags are string-only and include the source and ranking mode.

## ExecutionTrace (optional)

If `AgenticRagConfig.enable_execution_trace = true` and an `IExecutionTraceStore`
is available via DI, the loop exports a bounded `ExecutionTrace` tree
for each run. Export is best-effort and never fails the main path.

## Contracts (Protobuf)

Cross-boundary state/events are defined in:
`src/Aevatar.Agents.AI.Core/Messages/agentic_rag_messages.proto`

Key contracts:
- `AgenticRagState`, `AgenticRagConfig`
- `RagEvidenceSummary`, `RagCitation` (+ `MemoryEntryCitation`, `UriCitation`)
- `AgenticRagAnswerEvent`

## Examples and tests

- Example app: `examples/AgenticRagDemo/`
- Loop tests: `test/Aevatar.Agents.AI.Core.Tests/AgenticRag/AgenticRagLoopTests.cs`
- Retriever tests: `test/Aevatar.Agents.AI.Core.Tests/AgenticRag/MemoryStoreAgenticRagRetrieverTests.cs`

