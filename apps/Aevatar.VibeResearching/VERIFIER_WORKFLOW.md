# Verifier 在 Worker Phase 中的工作流程

## 📋 概述

`verifier` 是 Worker Phase 中第三个执行的 agent（在 `reasoner` 之后），负责验证 Reasoner 生成的关键声明和假设。它使用具体的检查方法（包括 Python 工具）来验证推理的正确性，为后续的 `dag_builder` 提供已验证的知识项。

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
    │   ├─> reasoner → 基于 planner 输出进行深度推理（第二个执行）
    │   ├─> verifier → 验证 reasoner 的假设（第三个执行）⭐
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

1. **关键声明验证**: 验证 Reasoner 生成的最关键声明和假设
2. **一致性检查**: 检查声明是否与 DAG facts 一致或矛盾
3. **计算验证**: 如果启用 Python 工具，使用 `python_exec` 进行数值/符号检查
4. **结构化输出**: 输出结构化的验证结果（Claim, Check, Result, Notes）
5. **为 Dag Builder 提供已验证知识**: 生成已验证的知识项，供 Dag Builder 提取

---

## 📥 输入

### 1. System Prompt（基础定义）

**文件位置**: `src/Aevatar.VibeResearching/Vibe/VibeVerifierAgent.cs`

```text
You are a verifier.

Goal:
- Verify the most critical claims using concrete checks.

Rules:
- If a hypothesis is CONSISTENT with the axioms/facts and represents a reasonable mathematical claim, it should be considered as "VERIFIED", even if proving it rigorously would require additional mathematical knowledge (theta series, mass formulas, combinatorial theorems, etc.).
- Only consider "NOT VERIFIED" if the hypothesis CONTRADICTS the axioms/facts or is logically inconsistent.
- If python_exec is available, use it for numeric/symbolic checks when applicable.
- Keep output short and structured:
  - Claim
  - Check
  - Result (VERIFIED / NOT VERIFIED / INCONCLUSIVE)
  - Notes
```

**关键规则说明**:
- **一致性优先**: 如果假设与公理/事实一致且是合理的数学声明，应该标记为 "VERIFIED"，即使严格证明需要额外数学知识
- **拒绝条件**: 只有当假设与公理/事实矛盾或逻辑不一致时才标记为 "NOT VERIFIED"
- **计算支持**: 如果可用，使用 `python_exec` 进行数值/符号检查
- **输出格式**: 保持简短和结构化，包含 Claim、Check、Result、Notes

### 2. System Prompt（最终发送给 LLM）

Materials Context 会通过 `req.Context["materials_context"]` 自动追加到 System Prompt 末尾：

```text
You are a verifier.

Goal:
- Verify the most critical claims using concrete checks.

Rules:
- If a hypothesis is CONSISTENT with the axioms/facts and represents a reasonable mathematical claim, it should be considered as "VERIFIED", even if proving it rigorously would require additional mathematical knowledge (theta series, mass formulas, combinatorial theorems, etc.).
- Only consider "NOT VERIFIED" if the hypothesis CONTRADICTS the axioms/facts or is logically inconsistent.
- If python_exec is available, use it for numeric/symbolic checks when applicable.
- Keep output short and structured:
  - Claim
  - Check
  - Result (VERIFIED / NOT VERIFIED / INCONCLUSIVE)
  - Notes

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
var userMessage = BuildWorkerMessage("verifier", ctx.Question, dag, attachments: ctx.Input.AttachmentPaths,
    extra: string.IsNullOrWhiteSpace(reasonerOutput) ? null : $"Reasoner output (excerpt):\n{Bound(reasonerOutput!, 3500)}");
```

**User Prompt 格式**:
```text
Role: verifier
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

[如果存在 reasonerOutput]
Reasoner output (excerpt):
{reasonerOutput (最多 3500 字符)}
```

**关键输入**:
- **Reasoner 输出**: Verifier 接收 Reasoner 的输出摘要（最多 3,500 字符），这是 Verifier 能够验证 Reasoner 推理结果的关键输入
- **Plan Context**: 从 DAG 中提取的 Plan 节点，帮助 Verifier 理解当前轮次的目标
- **DAG stats**: DAG 的节点和边数量，提供上下文信息

