# DAG 为空（nodes=0）的解决方案

## 🔍 当前状态

- ✅ Backend 正在运行
- ✅ Neo4j 服务可访问
- ✅ 有 13 个会话（说明系统已使用过）
- ❌ DAG 节点数为 0

## 🎯 可能的原因

### 1. 还没有运行过 Research Round ⚠️ **最可能**

**症状**：
- DAG 为空（nodes=0）
- 没有看到任何节点创建的日志

**解决**：
1. 在前端发送一条消息触发 research round
2. 等待 research round 完成
3. 检查 DAG API：`curl http://localhost:5678/api/dag/global | jq '.dag.nodes | length'`

### 2. Neo4j 密码错误

**症状**：
- 程序可以启动
- 但查询 Neo4j 时失败
- 日志中看到：`[DagStore] Failed to query Neo4j for global DAG: dagId=global, error=...`

**解决**：
1. 确认 Neo4j 密码是否正确
2. 修改 `boot.sh` 第 144 行，设置正确的密码
3. 或运行前设置：`export NEO4J_PASSWORD="your_actual_password"`
4. 重启程序

### 3. Dag_builder 输出为空或无法解析

**症状**：
- Research round 运行了
- 但日志中看到：`[DagConsensus] dag_builder output length: 0`
- 或：`[DagConsensus] Failed to parse dag_builder output`

**解决**：
1. 检查 `dag_builder` 是否被执行
2. 查看 `dag_builder` 的原始输出
3. 确认 LLM 调用是否成功

### 4. 节点创建失败

**症状**：
- 日志中看到：`[DagConsensus] Parsed candidate: nodes=X, edges=Y`（X > 0）
- 但日志中看到：`[DagStore] Failed to upsert dag node`

**解决**：
1. 查看具体错误信息
2. 检查 Neo4j 权限
3. 确认 Neo4j 连接是否正常

## 📋 诊断步骤

### Step 1: 确认是否运行过 Research Round

**检查方法**：
1. 查看程序日志，查找 `[VibeOrchestrator]` 或 `[vibe.dag_builder]` 相关消息
2. 如果没有看到这些消息，说明还没有运行过 research round

**如果还没有运行**：
- 在前端发送一条消息，例如："研究一下素数分解的唯一性"
- 等待 research round 完成
- 再次检查 DAG API

### Step 2: 检查 Neo4j 连接

**检查方法**：
查看程序日志中是否有：
```
[DagStore] Failed to query Neo4j for global DAG: dagId=global, error=...
```

**如果有错误**：
1. 确认 Neo4j 密码是否正确
2. 测试连接：
   ```bash
   # 如果安装了 cypher-shell
   echo "RETURN 1 as test;" | cypher-shell -u neo4j -p your_password -a bolt://localhost:7687
   ```

### Step 3: 检查 Dag_builder 输出

**检查方法**：
查看程序日志中的 `[DagConsensus]` 消息：
- `dag_builder output length: X` - 输出长度
- `Parsed candidate: nodes=X, edges=Y` - 解析结果
- `Failed to parse dag_builder output` - 解析失败

**如果输出为空**：
- 检查 `dag_builder` 是否被执行
- 查看 `dag_builder` 的完整输出

### Step 4: 检查节点创建

**检查方法**：
查看程序日志中的 `[DagStore]` 消息：
- `ApplyMutationAsync starting: dagId=global, nodeCount=X` - 开始创建节点
- `Upserting KnowledgeNode: nodeId=...` - 正在创建节点
- `Failed to upsert dag node` - 创建失败

## 🚀 快速验证

### 1. 运行 Research Round

在前端发送一条消息，触发 research round。

### 2. 查看日志

查找以下关键消息：

**正常流程**：
```
[VibeOrchestrator] Applying milestones DAG mutation: sessionId=..., nodeCount=X
[DagStore] ApplyMutationAsync starting: dagId=global, nodeCount=X
[DagStore] Creating PlanNode: nodeId=...
[vibe.dag_builder] Step started
[DagConsensus] dag_builder output length: X
[DagConsensus] Parsed candidate: nodes=X, edges=Y
[DagStore] Upserting KnowledgeNode: nodeId=...
[DagStore] BuildSnapshotFromGraphAsync (global): knowledgeNodes=X, planNodes=Y
```

**错误情况**：
```
[DagStore] Failed to query Neo4j for global DAG: dagId=global, error=...
[DagConsensus] Failed to parse dag_builder output
[DagConsensus] Parsed candidate is empty
[DagStore] Failed to upsert dag node
```

### 3. 验证 DAG API

```bash
curl http://localhost:5678/api/dag/global | jq '.dag.nodes | length'
```

应该返回大于 0 的数字。

## 💡 建议

1. **首先确认是否运行过 research round**
   - 如果没有，先发送消息触发 research round
   - 这是最可能的原因

2. **如果运行过但 DAG 仍为空**
   - 查看程序日志，查找错误消息
   - 特别关注 `[DagStore]` 和 `[DagConsensus]` 相关的日志

3. **分享日志**
   - 如果问题仍然存在，请分享程序日志
   - 特别是与 DAG 相关的所有消息
