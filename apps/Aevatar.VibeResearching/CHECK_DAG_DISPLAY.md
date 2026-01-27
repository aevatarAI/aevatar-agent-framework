# DAG 图显示检查清单

## ✅ 当前配置状态

根据验证脚本结果：

1. ✅ **boot.sh 配置**：所有必需的环境变量都已添加
2. ✅ **Neo4j 服务**：正在运行（Bolt 端口可访问）
3. ✅ **Java 环境**：已配置
4. ✅ **语法检查**：boot.sh 语法正确
5. ⚠️ **Neo4j 密码**：使用默认值 `password`（需要确认是否为实际密码）

## 🔍 关键检查点

### 1. Neo4j 密码验证 ⚠️ **最重要**

**问题**：如果密码错误，会发生什么？

- ✅ **程序可以启动**（Driver 创建成功，因为密码不为空）
- ❌ **查询会失败**（连接时认证失败）
- ❌ **DAG 返回空结果**（异常被捕获，返回空快照）

**验证方法**：

```bash
# 测试 Neo4j 连接（使用 boot.sh 中的密码）
export NEO4J_URI="bolt://localhost:7687"
export NEO4J_USERNAME="neo4j"
export NEO4J_PASSWORD="password"  # 使用 boot.sh 中的默认值

# 如果安装了 cypher-shell，可以测试连接
echo "RETURN 1 as test;" | cypher-shell -u neo4j -p password -a bolt://localhost:7687 2>&1
```

**如果连接失败**：
- 修改 `boot.sh` 第 144 行，将 `password` 替换为实际密码
- 或运行前设置：`export NEO4J_PASSWORD="your_actual_password"`

### 2. 程序启动检查

运行 `./boot.sh` 后，检查：

1. **后端是否成功启动**：
   ```bash
   curl http://localhost:5678/health
   ```
   应该返回 `{"status":"Healthy"}` 或类似响应

2. **Neo4j 连接是否成功**：
   查看程序日志，查找：
   - ❌ `ArgumentException: Neo4j Password is required` - 密码为空
   - ❌ `ServiceUnavailableException` - Neo4j 连接失败
   - ❌ `AuthenticationException` - 密码错误
   - ✅ 没有 Neo4j 相关错误 - 连接成功

### 3. Research Round 执行检查

发送消息后，检查日志中的关键步骤：

#### ✅ Brief Generation（Plan 节点）

```
[VibeOrchestrator] Applying milestones DAG mutation: sessionId=..., nodeCount=X
[DagStore] ApplyMutationAsync starting: dagId=global, nodeCount=X
[DagStore] Creating PlanNode: nodeId=..., label=...
[DagStore] PlanNode created successfully: nodeId=...
```

**如果没有这些日志**：
- Brief generation 可能失败
- 或 `BuildMilestonesPlanDagMutation` 返回 `null`

#### ✅ Dag Builder（Knowledge 节点）

```
[vibe.dag_builder] Step started
[DagConsensus] dag_builder output length: X, hasValue: true
[DagConsensus] Parsed candidate: nodes=X, edges=Y
```

**如果没有这些日志**：
- `dag_builder` 可能没有被执行
- 或输出为空

#### ✅ DAG Consensus（应用节点）

```
[vibe.dag_consensus] Step started
[DagStore] ApplyMutationAsync starting: dagId=global, nodeCount=X
[DagStore] Upserting KnowledgeNode: nodeId=..., label=..., type=...
[DagStore] KnowledgeNode upserted successfully: nodeId=...
```

**如果没有这些日志**：
- DAG Consensus 可能失败
- 或节点创建失败

#### ✅ DAG 查询（确认节点存在）

```
[DagStore] BuildSnapshotFromGraphAsync (global): knowledgeNodes=X, planNodes=Y, edges=Z
```

**如果看到错误**：
```
[DagStore] Failed to query Neo4j for global DAG: dagId=global, error=...
```
- Neo4j 连接失败
- 或密码错误

### 4. API 验证

```bash
# 检查 DAG API
curl http://localhost:5678/api/dag/global | jq '.dag.nodes | length'
```

