# DAG Consensus Prompts 分析

## 📋 概述

DAG Consensus 是 VibeResearching 系统中用于验证和合成 DAG mutation 的组件。它支持两种模式：

1. **`verifier-quorum`**（默认模式）：使用多个 verifier 投票
2. **`maker`**（可选模式）：使用 Cognitive DSL workflow

---

## 🔄 模式选择

**代码位置**: `DagConsensusRunner.cs` → `ResolveMode`

```csharp
private string ResolveMode(ConsensusInput input)
{
    // 1. 检查 candidate labels 中的 workflow 提示（罕见覆盖）
    if (input?.Candidate != null &&
        input.Candidate.Labels != null &&
        input.Candidate.Labels.TryGetValue("workflow", out var w) &&
        !string.IsNullOrWhiteSpace(w))
    {
        return w.Trim();
    }

    // 2. 从配置读取
    var mode = (_configuration.GetValue<string?>("Vibe:DagConsensus:Mode") ?? string.Empty).Trim();
    return mode.Length == 0 ? "verifier-quorum" : mode; // 默认 verifier-quorum
}
```

**默认模式**: `verifier-quorum`

---

## 模式 1: verifier-quorum（默认模式）

### 工作流程

**代码位置**: `DagConsensusRunner.Quorum.cs` → `RunVerifierQuorumAsync`

```
输入: ConsensusInput (包含 candidate mutation, current DAG, materials context)
    ↓
快速结构预检查 (PrecheckCandidate)
    ↓
如果预检查失败 → 直接拒绝
    ↓
否则 → 并行调用 N 个 verifier（默认 3 个）
    ↓
每个 verifier 有不同的 focus: "structure", "grounding", "safety"
    ↓
收集投票结果
    ↓
判断: approveCount >= quorum && redFlags.Count == 0
    ↓
返回 ConsensusResult
```

### System Prompt

**代码位置**: `VibeVerifierAgent.cs`

```text
You are a verifier.

Goal:
- Verify the most critical claims using concrete checks.

Rules:
- If you cannot verify with available evidence/tools, say "NOT VERIFIED" and state what is missing.
- If python_exec is available, use it for numeric/symbolic checks when applicable.
- Keep output short and structured:
  - Claim
  - Check
  - Result (VERIFIED / NOT VERIFIED / INCONCLUSIVE)
  - Notes
```

**注意**: verifier-quorum 模式**不会**注入 Materials Context 到 system prompt（与普通 verifier 不同）。

### User Prompt

**代码位置**: `DagConsensusRunner.Quorum.cs` → `BuildVerifierPrompt`

```text
You are voting on whether to ACCEPT a DAG mutation into the canonical DAG snapshot.

Focus:
- {focus}  // "structure", "grounding", 或 "safety"

Rules:
- Output STRICT JSON ONLY (no markdown, no code fences).
- If you see a HARD blocker, set approve=false and add it to redFlags.
- If you only have minor concerns, keep redFlags empty and explain in notes.
- Be conservative: do not approve incoherent, cyclic, or malformed mutations.
- IMPORTANT: missing source files / missing evidence are NOT hard blockers by themselves.
  If grounding is missing, keep redFlags empty and list what's missing in notes.

What to check (quickly):
- Structural soundness: ids, missing references, cycles, self-edges, direction (dependency -> dependent).
- Grounding (if materials are present): does the mutation align with DAG facts?
- Safety: no unbounded text, no fabricated citations.

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
      "label": "...",
      "proof": "...",
      "tags": {...}
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

MaterialsContext (optional):
{materialsContext}  // 最多 6000 字符

Output JSON schema:
{
  "approve": true,
  "redFlags": ["string"],
  "notes": "string"
}
```

### Focus 分配

**代码位置**: `DagConsensusRunner.Quorum.cs` → `RunVerifierQuorumAsync`

```csharp
var focusTags = new[] { "structure", "grounding", "safety" };
for (var i = 0; i < cfg.VerifierCount; i++)
{
    var focus = focusTags[i % focusTags.Length]; // 循环分配
    // ...
}
```

**默认配置**:
- `VerifierCount`: 3（从配置读取，默认 3）
- `Quorum`: 2（从配置读取，默认 2）

**投票逻辑**:
- 只有当 `approve=true` **且** `redFlags.Count == 0` 时，才计为一票通过
- 需要 `approveCount >= quorum` **且** `outFlags.Count == 0` 才能通过

### 预检查（Precheck）

**代码位置**: `DagConsensusRunner.Quorum.cs` → `PrecheckCandidate`

在调用 verifier 之前，会进行快速的结构预检查：

1. **missing_mutation_id**: mutationId 为空
2. **missing_author_agent**: authorAgent 为空
3. **self_edge**: 存在自环边（from == to）
4. **duplicate_node_id**: 存在重复的节点 ID

如果预检查失败，直接拒绝，不调用 verifier。

---

## 模式 2: maker（Cognitive DSL）

### 工作流程

**代码位置**: `DagConsensusRunner.cs` → `RunAsync`

