# DAG Consensus 更新问题诊断

## 🔍 问题描述

当 DAG Consensus 结束后，看到有假设被认证是正确的，但没有更新到 DAG 图中。

---

## 🔄 DAG 更新流程

### 1. DAG Consensus 通过

**代码位置**: `VibeOrchestrator.DagConsensus.cs` → `RunDagApplyAsync` (第 109-115 行)

```csharp
if (!consensus.Ok || consensus.Mutation == null)
{
    // Blocked - 不会更新 DAG
    return new DagRoundResult(false, true, candidate, null, ...);
}
```

**检查点**:
- ✅ `consensus.Ok == true`
- ✅ `consensus.Mutation != null`

---

### 2. 应用 Mutation

**代码位置**: `VibeOrchestrator.DagConsensus.cs` → `RunDagApplyAsync` (第 117-128 行)

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

**关键步骤**:
1. 获取 DAG ID (`session.EffectiveDagId`)
2. 添加共识元数据
3. 调用 `ApplyMutationAsync` 应用 mutation

---

### 3. 节点写入 Neo4j

**代码位置**: `DagStore.cs` → `ApplyMutationAsync` (第 138-197 行)

```csharp
foreach (var n in mutation.UpsertNodes)
{
    // ...
    if (n.Kind == SraDagNodeKind.Plan && isMilestoneMutation)
    {
        // PlanNode: 创建 Plan 节点
        await client.CreatePlanNodeAsync(...);
    }
    else
    {
        // KnowledgeNode: 使用 UpsertNodeAsync
        await client.UpsertNodeAsync(
            nodeId: id,
            nodeType: MapDagNodeType(n.Type),  // 关键：类型映射
            owner: ...,
            coreDescription: label,
            detailedDescription: detail,
            proof: proof,
            ...
        );
    }
}
```

**类型映射** (第 821-829 行):
```csharp
private static KnowledgeNodeType MapDagNodeType(SraDagNodeType t) =>
    t switch
    {
        SraDagNodeType.Axiom => KnowledgeNodeType.MathAxiom,
        SraDagNodeType.Theorem => KnowledgeNodeType.MathTheorem,
        SraDagNodeType.Hypothesis => KnowledgeNodeType.ResearchHypothesis,  // ← 假设映射
        SraDagNodeType.Assumption => KnowledgeNodeType.Note,
        _ => KnowledgeNodeType.Generic
    };
```

---

## 🐛 可能的原因

### 原因 1: 假设被转换为定理

**问题**: 当假设被验证为正确时，`dag_builder` 或 `maker` 可能会将其类型从 `"hypothesis"` 改为 `"theorem"`。

**原因**:
- 一个被验证的假设实际上就是一个定理
- `dag_builder` 的提示词要求根据 verifier 的分类设置类型
- `maker` 可能会规范化节点类型

**检查方法**:
1. 查看 consensus artifact 文件: `workspace/sessions/{sessionId}/artifacts/dag/consensus/*.json`
2. 检查 `parsed.nodes[].type` 字段
3. 如果类型是 `"theorem"` 而不是 `"hypothesis"`，这是预期的行为

**解决方案**:
- 这是**正常行为**：验证后的假设应该被标记为定理
- 如果希望保留假设类型，需要修改 `dag_builder` 或 `maker` 的提示词

---

### 原因 2: 节点没有被提取

**问题**: `dag_builder` 没有从 verifier 输出中提取假设节点。

**可能原因**:
- `dag_builder` 的输出为空或格式错误
- verifier 输出中没有明确标记假设为 "VERIFIED"
- `dag_builder` 没有正确解析 verifier 输出

**检查方法**:
1. 查看 `dag_builder` 的输出: `outputs["dag_builder"]`
2. 检查是否包含假设节点
3. 查看 consensus artifact 文件中的 `candidate.nodeCount`

**解决方案**:
- 检查 `dag_builder` 的提示词是否正确
- 确保 verifier 输出明确标记了验证状态
- 检查 `dag_builder` 的输出格式是否符合 JSON schema

---

### 原因 3: Consensus 被阻止

**问题**: Consensus 通过了，但 mutation 为空或被阻止。

**检查方法**:
1. 查看日志: `[DagConsensus] Parsed candidate: nodes={NodeCount}, edges={EdgeCount}`
2. 如果 `NodeCount == 0`，说明 candidate 为空
3. 检查 consensus artifact 文件中的 `status` 字段

**解决方案**:
- 如果 candidate 为空，检查 `dag_builder` 的输出
- 如果 consensus 被阻止，查看 `redFlags` 列表

---

### 原因 4: 节点类型映射错误

**问题**: 节点类型在映射过程中丢失或错误。

**检查方法**:
1. 查看 consensus artifact 文件中的 `parsed.nodes[].type`
2. 检查 `ApplyMutationAsync` 日志中的 `MapDagNodeType` 调用
3. 确认节点类型是否正确映射到 `KnowledgeNodeType`

**解决方案**:
- 检查 `ParseNodeType` 方法是否正确解析类型字符串
- 确认 `MapDagNodeType` 方法是否正确映射类型

---

### 原因 5: Neo4j 写入失败

**问题**: 节点写入 Neo4j 时失败，但错误被捕获。

**检查方法**:
1. 查看日志: `[DagStore] Failed to upsert dag node {NodeId}: {Error}`
2. 检查 Neo4j 连接状态
3. 确认节点 ID 是否有效

**解决方案**:
- 检查 Neo4j 连接配置
- 确认节点 ID 格式正确
- 检查 Neo4j 权限设置

---

### 原因 6: 前端没有刷新

**问题**: DAG 已经更新，但前端没有刷新显示。

