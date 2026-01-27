# DAG Consensus 工作流程分析

## 📋 概述

DAG Consensus 是 VibeResearching 系统中用于验证和合成 DAG mutation candidate 的**质量门控（quality gate）**。它位于 `dag_builder` 之后，负责验证 `dag_builder` 生成的 candidate mutation，确保其质量和一致性，然后应用到 DAG。

## 🎯 核心职责

1. **解析 Candidate**: 从 `dag_builder` 的输出中解析 JSON mutation candidate
2. **验证质量**: 通过共识机制验证 candidate 的质量和一致性
3. **合成/规范化**: 规范化节点和边的字段，移除重复项
4. **应用 Mutation**: 将验证通过的 mutation 应用到 DAG

## 🔄 在整个研究流程中的位置

```
ExecuteOneRoundAsync
    │
    ├─> Worker Phase
    │   └─> dag_builder → 生成 DAG mutation candidate (JSON)
    │
    ├─> DAG Consensus Phase ⭐
    │   ├─> 解析 candidate
    │   ├─> 验证 candidate (verifier-quorum 或 maker)
    │   └─> 应用 mutation → 更新 DAG
    │
    └─> Summary Phase
```

## 📊 完整工作流程

### Phase 1: 准备阶段

**代码位置**: `VibeOrchestrator.DagConsensus.cs` → `RunDagApplyAsync`

#### Step 1.1: 查询 Active Milestone

```csharp
var activeMilestoneId = await GetActiveMilestoneFromGraphAsync(session.Id, ct);
```

**目的**: 从 Neo4j 查询当前 Active 的 milestone Plan 节点

**用途**: 
- 作为 Knowledge 节点的默认 `motivatedByPlanNodeId`（如果 candidate 中未指定）
- 验证 candidate 中的 `motivatedByPlanNodeId` 引用

#### Step 1.2: 获取 dag_builder 输出

```csharp
var candidateText = outputs.TryGetValue("dag_builder", out var x) ? x : string.Empty;
```

**目的**: 从 `outputs` 字典中获取 `dag_builder` 的输出（JSON 字符串）

---

### Phase 2: 解析阶段

**代码位置**: `VibeOrchestrator.Parsing.cs` → `TryParseDagBuilderCandidate`

#### Step 2.1: 提取 JSON

```csharp
if (!TryExtractJson(raw, out var json) || string.IsNullOrWhiteSpace(json))
    return null;
```

**目的**: 从 `dag_builder` 的输出中提取 JSON（可能包含 Markdown 代码块）

**方法**: `TryExtractJson` 会：
- 检查是否以 `{` 或 `[` 开头
- 查找最后一个 `}` 或 `]`
- 尝试提取有效的 JSON 片段

#### Step 2.2: 反序列化 JSON

```csharp
parsed = JsonSerializer.Deserialize<DagCandidateJson>(json!, Json);
```

**目的**: 将 JSON 字符串反序列化为 `DagCandidateJson` 对象

**结构**:
```csharp
private sealed class DagCandidateJson
{
    public string? MutationId { get; init; }
    public string? AuthorAgent { get; init; }
    public List<DagNodeJson>? Nodes { get; init; }
    public List<DagEdgeJson>? Edges { get; init; }
}
```

#### Step 2.3: 构建 SraDagMutation

**代码位置**: `VibeOrchestrator.Parsing.cs` → `BuildDagMutation`、`AddDagNodes`、`AddDagEdges`

**步骤**:

1. **创建 Mutation 对象**:
   ```csharp
   var mutation = BuildDagMutation(sessionId, parsed, now);
   ```

2. **添加节点**:
   ```csharp
   AddDagNodes(parsed, now, mutation, motivatedByEdges, existingMilestones, activeMilestoneId);
   ```
   
   **关键验证**:
   - **只能创建 Knowledge 节点**: `dag_builder` 不能创建 Plan 节点
   - **验证 motivatedByPlanNodeId**: 
     - 如果未指定或无效，使用 `activeMilestoneId`
     - 如果 `activeMilestoneId` 无效，查找最佳匹配的 milestone
     - 如果找不到，使用最近的 milestone（最高 round index）
   - **创建 motivated_by 边**: 每个 Knowledge 节点必须链接到一个 Plan 节点