```
输入: ConsensusInput
    ↓
构建 task prompt (BuildTaskPrompt)
    ↓
调用 Cognitive DSL workflow "maker"
    ↓
解析 JSON 输出
    ↓
检查 redFlags
    ↓
构建最终 mutation
    ↓
返回 ConsensusResult
```

### System Prompt

**代码位置**: `workflows/maker.yaml`

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

Hard constraints:
- Do NOT invent facts.
- Do NOT fabricate citations.
- Do NOT claim verification without evidence.
- If a claim cannot be verified, state it as a hypothesis or assumption.
```

**注意**: maker 模式使用 Cognitive DSL workflow，system prompt 由 `maker.yaml` 定义。

### User Prompt (Task Prompt)

**代码位置**: `DagConsensusRunner.cs` → `BuildTaskPrompt`

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

**注意**: maker 模式的 task prompt **不包含** Materials Context（与 verifier-quorum 不同）。

---

## 📊 对比总结

| 特性 | verifier-quorum | maker |
|------|----------------|-------|
| **默认模式** | ✅ 是 | ❌ 否 |
| **System Prompt** | VibeVerifierAgent | maker.yaml (Cognitive DSL) |
| **User Prompt** | BuildVerifierPrompt | BuildTaskPrompt |
| **Materials Context** | ✅ 包含（最多 6000 字符） | ❌ 不包含 |
| **调用方式** | 并行调用 N 个 verifier | Cognitive DSL workflow |
| **Focus** | structure/grounding/safety（循环分配） | 无（统一处理） |
| **投票机制** | ✅ 是（需要 quorum） | ❌ 否（单一输出） |
| **输出格式** | JSON vote (approve, redFlags, notes) | JSON mutation (nodes, edges, redFlags) |
| **性能** | 轻量级（快速投票） | 重量级（完整推理） |

---

## 🔍 关键代码位置

### verifier-quorum

- **主流程**: `DagConsensusRunner.Quorum.cs` → `RunVerifierQuorumAsync`
- **Prompt 构建**: `DagConsensusRunner.Quorum.cs` → `BuildVerifierPrompt`
- **预检查**: `DagConsensusRunner.Quorum.cs` → `PrecheckCandidate`
- **投票解析**: `DagConsensusRunner.Quorum.cs` → `TryParseVote`
- **配置**: `DagConsensusRunner.Quorum.cs` → `GetQuorumConfig`

### maker

- **主流程**: `DagConsensusRunner.cs` → `RunAsync`
- **Prompt 构建**: `DagConsensusRunner.cs` → `BuildTaskPrompt`
- **Workflow 定义**: `workflows/maker.yaml`
- **JSON 解析**: `DagConsensusRunner.cs` → `TryExtractJson`

---

## 💡 设计决策

### 为什么 verifier-quorum 包含 Materials Context？

- **Grounding 检查**: verifier 需要检查 mutation 是否与 DAG facts 对齐
- **Focus "grounding"**: 专门有一个 verifier focus 是 "grounding"
- **软错误处理**: 如果 grounding 缺失，不会直接拒绝，而是记录在 notes 中

### 为什么 maker 不包含 Materials Context？

- **Cognitive DSL**: maker 使用 Cognitive DSL workflow，可能有自己的 context 管理
- **简化 prompt**: maker 的 task prompt 已经很长，避免过度复杂
- **Workflow 设计**: maker workflow 可能在其他步骤中处理 materials

### 为什么 verifier-quorum 使用多个 verifier？

- **多样性**: 不同的 focus（structure/grounding/safety）提供不同的视角
- **容错性**: 即使某个 verifier 失败，其他 verifier 仍可投票
- **共识机制**: 需要 quorum 才能通过，避免单一 verifier 的错误判断

---

## 🐛 常见问题

### 1. verifier-quorum 总是拒绝 mutation

**可能原因**:
- 预检查失败（missing_mutation_id, self_edge, duplicate_node_id）
- redFlags 不为空（即使 approve=true）
- approveCount < quorum

**解决**: 检查 artifact JSON 文件中的 `precheckFlags`、`redFlags` 和 `approveCount`

### 2. maker 模式未启用

**原因**: 默认模式是 `verifier-quorum`

**解决**: 设置配置 `Vibe:DagConsensus:Mode = "maker"`

### 3. Materials Context 未传递

**原因**: 
- verifier-quorum: 检查 `ConsensusInput.MaterialsContext` 是否设置
- maker: maker 模式不包含 Materials Context（设计决策）

**解决**: 
- verifier-quorum: 确保在调用 `RunAsync` 时传递 `MaterialsContext`
- maker: 这是预期行为，无需修改

---

## 📚 相关文件

- `DagConsensusRunner.cs`: 主入口和 maker 模式
- `DagConsensusRunner.Quorum.cs`: verifier-quorum 模式
- `VibeVerifierAgent.cs`: verifier system prompt
- `workflows/maker.yaml`: maker workflow 定义
- `VibeOrchestrator.cs`: 调用 DAG Consensus 的地方
