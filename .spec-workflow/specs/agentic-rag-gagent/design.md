# Design Document

## Overview

本设计在框架层提供一个可复用的 **Agentic RAG 基座**：`AgenticRagGAgent`（基类/抽象 Agent），用于在一次请求中执行有界的多步循环：

> Plan → Retrieve → Read/Synthesize → Critique → Stop/Continue

设计原则：
- **编排确定性**：循环与预算由框架控制，不把“是否继续检索”完全交给模型的自由发挥。
- **检索可插拔**：检索后端是 DI 边界（接口），框架只定义统一 evidence/citation 结构与 stop/budget 语义。
- **证据可追溯**：关键结论必须绑定引用；输出结构包含 citations + diagnostics。
- **默认安全 + best-effort**：失败可诊断，绝不无限循环，不泄露 secrets，不把大段原文塞进 state。

## Steering Document Alignment

### Technical Standards (tech.md)

- **.NET 10 / C#**：实现位于 `src/` 框架层，遵循现有 `AIGAgentBase`（LLM/Tools/Hooks/Telemetry）模式。
- **Protobuf-first**：跨边界类型（CustomState/CustomConfig/Events/Evidence/Citations）一律 `.proto` 定义并由 build 生成。
- **Runtime agnostic**：实现不依赖 Local/Orleans/ProtoActor 的特性；依赖通过 DI 注入（如 `IMemoryStore` / `IMemoryVectorIndex` / `IExecutionTraceStore`）。
- **Port Policy**：仓库内示例/默认配置不得使用 `:5000`；如需要监听端口，默认建议 `:5678` 且可配置。

### Project Structure (structure.md)

- **框架层实现**放在 `src/Aevatar.Agents.AI.Core/`（或在 design 中明确的新模块目录），不与任何业务子系统耦合。
- **契约（Protobuf）**与代码同模块维护，并在 `*.csproj` 中显式列出 `<Protobuf Include=... />`（与 `AI.Core` 现状一致）。
- **测试**放在 `test/` 对应模块测试工程（与现有测试组织保持一致）。
- **文档**：若引入新的框架级模块/目录结构，需同步更新 `src/Aevatar.Agents.AI.Core/docs/ARCHITECTURE.md`（架构变更仪式）。

## Code Reuse Analysis

### Existing Components to Leverage

- **`src/Aevatar.Agents.AI.Core/AIGAgentBase*.cs`**：复用 LLM provider、hooks/harness、tool 系统、history compaction、telemetry/logging 等成熟能力。
- **`AIGAgentBase<TCustomState, TCustomConfig>`**：复用 CustomState/CustomConfig 的 `google.protobuf.Any` 打包机制（强制 Protobuf），避免重复造“可扩展状态”轮子。
- **`IMemoryStore` / `IMemoryVectorIndex`（`src/Aevatar.Agents.Abstractions/Memory/*`）**：作为默认 Retriever 的 DI 边界，支持 lexical + semantic 检索。
- **`ExecutionTrace`（`src/Aevatar.Agents.Abstractions/execution_trace.proto`）**：用于把每轮/每阶段的决策与耗时记录成可导出的 trace（输出只保留摘要，不 dump 大 payload）。
- **`AevatarMemorySearchTool`**：现有“FTS + semantic rerank”的实现是检索策略参考；但 AgenticRAG 以 Retriever 接口为主，不强绑定 tool-call loop。

### Integration Points

- **Hooks/Harness pipeline**：RAG 内部 LLM 调用通过 `GenerateLLMWithHooksAsync(requestId, llmRequest, ct)` 走一致治理；内部调用默认 **不附加 tools**（避免递归与危险 side-effect）。
- **Memory subsystem**：默认 Retriever 可基于 `IMemoryVectorIndex.SearchAsync` + `IMemoryStore.SearchAsync` 实现混合检索，并输出统一 `RagEvidence`。
- **Observability**：每轮每阶段输出日志 + OTel spans；可选写入 `ExecutionTraceStore` 并在最终输出提供 trace link。

## Architecture

### High-level Flow

```mermaid
flowchart TD
  Q[User Query] --> LOOP{Iteration < Max? Budget OK?}
  LOOP -->|yes| P[Planner (LLM or heuristic)]
  P --> R[Retriever (pluggable)]
  R --> S[Reader/Synthesizer (LLM)]
  S --> C[Critic (LLM or heuristic)]
  C -->|pass| OUT[Answer + Citations + Diagnostics]
  C -->|fail + budget ok| LOOP
  LOOP -->|no| OUT2[Best-effort Answer or Refusal + Diagnostics]
```