---

## 🔧 执行流程

### 代码位置

**文件位置**: `VibeOrchestrator.Workers.cs` → `RunVerifierAsync` (第 388-480 行) 和 `RunMultiStageVerifierAsync` (第 488-510 行)

### 执行模式选择

**代码位置**: `VibeOrchestrator.ExecuteOneRound.Parts.cs` (第 623-655 行)

```csharp
// Check if multi-stage verification is enabled (default: true)
var useMultiStage = _core.Configuration?.GetValue<bool?>("Vibe:MultiStageVerification:Enabled") ?? true;

if (useMultiStage)
{
    // Multi-stage verification: Scout (2 workers) + Prover (5 workers)
    var multiStageResult = await RunMultiStageVerifierAsync(...);
    outputs[agent] = multiStageResult.Summary;
    outputs["verifier_passed"] = multiStageResult.OverallPass.ToString();
}
else
{
    // Legacy single-pass verification
    var (verifierOutput, verifierPrompt) = await RunVerifierAsync(...);
    outputs[agent] = verifierOutput;
}
```

**两种模式**:
1. **Multi-Stage Verification** (默认启用): 使用 `RunMultiStageVerifierAsync`
   - **当前实现**: 目前是 fallback 到单次验证（TODO: 实现完整的 Scout + Prover 阶段）
   - **计划**: Scout 阶段（2 workers）快速检测反例和缺失前提，Prover 阶段（5 workers，需 ≥3 通过）验证推理过程正确性
2. **Legacy Single-Pass**: 使用 `RunVerifierAsync`，单次验证

### Single-Pass 验证执行步骤

1. **触发事件**: 发布 `StepStartedEvent`，步骤名称为 `"vibe.verifier"`
2. **状态报告**: 发送状态消息 `"正在验证推理步骤的正确性..."`
3. **消息初始化**: 创建消息 ID `msg:{sessionId}:verifier:{runId}`
4. **构建提示词**:
   - 调用 `BuildWorkerMessage` 构建 User Prompt，包含 Reasoner 输出摘要
   - 获取 `VibeVerifierAgent.GetSystemPrompt()` 作为基础 System Prompt
   - 获取 `ctx.Materials.RenderedContext` 作为 Materials Context
   - 将 Materials Context 追加到 System Prompt 末尾
5. **获取 Agent**: 调用 `_core.Runtime.GetVerifierAgentAsync` 获取 verifier agent 实例
6. **构建请求**:
   ```csharp
   var req = new ChatRequest
   {
       Message = userMessage,
       RequestId = ctx.Input.RequestId ?? Guid.NewGuid().ToString("N"),
       StageHint = "session:vibe:verifier"
   };
   req.Context["agent_id"] = verId;
   req.Context["materials_context"] = materialsContext;
   ```
7. **流式/非流式调用**:
   - 检查 `ver.SupportsStreamingAsync`
   - 如果支持流式：调用 `ChatStreamAsync`，实时发送每个 chunk
   - 如果不支持流式：调用 `ChatAsync`，一次性获取完整响应
8. **实时输出**: 每个 chunk 都通过 `EmitAgentDelta` 实时发送给前端
9. **完成事件**: 发布 `StepFinishedEvent`
10. **输出截断**: 将输出限制在 20,000 字符以内
11. **记录 Prompt**: 创建 `AgentPromptRecord` 记录完整的 System Prompt、User Prompt、Materials Context 和输出
12. **返回结果**: 返回 `(output, promptRecord)`

### Multi-Stage 验证执行步骤（当前实现）

**代码位置**: `VibeOrchestrator.Workers.cs` → `RunMultiStageVerifierAsync` (第 488-510 行)

**当前实现**:
```csharp
// TODO: Implement full multi-stage verification (Scout + Prover phases)
// For now, delegate to single-pass verification as a fallback
var (output, promptRecord) = await RunVerifierAsync(ctx, dag, reasonerOutput, providerName, ct);

// Simple heuristic: check if output contains verification success indicators
var overallPass = output.Contains("VERIFIED", StringComparison.OrdinalIgnoreCase) ||
                  output.Contains("verified", StringComparison.OrdinalIgnoreCase) ||
                  (!output.Contains("NOT VERIFIED", StringComparison.OrdinalIgnoreCase) &&
                   !output.Contains("INCONCLUSIVE", StringComparison.OrdinalIgnoreCase));

return new MultiStageVerificationResult(
    Summary: output,
    OverallPass: overallPass,
    PromptRecord: promptRecord
);
```

