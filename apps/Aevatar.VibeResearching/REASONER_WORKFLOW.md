# Reasoner 在 Worker Phase 中的工作流程

## 📋 概述

`reasoner` 是 Worker Phase 中第二个执行的 agent（在 `planner` 之后），负责基于提供的材料（DAG facts）进行深度推理，生成具体的推理链和假设。它是连接 Planner 的计划和 Verifier 的验证之间的关键桥梁。

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
    │   ├─> reasoner → 基于 planner 输出进行深度推理（第二个执行）⭐
    │   ├─> verifier → 验证 reasoner 的假设
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

1. **基于材料的推理**: 所有非平凡声明必须基于 DAG facts 或明确标记为假设
2. **推理链构建**: 生成结构化的推理过程，展示从已知事实到结论的推导路径
3. **假设明确化**: 明确区分已验证的事实和需要验证的假设
4. **计算验证**: 如果启用 Python 工具，可以使用 `python_exec` 进行数值/符号计算验证
5. **为 Verifier 提供目标**: 生成需要验证的假设和声明，供 Verifier 验证

---

## 📥 输入

### 1. System Prompt（基础定义）

**文件位置**: `src/Aevatar.VibeResearching/Vibe/VibeReasonerAgent.cs`

```text
You are a research reasoner grounded in provided materials (DAG facts).

Inputs:
- A user question
- "Materials context" (DAG facts) when present

Rules:
- Ground every non-trivial claim in either:
  (a) a material id like [material:...], or
  (b) clearly marked as a hypothesis.
- If a claim is CONSISTENT with the axioms/facts, it should be accepted, even if proving it rigorously would require additional mathematical knowledge (theta series, mass formulas, combinatorial theorems, etc.).
- Only reject it and ask for missing evidence if the claim CONTRADICTS the axioms/facts or is logically inconsistent.
- When computation is needed, use python_exec (if available) to verify.
- Keep reasoning structured and concise; output should be readable in Markdown.
```

**关键规则说明**:
- **Grounding 要求**: 每个非平凡声明必须基于材料 ID（如 `[material:...]`）或明确标记为假设
- **一致性优先**: 如果声明与公理/事实一致，应该接受，即使严格证明需要额外数学知识
- **拒绝条件**: 只有当声明与公理/事实矛盾或逻辑不一致时才拒绝
- **计算支持**: 需要计算时，使用 `python_exec`（如果可用）进行验证
- **输出格式**: 保持结构化、简洁，使用 Markdown 格式

### 2. System Prompt（最终发送给 LLM）

Materials Context 会通过 `req.Context["materials_context"]` 自动追加到 System Prompt 末尾：

```text
You are a research reasoner grounded in provided materials (DAG facts).

Inputs:
- A user question
- "Materials context" (DAG facts) when present

Rules:
- Ground every non-trivial claim in either:
  (a) a material id like [material:...], or
  (b) clearly marked as a hypothesis.
- If a claim is CONSISTENT with the axioms/facts, it should be accepted, even if proving it rigorously would require additional mathematical knowledge (theta series, mass formulas, combinatorial theorems, etc.).
- Only reject it and ask for missing evidence if the claim CONTRADICTS the axioms/facts or is logically inconsistent.
- When computation is needed, use python_exec (if available) to verify.
- Keep reasoning structured and concise; output should be readable in Markdown.

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
var userMessage = BuildWorkerMessage("reasoner", ctx.Question, dag, attachments: ctx.Input.AttachmentPaths,
    extra: string.IsNullOrWhiteSpace(plannerOutput) ? null : $"Planner output (excerpt):\n{Bound(plannerOutput!, 12000)}");
```

**User Prompt 格式**:
```text
Role: reasoner
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

[如果存在 plannerOutput]
Planner output (excerpt):
{plannerOutput (最多 12000 字符)}
```

**关键输入**:
- **Planner 输出**: Reasoner 接收 Planner 的输出摘要（最多 12,000 字符），这是 Reasoner 能够基于 Planner 的计划进行推理的关键输入
- **Plan Context**: 从 DAG 中提取的 Plan 节点，帮助 Reasoner 理解当前轮次的目标
- **DAG stats**: DAG 的节点和边数量，提供上下文信息

---

## 🔧 执行流程

### 代码位置