3. **添加边**:
   ```csharp
   AddDagEdges(parsed, now, mutation);
   ```

4. **添加 motivated_by 边**:
   ```csharp
   AddMotivatedByEdges(mutation, now, motivatedByEdges);
   ```

#### Step 2.4: 验证空 Mutation

```csharp
if (candidate.UpsertNodes.Count == 0 && candidate.UpsertEdges.Count == 0)
{
    // EMPTY mutation means "no change" (do not stage / do not block).
    return new DagRoundResult(false, false, candidate, null, null, [], null);
}
```

**目的**: 空 mutation 表示"无变更"，不阻塞流程

---

### Phase 3: 共识验证阶段

**代码位置**: `DagConsensusRunner.cs` → `RunAsync`

#### Step 3.1: 选择共识模式

```csharp
var mode = ResolveMode(input);
if (!IsMaker(mode))
{
    return await RunVerifierQuorumAsync(input, ct);
}
```

**模式选择逻辑**:
1. 检查 `candidate.Labels["workflow"]`（罕见覆盖）
2. 从配置读取 `Vibe:DagConsensus:Mode`
3. 默认: `verifier-quorum`

#### Step 3.2: 执行共识验证

**两种模式**:

##### 模式 A: verifier-quorum（默认）

**代码位置**: `DagConsensusRunner.Quorum.cs` → `RunVerifierQuorumAsync`

**流程**:

1. **快速预检查** (`PrecheckCandidate`):
   - `missing_mutation_id`: mutationId 为空
   - `missing_author_agent`: authorAgent 为空
   - `self_edge`: 存在自环边（from == to）
   - `duplicate_node_id`: 存在重复的节点 ID
   
   **如果预检查失败**: 直接拒绝，不调用 verifier

2. **并行调用 N 个 Verifier**（默认 3 个）:
   ```csharp
   var focusTags = new[] { "structure", "grounding", "safety" };
   for (var i = 0; i < cfg.VerifierCount; i++)
   {
       var focus = focusTags[i % focusTags.Length];
       // 调用 verifier，每个有不同的 focus
   }
   ```

3. **收集投票**:
   - 每个 verifier 返回: `{ approve: true/false, redFlags: [...], notes: "..." }`
   - 只有当 `approve=true` **且** `redFlags.Count == 0` 时，才计为一票通过

4. **判断结果**:
   ```csharp
   var approveCount = votes.Count(v => v.Approve && v.RedFlags.Count == 0);
   var ok = approveCount >= cfg.Quorum && outFlags.Count == 0;
   ```
   
   **默认配置**:
   - `VerifierCount`: 3
   - `Quorum`: 2
   - 需要至少 2 票通过且无 redFlags

5. **构建 Accepted Mutation**:
   ```csharp
   if (ok)
       mutation = BuildAcceptedQuorumMutation(sessionId, candidate, cfg, approveCount);
   ```

##### 模式 B: maker（Cognitive DSL）

**代码位置**: `DagConsensusRunner.cs` → `RunAsync`

**流程**:

1. **构建 Task Prompt**:
   ```csharp
   var task = BuildTaskPrompt(input);
   ```

2. **调用 Cognitive DSL Workflow**:
   ```csharp
   rr = await _cognitive.ExecuteAsync(task, options, progress: input.Progress, ct: ct);
   ```

3. **解析输出**:
   - 提取 JSON
   - 反序列化为 `DagMutationJson`
   - 检查 `redFlags`

4. **构建 Mutation**:
   ```csharp
   var mutation = BuildMutation(ws.SessionId, input.Candidate, parsed);
   ```

---

### Phase 4: 应用阶段

**代码位置**: `VibeOrchestrator.DagConsensus.cs` → `RunDagApplyAsync`

#### Step 4.1: 检查共识结果

```csharp
if (!consensus.Ok || consensus.Mutation == null)
{
    // Blocked
    return new DagRoundResult(false, true, candidate, null, consensus.ArtifactPath, consensus.RedFlags, ...);
}
```

**如果被拒绝**:
- 返回 `DagRoundResult`，标记为 `Blocked=true`
- 包含 `RedFlags` 列表
- 保存 artifact 文件（用于调试）