**当前行为**:
- 调用 `RunVerifierAsync` 执行单次验证
- 使用简单的启发式方法检查输出中是否包含验证成功指示符
- 返回 `MultiStageVerificationResult`，包含摘要、整体通过状态和 Prompt 记录

**计划实现**（TODO）:
- **Scout 阶段**: 2 个 workers 并行快速检测反例和缺失前提
- **Prover 阶段**: 5 个 workers 并行验证推理过程正确性，需要 ≥3 个通过

### Python 工具支持

**配置位置**: `VibeVerifierAgent.cs` 构造函数

```csharp
var python = configuration?.GetSection("Python");
_pythonEnabled = python?.GetValue<bool?>("Enabled") ?? false;
_pythonTimeoutMs = python?.GetValue<int?>("TimeoutMs") ?? 15_000;
_pythonMaxOutputChars = python?.GetValue<int?>("MaxOutputChars") ?? 8_000;
```

**工具注册**:
- 如果 `_pythonEnabled` 为 `true`，Verifier 会注册 `PythonExecTool`
- Python 工具用于数值/符号计算验证
- 超时时间：15 秒（默认）
- 最大输出字符：8,000（默认）

**安全考虑**:
- Python 工具被标记为 "dangerous"，默认禁用
- 需要通过配置显式启用：`"Python": { "Enabled": true }`

### 状态管理

**代码位置**: `VibeVerifierAgent.cs` 构造函数

```csharp
// Verifier should be stateless across calls:
// - Avoid cross-round bleed and reduce token/state growth.
// - Quorum consensus may reuse multiple verifier instances.
EnableChatHistoryInState = false;
EnableChatHistoryCompaction = false;
ChatHistoryMaxMessages = 0;
ChatHistorySummaryMaxChars = 0;
```

**设计原因**:
- **无状态**: Verifier 在跨调用之间应该是无状态的
- **避免跨轮次污染**: 避免跨轮次的状态污染，减少 token/state 增长
- **Quorum 共识**: Quorum 共识可能重用多个 verifier 实例，无状态设计更安全

### 错误处理

- 如果发生异常（非取消异常），返回错误消息 `"[verifier error] {ex.Message}\n\n"`
- 仍然创建 `AgentPromptRecord` 记录错误情况
- 确保事件和状态消息正确发布

---

## 📤 输出格式

### 输出要求

Verifier 的输出是**自由格式 Markdown 文本**（非 JSON），但应遵循以下结构化格式：

1. **Claim（声明）**
   - 明确列出要验证的声明
   - 引用 Reasoner 输出中的具体假设

2. **Check（检查方法）**
   - 描述使用的验证方法
   - 如果使用 Python 工具，展示计算过程

3. **Result（结果）**
   - 必须是以下之一：`VERIFIED` / `NOT VERIFIED` / `INCONCLUSIVE`
   - 明确说明验证结果

4. **Notes（备注）**
   - 提供额外的上下文信息
   - 说明验证的限制或注意事项

### 示例输出

```markdown
## Verification Results

### Claim 1: f preserves structure
**Check**: 
- Referenced [dag:fact-002] which states that f preserves structural properties
- Verified consistency with axioms from [dag:fact-001]

**Result**: VERIFIED

**Notes**: The claim is consistent with the provided axioms. A rigorous proof would require additional mathematical knowledge (theta series), but the claim is reasonable and does not contradict existing facts.

---

### Claim 2: P depends only on structure
**Check**:
- Examined the definition of P from [dag:fact-001]
- Verified that P's properties depend solely on structural characteristics

**Result**: VERIFIED

**Notes**: The claim aligns with the axiom definition. No contradictions found.

---

### Claim 3: Counterexample exists for property Q
**Check**:
- Used python_exec to search for counterexamples:
```python
# Search for counterexamples
for x in test_set:
    if not Q(x):
        print(f"Counterexample found: {x}")
        break
