# DAG 节点创建流程诊断指南

## 🔍 问题：Research round 运行了但没有创建节点

## 📋 DAG 节点创建流程

### 1. Plan 节点创建（Milestone/Brief 阶段）

**位置**: `VibeOrchestrator.cs` → `TryGetBriefAsync` → `BuildMilestonesPlanDagMutation`

**触发时机**: `research_assistant` 生成 brief 时

**日志**:
```
[DagStore] ApplyMutationAsync starting: dagId=global, nodeCount=X
[DagStore] Creating PlanNode: nodeId=...
```

**检查方法**:
```bash
curl http://localhost:5678/api/dag/global | jq '.dag.nodes[] | select(.kind == "Plan") | .id'
```

### 2. Knowledge 节点创建（Worker 阶段）

**位置**: `VibeOrchestrator.DagConsensus.cs` → `RunDagApplyAsync`

**触发时机**: `dag_builder` 执行完成后

**关键步骤**:

#### Step 1: dag_builder 执行
- **位置**: `VibeOrchestrator.Workers.cs` → `RunDagBuilderAsync`
- **日志**: `[vibe.dag_builder] Step started` / `Step finished`
- **输出**: JSON 格式的 DAG candidate

#### Step 2: 解析 dag_builder 输出
- **位置**: `VibeOrchestrator.DagConsensus.cs` → `TryParseDagBuilderCandidate`
- **日志**:
  - `[DagConsensus] dag_builder output length: X`
  - `[DagConsensus] Failed to parse dag_builder output` ❌
  - `[DagConsensus] Parsed candidate: nodes=X, edges=Y` ✅

**可能失败的原因**:
1. `dag_builder` 输出为空 → `output length: 0`
2. 输出不是有效的 JSON → `Failed to parse`
3. JSON 格式不符合 `DagCandidateJson` schema → `Failed to parse`

#### Step 3: 检查 candidate 是否为空
- **位置**: `VibeOrchestrator.DagConsensus.cs` (第 70-76 行)
- **日志**: `[DagConsensus] Parsed candidate is empty (no nodes or edges)`
- **原因**: 解析成功但 `nodes` 和 `edges` 数组为空

#### Step 4: 共识验证
- **位置**: `DagConsensusRunner.cs` → `RunAsync`
- **模式**: `maker` 或 `verifier-quorum`
- **日志**:
  - `[DagConsensus] dag consensus error` ❌
  - `[DagConsensus] DAG Consensus (blocked)` ❌

**可能失败的原因**:
1. Maker workflow 执行失败
2. Verifier-quorum 投票未通过
3. Red flags 被触发

#### Step 5: 应用 Mutation
- **位置**: `DagStore.cs` → `ApplyMutationAsync`
- **日志**:
  - `[DagStore] ApplyMutationAsync starting: dagId=global, nodeCount=X`
  - `[DagStore] Upserting KnowledgeNode: nodeId=...` ✅

**可能失败的原因**:
1. Neo4j 连接失败
2. Neo4j 写入权限问题
3. 节点 ID 冲突

## 🔧 诊断步骤

### Step 1: 检查 Plan 节点是否存在

```bash
curl http://localhost:5678/api/dag/global | jq '.dag.nodes[] | select(.kind == "Plan") | {id, label, planStatus}'
```

**如果没有 Plan 节点**:
- Brief 阶段可能未执行
- 或 Brief 阶段执行失败
- 检查日志: `[VibeOrchestrator] Applying milestones DAG mutation`

### Step 2: 检查 dag_builder 是否执行

在后端终端输出中搜索：
```
[vibe.dag_builder] Step started
[vibe.dag_builder] Step finished
```

**如果没有这些日志**:
- `dag_builder` 可能不在 worker 列表中
- 检查 `research_assistant` 的 plan 输出

### Step 3: 检查 dag_builder 输出

在后端终端输出中搜索：
```
[DagConsensus] dag_builder output length: X
```

**如果 `length: 0`**:
- `dag_builder` 输出为空
- 检查 `dag_builder` 的 LLM 调用是否成功

**如果有长度但解析失败**:
- 检查 `[DagConsensus] Failed to parse dag_builder output`
- `dag_builder` 输出可能不是有效的 JSON
- 检查 `dag_builder` 的 system prompt 是否要求输出 JSON

### Step 4: 检查解析结果

在后端终端输出中搜索：
```
[DagConsensus] Parsed candidate: nodes=X, edges=Y
```

**如果 `nodes=0, edges=0`**:
- 解析成功但 candidate 为空
- 检查 `dag_builder` 的输出 JSON 结构
- 确认 `nodes` 和 `edges` 数组是否存在且非空

### Step 5: 检查共识验证

在后端终端输出中搜索：
```
[DagConsensus] dag consensus error
[DagConsensus] DAG Consensus (blocked)
```

**如果有错误**:
- 检查 `DagConsensusRunner` 的配置
- 检查 maker workflow 或 verifier-quorum 的执行日志

### Step 6: 检查节点创建

在后端终端输出中搜索：
```
[DagStore] ApplyMutationAsync starting
[DagStore] Upserting KnowledgeNode
```

**如果没有这些日志**:
- 共识验证未通过
- 或 candidate 为空

## 🎯 常见问题解决方案

### 问题 1: dag_builder 输出为空

**原因**:
- `dag_builder` 的 LLM 调用失败
- `dag_builder` 不在 worker 列表中

**解决**:
1. 检查 `dag_builder` 是否在 plan 的 workers 中
2. 检查 `dag_builder` 的 provider 配置
3. 检查 LLM API 调用是否成功

### 问题 2: dag_builder 输出无法解析

**原因**:
- 输出不是有效的 JSON
- JSON 格式不符合 `DagCandidateJson` schema

**解决**:
1. 检查 `dag_builder` 的 system prompt 是否要求输出 JSON
2. 检查 `dag_builder` 的输出格式
3. 查看 `dag_builder` 的原始输出（如果有日志）

### 问题 3: Parsed candidate 为空

**原因**:
- JSON 解析成功但 `nodes` 和 `edges` 数组为空
- `dag_builder` 没有提取到知识

**解决**:
1. 检查 `dag_builder` 的输入（worker outputs）
2. 检查 `dag_builder` 的 system prompt 是否明确要求创建节点
3. 确认 worker outputs 中是否有可提取的知识

### 问题 4: 共识验证失败

**原因**:
- Maker workflow 执行失败
- Verifier-quorum 投票未通过
- Red flags 被触发

**解决**:
1. 检查 `DagConsensusRunner` 的配置
2. 检查 maker workflow 的执行日志
3. 检查 red flags 的原因

### 问题 5: 没有 Plan 节点

**原因**:
- Brief 阶段未执行
- Brief 阶段执行失败
- Milestone DAG mutation 未应用

**解决**:
1. 检查 `research_assistant` brief 是否生成
2. 检查 `BuildMilestonesPlanDagMutation` 是否被调用
3. 检查日志: `[VibeOrchestrator] Applying milestones DAG mutation`

## 📝 检查清单

- [ ] Plan 节点是否存在？
- [ ] `dag_builder` 是否执行？
- [ ] `dag_builder` 输出是否为空？
- [ ] `dag_builder` 输出是否能解析？
- [ ] Parsed candidate 是否为空？
- [ ] 共识验证是否通过？
- [ ] `ApplyMutationAsync` 是否被调用？
- [ ] Neo4j 连接是否正常？