**预期结果**：
- 如果还没有运行 research round：返回 `0`（正常）
- 如果运行了 research round：返回大于 `0` 的数字

### 5. 前端显示检查

1. 打开 `http://localhost:5173`
2. 发送消息触发 research round
3. 等待 research round 完成
4. 检查 DAG 图是否显示

**如果 DAG 图仍然为空**：
- 检查浏览器控制台是否有错误
- 检查网络请求：`/api/dag/global` 返回的数据
- 查看后端日志确认节点是否创建成功

---

## 🎯 预期行为

### 如果一切正常：

1. ✅ 程序启动成功（无 Neo4j 连接错误）
2. ✅ 发送消息后，Brief Generation 创建 Plan 节点
3. ✅ Dag_builder 创建 Knowledge 节点
4. ✅ DAG Consensus 应用节点到 Neo4j
5. ✅ API `/api/dag/global` 返回节点数据
6. ✅ 前端 DAG 图显示节点

### 如果密码错误：

1. ✅ 程序启动成功（Driver 创建成功）
2. ❌ 查询 Neo4j 时失败（认证错误）
3. ❌ DAG 返回空结果（异常被捕获）
4. ❌ 前端显示空 DAG 图

**日志中会看到**：
```
[DagStore] Failed to query Neo4j for global DAG: dagId=global, error=...
```

---

## 📋 完整验证流程

### Step 1: 确认 Neo4j 密码

```bash
# 检查当前 Neo4j 密码（如果知道）
# 或者重置密码：
NEO4J_HOME=$(brew --prefix neo4j)
cd "$NEO4J_HOME/libexec"
./bin/neo4j-admin set-initial-password your_new_password
```

### Step 2: 修改 boot.sh（如果需要）

编辑 `boot.sh` 第 144 行：
```bash
export NEO4J_PASSWORD="${NEO4J_PASSWORD:-your_actual_password}"
```

### Step 3: 启动程序

```bash
./boot.sh
```

### Step 4: 检查启动日志

查看是否有 Neo4j 相关错误：
- ❌ `ArgumentException: Neo4j Password is required`
- ❌ `ServiceUnavailableException`
- ❌ `AuthenticationException`

### Step 5: 发送消息触发 Research Round

在前端发送一条消息，例如：
- "研究一下素数分解的唯一性"

### Step 6: 查看 Research Round 日志

查找关键日志消息（见上面的检查点）

### Step 7: 验证 DAG API

```bash
curl http://localhost:5678/api/dag/global | jq '.dag.nodes | length'
```

### Step 8: 检查前端显示

打开 `http://localhost:5173`，检查 DAG 图是否显示

---

## 🚨 常见问题

### 问题 1: 程序启动失败

**原因**：Neo4j 密码为空

**解决**：确保 `boot.sh` 中设置了 `NEO4J_PASSWORD`

### 问题 2: 程序启动成功但 DAG 为空

**原因**：
1. Neo4j 密码错误（认证失败）
2. 还没有运行 research round
3. Research round 运行了但节点创建失败

**解决**：
1. 检查日志中的 Neo4j 错误
2. 确认密码是否正确
3. 查看 research round 日志确认节点创建

### 问题 3: Research Round 运行但节点未创建

**原因**：
1. `dag_builder` 输出为空
2. `dag_builder` 输出无法解析
3. DAG Consensus 失败

**解决**：查看日志中的 `[DagConsensus]` 消息

---

## ✅ 总结

**当前配置状态**：
- ✅ boot.sh 已配置 Neo4j 环境变量
- ✅ Neo4j 服务正在运行
- ✅ Java 环境已配置
- ⚠️ 需要确认 Neo4j 密码是否正确

**预期结果**：
- ✅ 如果密码正确：DAG 图应该会正常显示
- ❌ 如果密码错误：程序可以启动，但 DAG 查询会失败，返回空结果

**下一步**：
1. 确认 Neo4j 密码是否正确
2. 如果密码不是 `password`，修改 `boot.sh` 或设置环境变量
3. 运行 `./boot.sh`
4. 发送消息触发 research round
5. 查看日志确认节点创建
6. 验证 DAG 图显示