```
- Result: No counterexample found in test set

**Result**: INCONCLUSIVE

**Notes**: Limited test set. Cannot definitively prove or disprove. Would need exhaustive search or formal proof.
```

---

## 🔗 与前后 Agent 的交互

### 1. 接收 Reasoner 输出

**代码位置**: `VibeOrchestrator.Workers.cs` → `RunVerifierAsync` (第 401-402 行)

```csharp
var userMessage = BuildWorkerMessage("verifier", ctx.Question, dag, attachments: ctx.Input.AttachmentPaths,
    extra: string.IsNullOrWhiteSpace(reasonerOutput) ? null : $"Reasoner output (excerpt):\n{Bound(reasonerOutput!, 3500)}");
```

**如何使用 Reasoner 输出**:
- Verifier 读取 Reasoner 的推理链和假设
- 识别需要验证的关键声明
- 使用具体的检查方法验证这些声明

### 2. 为 Dag Builder 提供输入

**代码位置**: `VibeOrchestrator.GoalsAndMessages.cs` → `BuildDagBuilderMessage`

**Dag Builder 的 User Prompt 包含**:
```text
Worker outputs (excerpts):
[planner]
{plannerOutput (最多 2200 字符)}

[reasoner]
{reasonerOutput (最多 2200 字符)}

[verifier]
{verifierOutput (最多 3000 字符)}  // ← Verifier 输出的摘要（优先级最高）
```

**Dag Builder 如何使用**:
- **优先级最高**: Verifier 的输出被优先处理，字符限制更高（3,000 vs 2,200）
- **提取已验证知识**: Dag Builder 从 Verifier 输出中提取已验证的知识项（axioms、theorems、definitions）
- **提取验证方法**: Verifier 的验证方法作为 `proof` 字段的主要来源（优先级：verifier > reasoner > planner）
- **设置验证状态**: 根据 Verifier 的结果设置节点的验证状态标签

### 3. 验证状态传递

**代码位置**: `VibeOrchestrator.ExecuteOneRound.Parts.cs` (第 637-638 行)

```csharp
// Store verification pass/fail status for downstream use
outputs["verifier_passed"] = multiStageResult.OverallPass.ToString();
```

**用途**:
- 将验证通过/失败状态存储在 `outputs["verifier_passed"]` 中
- 供下游组件（如 Summary 阶段）使用，了解验证结果

---

## 🎯 关键设计决策

### 1. 为什么在 Reasoner 之后执行？

- **依赖关系**: Verifier 需要 Reasoner 的推理链和假设作为验证目标
- **验证目标**: Reasoner 明确列出了需要验证的假设，Verifier 基于这些假设进行验证
- **知识提取**: Verifier 的输出为 Dag Builder 提供已验证的知识项

### 2. 为什么输出是自由格式 Markdown？

- **灵活性**: 验证过程可能很复杂，自由格式更灵活
- **可读性**: Markdown 格式便于展示验证方法和结果
- **结构化程度**: Verifier 的输出是"验证结果"，不需要像 DAG mutation 那样严格结构化

### 3. Materials Context 的作用

- **知识基础**: 提供当前 DAG 中的知识节点，让 Verifier 检查声明是否与已有知识一致
- **引用支持**: DAG FACT INDEX 允许 Verifier 引用已有知识节点
- **一致性检查**: 帮助 Verifier 判断声明是否与公理/事实一致或矛盾

### 4. Python 工具的作用

- **计算验证**: 对于需要数值/符号计算的问题，Python 工具可以提供验证
- **反例搜索**: 可以通过计算搜索反例
- **假设检验**: 可以通过计算检验假设是否成立
- **安全性**: 默认禁用，需要通过配置显式启用

### 5. 无状态设计

- **跨轮次隔离**: 避免跨轮次的状态污染
- **Quorum 共识**: Quorum 共识可能重用多个 verifier 实例，无状态设计更安全
- **Token 效率**: 减少 token/state 增长

### 6. Multi-Stage Verification 的设计

- **Scout 阶段**: 快速检测反例和缺失前提（2 workers）
- **Prover 阶段**: 验证推理过程正确性（5 workers，需 ≥3 通过）
- **当前状态**: 目前是 fallback 到单次验证，完整实现待开发

