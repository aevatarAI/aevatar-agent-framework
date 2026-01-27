# DAG 图未显示 - 完整诊断报告

## 🔍 根本原因确认

根据代码分析和诊断脚本结果，**DAG 图未显示的根本原因是：`boot.sh` 没有设置 Neo4j 环境变量**。

### 问题链

```
boot.sh 启动程序
    ↓
没有设置 NEO4J_PASSWORD 环境变量
    ↓
Program.cs: AddAevatarGraphNeo4j() 读取环境变量
    ↓
NEO4J_PASSWORD = "" (空字符串)
    ↓
Neo4jDriverFactory.Validate() 抛出异常
    ↓
或：Neo4j 连接失败（认证失败）
    ↓
BuildSnapshotFromGraphAsync() 捕获异常
    ↓
返回空快照 (nodes=0, edges=0)
    ↓
前端显示空 DAG 图
```

## ✅ 已完成的修复

### 1. 修改了 `boot.sh`

在启动后端前添加了 Neo4j 环境变量设置：

```bash
# Neo4j Configuration
export NEO4J_URI="${NEO4J_URI:-bolt://localhost:7687}"
export NEO4J_USERNAME="${NEO4J_USERNAME:-neo4j}"
export NEO4J_PASSWORD="${NEO4J_PASSWORD:-password}"  # ⚠️ 请替换为你的实际密码

# Java Environment
if [[ -z "$JAVA_HOME" ]]; then
  JAVA_HOME_CANDIDATE="$(brew --prefix openjdk@21)/libexec/openjdk.jdk/Contents/Home" 2>/dev/null
  if [[ -d "$JAVA_HOME_CANDIDATE" ]]; then
    export JAVA_HOME="$JAVA_HOME_CANDIDATE"
    export PATH="$JAVA_HOME/bin:$PATH"
  fi
fi
```

### 2. 添加了调试日志

在 `VibeOrchestrator.DagConsensus.cs` 中添加了详细的调试日志：
- `dag_builder` 输出长度
- 解析后的节点和边数量
- 解析失败的原因

## 📋 验证步骤

### Step 1: 设置 Neo4j 密码

**重要**：修改 `boot.sh` 第 140 行，将 `password` 替换为你的实际 Neo4j 密码：

```bash
export NEO4J_PASSWORD="${NEO4J_PASSWORD:-your_actual_password}"
```

或者，在运行 `boot.sh` 前设置环境变量：

```bash
export NEO4J_PASSWORD="your_actual_password"
./boot.sh
```

### Step 2: 确保 Neo4j 正在运行

```bash
cd /Users/chronoai/aevatar-agent-framework/apps/Aevatar.VibeResearching
./start_neo4j_with_java.sh
```

### Step 3: 启动程序

```bash
./boot.sh
```

### Step 4: 发送消息触发 Research Round

在前端发送一条消息，例如：
- "研究一下素数分解的唯一性"
- "证明某个数学定理"

### Step 5: 查看日志确认节点创建

查找以下关键日志：

#### ✅ 应该看到：

1. **Brief Generation** (创建 Plan 节点):
   ```
   [VibeOrchestrator] Applying milestones DAG mutation: sessionId=..., nodeCount=X
   [DagStore] ApplyMutationAsync starting: dagId=global, nodeCount=X
   [DagStore] Creating PlanNode: nodeId=..., label=...
   [DagStore] PlanNode created successfully: nodeId=...
   ```

2. **Dag Builder** (创建 Knowledge 节点):
   ```
   [vibe.dag_builder] Step started
   [DagConsensus] dag_builder output length: X, hasValue: true
   [DagConsensus] Parsed candidate: nodes=X, edges=Y
   ```

3. **DAG Consensus** (应用节点到 Neo4j):
   ```
   [vibe.dag_consensus] Step started
   [DagStore] ApplyMutationAsync starting: dagId=global, nodeCount=X
   [DagStore] Upserting KnowledgeNode: nodeId=..., label=..., type=...
   [DagStore] KnowledgeNode upserted successfully: nodeId=...
   ```

4. **查询 DAG** (确认节点存在):
   ```
   [DagStore] BuildSnapshotFromGraphAsync (global): knowledgeNodes=X, planNodes=Y, edges=Z
   ```

#### ❌ 如果看到错误：

1. **Neo4j 连接失败**:
   ```
   [DagStore] Failed to query Neo4j for global DAG: dagId=global, error=...
   ```
   **解决**：检查 Neo4j 是否运行，密码是否正确

2. **Dag_builder 输出为空**:
   ```
   [DagConsensus] dag_builder output length: 0
   [DagConsensus] Failed to parse dag_builder output
   ```
   **解决**：检查 `dag_builder` 是否被执行，LLM 调用是否成功

3. **节点创建失败**:
   ```
   [DagStore] Failed to upsert dag node ...: ...
   ```
   **解决**：查看具体错误信息，可能是 Neo4j 权限问题

### Step 6: 验证 DAG API

```bash
curl http://localhost:5678/api/dag/global | jq '.dag.nodes | length'
```

应该返回大于 0 的数字。

### Step 7: 检查前端显示

打开 `http://localhost:5173`，DAG 图应该显示出来。

---

## 🎯 关键发现总结

1. **Neo4j 服务**：✅ 正在运行（Bolt 和 HTTP 端口都可访问）
2. **Java 环境**：✅ 已配置（Java 21）
3. **环境变量**：⚠️ **`boot.sh` 中没有设置**（已修复）
4. **Neo4j 密码**：⚠️ **需要设置为实际密码**（用户需要修改）

---

## 💡 如果问题仍然存在

如果修复后仍然没有 DAG 图，请：

1. **运行诊断脚本**：
   ```bash
   ./diagnose_dag_issue.sh
   ```

2. **查看完整日志**：
   - 查找所有 `[DagStore]` 和 `[DagConsensus]` 日志
   - 确认是否有错误或警告

3. **检查节点创建流程**：
   - Brief Generation 是否成功？
   - Dag_builder 是否被执行？
   - Dag_builder 输出是否为空？
   - DAG Consensus 是否成功？
   - 节点是否真的创建到 Neo4j？

4. **直接查询 Neo4j**：
   ```bash
   # 使用 cypher-shell（如果已安装）
   echo "MATCH (n) RETURN count(n) as nodeCount;" | \
     cypher-shell -u neo4j -p your_password -a bolt://localhost:7687
   ```

5. **分享日志**：
   - 分享完整的程序日志
   - 特别是 `[DagStore]` 和 `[DagConsensus]` 相关的日志

---

## 📝 修改的文件

1. ✅ `boot.sh` - 添加了 Neo4j 环境变量设置
2. ✅ `VibeOrchestrator.DagConsensus.cs` - 添加了调试日志
3. ✅ `diagnose_dag_issue.sh` - 创建了诊断脚本
4. ✅ `DAG_ISSUE_ROOT_CAUSE.md` - 创建了根本原因分析文档

---

## 🚀 下一步

1. **修改 `boot.sh` 中的 Neo4j 密码**（第 140 行）
2. **重新启动程序**：`./boot.sh`
3. **发送消息触发 research round**
4. **查看日志确认节点创建**
5. **验证 DAG 图显示**
