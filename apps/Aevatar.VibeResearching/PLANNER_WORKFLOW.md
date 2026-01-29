# Planner 在 Worker Phase 中的工作流程

## 📋 概述

`planner` 是 Worker Phase 中的第一个执行 agent，负责将研究问题转化为可执行的计划，明确假设、未知项和所需证据。它是整个研究轮次的基础，为后续的 `reasoner`、`verifier` 和 `dag_builder` 提供方向指引。

---

## 🔄 在整个研究流程中的位置

```
ExecuteOneRoundAsync
    │
    ├─> Brief Generation (research_assistant [MODE:BRIEF])
    │   └─> 创建 Plan 节点（Milestones）→ DAG
    │
    ├─> Plan Phase (research_assistant [MODE:PLAN])
    │   └─> 创建 Plan 节点（Round Plans）→ DAG
    │
    ├─> Worker Phase ⭐
    │   ├─> planner → 生成执行计划（第一个执行）
    │   ├─> reasoner → 使用 planner 输出进行深度推理
    │   ├─> verifier → 验证假设
    │   ├─> librarian → 提取可信公理（可选）
    │   └─> dag_builder → 从 worker outputs 提取知识 → 创建 Knowledge 节点
    │
    ├─> DAG Consensus Phase
    │   └─> 应用 dag_builder 的输出 → 更新 DAG
    │
    └─> Summary Phase (research_assistant [MODE:SUMMARY])
        └─> 生成研究总结
```

---

## 🎯 核心职责

1. **问题分解**: 将研究问题分解为可执行的步骤
2. **假设明确化**: 明确区分哪些可以从公理推导，哪些需要经验证据
3. **未知项识别**: 识别需要进一步探索的未知领域
4. **计算规划**: 如果涉及计算，指定计算内容和可证伪假设的条件
5. **为后续 Agent 提供方向**: 为 `reasoner` 提供推理方向，为 `verifier` 提供验证目标

---

## 📥 输入

### 1. System Prompt（基础定义）

**文件位置**: `src/Aevatar.VibeResearching/Vibe/VibePlannerAgent.cs`

```text
You are a research planner.

Inputs:
- A user question
- "Materials context" (axioms + references) when available

Rules:
- Be explicit about assumptions and unknowns.
- Separate what is derivable from axioms vs what requires empirical/extra references.
- Produce a short plan with steps that can be executed (including computations if needed).
- If you propose computations, specify what to compute and what would falsify the hypothesis.
```

### 2. System Prompt（最终发送给 LLM）

Materials Context 会通过 `req.Context["materials_context"]` 自动追加到 System Prompt 末尾：

```text
You are a research planner.

Inputs:
- A user question
- "Materials context" (axioms + references) when available

Rules:
- Be explicit about assumptions and unknowns.
- Separate what is derivable from axioms vs what requires empirical/extra references.
- Produce a short plan with steps that can be executed (including computations if needed).
- If you propose computations, specify what to compute and what would falsify the hypothesis.

Materials context:
DAG FACT INDEX (cite by id):
- [dag:nodeId1] Title 1
- [dag:nodeId2] Title 2
... (最多 200 个 fact 的索引)

DAG FACTS (knowledge nodes):
[dag:nodeId1] (score=X) Title 1
Content of fact 1...
(最多 32 个按相关性排序的 fact 完整内容)

[dag:nodeId2] (score=Y) Title 2
Content of fact 2...
...
```

**Materials Context 内容**:
- **DAG FACT INDEX**: 最多 200 个知识节点的索引（用于引用）
- **DAG FACTS**: 最多 32 个按相关性排序的知识节点完整内容
- 这些内容来自 Materials Runtime，基于当前研究问题和 DAG 状态动态检索

### 3. User Prompt

**文件位置**: `VibeOrchestrator.GoalsAndMessages.cs` → `BuildWorkerMessage`

**构建逻辑**:
```csharp
var userMessage = BuildWorkerMessage("planner", ctx.Question, dag, attachments: ctx.Input.AttachmentPaths);
```

**User Prompt 格式**:
```text
Role: planner
Question: {ctx.Question}

[如果存在附件]
AttachmentPaths:
- {attachmentPath1}
- {attachmentPath2}

Plan:
{BuildPlanContextFromDag(dag)}
# 示例输出：
# - milestone-1: Expected output for milestone 1
# - milestone-2: Expected output for milestone 2
# - round-plan-abc123: Current round plan

DAG stats:
- nodes={dag.Nodes.Count}, edges={dag.Edges.Count}
```

**Plan Context 说明**:
- `BuildPlanContextFromDag` 从 DAG 中提取所有 Plan 节点（包括 Milestones 和 Round Plans）
- 格式为 `- {nodeId}: {label}`
- 这些 Plan 节点来自 `[MODE:BRIEF]` 生成的 Milestones 和 `[MODE:PLAN]` 生成的 Round Plans
- Planner 需要理解这些计划节点，并在其输出中考虑如何推进这些计划

