# Scout 阶段工作流程（Multi-Stage Verification）

## 📋 概述

`Scout` 是 Multi-Stage Verification 的第一阶段，负责快速检测反例和缺失前提。它是 Multi-Stage Verification 的"快速筛选"阶段，在更深入的 Prover 阶段之前识别明显的问题。

**当前状态**: ⚠️ **未完全实现** - 当前 Multi-Stage Verification 是 fallback 到单次验证

**设计意图**: Scout 阶段（2 workers）快速检测反例和缺失前提，Prover 阶段（5 workers，需 ≥3 通过）验证推理过程正确性

---

## 🔄 在 Multi-Stage Verification 中的位置

```
Multi-Stage Verification
    │
    ├─> Scout Phase（第一阶段）⭐
    │   ├─> Worker 1: 快速检测反例
    │   └─> Worker 2: 快速检测缺失前提
    │   └─> 汇总结果 → 决定是否进入 Prover 阶段
    │
    └─> Prover Phase（第二阶段）
        ├─> Worker 1-5: 并行验证推理过程正确性
        └─> 需要 ≥3 个通过 → 整体通过
```

---

## 🎯 核心职责

1. **快速反例检测**: 快速识别可能存在的反例，证明假设不成立
2. **缺失前提识别**: 识别推理过程中缺失的前提条件或假设
3. **快速筛选**: 在进入更深入的 Prover 阶段之前，快速筛选出明显有问题的推理
4. **效率优化**: 通过并行执行（2 workers）提高检测效率

---

## 📥 输入

### 1. System Prompt（设计意图）

**基于**: `VibeVerifierAgent.cs` 的 System Prompt，但针对 Scout 阶段进行优化

**预期 System Prompt**:
```text
You are a verification scout in a multi-stage verification process.

Goal:
- Quickly detect counterexamples and missing premises in the reasoning chain.
- Focus on fast, high-confidence detection rather than deep proof.

Role:
- You are the FIRST stage of verification (Scout phase).
- Your job is to quickly identify obvious problems before deeper verification.

Rules:
- Look for COUNTEREXAMPLES: concrete cases where the claim fails.
- Look for MISSING PREMISES: assumptions or facts that are used but not stated.
- If python_exec is available, use it for quick counterexample searches.
- Keep output VERY SHORT and focused:
  - Counterexamples found (if any)
  - Missing premises identified (if any)
  - Quick assessment: PROCEED / BLOCK
- If you find clear counterexamples or critical missing premises, recommend BLOCK.
- If no obvious issues, recommend PROCEED to Prover phase.

Output format:
- Counterexamples: [list any found]
- Missing Premises: [list any identified]
- Recommendation: PROCEED / BLOCK
- Brief Reason: [one sentence]
```

### 2. System Prompt（最终发送给 LLM）

Materials Context 会通过 `req.Context["materials_context"]` 自动追加到 System Prompt 末尾：

```text
You are a verification scout in a multi-stage verification process.

Goal:
- Quickly detect counterexamples and missing premises in the reasoning chain.
- Focus on fast, high-confidence detection rather than deep proof.

Role:
- You are the FIRST stage of verification (Scout phase).
- Your job is to quickly identify obvious problems before deeper verification.

Rules:
- Look for COUNTEREXAMPLES: concrete cases where the claim fails.
- Look for MISSING PREMISES: assumptions or facts that are used but not stated.
- If python_exec is available, use it for quick counterexample searches.
- Keep output VERY SHORT and focused:
  - Counterexamples found (if any)
  - Missing premises identified (if any)
  - Quick assessment: PROCEED / BLOCK
- If you find clear counterexamples or critical missing premises, recommend BLOCK.
- If no obvious issues, recommend PROCEED to Prover phase.

Output format:
- Counterexamples: [list any found]
- Missing Premises: [list any identified]
- Recommendation: PROCEED / BLOCK
- Brief Reason: [one sentence]

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

### 3. User Prompt（设计意图）

**基于**: `BuildWorkerMessage` 的格式，但针对 Scout 阶段进行优化

**预期 User Prompt 格式**:
```text
Role: verification_scout
Phase: Scout (Quick Counterexample and Missing Premise Detection)

Question: {ctx.Question}

Reasoner output (excerpt):
{reasonerOutput (最多 3500 字符)}

Plan:
{BuildPlanContextFromDag(dag)}
# 示例输出：
# - milestone-1: Expected output for milestone 1
# - milestone-2: Expected output for milestone 2
# - round-plan-abc123: Current round plan

DAG stats:
- nodes={dag.Nodes.Count}, edges={dag.Edges.Count}