### Key Design Choices

1. **Loop is framework-owned**：停止条件（轮次/时间/证据数量/上下文大小）由框架严格控制。
2. **Retriever is DI boundary**：允许接入不同后端（MemoryStore/VectorIndex/Elastic/pgvector/Web…），但输出必须被归一化为 `RagEvidence` + `RagCitation`。
3. **Evidence is bounded**：证据片段与诊断严格截断；避免把大段原文写进 Protobuf state。
4. **No accidental tool recursion**：RAG 内部 LLM 调用默认不附加 tools；需要 tools 时必须显式 opt-in（未来扩展）。

## Components and Interfaces

### Component 1 — `AgenticRagGAgent<TState, TConfig>`
- **Purpose:** 提供 Agentic RAG 循环骨架与默认策略（预算/停止/诊断/可观测）。
- **Location (planned):**
  - `src/Aevatar.Agents.AI.Core/AgenticRag/AgenticRagGAgent.cs`
- **Base:** `AIGAgentBase<TState, TConfig>`（保证 state/config 为 Protobuf，复用 LLM/hook/telemetry 能力）
- **Public APIs (planned):**
  - `Task<AgenticRagResponse> AnswerAsync(AgenticRagRequest request, CancellationToken ct)`
  - （可选）`PublishAsync(AgenticRagAnswerEvent)` 在完成时广播结果（与 `ChatAsync` 行为一致）

### Component 2 — `IAgenticRagRetriever`
- **Purpose:** 把“检索”从 Agentic 循环中解耦出来，成为可替换能力。
- **Location (planned):**
  - `src/Aevatar.Agents.AI.Core/AgenticRag/IAgenticRagRetriever.cs`
- **Interface (planned):**
  - `Task<IReadOnlyList<RagEvidence>> RetrieveAsync(RagRetrieveRequest request, CancellationToken ct)`

### Component 3 — Default Retriever: `MemoryStoreAgenticRagRetriever` (MVP)
- **Purpose:** 提供开箱即用的检索实现：优先 semantic（`IMemoryVectorIndex`），再 fallback lexical（`IMemoryStore`）。
- **Location (planned):**
  - `src/Aevatar.Agents.AI.Core/AgenticRag/MemoryStoreAgenticRagRetriever.cs`
- **Dependencies:** `IMemoryStore?`, `IMemoryVectorIndex?`, embedding generator（来自 `AIGAgentBase`）

### Component 4 — Planner / Synthesizer / Critic (角色化策略)

为避免“必须拆 4 个独立 Agent”的误解，本设计把四个角色作为 **可替换策略对象**：

- **`IAgenticRagPlanner`**：产出下一轮检索意图（queries/filters/stop-hint）
- **`IAgenticRagSynthesizer`**：基于证据生成带引用的答案草稿
- **`IAgenticRagCritic`**：检查草稿的关键断言是否有证据，产出 pass/fail + gaps

默认实现（MVP）可使用 LLM（无 tools）+ 结构化 JSON 输出；高级用户可替换为规则/领域策略。

## Data Models

### Protobuf Contracts (planned)

新增文件（示例命名，最终落点以实现任务为准）：
- `src/Aevatar.Agents.AI.Core/Messages/agentic_rag_messages.proto`
  - 并在 `Aevatar.Agents.AI.Core.csproj` 中新增 `<Protobuf Include="Messages\agentic_rag_messages.proto" GrpcServices="None" />`

核心消息（MVP）：