**文件位置**: `VibeOrchestrator.Workers.cs` → `RunReasonerAsync` (第 204-300 行)

### 执行步骤

1. **触发事件**: 发布 `StepStartedEvent`，步骤名称为 `"vibe.reasoner"`
2. **状态报告**: 发送状态消息 `"正在进行深度推理分析..."`
3. **消息初始化**: 创建消息 ID `msg:{sessionId}:reasoner:{runId}`
4. **构建提示词**:
   - 调用 `BuildWorkerMessage` 构建 User Prompt，包含 Planner 输出摘要
   - 获取 `VibeReasonerAgent.GetSystemPrompt()` 作为基础 System Prompt
   - 获取 `ctx.Materials.RenderedContext` 作为 Materials Context
   - 将 Materials Context 追加到 System Prompt 末尾
5. **获取 Agent**: 调用 `_core.Runtime.GetReasonerAgentAsync` 获取 reasoner agent 实例
6. **刷新工具快照**: 调用 `RefreshToolsSnapshotAsync` 确保 Python 工具（如果启用）可用
7. **构建请求**:
   ```csharp
   var req = new ChatRequest
   {
       Message = userMessage,
       RequestId = ctx.Input.RequestId ?? Guid.NewGuid().ToString("N"),
       StageHint = "session:vibe:reasoner"
   };
   req.Context["agent_id"] = reasonerId;
   req.Context["materials_context"] = materialsContext;
   ```
8. **流式/非流式调用**:
   - 检查 `reasoner.SupportsStreamingAsync`
   - 如果支持流式：调用 `ChatStreamAsync`，实时发送每个 chunk
   - 如果不支持流式：调用 `ChatAsync`，一次性获取完整响应
9. **实时输出**: 每个 chunk 都通过 `EmitAgentDelta` 实时发送给前端
10. **完成事件**: 发布 `StepFinishedEvent`
11. **输出截断**: 将输出限制在 40,000 字符以内（比 Planner 的 20,000 字符限制更高，因为推理过程可能更长）
12. **记录 Prompt**: 创建 `AgentPromptRecord` 记录完整的 System Prompt、User Prompt、Materials Context 和输出
13. **返回结果**: 返回 `(output, promptRecord)`

### Python 工具支持

**配置位置**: `VibeReasonerAgent.cs` 构造函数

```csharp
var python = configuration?.GetSection("Python");
_pythonEnabled = python?.GetValue<bool?>("Enabled") ?? false;
_pythonTimeoutMs = python?.GetValue<int?>("TimeoutMs") ?? 15_000;
_pythonMaxOutputChars = python?.GetValue<int?>("MaxOutputChars") ?? 8_000;
```

**工具注册**:
- 如果 `_pythonEnabled` 为 `true`，Reasoner 会注册 `PythonExecTool`
- Python 工具用于数值/符号计算验证
- 超时时间：15 秒（默认）
- 最大输出字符：8,000（默认）

**安全考虑**:
- Python 工具被标记为 "dangerous"，默认禁用
- 需要通过配置显式启用：`"Python": { "Enabled": true }`

### 错误处理

- 如果发生异常（非取消异常），返回错误消息 `"[reasoner error] {ex.Message}\n\n"`
- 仍然创建 `AgentPromptRecord` 记录错误情况
- 确保事件和状态消息正确发布

---

## 📤 输出格式

### 输出要求

Reasoner 的输出是**自由格式 Markdown 文本**（非 JSON），但应遵循以下结构：

1. **推理链 (Reasoning Chain)**
   - 展示从已知事实到结论的推导路径
   - 每一步都应该明确引用材料 ID 或标记为假设

2. **假设 (Hypotheses)**
   - 明确列出所有假设
   - 区分已验证的事实和需要验证的假设

3. **推导步骤 (Derivation Steps)**
   - 展示具体的推导过程
   - 如果使用了 Python 计算，展示计算过程和结果

4. **材料引用 (Material Citations)**
   - 使用 `[material:...]` 或 `[dag:nodeId]` 格式引用 DAG facts
   - 确保每个非平凡声明都有明确的来源

### 示例输出

