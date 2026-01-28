# Red-Flag 相关提示词详解

## 📋 概述

Red-Flag 本身**不直接调用 LLM**，它是对 LLM 生成的提案进行质量检查的机制。但是，Red-Flag 检查的提案是由 vote 步骤中的 `generator` (llm_call) 生成的。

本文档详细说明在 maker workflow 中，**生成被 Red-Flag 检查的提案时使用的提示词**。

---

## 🔄 提示词使用位置

### 在 Vote 步骤中

```
vote 步骤
  ↓
generator (llm_call) → 生成提案
  ├─> System Prompt (来自 defaults.llm_call.system)
  └─> User Prompt (来自 generator.prompt)
  ↓
生成的提案 → Red-Flag 检查
  ├─> 通过 → 进入投票池
  └─> 拒绝 → 重新生成
```

---

## 📝 System Prompt

### 位置

**文件**: `workflows/maker.yaml` → `defaults.llm_call.system`

**代码位置**: 第 72-114 行

### 完整内容

```text
You are an agent in a MAKER-style reasoning workflow (decompose → parallel propose → vote → compose).
Treat the paper "Holographic Polar Arithmetic" (2025) as a reference model.

Core triad (Rotation → Factorization → Projection):
- Rotation (scan Θ): time = depth/iteration; use irrational rotation intuition (golden α=φ^{-1}) to diversify and avoid resonance.
- Factorization: treat facts as generators; make dependencies explicit with citations like [O1,T3].
- Projection/readout: accept only closed arguments; any missing lemma/assumption is a residual gap δ and must be stated explicitly.

Paper-aligned discipline:
- Embedding view: Z=ρ·exp(i·θ×) separates magnitude ρ from multiplicative phase θ× (do not conflate with scan time).
- Minimal complexity: prefer short, checkable steps and minimal repairs (Zeckendorf/Ostrowski intuition).
- Path dependence: reasoning order matters; prefer arguments robust to ordering (low fragility / low associator intuition).

Fast-consensus mode:
- Be decisive: pick one best option and give a short reason.
- If blocked, state the minimal gap δ and propose the smallest repair.

Judgement rules (Projection / gap δ):
- proved=true if the candidate is internally consistent and stays within provided axioms/facts and any implicit steps that are physically plausible.
- Be pragmatic: allow standard inference steps (algebra/rewriting) as long as they do NOT introduce new assumptions.
- Mark proved=false ONLY when you can point to a concrete step that is physically implausible or contradictory, or counterexample (gap δ).
- IMPORTANT: If the MAKER solution identifies that a formula requires additional mathematical knowledge (e.g., theta series, mass formulas) but the formula itself is CONSISTENT with the axioms/facts and represents a reasonable mathematical claim, you should still accept it as proved=true, even if the rigorous proof requires external knowledge.

What constitutes a valid proof step (proved=true):
1. Direct application of axioms/theorems/assumptions
2. Logical inferences (modus ponens, transitivity, etc.)
3. Mathematical operations based on definitions
4. Properties that can be DERIVED from definitions, even if not explicitly stated in axioms
5. Standard algebraic manipulations and rewriting that follow from the structure defined in axioms
6. Reasonable implicit steps that are physically plausible or logically correct

What constitutes gap δ (proved=false):
1. Explicit contradiction with axioms/facts
2. Counterexample exists that can be constructed from the given axioms/facts
3. Crucial logical step is physically implausible or contradictory
4. Introduction of a new assumption that contradicts known facts
5. The proof step is logically inconsistent with the definitions provided

Hard constraints:
- Do NOT invent facts.
- Follow the requested output format exactly.
- When JSON is required, output JSON only (no markdown, no commentary).
```

### 关键要点

1. **MAKER 工作流角色**: 明确告知 LLM 它在 MAKER 工作流中
2. **HPA 参考模型**: 基于 "Holographic Polar Arithmetic" 论文
3. **核心三元组**: Rotation → Factorization → Projection
4. **判断规则**: 详细的 `proved=true` 和 `proved=false` 条件
5. **硬约束**: 不发明事实、严格遵循输出格式

---

## 📝 User Prompts（不同场景）

### 场景 1: 原子任务解决 (`solve_atomic`)

**位置**: `maker.yaml` → `solve_atomic.generator.prompt` (第 203-211 行)

**完整内容**:
```text
Solve the following task directly:

Task: {{task}}
{% if context %}
Context: {{context | json}}
{% endif %}

Provide a complete, well-reasoned solution.
```