```proto
syntax = "proto3";
option csharp_namespace = "Aevatar.Agents.AI.Core.Messages";

import "google/protobuf/timestamp.proto";
import "google/protobuf/any.proto";
import "memory.proto";

// ============================
//  State / Config (Custom Any)
// ============================

message AgenticRagState {
  string last_run_id = 1;
  int32 last_iterations = 2;
  string last_stop_reason = 3; // keep MVP simple; can evolve to enum
  repeated RagEvidenceSummary last_evidence = 4; // bounded snippets + citations only
}

message AgenticRagConfig {
  // Budgets / stop conditions
  int32 max_iterations = 1;          // default conservative
  int32 max_evidence_items = 2;      // default conservative
  int32 max_evidence_chars = 3;      // per evidence snippet
  int32 max_context_chars = 4;       // prompt assembly cap (best-effort)
  int32 call_timeout_ms = 5;         // per LLM call timeout (best-effort)
  bool enable_execution_trace = 6;   // default true (cheap, bounded)

  // Default retrieval scope (for memory-based retriever)
  Aevatar.Agents.Memory.MemoryScopeType scope_type = 10;
  string scope_id = 11;
  string memory_id = 12;
}

// ============================
//  Evidence / Citations
// ============================

message RagEvidenceSummary {
  string evidence_id = 1;
  string snippet = 2;              // bounded
  RagCitation citation = 3;
  double score = 4;                // optional similarity/rerank score
  map<string, string> tags = 100;  // small metadata only
}

message RagCitation {
  oneof ref {
    MemoryEntryCitation memory_entry = 1;
    UriCitation uri = 2;
  }
  map<string, string> tags = 100;
}

message MemoryEntryCitation {
  string memory_id = 1;
  string entry_id = 2;
  Aevatar.Agents.Memory.MemoryScope scope = 3;
}

message UriCitation {
  string uri = 1;
  int32 start = 2;
  int32 end = 3;
  string fragment = 4; // optional
}

// ============================
//  Output Event (cross-boundary)
// ============================

message AgenticRagAnswerEvent {
  string request_id = 1;
  string run_id = 2;
  string answer = 3;
  repeated RagEvidenceSummary evidence = 4;
  google.protobuf.Timestamp timestamp = 5;
  map<string, string> diagnostics = 100; // bounded key/value summary
}
```

说明：
- **State/Config** 通过 `AIGAgentBase<TState,TConfig>` 的 `CustomState/CustomConfig` 以 `Any` 封装，保证跨 runtime 兼容。
- **Evidence** 只保存“可显示摘要 + citation 指针”，禁止存放大段原文。
- **AnswerEvent** 用于跨边界传播结果（类似 `ChatResponseEvent`）；如需更细粒度 step events，可在后续扩展（或用 `ExecutionTrace`）。

## Error Handling

### Error Scenarios

1. **Retriever 未配置 / 无数据**
   - **Handling:** 输出可诊断的降级结果（例如：拒答或仅给出“缺证据”说明），stop_reason=NO_EVIDENCE。
   - **User Impact:** 明确提示需要配置 Retriever/索引数据，并保留 diagnostics（iteration=0, evidence=0）。

2. **Embedding 不可用（semantic retrieval disabled）**
   - **Handling:** 自动降级 lexical-only；在 diagnostics 标注 `semantic=disabled`。
   - **User Impact:** 质量下降但仍可工作。

3. **LLM 调用失败（plan/synthesis/critic）**
   - **Handling:** best-effort：记录诊断（error_type/message），并在预算允许时重试有限次（上限由 config 决定）；否则停止并给出可读错误摘要。
   - **User Impact:** 不会卡死；可从日志/trace 定位失败点。

4. **超时/取消**
   - **Handling:** 遵守上层 `CancellationToken`；及时停止并清理，stop_reason=CANCELLED/TIMEOUT。
   - **User Impact:** 快速返回，不产生后续轮次副作用。

## Testing Strategy

### Unit Testing

- **Loop / stop conditions**：最大轮次、预算耗尽、无证据、critic 打回后继续、最终停止原因。
- **Evidence bounding**：证据数量/每条 snippet 字符数/上下文拼装上限。
- **Retriever fallback**：semantic 不可用时 lexical-only；retriever 抛错时 best-effort 降级。
- **Citation integrity**：输出 citations 与 evidence 一致，且无空引用（关键断言必须绑定引用的规则用 critic/fallback 覆盖）。

测试实现手段：
- 用 fake `IAgenticRagRetriever/Planner/Synthesizer/Critic` 做 deterministic 流程测试（无需真实 LLM）。
- 对 memory-based retriever 提供小型 in-memory `IMemoryStore/IMemoryVectorIndex` stub 进行覆盖。

### Integration Testing

- 在 Local runtime 下跑一个最小 demo agent：写入少量 `MemoryEntry` + `MemoryVectorRecord`，然后跑一次 RAG，断言输出含 citations 与 trace id。

### End-to-End Testing

- 作为后续扩展：在 `examples/` 提供可运行示例（需用户提供 LLM provider 配置），验证“10 分钟跑通带引用输出”。