#### Step 4.2: 应用 Mutation

```csharp
var dagId = session.EffectiveDagId;
var accepted = consensus.Mutation;

// 添加共识元数据
if (!string.IsNullOrWhiteSpace(consensus.Workflow))
    accepted.Labels["consensus_workflow"] = consensus.Workflow;
if (!string.IsNullOrWhiteSpace(consensus.ArtifactPath))
    accepted.Labels["consensus_artifact"] = consensus.ArtifactPath;

// 应用到 DAG
var applied = await _core.Dag.ApplyMutationAsync(dagId, accepted, ct);
```

**步骤**:
1. 获取 DAG ID（`session.EffectiveDagId`）
2. 添加共识元数据（workflow、artifact path）
3. 调用 `DagStore.ApplyMutationAsync` 应用 mutation
4. 发布事件: `aevatar.vibe.dag_updated`

#### Step 4.3: 返回结果

```csharp
return new DagRoundResult(
    true,                    // Ok
    false,                   // Blocked
    candidate,               // Candidate mutation
    accepted,                // Accepted mutation
    null,                    // StagedPath
    [],                      // RedFlags
    consensus.ArtifactPath   // ArtifactPath
);
```

---

## 🔍 详细流程图

```
dag_builder 输出 (JSON)
    ↓
[Phase 1: 准备]
    ├─> 查询 Active Milestone (Neo4j)
    └─> 获取 dag_builder 输出
    ↓
[Phase 2: 解析]
    ├─> 提取 JSON
    ├─> 反序列化
    ├─> 构建 SraDagMutation
    │   ├─> 添加节点（验证 motivatedByPlanNodeId）
    │   ├─> 添加边
    │   └─> 添加 motivated_by 边
    └─> 验证空 mutation
    ↓
[Phase 3: 共识验证]
    ├─> 选择模式 (verifier-quorum / maker)
    │
    ├─> [verifier-quorum]
    │   ├─> 预检查（结构验证）
    │   ├─> 并行调用 N 个 verifier
    │   ├─> 收集投票
    │   └─> 判断: approveCount >= quorum?
    │
    └─> [maker]
        ├─> 构建 task prompt
        ├─> 调用 Cognitive DSL workflow
        ├─> 解析输出
        └─> 检查 redFlags
    ↓
[Phase 4: 应用]
    ├─> 检查共识结果
    ├─> 应用 mutation → DAG
    └─> 发布事件
```

---

## 📝 关键数据结构

### ConsensusInput

```csharp
public sealed record ConsensusInput(
    string SessionId,
    string RunId,
    SraDagSnapshot Current,           // 当前 DAG snapshot
    SraDagMutation Candidate,        // dag_builder 生成的 candidate
    string? MaterialsContext = null, // Materials context（用于 verifier-quorum）
    string? ProviderName = null,
    int? ConsensusK = null,          // 共识票数（maker 模式）
    int? MaxRounds = null,           // 最大轮数（maker 模式）
    int? WorkerCount = null,
    int? MaxDepth = null,
    IProgress<ReasoningProgress>? Progress = null);
```

### ConsensusResult

```csharp
public sealed record ConsensusResult(
    bool Ok,                          // 是否通过
    bool Blocked,                     // 是否被阻止
    string Workflow,                  // 使用的 workflow ("verifier-quorum" / "maker")
    SraDagMutation? Mutation,        // 验证后的 mutation（如果通过）
    IReadOnlyList<string> RedFlags,   // 拒绝原因列表
    string? ArtifactPath,             // Artifact 文件路径
    string? Error);                   // 错误信息
```

### DagRoundResult

```csharp
public sealed record DagRoundResult(
    bool Accepted,                    // Mutation 是否被接受
    bool Blocked,                     // 是否被阻止
    SraDagMutation? Candidate,       // 原始 candidate
    SraDagMutation? AcceptedMutation, // 接受的 mutation
    string? StagedPath,               // Staged 路径（如果被阻止）
    IReadOnlyList<string> RedFlags,   // 拒绝原因
    string? ArtifactPath);            // Artifact 路径
```

---

## 🎯 关键设计决策