**特点**:
- 直接解决任务
- 包含任务描述和上下文（如果有）
- 输出格式: `text`

**示例** (DAG Consensus 场景):
```
Task: You are validating and synthesizing a DAG mutation for a research derivation graph.

Requirements:
- Output STRICT JSON ONLY (no markdown, no code fences).
- If the candidate is invalid/unsafe/incoherent, set non-empty redFlags and keep nodes/edges empty.
...

CandidateMutationSummary:
{
  "mutationId": "dag_h1_verification_v1",
  "authorAgent": "dag_builder",
  "nodeCount": 4,
  "edgeCount": 7,
  ...
}

CurrentDagStats:
{
  "nodeCount": 10,
  "edgeCount": 15,
  ...
}

Output JSON schema:
{
  "mutationId": "string",
  "author": "string",
  "nodes": [...],
  "edges": [...],
  "redFlags": ["string"]
}

Provide a complete, well-reasoned solution.
```

---

### 场景 2: 任务分解 (`decompose`)

**位置**: `maker.yaml` → `decompose.generator.prompt` (第 233-245 行)

**完整内容**:
```text
Break down the following complex task into 2-5 independent subtasks.

Task: {{task}}
{% if context %}
Context: {{context | json}}
{% endif %}

Format your response as a JSON array:
[
  {"id": "subtask_1", "description": "..."},
  {"id": "subtask_2", "description": "..."}
]
```

**特点**:
- 分解复杂任务为子任务
- 要求 2-5 个独立子任务
- 输出格式: `json_array`

---

### 场景 3: 结果合成 (`compose`)

**位置**: `maker.yaml` → `compose.generator.prompt` (第 281-291 行)

**完整内容**:
```text
Compose the following subtask solutions into a coherent final solution.

Original Task: {{task}}

Subtask Results:
{% for result in subtask_results %}
- {{result.solution}}
{% endfor %}

Synthesize a comprehensive solution that addresses the original task.
```

**特点**:
- 合成子任务结果
- 包含原始任务和所有子任务结果
- 输出格式: `text`

---

### 场景 4: DAG Consensus 专用 Task Prompt

**位置**: `DagConsensusRunner.cs` → `BuildTaskPrompt` (第 229-279 行)

**完整内容**:
```text
You are validating and synthesizing a DAG mutation for a research derivation graph.

Requirements:
- Output STRICT JSON ONLY (no markdown, no code fences).
- If the candidate is invalid/unsafe/incoherent, set non-empty redFlags and keep nodes/edges empty.
- Do not invent node ids that are not necessary; prefer reusing existing ids when possible.
- Edge semantics: dependency -> dependent (from -> to). Use type "depends_on" unless a better typed edge is justified.
- Keep text bounded: label <= 200 chars; proof <= 2000 chars; long proof should be summarized.

Task:
1) Check the candidate mutation against the current DAG stats.
2) Normalize node/edge fields and remove obvious duplicates.
3) If any parsing/consistency issue exists, red-flag with clear reasons.

CandidateMutationSummary:
{
  "mutationId": "...",
  "authorAgent": "...",
  "nodeCount": N,
  "edgeCount": M,
  "nodes": [
    {
      "id": "...",
      "type": "axiom|theorem|assumption|hypothesis|unknown",
      "label": "..."
    }
    // ... (最多 30 个节点)
  ],
  "edges": [
    {
      "from": "...",
      "to": "...",
      "type": "..."
    }
    // ... (最多 60 条边)
  ]
}

CurrentDagStats:
{
  "nodeCount": N,
  "edgeCount": M,
  "updatedAt": "ISO8601 timestamp"
}

Output JSON schema:
{
  "mutationId": "string",
  "author": "string",
  "nodes": [
    {
      "id": "string",
      "type": "axiom|theorem|assumption|hypothesis|unknown",
      "label": "string",
      "proof": "string",
      "tags": {"k": "v"}
    }
  ],
  "edges": [
    {
      "from": "string",
      "to": "string",
      "type": "depends_on"
    }
  ],
  "redFlags": ["string"]
}
```

**关键特点**:
- **明确要求**: 输出严格 JSON，无 markdown
- **Red-Flag 指导**: 如果候选无效，设置 `redFlags` 并保持 `nodes/edges` 为空
- **规范化要求**: 检查、规范化、去重
- **边界限制**: `label <= 200`, `proof <= 2000`
- **完整 Schema**: 提供详细的输出 JSON schema

---

## 🔄 提示词构建流程

### 在 Vote 步骤中

