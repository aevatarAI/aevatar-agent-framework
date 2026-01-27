# Plan 节点不存在问题诊断

## 📋 Plan 节点创建流程

### 1. Brief 阶段（`TryGetBriefAsync`）

**位置**: `VibeOrchestrator.cs` → `RunStepBestEffortAsync` (第 170-243 行)

**关键步骤**:
1. 调用 `TryGetBriefAsync` 生成 brief
2. 如果 brief 生成成功，保存 brief
3. 调用 `BuildMilestonesPlanDagMutation` 创建 mutation
4. 调用 `ApplyMutationAsync` 应用 mutation

**关键条件**:
```csharp
// VibeOrchestrator.PlanDag.cs 第 74-77 行
if (string.IsNullOrWhiteSpace(sessionId) || string.IsNullOrWhiteSpace(runId))
    return null;
if (brief == null || brief.Milestones.Count == 0)
    return null;  // ⚠️ 如果 brief 为 null 或 milestones 为空，返回 null
```

### 2. BuildMilestonesPlanDagMutation

**位置**: `VibeOrchestrator.PlanDag.cs` (第 68-120 行)

**返回 null 的情况**:
1. `sessionId` 或 `runId` 为空
2. `brief` 为 null
3. `brief.Milestones.Count == 0` ⚠️ **最可能的原因**

### 3. ApplyMutationAsync

**位置**: `DagStore.cs` → `ApplyMutationAsync` (第 109-350 行)

**Plan 节点创建条件**:
```csharp
// DagStore.cs 第 154-157 行
var isMilestoneMutation = mutation.Labels.TryGetValue("planKind", out var planKind) &&
                          string.Equals(planKind, "milestone", StringComparison.OrdinalIgnoreCase);

if (n.Kind == SraDagNodeKind.Plan && isMilestoneMutation)
{
    // 创建 Plan 节点
}
```

## 🔍 诊断步骤

### Step 1: 检查 Brief 是否生成

**在后端终端输出中搜索**:
```
[VibeOrchestrator] research_assistant brief
```

**如果没有看到**:
- Brief 阶段可能未执行
- 或 Brief 阶段执行失败

### Step 2: 检查 Brief 是否包含 Milestones

**在后端终端输出中搜索**:
```
Applying milestones DAG mutation: sessionId=..., nodeCount=X
```

**如果没有看到这条日志**:
- `BuildMilestonesPlanDagMutation` 返回了 null
- 可能原因：
  1. Brief 为 null
  2. Brief.Milestones.Count == 0 ⚠️ **最可能**

### Step 3: 检查 Mutation 是否应用

**在后端终端输出中搜索**:
```
[DagStore] ApplyMutationAsync starting: dagId=global, nodeCount=X
[DagStore] Creating PlanNode: nodeId=...
```

**如果没有看到**:
- Mutation 可能未应用
- 或 ApplyMutationAsync 执行失败

### Step 4: 检查是否有错误

**在后端终端输出中搜索**:
```
[VibeOrchestrator] research_assistant brief failed
[DagStore] Failed to query Neo4j
[DagStore] Failed to upsert dag node
```

## 🎯 可能的原因

### 原因 1: Brief 未生成 ⚠️ **最可能**

**症状**:
- 没有看到 `[VibeOrchestrator] research_assistant brief` 相关日志
- 或看到 `research_assistant brief failed`

**解决**:
1. 检查 `research_assistant` 的 LLM 调用是否成功
2. 检查 Brief JSON 解析是否成功
3. 检查 Brief 的 JSON 格式是否符合要求

### 原因 2: Brief.Milestones 为空 ⚠️ **最可能**

**症状**:
- Brief 生成成功
- 但没有看到 `Applying milestones DAG mutation` 日志
- 或看到 `nodeCount=0`

**原因**:
- `research_assistant` 返回的 brief JSON 中 `milestones` 数组为空
- 或 `milestones` 字段不存在

**解决**:
1. 检查 `research_assistant` 的 system prompt 是否要求输出 milestones
2. 检查 Brief JSON 中的 `milestones` 字段
3. 确认 `milestones` 数组是否包含至少一个 milestone

### 原因 3: BuildMilestonesPlanDagMutation 返回 null

**症状**:
- Brief 生成成功且包含 milestones
- 但没有看到 `Applying milestones DAG mutation` 日志

**原因**:
- `sessionId` 或 `runId` 为空（不太可能）
- Brief 为 null（不太可能，因为已经生成）

### 原因 4: ApplyMutationAsync 执行失败

**症状**:
- 看到 `Applying milestones DAG mutation` 日志
- 但没有看到 `[DagStore] Creating PlanNode` 日志

**原因**:
1. Neo4j 连接失败
2. Neo4j 写入权限问题
3. Mutation 中的节点 ID 格式错误

## 🔧 检查清单

- [ ] Brief 是否生成成功？
- [ ] Brief 是否包含 milestones？
- [ ] `BuildMilestonesPlanDagMutation` 是否返回非 null？
- [ ] `ApplyMutationAsync` 是否被调用？
- [ ] Plan 节点是否成功创建？
- [ ] 是否有错误消息？

## 💡 快速检查命令

```bash
# 检查最新的 session
LATEST_SESSION=$(curl -s http://localhost:5678/api/sessions | jq -r '.sessions[0].sessionId')
echo "Session: $LATEST_SESSION"

# 检查 DAG 中的 Plan 节点
curl -s http://localhost:5678/api/dag/global | jq '.dag.nodes[] | select(.kind == "Plan") | {id, label, planStatus}'
```

## 📝 日志关键词

请在后端终端输出中搜索以下关键词：

1. **Brief 生成**:
   - `research_assistant brief`
   - `brief failed`
   - `brief_updated`

2. **Milestone Mutation**:
   - `Applying milestones DAG mutation`
   - `Milestones DAG mutation applied`
   - `milestones_plan_dag_written`

3. **Plan 节点创建**:
   - `ApplyMutationAsync starting`
   - `Creating PlanNode`
   - `PlanNode created successfully`

4. **错误**:
   - `Failed to`
   - `error`
   - `exception`