### 7. 输出长度限制

- **20,000 字符**: 与 Planner 相同，因为验证结果应该简洁
- **摘要传递**: 传递给 Dag Builder 时，会进一步截断（3,000 字符，但优先级最高）

---

## 📊 输出示例分析

### 场景：数学证明研究

**输入**:
- Question: "Prove that property P holds for all elements in set S"
- Reasoner Output: "H1: f preserves structure. H2: P depends only on structure."
- Materials Context: [dag:fact-001] (base case), [dag:fact-002] (structure theorem)

**Verifier 输出**:
```markdown
## Verification Results

### Claim 1: f preserves structure (H1)
**Check**: 
- Referenced [dag:fact-002] which explicitly states that f preserves structural properties
- Verified consistency with axioms from [dag:fact-001]

**Result**: VERIFIED

**Notes**: The claim is consistent with the provided axioms. A rigorous proof would require additional mathematical knowledge, but the claim is reasonable and does not contradict existing facts.

---

### Claim 2: P depends only on structure (H2)
**Check**:
- Examined the definition of P from [dag:fact-001]
- Verified that P's properties depend solely on structural characteristics
- Used python_exec to verify with sample elements:
```python
# Verify P depends only on structure
for x in sample_set:
    structure = extract_structure(x)
    assert P(x) == P_from_structure(structure)
```
Result: All samples passed

**Result**: VERIFIED

**Notes**: Verified both axiomatically and computationally. The claim is well-supported.
```

**后续流程**:
1. **Dag Builder** 读取此输出，提取已验证的知识项
2. 使用 Verifier 的验证方法作为 `proof` 字段
3. 创建 Knowledge 节点，设置验证状态标签

---

## 🔍 调试和监控

### 日志位置

- **Prompt 记录**: `AgentPromptRecord` 保存在 `promptRecords["verifier"]` 中
- **输出内容**: 保存在 `outputs["verifier"]` 中，后续会被传递给 dag_builder
- **验证状态**: 保存在 `outputs["verifier_passed"]` 中（Multi-Stage 模式）
- **事件流**: `StepStartedEvent` 和 `StepFinishedEvent` 通过 `session.Events` 发布

### 常见问题

1. **输出为空或过短**
   - 检查 Reasoner 输出是否正确传递
   - 检查 Materials Context 是否正确注入
   - 检查 Plan Context 是否包含有效的 Plan 节点

2. **验证结果不明确**
   - 确认 Verifier 是否正确理解了 Reasoner 的假设
   - 检查 Materials Context 是否包含足够的上下文信息
   - 确认 Verifier 是否正确使用了 Python 工具（如果启用）

3. **Python 工具未执行**
   - 检查配置中 `Python.Enabled` 是否为 `true`
   - 检查 Python 工具的超时和输出限制设置

4. **后续 Agent 无法使用 Verifier 输出**
   - 检查输出是否被正确截断（20,000 字符限制）
   - 检查 Dag Builder 的 User Prompt 是否包含 Verifier 输出摘要

5. **Multi-Stage Verification 未生效**
   - 检查配置中 `Vibe:MultiStageVerification:Enabled` 是否为 `true`
   - 注意：当前实现是 fallback 到单次验证，完整实现待开发

---

## 📚 相关文档

- [REASONER_WORKFLOW.md](./REASONER_WORKFLOW.md) - Reasoner 的工作流程
- [DAG_BUILDER_WORKFLOW.md](./DAG_BUILDER_WORKFLOW.md) - Dag Builder 如何从 worker outputs 提取知识
- [AGENT_PROMPTS.md](./AGENT_PROMPTS.md) - 所有 Agent 的提示词详细说明
- [RESEARCH_ROUND_AND_DAG_FLOW.md](./RESEARCH_ROUND_AND_DAG_FLOW.md) - 完整的研究轮次和 DAG 流程
- [DAG_CONSENSUS_WORKFLOW.md](./DAG_CONSENSUS_WORKFLOW.md) - DAG Consensus 中的 Verifier Quorum 模式

---

*最后更新: 2026-01-29 | Aevatar VibeResearching System*