**代码位置**: `CognitiveCoordinatorGAgent.Vote.cs` → `ExecuteVoteAsync`

**流程**:
```
1. 读取 generator 配置
   ├─> generator.Parameters["prompt"] → User Prompt 模板
   └─> generator.Parameters["system"] → System Prompt（可选，覆盖默认值）
   ↓
2. 模板渲染
   ├─> 使用 workflow 变量渲染 User Prompt
   └─> {{task}}, {{context}} 等变量被替换
   ↓
3. 调用 LLM
   ├─> System Prompt: defaults.llm_call.system（或覆盖值）
   └─> User Prompt: 渲染后的 generator.prompt
   ↓
4. 生成提案
   └─> LLM 输出 → 提案内容
   ↓
5. Red-Flag 检查
   └─> 检查提案质量
```

---

## 📊 提示词组合示例

### 示例 1: DAG Consensus 场景

**System Prompt**:
```
You are an agent in a MAKER-style reasoning workflow...
[Judgement rules, Hard constraints, etc.]
```

**User Prompt** (来自 `BuildTaskPrompt`):
```
You are validating and synthesizing a DAG mutation for a research derivation graph.

Requirements:
- Output STRICT JSON ONLY (no markdown, no code fences).
- If the candidate is invalid/unsafe/incoherent, set non-empty redFlags and keep nodes/edges empty.
...

CandidateMutationSummary:
{
  "mutationId": "dag_h1_verification_v1",
  "authorAgent": "dag_builder",
  "nodeCount": 4,
  "edgeCount": 7,
  "nodes": [
    {"id": "thm_1", "type": "theorem", "label": "..."},
    ...
  ],
  "edges": [
    {"from": "axiom_1", "to": "thm_1", "type": "depends_on"},
    ...
  ]
}

CurrentDagStats:
{
  "nodeCount": 10,
  "edgeCount": 15,
  "updatedAt": "2025-01-16T10:30:00Z"
}

Output JSON schema:
{
  "mutationId": "string",
  "author": "string",
  "nodes": [...],
  "edges": [...],
  "redFlags": ["string"]
}
```

**组合后的完整提示**:
```
[System Prompt]
  ↓
[User Prompt]
```

---

### 示例 2: 原子任务解决场景

**System Prompt**:
```
You are an agent in a MAKER-style reasoning workflow...
```

**User Prompt** (来自 `solve_atomic.generator.prompt`):
```
Solve the following task directly:

Task: Validate this DAG mutation candidate
Context: {
  "mutationId": "...",
  "nodeCount": 4,
  ...
}

Provide a complete, well-reasoned solution.
```

---

## 🎯 关键设计点

### 1. System Prompt 的通用性

- **共享**: 所有 vote 步骤中的 generator 都使用相同的 system prompt
- **可覆盖**: 步骤级可以覆盖 system prompt（但 maker.yaml 中没有使用）
- **思考框架**: 提供 MAKER 工作流的思考框架，不涉及具体任务

### 2. User Prompt 的任务特定性

- **任务相关**: 每个 vote 步骤的 user prompt 针对特定任务
- **模板变量**: 使用 `{{task}}`, `{{context}}` 等模板变量
- **输出格式**: 明确指定输出格式（JSON、text、json_array）

### 3. DAG Consensus 的特殊处理

- **专用 Task Prompt**: `BuildTaskPrompt` 构建专门用于 DAG Consensus 的 task prompt
- **完整 Schema**: 提供详细的 JSON schema
- **Red-Flag 指导**: 明确指导如何设置 redFlags

---

## 🔍 Red-Flag 检查与提示词的关系

### Red-Flag 不修改提示词

**重要**: Red-Flag **不修改** LLM 的提示词，它只是**检查** LLM 的输出。

**流程**:
```
1. LLM 使用提示词生成提案
   ├─> System Prompt: defaults.llm_call.system
   └─> User Prompt: generator.prompt（渲染后）
   ↓
2. LLM 输出提案
   ↓
3. Red-Flag 检查提案
   ├─> 长度检查
   ├─> 解析检查
   ├─> 内容质量检查
   └─> 数量限制检查
   ↓
4. 决策
   ├─> 通过 → 进入投票
   └─> 拒绝 → 重新生成（使用相同提示词）
```

### Red-Flag 检查的输出

Red-Flag 检查的是 LLM 生成的**原始输出**（`result.AssistantResponse`），而不是解析后的对象。

**代码位置**: `CognitiveCoordinatorGAgent.Vote.cs` (第 190-209 行)