```markdown
## Reasoning Chain

### Step 1: Base Case
From [dag:fact-001], we know that property P holds for the minimal element in set S.

### Step 2: Inductive Step
**Hypothesis**: If P holds for element x, then P holds for f(x).

Based on the structure theorem from [dag:fact-002], we can derive:
- f(x) maintains the same structure as x
- The properties that make P true for x are preserved in f(x)

**Derivation**:
1. From [dag:fact-002], we know that f preserves structure.
2. Since P depends only on structure (from [dag:fact-001]), P(f(x)) follows.

### Step 3: Conclusion
By induction, P holds for all elements in S.

## Hypotheses to Verify
- **H1**: f preserves structure (needs verification)
- **H2**: P depends only on structure (needs verification)

## Computations (if applicable)
Using python_exec to verify the base case:
```python
# Check minimal element
min_element = find_minimal(S)
assert P(min_element) == True
```
Result: VERIFIED
```

---

## 🔗 与前后 Agent 的交互

### 1. 接收 Planner 输出

**代码位置**: `VibeOrchestrator.Workers.cs` → `RunReasonerAsync` (第 217-218 行)

```csharp
var userMessage = BuildWorkerMessage("reasoner", ctx.Question, dag, attachments: ctx.Input.AttachmentPaths,
    extra: string.IsNullOrWhiteSpace(plannerOutput) ? null : $"Planner output (excerpt):\n{Bound(plannerOutput!, 12000)}");
```

**如何使用 Planner 输出**:
- Reasoner 读取 Planner 的执行步骤
- 基于这些步骤进行深度推理
- 将 Planner 的抽象计划转化为具体的推理链

### 2. 为 Verifier 提供输入

**代码位置**: `VibeOrchestrator.Workers.cs` → `RunVerifierAsync` / `RunMultiStageVerifierAsync`

```csharp
var (verifierOutput, verifierPrompt) = await RunVerifierAsync(
    ctx,
    getDagSnapshot(),
    outputs.TryGetValue("reasoner", out var r) ? r : null,  // ← Reasoner 输出作为输入
    provider,
    ct);
```

**Verifier 的 User Prompt 包含**:
```text
Role: verifier
Question: {ctx.Question}
...

Reasoner output (excerpt):
{Bound(reasonerOutput, 3500)}  // ← Reasoner 输出的摘要（最多 3500 字符）
```

**Verifier 如何使用**:
- Verifier 读取 Reasoner 的推理链和假设
- 验证关键声明和假设
- 使用 Python 工具（如果可用）进行数值/符号检查

### 3. Dag Builder 使用 Reasoner 输出

**代码位置**: `VibeOrchestrator.GoalsAndMessages.cs` → `BuildDagBuilderMessage`

**Dag Builder 的 User Prompt 包含**:
```text
Worker outputs (excerpts):
[planner]
{plannerOutput (最多 2200 字符)}

[reasoner]
{reasonerOutput (最多 2200 字符)}  // ← Reasoner 输出的摘要

[verifier]
{verifierOutput (最多 3000 字符)}
```

**Dag Builder 如何使用**:
- Dag Builder 从 Reasoner 输出中提取知识项（axioms、theorems、definitions）
- 提取推理步骤作为 `proof` 字段的来源（优先级：verifier > reasoner > planner）
- 创建 Knowledge 节点时，需要关联到 Plan 节点（通过 `motivatedByPlanNodeId`）

---

## 🎯 关键设计决策

### 1. 为什么在 Planner 之后执行？

- **依赖关系**: Reasoner 需要 Planner 的计划作为推理起点
- **方向明确**: Planner 设定了研究方向，Reasoner 在此基础上进行深度推理
- **假设明确化**: Planner 识别了假设和未知项，Reasoner 需要明确这些假设的推导过程

### 2. 为什么输出是自由格式 Markdown？

- **灵活性**: 推理过程可能很复杂，自由格式更灵活
- **可读性**: Markdown 格式便于展示推理链和推导步骤
- **结构化程度**: Reasoner 的输出是"推理过程"，不需要像 DAG mutation 那样严格结构化

### 3. Materials Context 的作用

- **知识基础**: 提供当前 DAG 中的知识节点，让 Reasoner 基于已有知识进行推理
- **引用支持**: DAG FACT INDEX 允许 Reasoner 引用已有知识节点
- **一致性检查**: 帮助 Reasoner 检查推理结果是否与已有知识一致

