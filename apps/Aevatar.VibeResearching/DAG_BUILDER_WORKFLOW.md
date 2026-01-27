# DAG Builder 工作流程分析

## 📋 概述

`dag_builder` 是 VibeResearching 系统中负责从 worker outputs 中提取知识并构建 DAG mutation candidate 的 agent。它位于 worker phase 的最后，在 `verifier` 之后执行。

## 🎯 核心职责

1. **提取知识**: 从 `planner`、`reasoner`、`verifier`、`librarian` 的输出中提取知识项
2. **构建 DAG 节点**: 将提取的知识转换为 DAG Knowledge 节点
3. **建立依赖关系**: 创建节点之间的依赖边（edges）
4. **生成候选 mutation**: 输出 JSON 格式的 DAG mutation candidate

## 🔄 在整个研究流程中的位置

```
ExecuteOneRoundAsync
    │
    ├─> Brief Generation (research_assistant [MODE:BRIEF])
    │   └─> 创建 Plan 节点（Milestones）
    │
    ├─> Plan Phase (research_assistant [MODE:PLAN])
    │   └─> 创建 Plan 节点（Round Plans）
    │
    ├─> Worker Phase
    │   ├─> planner → 生成执行计划
    │   ├─> reasoner → 生成推理过程
    │   ├─> verifier → 验证假设
    │   ├─> librarian → 提取可信公理（可选）
    │   └─> dag_builder → 提取知识 → 构建 DAG mutation candidate ⭐
    │
    ├─> DAG Consensus Phase
    │   ├─> 解析 dag_builder 的输出
    │   ├─> 验证 candidate mutation
    │   └─> 应用 mutation → 更新 DAG
    │
    └─> Summary Phase (research_assistant [MODE:SUMMARY])
```

## 📥 输入

### 1. System Prompt

**文件位置**: `VibeDagBuilderAgent.cs`

```text
You are a DAG builder for a research derivation graph.

Task:
- Propose a minimal, consistent DAG mutation that captures new derivations from this round.
- If nothing is ready, output an EMPTY mutation with nodes=[] and edges=[].
- If the librarian provides "trusted axioms" with citations, you SHOULD include them as AXIOM nodes.
  Treat them as axioms (no verifier step required) but keep citations in tags.

CRITICAL - Extract knowledge from verifier output:
- The verifier output contains verified knowledge items (axioms, theorems, definitions) that have been checked.
- You MUST extract and include these verified items as DAG nodes:
  * AXIOM nodes: Extract from verifier output when it mentions verified axioms or foundational statements.
  * THEOREM nodes: Extract from verifier output when it mentions verified theorems or proven statements.
  * ASSUMPTION/DEFINITION nodes: Extract from verifier output when it mentions verified definitions or assumptions.
- For each extracted knowledge item:
  * Use the exact statement from verifier output as the "label".
  * Set "type" to "axiom", "theorem", "assumption", or "definition" based on verifier's classification.
  * If verifier marked it as "VERIFIED", include tag: { "verification_status": "verified", "verified_by": "verifier" }.
  * If verifier marked it as "NOT VERIFIED" or "INCONCLUSIVE", you may still include it but mark appropriately in tags.
  * Extract any proof or verification method from verifier output and include in "proof" field (if available).
- Priority: Items marked as "VERIFIED" by verifier should be included first.
- Cross-reference with reasoner output: If reasoner listed axioms/theorems/definitions and verifier verified them, 
  combine the information (use reasoner's complete statement, verifier's verification status).

Helpful tools (optional):
- You MAY call graph_get_snapshot to see existing node ids/types and reuse them.
- You MAY call graph_explain_node(nodeId) to understand dependencies and avoid cycles.

Rules:
- Output STRICT JSON ONLY (no markdown, no code fences).
- Node.id should be stable and short (e.g. "thm_pythagoras_v1").
- Edge semantics: dependency -> dependent (from -> to).
- Use edge.type "depends_on" unless a stronger type is clearly justified.
- Keep label <= 200 chars; proof <= 1200 chars.
- For axioms from papers, put citation/source in tags, e.g.:
  tags: { "sourcePath": "...", "citation": "...", "trusted": "paper" }.

IMPORTANT - Provenance tracking:
- Each knowledge node MUST specify "motivatedByPlanNodeId" to link it to the plan step that motivated its creation.
- CRITICAL: Use the EXACT plan node ID from the "Plan:" section in the context (e.g., "plan_abc_123_ms_r1").
  Do NOT construct the ID yourself - copy it exactly as shown in the plan context.
- Look at the current round context to determine which milestone/plan node is being executed.

Schema (updated):
{
  "mutationId": "string",
  "authorAgent": "dag_builder",
  "nodes": [
    {
      "id": "string",
      "type": "axiom|theorem|assumption|hypothesis|unknown",
      "kind": "knowledge",
      "label": "string",
      "proof": "string",
      "motivatedByPlanNodeId": "string (required for knowledge nodes)",
      "tags": { "k": "v" }
    }
  ],
  "edges": [
    { "from": "string", "to": "string", "type": "depends_on" }
  ]
}
```

