# Scout 阶段提示词详解

## 📋 概述

Scout 是 Multi-Stage Verification 的第一阶段，负责快速检测反例和缺失前提。2 个 Scout workers 并行执行，任一 worker 推荐 BLOCK 则整体 BLOCK。

---

## 🔄 在 Multi-Stage Verification 中的位置

```
Multi-Stage Verification
    │
    ├─> Scout Phase（第一阶段）⭐
    │   ├─> Worker 1: 反例检测（counterexample）
    │   └─> Worker 2: 缺失前提识别（premise）
    │   └─> 如果任一 BLOCK → 提前返回
    │
    └─> Prover Phase（第二阶段）
        └─> 仅在 Scout 通过时执行
```

---

## 📥 提示词结构

### 1. System Prompt（基础定义）

**文件位置**: `src/Aevatar.VibeResearching/Vibe/VibeVerifierAgent.cs` → `GetScoutSystemPrompt(string focus)`

**基础 System Prompt**:
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

### 2. Focus-Specific System Prompt

根据 `focus` 参数，会添加不同的特定任务说明：

#### Worker 1: Counterexample Scout (`focus = "counterexample"`)

```text
{基础 System Prompt}

Focus: Counterexample Detection

Your specific task:
- Search for concrete examples where the claim fails.
- Use python_exec (if available) to test edge cases.
- Look for boundary conditions, special cases, or exceptions.
- If you find ANY counterexample, recommend BLOCK.
```

#### Worker 2: Missing Premise Scout (`focus = "premise"`)

```text
{基础 System Prompt}

Focus: Missing Premise Detection

Your specific task:
- Identify assumptions or facts that are used but not explicitly stated.
- Check if all dependencies are properly cited.
- Look for logical gaps in the reasoning chain.
- If you find CRITICAL missing premises, recommend BLOCK.
```

### 3. System Prompt（最终发送给 LLM）

Materials Context 会通过 `req.Context["materials_context"]` 自动追加到 System Prompt 末尾：

```text
{上面的 System Prompt（包含 focus-specific 部分）}

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

**文件位置**: `VibeOrchestrator.Workers.cs` → `RunScoutWorkerAsync`

**构建逻辑**:
```csharp
// 1. 构建基础 User Message
var baseUserMessage = BuildWorkerMessage("verification_scout", ctx.Question, dag, attachments: ctx.Input.AttachmentPaths,
    extra: string.IsNullOrWhiteSpace(reasonerOutput) ? null : $"Reasoner output (excerpt):\n{Bound(reasonerOutput!, 3500)}");

// 2. 添加 focus-specific 任务说明
var userMessage = new StringBuilder(baseUserMessage);
userMessage.AppendLine();
userMessage.AppendLine($"Focus: {focus}");
userMessage.AppendLine();
if (focus == "counterexample")
{
    userMessage.AppendLine("Task:");
    userMessage.AppendLine("- Scan the reasoning chain for obvious counterexamples.");
    userMessage.AppendLine("- Test edge cases and boundary conditions.");
    userMessage.AppendLine("- Use computational checks if applicable.");
}
else // premise
{
    userMessage.AppendLine("Task:");
    userMessage.AppendLine("- Scan the reasoning chain for missing premises.");
    userMessage.AppendLine("- Identify unstated assumptions.");
    userMessage.AppendLine("- Check if all dependencies are properly cited.");
}
```

**User Prompt 格式**:

#### Worker 1 (Counterexample Scout):
```text
Role: verification_scout
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

Focus: counterexample

Task:
- Scan the reasoning chain for obvious counterexamples.
- Test edge cases and boundary conditions.
- Use computational checks if applicable.
```

#### Worker 2 (Missing Premise Scout):
```text
Role: verification_scout
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

Focus: premise

Task:
- Scan the reasoning chain for missing premises.
- Identify unstated assumptions.
- Check if all dependencies are properly cited.
```

---

## 📤 输出格式

### 输出要求

Scout 的输出是**自由格式文本**（非 JSON），但应遵循以下结构化格式：

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

### 示例输出

#### Worker 1 (Counterexample Scout) - 示例 1: 发现反例
```markdown
## Scout Report

### Counterexamples Found:
- Counterexample: 2 is a prime number but is even (not odd)
- This contradicts the claim "All prime numbers are odd"

### Missing Premises Identified:
- None found

### Recommendation: BLOCK

### Brief Reason:
Clear counterexample found: 2 is prime but even.
```

#### Worker 1 (Counterexample Scout) - 示例 2: 无反例
```markdown
## Scout Report

### Counterexamples Found:
- None found (claim appears consistent)

### Missing Premises Identified:
- None

### Recommendation: PROCEED