Task:
- Quickly scan the reasoning chain for obvious counterexamples.
- Identify any missing premises or unstated assumptions.
- Provide a quick assessment: PROCEED to Prover phase or BLOCK due to obvious issues.
```

---

## 🔧 执行流程（设计意图）

### 代码位置（计划）

**文件位置**: `VibeOrchestrator.Workers.cs` → `RunMultiStageVerifierAsync` (待实现)

### 预期执行步骤

1. **触发事件**: 发布 `StepStartedEvent`，步骤名称为 `"vibe.verifier.scout"`
2. **状态报告**: 发送状态消息 `"🔍 Scout 阶段：快速检测反例和缺失前提 (2 workers)..."`
3. **并行执行**: 启动 2 个 Scout workers 并行执行
   - **Worker 1**: 专注于反例检测
   - **Worker 2**: 专注于缺失前提识别
4. **构建提示词**:
   - 为每个 worker 构建专门的 User Prompt（基于角色：counterexample_scout 或 premise_scout）
   - 使用 Scout 专用的 System Prompt
   - 包含 Materials Context
5. **并行调用**: 同时调用 2 个 verifier agent 实例
6. **收集结果**: 收集 2 个 workers 的输出
7. **汇总分析**:
   - 如果任一 worker 推荐 BLOCK，整体推荐 BLOCK
   - 如果两个 workers 都推荐 PROCEED，进入 Prover 阶段
8. **返回结果**: 返回 Scout 阶段的汇总结果

### Worker 1: 反例检测 Scout

**预期 System Prompt 附加**:
```text
Focus: Counterexample Detection

Your specific task:
- Search for concrete examples where the claim fails.
- Use python_exec (if available) to test edge cases.
- Look for boundary conditions, special cases, or exceptions.
- If you find ANY counterexample, recommend BLOCK.
```

**预期 User Prompt 附加**:
```text
Focus: Counterexample Detection

Scan the reasoning chain for counterexamples:
- Test edge cases and boundary conditions
- Look for special cases where the claim might fail
- Use computational checks if applicable
```

### Worker 2: 缺失前提检测 Scout

**预期 System Prompt 附加**:
```text
Focus: Missing Premise Detection

Your specific task:
- Identify assumptions or facts that are used but not explicitly stated.
- Check if all dependencies are properly cited.
- Look for logical gaps in the reasoning chain.
- If you find CRITICAL missing premises, recommend BLOCK.
```

**预期 User Prompt 附加**:
```text
Focus: Missing Premise Detection

Scan the reasoning chain for missing premises:
- Identify unstated assumptions
- Check if all dependencies are properly cited
- Look for logical gaps
```

---

## 📤 输出格式（设计意图）

### Scout Worker 输出格式

每个 Scout worker 应该输出简洁的结构化文本：

```markdown
## Scout Report

### Counterexamples Found:
- [如果找到反例，列出]
- [如果没有，写 "None found"]

### Missing Premises Identified:
- [如果找到缺失前提，列出]
- [如果没有，写 "None found"]

### Recommendation: PROCEED / BLOCK

### Brief Reason:
[一句话说明原因]
```

### Scout 阶段汇总输出

```markdown
## Scout Phase Summary

### Worker 1 (Counterexample Scout):
- Recommendation: PROCEED / BLOCK
- Counterexamples: [list]

### Worker 2 (Missing Premise Scout):
- Recommendation: PROCEED / BLOCK
- Missing Premises: [list]

### Overall Scout Decision: PROCEED / BLOCK

### Reason:
[汇总两个 workers 的结果]
```

---

## 🔗 与 Prover 阶段的交互

### 决策流程

```
Scout Phase
    │
    ├─> 任一 Worker 推荐 BLOCK
    │   └─> 整体 BLOCK → 不进入 Prover 阶段
    │
    └─> 两个 Workers 都推荐 PROCEED
        └─> 进入 Prover 阶段（5 workers）
            └─> 需要 ≥3 个通过 → 整体通过
```

### 传递给 Prover 阶段的信息

如果 Scout 阶段推荐 PROCEED，Prover 阶段会接收：
- Reasoner 的完整输出
- Scout 阶段的汇总结果（作为上下文）
- Materials Context
- DAG 状态

---

## 🎯 关键设计决策

### 1. 为什么需要 Scout 阶段？

- **效率优化**: 快速识别明显问题，避免浪费资源在深度验证上
- **早期拦截**: 在进入更耗时的 Prover 阶段之前拦截明显错误的推理
- **并行检测**: 2 个 workers 并行执行，提高检测效率

### 2. 为什么 2 个 Workers？

- **分工明确**: 一个专注于反例，一个专注于缺失前提
- **并行效率**: 2 个 workers 可以并行执行，不会显著增加延迟
- **容错性**: 即使一个 worker 失败，另一个仍可提供结果

### 3. 为什么输出要简洁？

- **快速决策**: Scout 阶段的目标是快速决策，不需要详细分析
- **效率优先**: 详细分析留给 Prover 阶段
- **减少 Token**: 简洁输出减少 LLM 调用成本

### 4. 为什么使用 PROCEED / BLOCK？

- **明确决策**: 二元决策更清晰，避免模糊状态
- **流程控制**: 明确控制是否进入 Prover 阶段
- **简单高效**: 简单的决策机制更容易实现和维护

---

## 📊 示例场景

### 场景 1: 发现反例

**输入**:
- Reasoner Output: "H1: All even numbers are divisible by 2. Therefore, 4 is divisible by 2."

**Scout Worker 1 (Counterexample Scout) 输出**:
```markdown
## Scout Report