### 2. User Prompt

**文件位置**: `VibeOrchestrator.GoalsAndMessages.cs` → `BuildDagBuilderMessage`

```text
You must output STRICT JSON ONLY.

Question: {question}

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

Current DAG: nodes={dag.Nodes.Count}, edges={dag.Edges.Count}

[如果存在 librarian axioms]
Librarian trusted axioms (include as AXIOM nodes when appropriate):
{librarianAxioms JSON}

Worker outputs (excerpts):
[planner]
{plannerOutput (最多 2200 字符)}

[reasoner]
{reasonerOutput (最多 2200 字符)}

[librarian]
{librarianOutput (最多 2200 字符)}

[verifier]
{verifierOutput (最多 3000 字符，优先处理)}
CRITICAL: Extract verified axioms, theorems, and definitions from this output:
```

**关键点**:
- **Verifier 输出优先**: verifier 的输出被优先处理，字符限制更高（3000 vs 2200）
- **Plan Context**: 包含当前 DAG 中的 Plan 节点（milestones 和 round plans）
- **Materials Context**: 通过 `req.Context["materials_context"]` 注入到 system prompt

## 📤 输出

### JSON Schema

```json
{
  "mutationId": "string",
  "authorAgent": "dag_builder",
  "nodes": [
    {
      "id": "string",
      "type": "axiom|theorem|assumption|hypothesis|unknown",
      "kind": "knowledge",
      "label": "string (<= 200 chars)",
      "proof": "string (<= 1200 chars)",
      "motivatedByPlanNodeId": "string (required)",
      "tags": {
        "verification_status": "verified",
        "verified_by": "verifier",
        "sourcePath": "...",
        "citation": "...",
        "trusted": "paper"
      }
    }
  ],
  "edges": [
    {
      "from": "string",
      "to": "string",
      "type": "depends_on"
    }
  ]
}
```

### 输出限制

- **Label**: 最多 200 字符
- **Proof**: 最多 1200 字符
- **输出格式**: STRICT JSON ONLY（无 Markdown，无代码块）

## 🔍 详细工作流程

### Step 1: 触发时机

**代码位置**: `VibeOrchestrator.ExecuteOneRound.Parts.cs` → `RunWorkerPhaseAsync`

```csharp
case "dag_builder":
{
    // Refresh DAG snapshot right before builder (other sessions may have mutated the shared DAG).
    var fresh = await _core.Dag.LoadSnapshotAsync(dagId, ct);
    onDagRefreshed(fresh);

    var provider = await EnsureProviderRunnableOrPauseAsync(...);
    outputs[agent] = await RunDagBuilderAsync(
        ctx,
        fresh,
        outputs,
        librarianAxioms,
        provider,
        ct);
    break;
}
```

**关键点**:
- 在执行 `dag_builder` 之前，会刷新 DAG snapshot（因为其他 session 可能已经修改了共享 DAG）
- `dag_builder` 在所有其他 workers（planner, reasoner, verifier, librarian）之后执行

---

### Step 2: 构建 User Prompt

**代码位置**: `VibeOrchestrator.GoalsAndMessages.cs` → `BuildDagBuilderMessage`

**步骤**:

1. **添加 Question 和附件**
2. **添加 Plan Context**: 调用 `BuildPlanContextFromDag(dag)` 获取 Plan 节点摘要
3. **添加 DAG Stats**: 当前 DAG 的节点数和边数
4. **添加 Librarian Axioms**: 如果存在，以 JSON 格式添加
5. **添加 Worker Outputs**:
   - **优先处理 verifier 输出**（最多 3000 字符）
   - 然后添加其他 worker 输出（最多 2200 字符）
   - 跳过 `dag_builder` 自己的输出

