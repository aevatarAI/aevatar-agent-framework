# Design Document

## Overview

本设计文档描述对 `AIGAgentBase*` 的“重构型治理”（refactor-only）方案：通过拆分 partial 文件、收敛关键上下文 key、统一同步/流式策略与可观测性，让 AI.Core 基类从“多域巨石”回到可维护的模块化结构。

注意：本规格不引入新业务能力；所有改动以“行为不变、测试不退化”为硬约束。

## Steering Document Alignment

### Technical Standards (tech.md)

仓库未提供 steering `tech.md`；本设计遵循仓库既有标准：
- 文件规模：单文件不超过 800 行（`AGENTS.md`）。
- 跨边界类型必须 Protobuf：本规格不新增跨边界类型；若后续需要，将另起 proto 规格。
- best-effort：失败不阻塞主链路，但要可观测。

### Project Structure (structure.md)

仓库未提供 steering `structure.md`；本设计延续 AI.Core 的现有组织方式：
- `AIGAgentBase.*.cs` 采用 `partial` 进行“主题拆分”
- 公共 helper/常量放入 `Helpers/` 或 `Utils/`，避免跨文件相互耦合

## Code Reuse Analysis

### Existing Components to Leverage

- **现有拆分模式**：`AIGAgentBase.Tools.cs` / `AIGAgentBase.AgentSkills.cs` / `AIGAgentBase.MemoryStore.cs` 已经是按主题拆分的 partial。
- **近期已完成拆分**（已在代码中落地）：
  - `AIGAgentBase.Chat.cs`：Chat/Streaming
  - `AIGAgentBase.History.cs`：History/Compaction/Summary（Layer 1+2）
- **工具治理**：`AIGAgentBase.Tools.cs` 内已包含 allowlist + 安全策略；`AIGAgentBase.Hooks.cs` 内有 hook 管线对接点。

### Integration Points

- **Chat/Streaming**：`ChatAsync` / `ChatStreamAsync` 的策略一致性（Hooks/预算/安全）
- **Tool Loop**：allowlist、tool policy 与 hooks 的交互边界
- **History Summary**：`State.Context["history_summary"]` 的单点封装与对外可观测

## Architecture

### Modular Design Principles

- **主题拆分**：每个 partial 文件聚焦一个 domain（Chat、History、Tools、Skills、Hooks、MemoryStore、Init/Embeddings）
- **关键 key 单点管理**：对于 `history_summary`、`aevatar.allowed_tools` 等关键 key，集中到一个静态类或 helper，禁止散落硬编码
- **一致性优先**：同步与流式调用遵循同一套策略；无法做到的部分必须显式记录差异与 TODO（可观测）

### 拟议目录结构（目标态）

```mermaid
graph TD
  A[AIGAgentBase.cs (core fields + init/provider/embeddings)] --> B[AIGAgentBase.Chat.cs]
  A --> C[AIGAgentBase.History.cs]
  A --> D[AIGAgentBase.Tools.cs]
  A --> E[AIGAgentBase.AgentSkills.cs]
  A --> F[AIGAgentBase.Hooks.cs]
  A --> G[AIGAgentBase.MemoryStore.cs]
  D --> H[Utils/Key helpers]
  C --> H
  F --> H
```

## Components and Interfaces

### Component 1: Context/Metadata Key Registry（helper）

- **Purpose:** 收敛关键上下文 key 的定义与访问方式，减少 stringly-typed 传播
- **Interfaces:** `const string HistorySummary = "history_summary"`、`const string ToolAllowlist = "aevatar.allowed_tools"` 等；提供 `TryGet/Set/Clear` helper（含类型分支集中）
- **Dependencies:** 无
- **Reuses:** 复用 `AIGAgentBase.Tools.cs` 现有 allowlist 解析逻辑（抽取到 helper）

### Component 2: Streaming hook alignment（最小对齐）

- **Purpose:** 让 `ChatStreamAsync` 至少执行 “BeforeLLMRequest” hooks（预算告警/策略打标）并保持工具可见性策略一致
- **Interfaces:** 在 streaming 构建 `llmRequest` 后调用 `GenerateLLMWithHooksAsync` 的轻量前置阶段（不强行重写 provider stream）
- **Dependencies:** `AIGAgentBase.Hooks.cs`
- **Reuses:** 复用 `ContextBudgetMonitorHook` 的 warn-only 模式

## Data Models

本规格不新增跨边界模型；仅新增内部 helper/常量类型（若需要）。

## Error Handling

### Error Scenarios

1. **Scenario: best-effort 吞异常导致无法排障**
   - **Handling:** 将关键吞异常分支提升为 debug 日志（并可节流）
   - **User Impact:** 无（仍 best-effort），但可定位

2. **Scenario: string key 漂移**
   - **Handling:** 关键 key 集中到 helper；替换散落硬编码
   - **User Impact:** 无，但降低运行时错误概率

## Testing Strategy

### Unit Testing

- helper 的 key get/set/clear 覆盖（尤其 allowlist 多类型分支）
- streaming 对齐的最小 hook 行为（warn-only 不改变输出）

### Integration Testing

- 复用 `Aevatar.Agents.AI.Core.Tests`：确保拆分不影响 `InitializeAsync/ChatAsync/ToolLoop` 既有用例

### End-to-End Testing

本规格不新增 E2E；以现有 test 项目回归为准。