---

## 🔧 执行流程

### 代码位置

**文件位置**: `VibeOrchestrator.Workers.cs` → `RunPlannerAsync` (第 123-202 行)

### 执行步骤

1. **触发事件**: 发布 `StepStartedEvent`，步骤名称为 `"vibe.planner"`
2. **状态报告**: 发送状态消息 `"正在分析研究问题，制定研究计划..."`
3. **消息初始化**: 创建消息 ID `msg:{sessionId}:planner:{runId}`
4. **构建提示词**:
   - 调用 `BuildWorkerMessage` 构建 User Prompt
   - 获取 `VibePlannerAgent.GetSystemPrompt()` 作为基础 System Prompt
   - 获取 `ctx.Materials.RenderedContext` 作为 Materials Context
   - 将 Materials Context 追加到 System Prompt 末尾
5. **获取 Agent**: 调用 `_core.Runtime.GetPlannerAgentAsync` 获取 planner agent 实例
6. **构建请求**:
   ```csharp
   var req = new ChatRequest
   {
       Message = userMessage,
       RequestId = ctx.Input.RequestId ?? Guid.NewGuid().ToString("N"),
       StageHint = "session:vibe:planner"
   };
   req.Context["agent_id"] = plannerId;
   req.Context["materials_context"] = materialsContext;
   ```
7. **流式调用**: 调用 `planner.ChatStreamAsync(req, ct)` 进行流式 LLM 调用
8. **实时输出**: 每个 chunk 都通过 `EmitAgentDelta` 实时发送给前端
9. **完成事件**: 发布 `StepFinishedEvent`
10. **输出截断**: 将输出限制在 20,000 字符以内
11. **记录 Prompt**: 创建 `AgentPromptRecord` 记录完整的 System Prompt、User Prompt、Materials Context 和输出
12. **返回结果**: 返回 `(output, promptRecord)`

### 错误处理

- 如果发生异常（非取消异常），返回错误消息 `"[planner error] {ex.Message}\n\n"`
- 仍然创建 `AgentPromptRecord` 记录错误情况
- 确保事件和状态消息正确发布

---

## 📤 输出格式

### 输出要求

Planner 的输出是**自由格式文本**（非 JSON），但应遵循以下结构：

1. **假设 (Assumptions)**
   - 明确列出所有假设
   - 区分哪些可以从公理推导，哪些需要经验证据

2. **未知项 (Unknowns)**
   - 列出需要进一步探索的未知领域
   - 明确哪些信息缺失

3. **执行步骤 (Executable Steps)**
   - 列出可执行的步骤
   - 如果涉及计算，指定：
     - 计算什么
     - 什么结果会证伪假设

4. **证据需求 (Required Evidence)**
   - 明确需要哪些证据来支持或反驳假设

### 示例输出

```
## Assumptions
- We assume that the mathematical framework provided in the materials is consistent.
- We assume that the computational resources are sufficient for the proposed calculations.

## Unknowns
- The exact relationship between X and Y needs to be determined.
- Whether condition Z holds in this context is unclear.

## Executable Steps
1. Derive the relationship between X and Y from the axioms provided in [dag:fact-001].
2. Compute the value of function f(x) for the given parameters.
   - If f(x) < 0, this would falsify hypothesis H1.
3. Verify that property P holds under conditions C1, C2, C3.

## Required Evidence
- Empirical data to validate assumption A1.
- Reference to theorem T from literature to support step 2.
```

---

## 🔗 与后续 Agent 的交互

### 1. Reasoner 使用 Planner 输出

**代码位置**: `VibeOrchestrator.Workers.cs` → `RunReasonerAsync` (第 204-302 行)

```csharp
var (reasonerOutput, reasonerPrompt) = await RunReasonerAsync(
    ctx,
    getDagSnapshot(),
    outputs.TryGetValue("planner", out var p) ? p : null,  // ← Planner 输出作为输入
    provider,
    ct);
```

**Reasoner 的 User Prompt 包含**:
```text
Role: reasoner
Question: {ctx.Question}
...

Planner output (excerpt):
{Bound(plannerOutput, 12000)}  // ← Planner 输出的摘要（最多 12000 字符）
```

**Reasoner 如何使用**:
- Reasoner 读取 planner 的执行步骤
- 基于这些步骤进行深度推理
- 生成具体的推理链和假设

### 2. Dag Builder 使用 Planner 输出

**代码位置**: `VibeOrchestrator.GoalsAndMessages.cs` → `BuildDagBuilderMessage`

**Dag Builder 的 User Prompt 包含**:
```text
Worker outputs (excerpts):
[planner]
{plannerOutput (最多 2200 字符)}  // ← Planner 输出的摘要（最多 2200 字符）

[reasoner]
{reasonerOutput (最多 2200 字符)}

[verifier]
{verifierOutput (最多 3000 字符)}
```