**关键代码**:
```csharp
// Prioritize verifier output - extract knowledge items from it
if (outputs.TryGetValue("verifier", out var verifierOutput) && !string.IsNullOrWhiteSpace(verifierOutput))
{
    sb.AppendLine("[verifier]");
    sb.AppendLine("CRITICAL: Extract verified axioms, theorems, and definitions from this output:");
    sb.AppendLine(Bound(verifierOutput, 3000)); // Increased limit for verifier output
    sb.AppendLine();
}

// Then include other worker outputs
foreach (var (k, v) in outputs.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
{
    if (string.Equals(k, "dag_builder", StringComparison.OrdinalIgnoreCase) || 
        string.Equals(k, "verifier", StringComparison.OrdinalIgnoreCase))
        continue; // Skip dag_builder's own output and already-processed verifier output
    sb.AppendLine($"[{k}]");
    sb.AppendLine(Bound(v ?? "", 2200));
    sb.AppendLine();
}
```

---

### Step 3: 调用 LLM

**代码位置**: `VibeOrchestrator.Workers.cs` → `RunDagBuilderAsync`

**步骤**:

1. **发布 StepStartedEvent**: `stepName = "vibe.dag_builder"`
2. **获取 Agent**: `GetDagBuilderAgentAsync(sessionId, providerName)`
3. **构建 ChatRequest**:
   ```csharp
   var req = new ChatRequest
   {
       Message = BuildDagBuilderMessage(...),
       RequestId = ctx.Input.RequestId ?? Guid.NewGuid().ToString("N"),
       StageHint = "session:vibe:dag_builder"
   };
   req.Context["agent_id"] = dbId;
   req.Context["materials_context"] = ctx.Materials.RenderedContext;
   ```
4. **调用 LLM**:
   - 如果支持 streaming: `ChatStreamAsync`
   - 否则: `ChatAsync`
5. **流式输出到 UI**: 以 ````json` 代码块格式输出
6. **发布 StepFinishedEvent**: `stepName = "vibe.dag_builder"`

---

### Step 4: 输出处理

**代码位置**: `VibeOrchestrator.Workers.cs` → `RunDagBuilderAsync`

**输出格式**:
- 以 ````json` 代码块格式输出到 UI
- 输出限制: 最多 20,000 字符
- 即使 JSON 无效，也会显示给用户（便于调试）

---

### Step 5: 传递给 DAG Consensus

**代码位置**: `VibeOrchestrator.DagConsensus.cs` → `RunDagApplyAsync`

**步骤**:

1. **获取 dag_builder 输出**:
   ```csharp
   var candidateText = outputs.TryGetValue("dag_builder", out var x) ? x : string.Empty;
   ```

2. **解析 JSON**:
   ```csharp
   var candidate = TryParseDagBuilderCandidate(session.Id, candidateText, currentDag, activeMilestoneId);
   ```

3. **验证**:
   - 如果解析失败 → 返回空结果
   - 如果是空 mutation → 跳过共识（不阻塞）

4. **传递给 DAG Consensus**:
   - `verifier-quorum` 模式: 多个 verifier 投票
   - `maker` 模式: Cognitive DSL workflow

---

## 🔗 与 DAG Consensus 的交互

### 解析 Candidate

**代码位置**: `VibeOrchestrator.Parsing.cs` → `TryParseDagBuilderCandidate`

**步骤**:

1. **提取 JSON**: 使用 `TryExtractJson` 从输出中提取 JSON
2. **反序列化**: 解析为 `DagBuilderMutationJson`
3. **构建 SraDagMutation**:
   - 设置 `SessionId`、`MutationId`、`AuthorAgent`
   - 转换 nodes（验证 `motivatedByPlanNodeId`）
   - 转换 edges
   - **限制**: `dag_builder` **只能创建 Knowledge 节点**（不能创建 Plan 节点）

**关键验证**:
```csharp
// IMPORTANT: dag_builder can ONLY create Knowledge nodes.
if (node.Kind != SraDagNodeKind.Knowledge)
{
    _host.Logger.LogWarning("[DagConsensus] dag_builder attempted to create non-Knowledge node: {Kind}", node.Kind);
    continue; // Skip non-Knowledge nodes
}
```

---

## 📊 知识提取策略

### 1. 从 Verifier 输出提取（优先）

**策略**:
- 提取标记为 "VERIFIED" 的知识项
- 提取验证状态和验证方法
- 优先包含已验证的知识

**标签**:
```json
{
  "verification_status": "verified",
  "verified_by": "verifier"
}
```