### Counterexamples Found:
- None found (claim is correct)

### Missing Premises Identified:
- None

### Recommendation: PROCEED

### Brief Reason:
No counterexamples found in quick scan.
```

**Scout Worker 2 (Missing Premise Scout) 输出**:
```markdown
## Scout Report

### Counterexamples Found:
- None

### Missing Premises Identified:
- None (all premises are stated)

### Recommendation: PROCEED

### Brief Reason:
All premises are properly stated.
```

**Scout 阶段汇总**: PROCEED → 进入 Prover 阶段

---

### 场景 2: 发现缺失前提

**输入**:
- Reasoner Output: "H1: f preserves structure. Therefore, P(f(x)) holds."

**Scout Worker 1 (Counterexample Scout) 输出**:
```markdown
## Scout Report

### Counterexamples Found:
- None found

### Missing Premises Identified:
- None

### Recommendation: PROCEED

### Brief Reason:
No counterexamples found.
```

**Scout Worker 2 (Missing Premise Scout) 输出**:
```markdown
## Scout Report

### Counterexamples Found:
- None

### Missing Premises Identified:
- Missing premise: "P depends only on structure" is used but not stated
- Missing premise: "P(x) holds" is assumed but not verified

### Recommendation: BLOCK

### Brief Reason:
Critical missing premises identified that are essential for the conclusion.
```

**Scout 阶段汇总**: BLOCK → 不进入 Prover 阶段，直接返回失败

---

### 场景 3: 发现反例

**输入**:
- Reasoner Output: "H1: All prime numbers are odd."

**Scout Worker 1 (Counterexample Scout) 输出**:
```markdown
## Scout Report

### Counterexamples Found:
- Counterexample: 2 is a prime number but is even (not odd)

### Missing Premises Identified:
- None

### Recommendation: BLOCK

### Brief Reason:
Clear counterexample found: 2 is prime but even.
```

**Scout Worker 2 (Missing Premise Scout) 输出**:
```markdown
## Scout Report

### Counterexamples Found:
- None

### Missing Premises Identified:
- None

### Recommendation: PROCEED

### Brief Reason:
No missing premises identified.
```

**Scout 阶段汇总**: BLOCK（因为 Worker 1 推荐 BLOCK）→ 不进入 Prover 阶段，直接返回失败

---

## 🔍 实现状态

### 当前实现

**代码位置**: `VibeOrchestrator.Workers.cs` → `RunMultiStageVerifierAsync` (第 488-510 行)

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
- Fallback 到单次验证（`RunVerifierAsync`）
- 使用简单的启发式方法检查验证结果
- 不执行真正的 Scout + Prover 两阶段验证

### 计划实现（TODO）

1. **实现 Scout 阶段**:
   - 创建 `RunScoutPhaseAsync` 方法
   - 实现 2 个 workers 的并行执行
   - 实现 Scout 专用的 System Prompt 和 User Prompt
   - 实现结果汇总逻辑

2. **实现 Prover 阶段**:
   - 创建 `RunProverPhaseAsync` 方法
   - 实现 5 个 workers 的并行执行
   - 实现 Prover 专用的 System Prompt 和 User Prompt
   - 实现 ≥3 通过的判断逻辑

3. **集成两个阶段**:
   - 在 `RunMultiStageVerifierAsync` 中调用 Scout 阶段
   - 根据 Scout 结果决定是否进入 Prover 阶段
   - 返回完整的 Multi-Stage Verification 结果

---

## 📚 相关文档

- [VERIFIER_WORKFLOW.md](./VERIFIER_WORKFLOW.md) - Verifier 的完整工作流程
- [DAG_CONSENSUS_WORKFLOW.md](./DAG_CONSENSUS_WORKFLOW.md) - DAG Consensus 中的 Verifier Quorum 模式
- [AGENT_PROMPTS.md](./AGENT_PROMPTS.md) - 所有 Agent 的提示词详细说明

---

## 💡 设计参考

### 类似模式

1. **DAG Consensus Verifier Quorum**:
   - 使用多个 verifier 并行验证
   - 每个 verifier 有不同的 focus（structure, grounding, safety）
   - 需要 quorum 才能通过

2. **MAKER Workflow**:
   - 使用多个 workers 并行生成提案
   - 投票机制决定最终结果
   - Red-Flag 检查过滤明显错误的提案

### Scout 阶段的独特之处

- **快速筛选**: 专注于快速识别明显问题
- **早期拦截**: 在深度验证之前拦截错误
- **效率优先**: 简洁输出，快速决策

---

*最后更新: 2026-01-29 | Aevatar VibeResearching System*

**注意**: 本文档描述的是 Scout 阶段的**设计意图**和**预期实现**。当前代码中 Scout 阶段尚未完全实现，实际实现可能与本文档描述有所不同。