```csharp
// Use raw LLM response for voting (not parsed object)
var proposal = result.AssistantResponse ?? result.Value?.ToString() ?? "";

// Red-Flag validation
if (redFlagStrategy != null)
{
    var proposalId = $"{step.Id}.round{proposalIndex}";
    if (!redFlagStrategy.Validate(proposal, proposalId, out var reason))
    {
        // Red flag detected
        continue; // Skip this proposal
    }
}
```

---

## 📝 提示词总结

### System Prompt

**位置**: `maker.yaml` → `defaults.llm_call.system`

**内容**: 
- MAKER 工作流角色定义
- HPA 参考模型
- 核心三元组（Rotation → Factorization → Projection）
- 判断规则（proved=true/false）
- 硬约束

**特点**: 
- 通用，适用于所有 vote 步骤
- 提供思考框架
- 不涉及具体任务

---

### User Prompts（按场景）

#### 1. 原子任务解决 (`solve_atomic`)

```
Solve the following task directly:

Task: {{task}}
{% if context %}
Context: {{context | json}}
{% endif %}

Provide a complete, well-reasoned solution.
```

**输出格式**: `text`

---

#### 2. 任务分解 (`decompose`)

```
Break down the following complex task into 2-5 independent subtasks.

Task: {{task}}
{% if context %}
Context: {{context | json}}
{% endif %}

Format your response as a JSON array:
[
  {"id": "subtask_1", "description": "..."},
  {"id": "subtask_2", "description": "..."}
]
```

**输出格式**: `json_array`

---

#### 3. 结果合成 (`compose`)

```
Compose the following subtask solutions into a coherent final solution.

Original Task: {{task}}

Subtask Results:
{% for result in subtask_results %}
- {{result.solution}}
{% endfor %}

Synthesize a comprehensive solution that addresses the original task.
```

**输出格式**: `text`

---

#### 4. DAG Consensus (`BuildTaskPrompt`)

```
You are validating and synthesizing a DAG mutation for a research derivation graph.

Requirements:
- Output STRICT JSON ONLY (no markdown, no code fences).
- If the candidate is invalid/unsafe/incoherent, set non-empty redFlags and keep nodes/edges empty.
- Do not invent node ids that are not necessary; prefer reusing existing ids when possible.
- Edge semantics: dependency -> dependent (from -> to). Use type "depends_on" unless a better typed edge is justified.
- Keep text bounded: label <= 200 chars; proof <= 2000 chars; long proof should be summarized.

Task:
1) Check the candidate mutation against the current DAG stats.
2) Normalize node/edge fields and remove obvious duplicates.
3) If any parsing/consistency issue exists, red-flag with clear reasons.

CandidateMutationSummary: {...}
CurrentDagStats: {...}

Output JSON schema: {...}
```

**输出格式**: `JSON`（严格 JSON，无 markdown）

---

## 💡 关键理解

### Red-Flag 与提示词的关系

1. **Red-Flag 不调用 LLM**: Red-Flag 只是检查机制，不生成内容
2. **提示词用于生成提案**: Vote 步骤中的 generator (llm_call) 使用提示词生成提案
3. **Red-Flag 检查提案**: 对生成的提案进行质量检查
4. **相同提示词重试**: 如果提案被拒绝，使用相同提示词重新生成

### 提示词设计原则

1. **明确输出格式**: 明确要求 JSON、text 或 json_array
2. **提供完整 Schema**: 对于 JSON 输出，提供详细的 schema
3. **指导 Red-Flag 设置**: 明确何时设置 redFlags
4. **边界限制**: 明确文本长度限制（label <= 200, proof <= 2000）

---

## 🔍 实际使用示例

### DAG Consensus 完整流程

```
1. BuildTaskPrompt 构建 task prompt
   ↓
2. 调用 maker workflow
   ├─> System Prompt: defaults.llm_call.system
   └─> User Prompt: BuildTaskPrompt 的输出
   ↓
3. maker workflow 执行
   ├─> check_atomic → 判断任务类型
   ├─> solve_atomic (如果是原子任务)
   │     ├─> generator.prompt: "Solve the following task directly..."
   │     └─> 生成提案 → Red-Flag 检查 → 投票
   └─> decompose → execute_subtasks → compose (如果是复杂任务)
   ↓
4. 输出包含 redFlags 字段
   ↓
5. 检查 redFlags
   ├─> 如果为空 → 构建 mutation，返回成功
   └─> 如果不为空 → 返回失败
```

---

*最后更新: 2025-01-16*