### 4. Python 工具的作用

- **计算验证**: 对于需要数值/符号计算的问题，Python 工具可以提供验证
- **假设检验**: 可以通过计算检验假设是否成立
- **安全性**: 默认禁用，需要通过配置显式启用

### 5. 输出长度限制

- **40,000 字符**: 比 Planner 的 20,000 字符限制更高，因为推理过程可能更长
- **截断处理**: 使用 `Bound(sb.ToString(), 40_000)` 确保输出不超过限制
- **摘要传递**: 传递给 Verifier 和 Dag Builder 时，会进一步截断（Verifier: 3,500 字符，Dag Builder: 2,200 字符）

---

## 📊 输出示例分析

### 场景：数学证明研究

**输入**:
- Question: "Prove that property P holds for all elements in set S"
- Planner Output: "1. Verify base case. 2. Assume P(x), prove P(f(x)). 3. Use structure theorem."
- Materials Context: [dag:fact-001] (base case), [dag:fact-002] (structure theorem)

**Reasoner 输出**:
```markdown
## Reasoning Chain

### Step 1: Base Case Verification
From [dag:fact-001], we know that property P holds for the minimal element in set S. This establishes the base case for our induction.

### Step 2: Inductive Hypothesis
**Hypothesis H1**: If P holds for element x, then P holds for f(x).

### Step 3: Inductive Step Derivation
Based on the structure theorem from [dag:fact-002]:
- The structure theorem states that f preserves the key structural properties
- Since P depends only on these structural properties (from [dag:fact-001])
- We can conclude that P(f(x)) holds

**Derivation**:
1. From [dag:fact-002]: f preserves structure
2. From [dag:fact-001]: P depends only on structure
3. Therefore: P(f(x)) follows from P(x)

### Step 4: Conclusion
By mathematical induction, P holds for all elements in S.

## Hypotheses to Verify
- **H1**: f preserves structure (needs verification via [dag:fact-002])
- **H2**: P depends only on structure (needs verification via [dag:fact-001])

## Computations
Using python_exec to verify the base case:
```python
min_element = find_minimal(S)
assert P(min_element) == True
```
Result: VERIFIED
```

**后续流程**:
1. **Verifier** 读取此输出，验证 H1 和 H2 假设
2. **Dag Builder** 从所有 worker 输出中提取知识，创建 Knowledge 节点，并关联到 Plan 节点

---

## 🔍 调试和监控

### 日志位置

- **Prompt 记录**: `AgentPromptRecord` 保存在 `promptRecords["reasoner"]` 中
- **输出内容**: 保存在 `outputs["reasoner"]` 中，后续会被传递给 verifier 和 dag_builder
- **事件流**: `StepStartedEvent` 和 `StepFinishedEvent` 通过 `session.Events` 发布

### 常见问题

1. **输出为空或过短**
   - 检查 Planner 输出是否正确传递
   - 检查 Materials Context 是否正确注入
   - 检查 Plan Context 是否包含有效的 Plan 节点

2. **推理链不完整**
   - 确认 Reasoner 是否正确引用了 DAG facts
   - 检查 Materials Context 是否包含足够的上下文信息

3. **Python 工具未执行**
   - 检查配置中 `Python.Enabled` 是否为 `true`
   - 检查 `RefreshToolsSnapshotAsync` 是否成功执行
   - 检查 Python 工具的超时和输出限制设置

4. **后续 Agent 无法使用 Reasoner 输出**
   - 检查输出是否被正确截断（40,000 字符限制）
   - 检查 Verifier 的 User Prompt 是否包含 Reasoner 输出摘要

---

## 📚 相关文档

- [PLANNER_WORKFLOW.md](./PLANNER_WORKFLOW.md) - Planner 的工作流程
- [DAG_BUILDER_WORKFLOW.md](./DAG_BUILDER_WORKFLOW.md) - Dag Builder 如何从 worker outputs 提取知识
- [AGENT_PROMPTS.md](./AGENT_PROMPTS.md) - 所有 Agent 的提示词详细说明
- [RESEARCH_ROUND_AND_DAG_FLOW.md](./RESEARCH_ROUND_AND_DAG_FLOW.md) - 完整的研究轮次和 DAG 流程

---

*最后更新: 2026-01-29 | Aevatar VibeResearching System*