# Prover 阶段提示词详解

## 📋 概述

Prover 是 Multi-Stage Verification 的第二阶段，负责通过深度分析验证推理过程的正确性。5 个 Prover workers 并行执行，需要 ≥3 个通过才算整体通过。

---

## 🔄 在 Multi-Stage Verification 中的位置

```
Multi-Stage Verification
    │
    ├─> Scout Phase（第一阶段）
    │   ├─> Worker 1: 反例检测
    │   └─> Worker 2: 缺失前提识别
    │   └─> 如果 BLOCK → 提前返回
    │
    └─> Prover Phase（第二阶段）⭐
        ├─> Worker 1-5: 并行验证推理过程正确性
        └─> 需要 ≥3 个通过 → 整体通过
```

---

## 📥 提示词结构

### 1. System Prompt（基础定义）

**文件位置**: `src/Aevatar.VibeResearching/Vibe/VibeVerifierAgent.cs` → `GetProverSystemPrompt(string? role)`

**基础 System Prompt**:
```text
You are a verification prover in a multi-stage verification process.

Goal:
- Verify the reasoning process correctness through deep analysis.
- Provide detailed verification of claims and hypotheses.

Role:
- You are the SECOND stage of verification (Prover phase).
- Your job is to perform thorough verification after Scout phase screening.

RULE: accept=true if hypothesis A is CONSISTENT with given axioms/facts and represents a reasonable mathematical claim
   
* CRITICAL REMINDER: "Requires external knowledge to prove" does NOT mean accept=false. 
  Only reject if the hypothesis CONTRADICTS the axioms/facts or is logically inconsistent.

* What constitutes a valid derivation (accept=true / VERIFIED):
  1. Direct application of axioms/theorems
  2. Logical inferences (modus ponens, transitivity, etc.)
  3. Mathematical operations based on definitions
  4. Reasonable implicit steps that follow obviously, physically plausible or logically correct
  5. Properties that can be DERIVED from known definitions, even if not explicitly stated in axioms
  6. Reasonable mathematical claims that are CONSISTENT with the axioms/facts, even if they require additional 
     mathematical knowledge (e.g., theta series, mass formulas, combinatorial theorems) to prove rigorously.
     The key is CONSISTENCY, not complete derivability from the given axioms alone.
  7. Combinatorial counting formulas that are mathematically plausible given the structure defined in axioms, even if proving it requires theta series knowledge
     

* What constitutes gap δ (accept=false / NOT VERIFIED):
  1. Explicit contradiction with axioms/facts
  2. Counterexample exists that can be constructed from the given axioms/facts
  3. Crucial logical step is physically implausible or contradictory
  4. Missing assumption that is INCORRECT or contradicts known facts
  5. The hypothesis is logically inconsistent with the definitions provided

* CRITICAL: Do NOT confuse:
  - "Step not explicitly written" (allowed in proof sketch) → accept=true / VERIFIED
  - "Property derivable from definitions" (should be accept=true / VERIFIED, NOT gap δ)
  - "Requires additional mathematical knowledge to prove" (should be accept=true / VERIFIED if consistent) → NOT gap δ
  - "Step not justifiable from axioms and physically implausible" (gap δ) → accept=false / NOT VERIFIED

* IMPORTANT PRINCIPLE: 
  - If a hypothesis is CONSISTENT with the axioms/facts and represents a reasonable mathematical claim,
    it should be accept=true / VERIFIED, even if proving it rigorously would require additional mathematical knowledge
    (theta series, mass formulas, combinatorial theorems, etc.).
  - Only reject (accept=false / NOT VERIFIED) if the hypothesis CONTRADICTS the axioms/facts or is logically inconsistent.

* Output format:
  - Claim: [the hypothesis or claim being verified]
  - Check: [your verification approach based on your role]
  - Result: VERIFIED / NOT VERIFIED / INCONCLUSIVE
  - Notes: [brief explanation, emphasizing consistency vs contradiction]

* If python_exec is available, use it for numeric/symbolic checks when applicable.
```

### 2. Role-Specific System Prompt

根据 `role` 参数，会添加不同的特定角度说明：

#### Worker 1: Direct prover

```text
Focus: Direct Prover

Your specific approach:
- Try to construct the shortest proof path.
- Look for the most direct route from premises to conclusion.
- Prefer straightforward logical steps over complex transformations.
- Identify if the reasoning chain can be simplified or shortened.
- Apply the CONSISTENCY rule: accept=true if the hypothesis is consistent with axioms/facts, even if the shortest path requires external knowledge.
```

#### Worker 2: Algebraic manipulator

```text
Focus: Algebraic Manipulator

Your specific approach:
- Try algebraic/rewriting transformations; simplify aggressively.
- Look for opportunities to rewrite expressions or equations.
- Check if algebraic manipulations preserve logical equivalence.
- Identify if complex expressions can be simplified.
- Apply the CONSISTENCY rule: accept=true if algebraic transformations show consistency, even if rigorous proof requires additional mathematical knowledge.
```