**检查方法**:
1. 检查 Neo4j 数据库，确认节点是否存在
2. 查看 `aevatar.vibe.dag_updated` 事件是否发布
3. 检查前端是否监听了该事件

**解决方案**:
- 手动刷新前端页面
- 检查前端事件监听器
- 确认 SSE 连接正常

---

## 🔧 诊断步骤

### Step 1: 检查 Consensus 结果

**位置**: `workspace/sessions/{sessionId}/artifacts/dag/consensus/*.json`

**检查项**:
```json
{
  "status": "accepted",  // 应该是 "accepted"
  "parsed": {
    "nodes": [
      {
        "id": "...",
        "type": "hypothesis" | "theorem",  // 检查类型
        "label": "...",
        "proof": "..."
      }
    ]
  }
}
```

---

### Step 2: 检查日志

**关键日志**:
```
[DagConsensus] Parsed candidate: nodes={NodeCount}, edges={EdgeCount}
[DagStore] ApplyMutationAsync starting: dagId={DagId}, nodeCount={NodeCount}
[DagStore] Upserting KnowledgeNode: nodeId={NodeId}, type={Type}
[DagStore] KnowledgeNode upserted successfully: nodeId={NodeId}
```

**如果看到错误**:
```
[DagStore] Failed to upsert dag node {NodeId}: {Error}
```

---

### Step 3: 检查 Neo4j

**查询节点**:
```cypher
MATCH (n)
WHERE n.id CONTAINS "hyp" OR n.type = "ResearchHypothesis"
RETURN n.id, n.type, n.coreDescription, n.detailedDescription
LIMIT 20
```

**查询最近的更新**:
```cypher
MATCH (n)
WHERE n.updatedAt IS NOT NULL
RETURN n.id, n.type, n.updatedAt
ORDER BY n.updatedAt DESC
LIMIT 10
```

---

### Step 4: 检查事件

**事件名称**: `aevatar.vibe.dag_updated`

**事件内容**:
```json
{
  "sessionId": "...",
  "dagId": "...",
  "runId": "...",
  "mutationId": "...",
  "nodes": 5,  // 检查节点数量
  "edges": 3,
  "consensusWorkflow": "maker" | "verifier-quorum",
  "updatedAt": "2025-01-16T..."
}
```

---

## 💡 常见场景

### 场景 1: 假设被转换为定理

**现象**: Consensus 通过了，但 DAG 中没有 hypothesis 节点，只有 theorem 节点。

**原因**: 这是**正常行为**。验证后的假设应该被标记为定理。

**解决方案**: 
- 这是预期的行为，不需要修改
- 如果需要保留假设类型，修改 `dag_builder` 或 `maker` 的提示词

---

### 场景 2: 节点没有被提取

**现象**: Consensus artifact 中 `parsed.nodes` 为空或没有假设节点。

**原因**: `dag_builder` 没有从 verifier 输出中提取假设。

**解决方案**:
1. 检查 `dag_builder` 的输出
2. 确认 verifier 输出中是否明确标记了假设
3. 修改 `dag_builder` 的提示词，明确要求提取假设

---

### 场景 3: Consensus 被阻止

**现象**: Consensus artifact 中 `status` 为 `"blocked"`。

**原因**: Consensus 检查失败，mutation 被阻止。

**解决方案**:
1. 查看 `redFlags` 列表
2. 检查 consensus artifact 文件中的错误信息
3. 修复导致阻止的问题

---

### 场景 4: Neo4j 写入失败

**现象**: 日志中显示 `Failed to upsert dag node`。

**原因**: Neo4j 连接失败或节点 ID 无效。

**解决方案**:
1. 检查 Neo4j 连接配置
2. 确认节点 ID 格式正确
3. 检查 Neo4j 权限设置

---

## 🎯 快速检查清单

- [ ] Consensus artifact 文件存在且 `status` 为 `"accepted"`
- [ ] `parsed.nodes` 中包含假设节点（或验证后的定理节点）
- [ ] 日志中显示 `ApplyMutationAsync starting` 且 `nodeCount > 0`
- [ ] 日志中显示 `KnowledgeNode upserted successfully`
- [ ] Neo4j 中查询到对应的节点
- [ ] `aevatar.vibe.dag_updated` 事件已发布
- [ ] 前端已刷新显示

---

## 📝 调试命令

### 查看最近的 Consensus Artifacts

```bash
find workspace/sessions -path "*/consensus/*.json" -type f -exec ls -lt {} + | head -5
```

### 查看 Artifact 内容

```bash
cat workspace/sessions/{sessionId}/artifacts/dag/consensus/{latest_file}.json | jq '.parsed.nodes[] | select(.type == "hypothesis" or .type == "theorem")'
```

### 检查 Neo4j 节点

```bash
# 使用 check_dag.sh
./check_dag.sh
```

---

## 🔍 详细日志位置

1. **Consensus Artifacts**: `workspace/sessions/{sessionId}/artifacts/dag/consensus/*.json`
2. **服务器日志**: 查看应用日志中的 `[DagConsensus]` 和 `[DagStore]` 前缀
3. **Neo4j 日志**: 检查 Neo4j 日志文件（如果启用）

---

## 💡 总结

**最可能的原因**:
1. ✅ **假设被转换为定理**（正常行为）
2. ⚠️ **节点没有被提取**（需要检查 `dag_builder` 输出）
3. ⚠️ **Consensus 被阻止**（需要查看 `redFlags`）
4. ⚠️ **Neo4j 写入失败**（需要检查连接和日志）

**建议**:
1. 首先检查 consensus artifact 文件
2. 确认节点类型是 `"theorem"` 还是 `"hypothesis"`
3. 检查 Neo4j 中是否存在对应的节点
4. 查看日志中的错误信息

---

*最后更新: 2025-01-16*