### Brief Reason:
No counterexamples found in quick scan.
```

#### Worker 2 (Missing Premise Scout) - 示例 1: 发现缺失前提
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

#### Worker 2 (Missing Premise Scout) - 示例 2: 无缺失前提
```markdown
## Scout Report

### Counterexamples Found:
- None

### Missing Premises Identified:
- None (all premises are properly stated)

### Recommendation: PROCEED

### Brief Reason:
All premises are properly stated.
```

---

## 🔧 执行流程

### 代码位置

**文件位置**: `VibeOrchestrator.Workers.cs` → `RunScoutPhaseAsync` (第 571-637 行) 和 `RunScoutWorkerAsync` (第 639-745 行)

### 执行步骤

1. **触发事件**: 发布 `StepStartedEvent`，步骤名称为 `"vibe.verifier.scout"`
2. **状态报告**: 发送状态消息 `"🔍 Scout 阶段：快速检测反例和缺失前提 (2 workers)..."`
3. **并行执行**: 启动 2 个 Scout workers 并行执行
   - **Worker 1**: `focus = "counterexample"`
   - **Worker 2**: `focus = "premise"`
4. **构建提示词**:
   - 为每个 worker 构建 focus-specific User Prompt
   - 使用 `VibeVerifierAgent.GetScoutSystemPrompt(focus)` 作为基础 System Prompt
   - 包含 Materials Context
5. **并行调用**: 同时调用 2 个 verifier agent 实例
6. **收集结果**: 收集 2 个 workers 的输出
7. **解析推荐**: 
   - 检查输出中是否包含 "PROCEED"（不区分大小写）
   - 且不包含 "BLOCK"
8. **汇总分析**: 
   - 如果任一 worker 推荐 BLOCK，整体推荐 BLOCK
   - 如果两个 workers 都推荐 PROCEED，整体推荐 PROCEED

---

## 🎯 关键设计决策

### 1. 为什么需要 2 个 Workers？

- **分工明确**: 一个专注于反例，一个专注于缺失前提
- **并行效率**: 2 个 workers 可以并行执行，不会显著增加延迟
- **容错性**: 即使一个 worker 失败，另一个仍可提供结果

### 2. 为什么输出要简洁？

- **快速决策**: Scout 阶段的目标是快速决策，不需要详细分析
- **效率优先**: 详细分析留给 Prover 阶段
- **减少 Token**: 简洁输出减少 LLM 调用成本

### 3. 为什么使用 PROCEED / BLOCK？

- **明确决策**: 二元决策更清晰，避免模糊状态
- **流程控制**: 明确控制是否进入 Prover 阶段
- **简单高效**: 简单的决策机制更容易实现和维护

### 4. 为什么任一 BLOCK 就整体 BLOCK？

- **保守策略**: 快速拦截明显问题，避免浪费资源
- **早期拦截**: 在进入更耗时的 Prover 阶段之前拦截错误
- **效率优化**: 快速识别问题，避免不必要的深度验证

---

## 📊 验证结果解析

### 代码位置

**文件位置**: `VibeOrchestrator.Workers.cs` → `RunScoutWorkerAsync` (第 690-694 行)

```csharp
// Parse recommendation: look for "PROCEED" or "BLOCK" in output
var recommendProceed = output.Contains("PROCEED", StringComparison.OrdinalIgnoreCase) &&
                      !output.Contains("BLOCK", StringComparison.OrdinalIgnoreCase);
```

### 判断逻辑

- **PROCEED**: 输出中包含 "PROCEED"（不区分大小写），且不包含 "BLOCK"
- **BLOCK**: 输出中包含 "BLOCK"（不区分大小写）

### 最终判断

```csharp
// Analyze results: if any worker recommends BLOCK, overall is BLOCK
var overallProceed = workerResults.All(r => r.RecommendProceed);
```

- **PROCEED**: 两个 workers 都推荐 PROCEED
- **BLOCK**: 任一 worker 推荐 BLOCK

---

## 🔗 与前后阶段的交互

### 1. 接收 Reasoner 输出

- **输入**: 接收 Reasoner 的输出摘要（最多 3,500 字符）
- **用途**: 基于 Reasoner 的推理链快速检测反例和缺失前提

### 2. 为 Prover 阶段提供筛选

- **输出**: Scout 阶段的汇总结果和建议
- **用途**: 如果 Scout 推荐 BLOCK，不进入 Prover 阶段；如果推荐 PROCEED，进入 Prover 阶段

---

## 📚 相关文档

- [PROVER_PROMPTS.md](./PROVER_PROMPTS.md) - Prover 阶段的提示词
- [VERIFIER_WORKFLOW.md](./VERIFIER_WORKFLOW.md) - Verifier 的完整工作流程
- [SCOUT_WORKFLOW.md](./SCOUT_WORKFLOW.md) - Scout 阶段的完整工作流程

---

*最后更新: 2026-01-29 | Aevatar VibeResearching System*