**Dag Builder 如何使用**:
- Dag Builder 从 planner 输出中提取知识项
- 提取的方法论和步骤可以作为 `proof` 字段的来源（优先级：verifier > reasoner > planner）
- 创建 Knowledge 节点时，需要关联到 Plan 节点（通过 `motivatedByPlanNodeId`）

---

## 🎯 关键设计决策

### 1. 为什么 Planner 是第一个 Worker？

- **方向设定**: Planner 为整个研究轮次设定方向
- **假设明确化**: 提前明确假设和未知项，避免后续 agent 走弯路
- **依赖关系**: Reasoner 需要 Planner 的输出作为推理起点

### 2. 为什么输出是自由格式而非 JSON？

- **灵活性**: Planner 需要处理各种类型的研究问题，自由格式更灵活
- **可读性**: 自由格式的输出更容易被后续 agent（reasoner）理解和使用
- **结构化程度**: Planner 的输出是"计划"，不需要像 DAG mutation 那样严格结构化

### 3. Materials Context 的作用

- **知识基础**: 提供当前 DAG 中的知识节点，让 Planner 了解已有知识
- **引用支持**: DAG FACT INDEX 允许 Planner 引用已有知识节点
- **避免重复**: 帮助 Planner 识别哪些知识已经存在，避免重复工作

### 4. Plan Context 的作用

- **计划对齐**: 让 Planner 了解当前轮次的目标（来自 Round Plan）和整体里程碑（来自 Milestones）
- **上下文理解**: Planner 需要知道自己在整个研究流程中的位置
- **输出关联**: Planner 的输出应该与 Plan 节点对齐，以便后续 Dag Builder 创建 `motivatedByPlanNodeId` 关联

---

## 📊 输出示例分析

### 场景：数学证明研究

**输入**:
- Question: "Prove that property P holds for all elements in set S"
- Plan: "milestone-1: Establish the base case for property P"

**Planner 输出**:
```
## Assumptions
- Set S is well-defined and non-empty.
- The axioms from [dag:fact-001] about set operations are valid.

## Unknowns
- Whether the induction step can be completed.
- What additional lemmas might be needed.

## Executable Steps
1. Verify the base case: Check if P holds for the minimal element in S.
2. Assume P holds for element x, then prove P holds for f(x).
   - If we find a counterexample where P(f(x)) is false, this falsifies the hypothesis.
3. Use the structure theorem from [dag:fact-002] to guide the proof.

## Required Evidence
- Axioms from [dag:fact-001] for set operations.
- Structure theorem from [dag:fact-002] for the proof framework.
```

**后续流程**:
1. **Reasoner** 读取此输出，进行深度推理，生成具体的证明步骤
2. **Verifier** 验证证明步骤的正确性
3. **Dag Builder** 从所有 worker 输出中提取知识，创建 Knowledge 节点，并关联到 Plan 节点 `milestone-1`

---

## 🔍 调试和监控

### 日志位置

- **Prompt 记录**: `AgentPromptRecord` 保存在 `promptRecords["planner"]` 中
- **输出内容**: 保存在 `outputs["planner"]` 中，后续会被传递给 reasoner 和 dag_builder
- **事件流**: `StepStartedEvent` 和 `StepFinishedEvent` 通过 `session.Events` 发布

### 常见问题

1. **输出为空或过短**
   - 检查 Materials Context 是否正确注入
   - 检查 Plan Context 是否包含有效的 Plan 节点
   - 检查 User Prompt 是否完整

2. **输出与 Plan 不对齐**
   - 确认 `BuildPlanContextFromDag` 正确提取了 Plan 节点
   - 检查 Planner 是否理解了 Plan Context 的含义

3. **后续 Agent 无法使用 Planner 输出**
   - 检查输出是否被正确截断（20,000 字符限制）
   - 检查 Reasoner 的 User Prompt 是否包含 Planner 输出摘要

---

## 📚 相关文档

- [RESEARCH_ASSISTANT_WORKFLOW.md](./RESEARCH_ASSISTANT_WORKFLOW.md) - Research Assistant 的三种模式（BRIEF/PLAN/SUMMARY）
- [DAG_BUILDER_WORKFLOW.md](./DAG_BUILDER_WORKFLOW.md) - Dag Builder 如何从 worker outputs 提取知识
- [AGENT_PROMPTS.md](./AGENT_PROMPTS.md) - 所有 Agent 的提示词详细说明
- [RESEARCH_ROUND_AND_DAG_FLOW.md](./RESEARCH_ROUND_AND_DAG_FLOW.md) - 完整的研究轮次和 DAG 流程

---

*最后更新: 2026-01-29 | Aevatar VibeResearching System*