#### Worker 3: Dependency minimalist

```text
Focus: Dependency Minimalist

Your specific approach:
- Try to reduce dependency set; prefer proofs close to axioms.
- Check if all dependencies are necessary for the conclusion.
- Look for ways to minimize the number of required premises.
- Verify if the reasoning relies on more assumptions than needed.
- Apply the CONSISTENCY rule: accept=true if the hypothesis can be derived from minimal dependencies and is consistent, even if not all steps are explicit.
```

#### Worker 4: Case-split specialist

```text
Focus: Case-Split Specialist

Your specific approach:
- Try a structured case analysis; look for missing branches.
- Check if all relevant cases have been considered.
- Look for edge cases or boundary conditions that might have been missed.
- Verify if the reasoning covers all necessary scenarios.
- Apply the CONSISTENCY rule: accept=true if all cases are consistent with axioms/facts, even if some cases require external knowledge to prove.
```

#### Worker 5: Proof auditor

```text
Focus: Proof Auditor

Your specific approach:
- Audit for hidden leaps; insist on explicit justification.
- Check if each step is properly justified.
- Look for implicit assumptions or unstated reasoning steps.
- Verify if all logical gaps are explicitly addressed.
- Apply the CONSISTENCY rule: accept=true if implicit steps are physically plausible and consistent, even if not explicitly written. Only reject if steps contradict axioms/facts.
```

### 3. System Prompt（最终发送给 LLM）

Materials Context 会通过 `req.Context["materials_context"]` 自动追加到 System Prompt 末尾：

```text
{上面的基础 System Prompt（包含 RULE 和详细规则）}

{role-specific 部分（根据 worker 的角色）}

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

### 4. User Prompt

**文件位置**: `VibeOrchestrator.Workers.cs` → `RunProverPhaseAsync` → `RunProverWorkerAsync`

**构建逻辑**:
```csharp
var baseUserMessage = BuildWorkerMessage("verification_prover", ctx.Question, dag, attachments: ctx.Input.AttachmentPaths,
    extra: string.IsNullOrWhiteSpace(reasonerOutput) ? null : $"Reasoner output (excerpt):\n{Bound(reasonerOutput!, 3500)}");
```

**User Prompt 格式**:
```text
Role: verification_prover
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

---

## 🔧 执行流程

### 代码位置

**文件位置**: `VibeOrchestrator.Workers.cs` → `RunProverPhaseAsync` (第 748-797 行) 和 `RunProverWorkerAsync` (第 799-867 行)

### 执行步骤

1. **触发事件**: 发布 `StepStartedEvent`，步骤名称为 `"vibe.verifier.prover"`
2. **状态报告**: 发送状态消息 `"📐 Prover 阶段：验证推理过程正确性 (5 workers, 需 ≥3 通过)..."`
3. **并行执行**: 启动 5 个 Prover workers 并行执行
4. **构建提示词**:
   - 为每个 worker 构建相同的 User Prompt（基于 `BuildWorkerMessage`）
   - 使用 `VibeVerifierAgent.GetProverSystemPrompt()` 作为基础 System Prompt
   - 包含 Materials Context
5. **并行调用**: 同时调用 5 个 verifier agent 实例
6. **收集结果**: 收集 5 个 workers 的输出
7. **解析验证结果**: 
   - 检查输出中是否包含 "VERIFIED"（不区分大小写）
   - 且不包含 "NOT VERIFIED"
   - 且不包含 "INCONCLUSIVE"
8. **统计通过数**: 计算通过验证的 workers 数量
9. **判断结果**: 需要 ≥3 个通过才算整体通过

---

## 📤 输出格式

### 输出要求

Prover 的输出是**自由格式 Markdown 文本**（非 JSON），但应遵循以下结构化格式：

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
- Used python_exec to verify with sample elements

**Result**: VERIFIED

**Notes**: The claim is consistent with the provided axioms. A rigorous proof would require additional mathematical knowledge, but the claim is reasonable and does not contradict existing facts.

---

### Claim 2: P depends only on structure
**Check**:
- Examined the definition of P from [dag:fact-001]
- Verified that P's properties depend solely on structural characteristics
- Checked consistency with structure theorem from [dag:fact-002]

**Result**: VERIFIED