### 1. 为什么需要 DAG Consensus？

- **质量保证**: 确保只有高质量、一致的知识进入 DAG
- **错误检测**: 捕获结构错误、逻辑矛盾、循环依赖等
- **规范化**: 统一节点和边的格式，移除重复项

### 2. 为什么有两种模式？

- **verifier-quorum**: 轻量级、快速、适合大多数场景
- **maker**: 重量级、更智能、适合复杂场景

### 3. 为什么需要 motivatedByPlanNodeId？

- **可追溯性**: 每个知识节点都能追溯到其来源计划
- **上下文关联**: 将知识与研究目标关联
- **审计支持**: 支持研究过程的审计和审查

### 4. 为什么空 mutation 不阻塞？

- **灵活性**: 允许 `dag_builder` 在某些情况下不生成节点
- **容错性**: 避免因空输出导致整个流程失败

---

## 🔧 配置选项

### verifier-quorum 配置

```json
{
  "Vibe": {
    "DagConsensus": {
      "VerifierCount": 3,    // Verifier 数量（默认 3）
      "Quorum": 2            // 所需通过票数（默认 2）
    }
  }
}
```

### maker 配置

```json
{
  "Vibe": {
    "DagConsensus": {
      "Mode": "maker",       // 使用 maker 模式
      "ConsensusK": 3,       // 共识票数
      "MaxRounds": 10,       // 最大轮数
      "WorkerCount": 5,      // Worker 数量
      "MaxDepth": 50         // 最大递归深度
    }
  }
}
```

---

## 📊 Artifact 文件

共识过程会生成 artifact 文件，保存在：

```
workspace/sessions/{sessionId}/artifacts/dag/consensus/
```

**文件命名**:
- verifier-quorum: `quorum_{timestamp}_{guid}.json`
- maker: `{timestamp}_{guid}.json`

**文件内容**:
- 共识配置
- 投票结果（verifier-quorum）或推理结果（maker）
- Candidate mutation
- 最终 mutation（如果通过）
- RedFlags（如果被拒绝）
- 统计信息（LLM 调用次数、token 使用等）

---

## 🐛 常见问题

### 1. Candidate 解析失败

**可能原因**:
- `dag_builder` 输出不是有效的 JSON
- JSON 格式不符合 schema
- 缺少必需字段

**解决**: 检查 `dag_builder` 的输出，确保是有效的 JSON

### 2. motivatedByPlanNodeId 无效

**可能原因**:
- Plan 节点 ID 拼写错误
- Plan 节点不存在于当前 DAG
- Active milestone 未设置

**解决**: 
- 系统会自动修复（使用 activeMilestoneId 或最佳匹配）
- 检查 Plan Context 中的 Plan 节点 ID

### 3. 共识总是拒绝

**可能原因**:
- 预检查失败（self-edge、duplicate_node_id 等）
- Verifier 投票不足（approveCount < quorum）
- RedFlags 不为空

**解决**: 
- 检查 artifact 文件中的 `precheckFlags`、`redFlags` 和投票结果
- 调整 verifier-quorum 配置（增加 VerifierCount 或降低 Quorum）

### 4. Mutation 应用失败

**可能原因**:
- DAG Store 连接失败
- Neo4j 错误
- 节点 ID 冲突

**解决**: 检查 DAG Store 日志和 Neo4j 连接

---

## 📚 相关文件

- `VibeOrchestrator.DagConsensus.cs`: 主入口和流程控制
- `DagConsensusRunner.cs`: 共识执行器（maker 模式）
- `DagConsensusRunner.Quorum.cs`: verifier-quorum 实现
- `VibeOrchestrator.Parsing.cs`: Candidate 解析逻辑
- `DagStore.cs`: DAG mutation 应用
- `workflows/maker.yaml`: maker workflow 定义

---

## 💡 最佳实践

1. **使用 verifier-quorum 作为默认**: 快速、轻量级，适合大多数场景
2. **检查 artifact 文件**: 了解共识过程的详细信息
3. **验证 motivatedByPlanNodeId**: 确保 Knowledge 节点正确链接到 Plan 节点
4. **监控 RedFlags**: 识别常见问题模式，优化 `dag_builder` 的输出