### 2. 从 Reasoner 输出提取

**策略**:
- 提取列出的 axioms、theorems、definitions
- 与 verifier 输出交叉引用
- 使用 reasoner 的完整陈述，verifier 的验证状态

### 3. 从 Librarian 输出提取

**策略**:
- 提取 "trusted axioms"
- 标记为 AXIOM 节点
- 包含 citation 和 sourcePath

**标签**:
```json
{
  "sourcePath": "...",
  "citation": "...",
  "trusted": "paper"
}
```

### 4. 从 Planner 输出提取

**策略**:
- 提取执行计划中的知识项
- 提取假设和未知项

---

## 🎯 关键设计决策

### 1. 为什么 dag_builder 在 verifier 之后？

- **验证优先**: 优先提取已验证的知识
- **质量保证**: 确保 DAG 节点基于已验证的内容
- **减少噪音**: 避免未经验证的知识污染 DAG

### 2. 为什么需要 motivatedByPlanNodeId？

- **可追溯性**: 每个知识节点都能追溯到其来源计划
- **上下文关联**: 将知识与研究目标关联
- **审计支持**: 支持研究过程的审计和审查

### 3. 为什么只能创建 Knowledge 节点？

- **职责分离**: Plan 节点由 `research_assistant` 创建
- **避免冲突**: 防止 dag_builder 修改研究计划
- **一致性**: 确保 Plan 节点的创建逻辑统一

### 4. 为什么优先处理 verifier 输出？

- **质量优先**: 已验证的知识更可靠
- **减少错误**: 避免未经验证的知识进入 DAG
- **效率提升**: 优先处理最重要的信息源

---

## 🛠️ 工具支持

`dag_builder` 可以使用以下工具（可选）:

1. **GraphGetSnapshotTool**: 查看现有节点 ID 和类型，避免重复
2. **GraphExplainNodeTool**: 理解依赖关系，避免循环

**代码位置**: `VibeDagBuilderAgent.cs` → `RegisterToolsAsync`

---

## 📝 输出示例

```json
{
  "mutationId": "dag_builder_abc123",
  "authorAgent": "dag_builder",
  "nodes": [
    {
      "id": "thm_pythagoras_v1",
      "type": "theorem",
      "kind": "knowledge",
      "label": "Pythagorean theorem: a² + b² = c²",
      "proof": "Verified by verifier using geometric proof",
      "motivatedByPlanNodeId": "plan_ms_r1",
      "tags": {
        "verification_status": "verified",
        "verified_by": "verifier"
      }
    },
    {
      "id": "axiom_euclidean_space",
      "type": "axiom",
      "kind": "knowledge",
      "label": "Euclidean space axioms",
      "proof": "",
      "motivatedByPlanNodeId": "plan_ms_r1",
      "tags": {
        "sourcePath": "/papers/euclid.pdf",
        "citation": "Euclid, Elements",
        "trusted": "paper"
      }
    }
  ],
  "edges": [
    {
      "from": "axiom_euclidean_space",
      "to": "thm_pythagoras_v1",
      "type": "depends_on"
    }
  ]
}
```

---

## 🐛 常见问题

### 1. dag_builder 输出为空

**可能原因**:
- Worker outputs 中没有可提取的知识
- JSON 解析失败
- 所有知识项都未经验证

**解决**: 检查 worker outputs，确保有可提取的知识

### 2. motivatedByPlanNodeId 不匹配

**可能原因**:
- Plan 节点 ID 拼写错误
- Plan 节点不存在于当前 DAG

**解决**: 检查 Plan Context，确保使用正确的 Plan 节点 ID

### 3. 节点类型错误

**可能原因**:
- 尝试创建 Plan 节点（不允许）
- 节点类型不在允许列表中

**解决**: 确保只创建 Knowledge 节点，类型为 axiom/theorem/assumption/hypothesis/unknown

---

## 📚 相关文件

- `VibeDagBuilderAgent.cs`: Agent 定义和 System Prompt
- `VibeOrchestrator.Workers.cs`: `RunDagBuilderAsync` 实现
- `VibeOrchestrator.GoalsAndMessages.cs`: `BuildDagBuilderMessage` 实现
- `VibeOrchestrator.Parsing.cs`: `TryParseDagBuilderCandidate` 实现
- `VibeOrchestrator.DagConsensus.cs`: DAG Consensus 调用
- `VibeOrchestrator.ExecuteOneRound.Parts.cs`: Worker phase 执行