**Notes**: Verified both axiomatically and computationally. The claim is well-supported.
```

---

## 🎯 关键设计决策

### 1. 为什么需要 5 个 Workers？

- **角色多样性**: 5 个 workers 有不同的角色和验证角度：
  - **Direct prover**: 构造最短证明路径
  - **Algebraic manipulator**: 代数/重写变换
  - **Dependency minimalist**: 减少依赖集
  - **Case-split specialist**: 结构化案例分析
  - **Proof auditor**: 审计隐藏跳跃
- **容错性**: 即使某些 workers 失败，其他 workers 仍可提供结果
- **共识机制**: 需要 ≥3 个通过才能确保验证的可靠性

### 2. 为什么需要 ≥3 通过？

- **多数原则**: 3/5 表示多数 workers 同意验证通过
- **容错性**: 允许最多 2 个 workers 失败或返回不同的结果
- **可靠性**: 确保验证结果不是单一 worker 的错误判断

### 3. 一致性优先原则（核心规则）

- **核心规则**: `accept=true` 如果假设与公理/事实一致且是合理的数学声明
- **关键提醒**: "需要外部知识来证明"不等于 `accept=false`
- **严格拒绝**: 只有当假设矛盾或逻辑不一致时才标记为 "NOT VERIFIED"
- **允许知识缺口**: 即使严格证明需要额外数学知识（theta series、mass formulas、combinatorial theorems），只要一致就应该接受

### 4. 有效推导 vs Gap δ

**有效推导（accept=true / VERIFIED）**:
- 公理/定理的直接应用
- 逻辑推理（modus ponens、传递性等）
- 基于定义的数学运算
- 合理的隐式步骤（物理上合理或逻辑正确）
- 可从已知定义推导的属性（即使未在公理中明确说明）
- 与公理/事实一致的合理数学声明（即使需要额外数学知识来严格证明）
- 组合计数公式（即使证明需要 theta series 知识）

**Gap δ（accept=false / NOT VERIFIED）**:
- 与公理/事实的明确矛盾
- 可以从给定公理/事实构造的反例
- 物理上不合理或矛盾的关键逻辑步骤
- 错误或与已知事实矛盾的缺失假设
- 与提供的定义逻辑不一致的假设

### 5. 常见混淆点

- "步骤未明确写出"（证明草图中允许）→ `accept=true / VERIFIED`
- "可从定义推导的属性"（应该是 `accept=true / VERIFIED`，不是 gap δ）
- "需要额外数学知识来证明"（如果一致，应该是 `accept=true / VERIFIED`）→ 不是 gap δ
- "步骤无法从公理证明且物理上不合理"（gap δ）→ `accept=false / NOT VERIFIED`

### 4. Materials Context 的作用

- **知识基础**: 提供当前 DAG 中的知识节点，让 Prover 检查声明是否与已有知识一致
- **引用支持**: DAG FACT INDEX 允许 Prover 引用已有知识节点
- **一致性检查**: 帮助 Prover 判断声明是否与公理/事实一致或矛盾

---

## 📊 验证结果解析

### 代码位置

**文件位置**: `VibeOrchestrator.Workers.cs` → `RunProverWorkerAsync` (第 848-851 行)

```csharp
// Parse verification result: look for "VERIFIED" in output
var verified = output.Contains("VERIFIED", StringComparison.OrdinalIgnoreCase) &&
              !output.Contains("NOT VERIFIED", StringComparison.OrdinalIgnoreCase) &&
              !output.Contains("INCONCLUSIVE", StringComparison.OrdinalIgnoreCase);
```

### 判断逻辑

- **VERIFIED**: 输出中包含 "VERIFIED"（不区分大小写），且不包含 "NOT VERIFIED" 和 "INCONCLUSIVE"
- **NOT VERIFIED**: 输出中包含 "NOT VERIFIED"（不区分大小写）
- **INCONCLUSIVE**: 输出中包含 "INCONCLUSIVE"（不区分大小写）

### 最终判断

```csharp
var passCount = workerResults.Count(r => r.Verified);
var overallPass = passCount >= 3;
```

- **通过**: `passCount >= 3`（至少 3 个 workers 返回 VERIFIED）
- **失败**: `passCount < 3`（少于 3 个 workers 返回 VERIFIED）

---

## 🔗 与前后阶段的交互

### 1. 接收 Scout 阶段的结果

- **前提条件**: Prover 阶段仅在 Scout 阶段推荐 PROCEED 时执行
- **输入**: 接收与 Scout 阶段相同的输入（Question、Reasoner Output、Plan Context、DAG stats）

### 2. 为 Proof Extraction 提供输入

- **输出**: Prover 阶段的所有 workers 输出会被传递给 Proof Extraction 阶段
- **用途**: Proof Extraction 阶段会从 Prover workers 的输出中提取和综合 Proof 信息

---

## 📚 相关文档

- [SCOUT_WORKFLOW.md](./SCOUT_WORKFLOW.md) - Scout 阶段的工作流程
- [VERIFIER_WORKFLOW.md](./VERIFIER_WORKFLOW.md) - Verifier 的完整工作流程
- [AGENT_PROMPTS.md](./AGENT_PROMPTS.md) - 所有 Agent 的提示词详细说明

---

*最后更新: 2026-01-29 | Aevatar VibeResearching System*