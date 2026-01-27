# DAG 图未显示的根本原因分析

## 🔍 问题确认

根据代码分析，DAG 图未显示的根本原因可能是以下几个之一：

### 1. Neo4j 环境变量未设置 ⚠️ **最可能的原因**

**问题**：
- `boot.sh` 启动程序时**没有设置 Neo4j 环境变量**
- `Program.cs` 第 247 行调用 `AddAevatarGraphNeo4j()`，它会从环境变量读取配置
- 如果 `NEO4J_PASSWORD` 为空，Neo4j Driver 创建会失败

**证据**：
```csharp
// Neo4jServiceCollectionExtensions.cs 第 21-24 行
options.Uri = Environment.GetEnvironmentVariable("NEO4J_URI") ?? "bolt://localhost:7687";
options.Username = Environment.GetEnvironmentVariable("NEO4J_USERNAME") ?? "neo4j";
options.Password = Environment.GetEnvironmentVariable("NEO4J_PASSWORD") ?? string.Empty;  // ⚠️ 空字符串！
```

**验证**：
```csharp
// Neo4jDriverFactory.cs 第 42-44 行
if (string.IsNullOrWhiteSpace(options.Password))
{
    throw new ArgumentException("Neo4j Password is required.", nameof(options));
}
```

**解决方案**：
修改 `boot.sh`，在启动后端前设置环境变量：

```bash
# 在 boot.sh 第 140-146 行之前添加
export NEO4J_URI="${NEO4J_URI:-bolt://localhost:7687}"
export NEO4J_USERNAME="${NEO4J_USERNAME:-neo4j}"
export NEO4J_PASSWORD="${NEO4J_PASSWORD:-password}"  # 替换为你的实际密码

# 设置 Java 环境（如果需要）
export JAVA_HOME="${JAVA_HOME:-$(brew --prefix openjdk@21)/libexec/openjdk.jdk/Contents/Home}"
export PATH="${JAVA_HOME}/bin:${PATH}"
```

### 2. Neo4j 连接失败但被静默处理

**问题**：
- `BuildSnapshotFromGraphAsync` 捕获异常并返回空快照
- 节点创建失败时只记录警告，不抛出异常

**证据**：
```csharp
// DagStore.cs 第 666-672 行
catch (Exception ex)
{
    _logger.LogError(ex, "[DagStore] Failed to query Neo4j for global DAG: dagId={DagId}, error={Error}", dagId, ex.Message);
    // Return empty snapshot on error
    allNodes = new List<IGraphNode>();
    allEdges = new List<KnowledgeEdge>();
}
```

**验证**：
查看日志中是否有：
```
[DagStore] Failed to query Neo4j for global DAG: dagId=global, error=...
```

### 3. Dag_builder 输出为空或无法解析

**问题**：
- `dag_builder` 没有产生输出
- 或输出格式不正确，无法解析为 JSON

**证据**：
```csharp
// VibeOrchestrator.DagConsensus.cs 第 45-52 行
var candidateText = outputs.TryGetValue("dag_builder", out var x) ? x : string.Empty;
var candidate = TryParseDagBuilderCandidate(session.Id, candidateText, currentDag, activeMilestoneId);

if (candidate == null)
{
    // 返回空结果，不创建节点
    return new DagRoundResult(false, false, null, null, null, [], null);
}
```

**验证**：
查看日志中是否有：
```
[DagConsensus] dag_builder output length: 0
[DagConsensus] Failed to parse dag_builder output
```

### 4. 节点创建失败但被捕获

**问题**：
- `ApplyMutationAsync` 中节点创建失败时只记录警告
- 异常被捕获，不会中断整个流程

**证据**：
```csharp
// DagStore.cs 第 199-202 行
catch (Exception ex)
{
    _logger.LogWarning(ex, "[DagStore] Failed to upsert dag node {NodeId}: {Error}", id, ex.Message);
}
```

**验证**：
查看日志中是否有：
```
[DagStore] Failed to upsert dag node ...
```

---

## 📋 完整诊断流程

### Step 1: 检查 Neo4j 配置

```bash
# 运行诊断脚本
cd /Users/chronoai/aevatar-agent-framework/apps/Aevatar.VibeResearching
./diagnose_dag_issue.sh
```

### Step 2: 检查程序日志

运行程序后，查找以下关键日志：

#### ✅ 正常流程应该看到：

1. **Brief Generation**:
   ```
   [VibeOrchestrator] Applying milestones DAG mutation: sessionId=..., nodeCount=X
   [DagStore] ApplyMutationAsync starting: dagId=global, nodeCount=X
   [DagStore] Creating PlanNode: nodeId=..., label=...
   [DagStore] PlanNode created successfully: nodeId=...
   ```

2. **Dag Builder**:
   ```
   [vibe.dag_builder] Step started
   [DagConsensus] dag_builder output length: X, hasValue: true
   [DagConsensus] Parsed candidate: nodes=X, edges=Y
   ```

3. **DAG Consensus**:
   ```
   [vibe.dag_consensus] Step started
   [DagStore] ApplyMutationAsync starting: dagId=global, nodeCount=X
   [DagStore] Upserting KnowledgeNode: nodeId=..., label=..., type=...
   [DagStore] KnowledgeNode upserted successfully: nodeId=...
   ```

4. **查询 DAG**:
   ```
   [DagStore] BuildSnapshotFromGraphAsync (global): knowledgeNodes=X, planNodes=Y, edges=Z
   ```

#### ❌ 错误情况：

1. **Neo4j 连接失败**:
   ```
   [DagStore] Failed to query Neo4j for global DAG: dagId=global, error=...
   ```

2. **Dag_builder 输出为空**:
   ```
   [DagConsensus] dag_builder output length: 0
   [DagConsensus] Failed to parse dag_builder output
   ```

3. **节点创建失败**:
   ```
   [DagStore] Failed to upsert dag node ...: ...
   ```

### Step 3: 修复 boot.sh

修改 `boot.sh`，在启动后端前设置环境变量：

```bash
# 在 boot.sh 第 139 行之前添加
# ============================================================
#  Neo4j Configuration
# ============================================================
export NEO4J_URI="${NEO4J_URI:-bolt://localhost:7687}"
export NEO4J_USERNAME="${NEO4J_USERNAME:-neo4j}"
export NEO4J_PASSWORD="${NEO4J_PASSWORD:-password}"  # ⚠️ 替换为你的实际密码

# Java Environment (for Neo4j)
if [ -z "$JAVA_HOME" ]; then
    JAVA_HOME_CANDIDATE=$(brew --prefix openjdk@21)/libexec/openjdk.jdk/Contents/Home 2>/dev/null
    if [ -d "$JAVA_HOME_CANDIDATE" ]; then
        export JAVA_HOME="$JAVA_HOME_CANDIDATE"
        export PATH="$JAVA_HOME/bin:$PATH"
    fi
fi
```

### Step 4: 验证修复

1. 重新启动程序：`./boot.sh`
2. 发送消息触发 research round
3. 查看日志确认节点创建成功
4. 检查 API：`curl http://localhost:5678/api/dag/global | jq '.dag.nodes | length'`

---

## 🎯 最可能的根本原因

**Neo4j 密码未设置**，导致：
1. Neo4j Driver 创建失败（启动时）
2. 或连接时认证失败（运行时）
3. 查询返回空结果
4. DAG 显示为空

**立即修复**：
1. 修改 `boot.sh` 添加 Neo4j 环境变量
2. 确保 Neo4j 正在运行
3. 重新启动程序
