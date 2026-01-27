# DAG 节点创建调试指南

## 🔍 问题诊断

如果 DAG 返回 `nodes=0, edges=0`，请按以下步骤检查：

### 1. 检查日志中的关键步骤

运行程序后，查找以下日志消息：

#### ✅ Brief Generation 阶段（Plan 节点）

```
[VibeOrchestrator] Applying milestones DAG mutation: sessionId=..., nodeCount=X
[DagStore] ApplyMutationAsync starting: dagId=global, nodeCount=X
[DagStore] Creating PlanNode: nodeId=..., label=...
[DagStore] PlanNode created successfully: nodeId=...
```

**如果没有这些日志**：
- Brief generation 可能失败
- 或者 `BuildMilestonesPlanDagMutation` 返回 `null`

#### ✅ Dag Builder 阶段

```
[vibe.dag_builder] Step started
[DagStore] ApplyMutationAsync starting: dagId=global, nodeCount=X
[DagStore] Upserting KnowledgeNode: nodeId=..., type=..., label=...
[DagStore] KnowledgeNode upserted successfully: nodeId=...
```

**如果没有这些日志**：
- `dag_builder` 可能没有被执行
- 或者 `dag_builder` 的输出为空/无法解析
- 或者 `RunDagApplyAsync` 没有被调用

#### ✅ DAG Consensus 阶段

```
[vibe.dag_consensus] Step started
[DagStore] ApplyMutationAsync starting: dagId=global, nodeCount=X
```

**如果没有这些日志**：
- `RunDagApplyAsync` 可能没有被调用
- 或者 `dag_builder` 的输出解析失败

### 2. 检查 dag_builder 的输出

查看程序日志中 `dag_builder` 的原始输出：

```
```json
{
  "mutationId": "...",
  "nodes": [...],
  "edges": [...]
}
```
```

**如果输出为空或格式错误**：
- `dag_builder` 的 LLM 调用可能失败
- 或者输出格式不符合预期

### 3. 检查 Neo4j 连接

```bash
# 检查 Bolt 端口
nc -zv localhost 7687

# 检查 Neo4j 状态
JAVA_HOME=$(brew --prefix openjdk@21)/libexec/openjdk.jdk/Contents/Home
export JAVA_HOME="$JAVA_HOME"
export PATH="$JAVA_HOME/bin:$PATH"
NEO4J_HOME=$(brew --prefix neo4j)
cd "$NEO4J_HOME/libexec"
./bin/neo4j status
```

### 4. 检查节点是否真的被创建到 Neo4j

查看日志中是否有 Neo4j 错误：

```
[DagStore] Failed to query Neo4j for global DAG: dagId=global, error=...
```

**如果有错误**：
- Neo4j 连接问题
- 或者节点创建失败

### 5. 检查 worker 执行顺序

确认 `dag_builder` 是否在 worker 列表中：

```
Workers: planner, reasoner, verifier, dag_builder
```

**如果 `dag_builder` 不在列表中**：
- Plan 可能没有包含 `dag_builder`
- 或者 worker 被过滤掉了

---

## 🛠️ 常见问题及解决方案

### 问题 1: Brief Generation 没有创建 Plan 节点

**可能原因**:
- Brief generation 失败
- `BuildMilestonesPlanDagMutation` 返回 `null`
- Milestones 为空

**解决方案**:
1. 检查 Brief generation 的日志
2. 确认 `saved.Milestones.Count > 0`
3. 检查 `BuildMilestonesPlanDagMutation` 的实现

### 问题 2: Dag_builder 输出为空

**可能原因**:
- `dag_builder` 的 LLM 调用失败
- `dag_builder` 的输出格式不正确
- Worker outputs 为空

**解决方案**:
1. 检查 `dag_builder` 的日志输出
2. 确认 `outputs["dag_builder"]` 不为空
3. 检查 `BuildDagBuilderMessage` 构建的 prompt

### 问题 3: Dag_builder 输出无法解析

**可能原因**:
- JSON 格式错误
- 缺少必需字段（`mutationId`, `nodes`, `edges`）
- JSON 解析异常

**解决方案**:
1. 查看 `dag_builder` 的原始输出
2. 检查 `TryParseDagBuilderCandidate` 的解析逻辑
3. 确认输出符合预期格式

### 问题 4: RunDagApplyAsync 返回空 mutation

**可能原因**:
- `candidate == null`
- `candidate.UpsertNodes.Count == 0`
- `candidate.UpsertEdges.Count == 0`

**解决方案**:
1. 检查 `RunDagApplyAsync` 的日志
2. 确认 `candidate` 不为空
3. 检查 `TryParseDagBuilderCandidate` 的返回值

### 问题 5: ApplyMutationAsync 没有创建节点

**可能原因**:
- Neo4j 连接失败
- 节点创建异常被捕获
- `client.CreatePlanNodeAsync` 或 `client.UpsertNodeAsync` 失败

**解决方案**:
1. 检查 Neo4j 连接状态
2. 查看异常日志
3. 确认节点创建逻辑正确执行

---

## 📋 调试检查清单

- [ ] Brief Generation 成功执行
- [ ] Plan Phase 成功执行
- [ ] Worker Phase 包含 `dag_builder`
- [ ] `dag_builder` 成功执行并产生输出
- [ ] `dag_builder` 输出格式正确（JSON）
- [ ] `TryParseDagBuilderCandidate` 成功解析
- [ ] `RunDagApplyAsync` 被调用
- [ ] `ApplyMutationAsync` 被调用
- [ ] Neo4j 连接正常
- [ ] 节点创建到 Neo4j 成功
- [ ] `BuildSnapshotFromGraphAsync` 成功查询节点

---

## 🔧 添加更多日志

如果需要更详细的调试信息，可以在以下位置添加日志：

1. **`VibeOrchestrator.DagConsensus.cs`** (第 45-46 行)
   ```csharp
   var candidateText = outputs.TryGetValue("dag_builder", out var x) ? x : string.Empty;
   _logger.LogInformation("[DagConsensus] dag_builder output length: {Length}", candidateText.Length);
   var candidate = TryParseDagBuilderCandidate(...);
   _logger.LogInformation("[DagConsensus] Parsed candidate: nodes={NodeCount}, edges={EdgeCount}", 
       candidate?.UpsertNodes.Count ?? 0, candidate?.UpsertEdges.Count ?? 0);
   ```

2. **`DagStore.cs`** (第 138-200 行)
   ```csharp
   foreach (var n in mutation.UpsertNodes)
   {
       _logger.LogInformation("[DagStore] Processing node: id={Id}, kind={Kind}", n.Id, n.Kind);
       // ... existing code ...
   }
   ```

3. **`VibeOrchestrator.ExecuteOneRound.Parts.cs`** (第 672 行)
   ```csharp
   outputs[agent] = await RunDagBuilderAsync(...);
   _logger.LogInformation("[WorkerPhase] dag_builder output: length={Length}", outputs[agent]?.Length ?? 0);
   ```

---

## 📝 下一步

1. 运行程序并收集完整日志
2. 检查上述关键步骤的日志
3. 根据日志定位问题所在
4. 应用相应的解决方案
