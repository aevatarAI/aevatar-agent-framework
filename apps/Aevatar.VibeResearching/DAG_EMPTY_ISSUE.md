# DAG 为空（0 nodes, 0 edges）问题分析

## 📋 问题描述

重启 Cursor 后运行程序，发现 DAG 快照返回了 0 个节点和 0 条边：

```
info: VibeResearching.Api.Vibe.Dag.DagStore[0]
      [DagStore] GetSnapshotForListAsync returning: dagId=global, nodes=0, edges=0, truncated={ nodes = False, edges = False }
```

---

## 🔍 问题原因分析

### 1. Neo4j 数据库为空或数据丢失

**代码位置**: `DagStore.cs` → `BuildSnapshotFromGraphAsync` (第 643-672 行)

**问题根源**:
- 对于 `global` DAG，系统会从 Neo4j 查询所有节点和边
- 如果 Neo4j 数据库为空或数据被清空，查询会返回空结果
- 代码会捕获异常并返回空 snapshot，但不会抛出错误

**关键代码**:
```csharp
if (dagId == ResearchSession.GlobalDagId)
{
    try
    {
        var knowledgeNodes = await client.GetAllKnowledgeNodesGlobalAsync(ct);
        var planNodes = await client.GetAllPlanNodesGlobalAsync(ct);
        var edgesWithSession = await client.GetAllEdgesGlobalAsync(ct);
        allNodes = knowledgeNodes.Cast<IGraphNode>().Concat(planNodes.Cast<IGraphNode>()).ToList();
        allEdges = edgesWithSession.Select(e => e.Edge).ToList();
        _logger.LogInformation("[DagStore] BuildSnapshotFromGraphAsync (global): knowledgeNodes={KnowledgeCount}, planNodes={PlanCount}, edges={EdgeCount}",
            knowledgeNodes.Count, planNodes.Count, allEdges.Count);
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "[DagStore] Failed to query Neo4j for global DAG: dagId={DagId}, error={Error}", dagId, ex.Message);
        // Return empty snapshot on error
        allNodes = new List<IGraphNode>();
        allEdges = new List<KnowledgeEdge>();
    }
}
```

---

### 2. Neo4j 连接失败

**可能原因**:
- Neo4j 服务未启动
- Neo4j 连接配置错误（URI、用户名、密码）
- 网络连接问题
- Neo4j 数据库名称不匹配

**检查方法**:
```bash
# 检查 Neo4j 连接
./check_neo4j_connection.sh

# 检查 DAG 状态
./check_dag.sh
```

---

### 3. 数据库被重置或清空

**可能原因**:
- Neo4j 数据库被手动清空
- Neo4j 数据库被重置
- 数据库文件被删除
- 使用了新的 Neo4j 实例

**检查方法**:
```bash
# 连接到 Neo4j 并查询节点数量
cypher-shell -u neo4j -p <password> "MATCH (n) RETURN count(n) as nodeCount"

# 查询所有节点
cypher-shell -u neo4j -p <password> "MATCH (n) RETURN n LIMIT 10"
```

---

### 4. DAG ID 不匹配

**代码位置**: `ResearchSession.cs` → `EffectiveDagId`

**问题根源**:
- 如果 `DagId` 为空，系统会使用 `GlobalDagId = "global"`
- 如果之前的数据存储在特定的 `dagId` 下，但现在使用 `global`，可能查询不到数据

**关键代码**:
```csharp
public const string GlobalDagId = "global";

public string EffectiveDagId => string.IsNullOrWhiteSpace(DagId) ? GlobalDagId : DagId.Trim();
```

---

## 🔧 解决方案

### 方案 1: 检查 Neo4j 连接和状态

**步骤**:
1. **检查 Neo4j 是否运行**:
   ```bash
   # macOS
   brew services list | grep neo4j
   
   # 或检查进程
   ps aux | grep neo4j
   ```

2. **检查环境变量**:
   ```bash
   echo $NEO4J_URI
   echo $NEO4J_USERNAME
   echo $NEO4J_PASSWORD
   echo $NEO4J_DATABASE
   ```

3. **测试连接**:
   ```bash
   ./check_neo4j_connection.sh
   ```

4. **启动 Neo4j**（如果未运行）:
   ```bash
   ./start_neo4j.sh
   ```

---

### 方案 2: 检查数据库中的数据

**步骤**:
1. **连接到 Neo4j**:
   ```bash
   cypher-shell -u neo4j -p <password> -a bolt://localhost:7687
   ```

2. **查询节点数量**:
   ```cypher
   MATCH (n) RETURN count(n) as nodeCount;
   ```

3. **查询所有节点**:
   ```cypher
   MATCH (n) RETURN n LIMIT 20;
   ```

4. **查询特定类型的节点**:
   ```cypher
   MATCH (n:KnowledgeNode) RETURN n LIMIT 20;
   MATCH (n:PlanNode) RETURN n LIMIT 20;
   ```

---

### 方案 3: 检查 DAG 快照文件

**位置**: `workspace/dags/{dagId}/artifacts/dag/snapshot.json`

**步骤**:
1. **查找快照文件**:
   ```bash
   find workspace -name "snapshot.json" -type f
   ```

2. **检查快照内容**:
   ```bash
   cat workspace/dags/global/artifacts/dag/snapshot.json | jq '.nodes | length'
   cat workspace/dags/global/artifacts/dag/snapshot.json | jq '.edges | length'
   ```

3. **如果快照文件存在但为空**:
   - 可能是快照文件损坏
   - 可能需要重新从 Neo4j 构建快照

---

### 方案 4: 检查日志中的错误信息

**查找错误日志**:
```bash
# 查找 Neo4j 连接错误
grep -i "neo4j\|Failed to query\|Connection refused" logs/*.log

# 查找 DAG 构建错误
grep -i "BuildSnapshotFromGraphAsync\|Failed to query Neo4j" logs/*.log
```

**常见错误**:
- `Connection refused`: Neo4j 服务未启动
- `Authentication failed`: 用户名或密码错误
- `Database not found`: 数据库名称不匹配
- `Timeout`: 网络连接超时

---

### 方案 5: 重新初始化 DAG（如果数据确实丢失）

**警告**: 这会删除所有现有数据！

**步骤**:
1. **备份现有数据**（如果有）:
   ```bash
   # 导出 Neo4j 数据
   neo4j-admin database dump neo4j --to-path=/path/to/backup
   ```

2. **清空数据库**（如果需要）:
   ```cypher
   MATCH (n) DETACH DELETE n;
   ```

3. **重新运行程序**:
   - 程序会自动创建新的节点和边
   - 从 `research_assistant` 开始新的研究

---

## 🔍 诊断步骤

### 1. 检查 Neo4j 服务状态

```bash
# macOS (Homebrew)
brew services list | grep neo4j

# 或直接检查
neo4j status
```

### 2. 检查环境变量

```bash
# 在 boot.sh 或 .env 文件中检查
cat boot.sh | grep NEO4J
```

### 3. 测试 Neo4j 连接

```bash
# 使用提供的脚本
./check_neo4j_connection.sh

# 或手动测试
cypher-shell -u $NEO4J_USERNAME -p $NEO4J_PASSWORD -a $NEO4J_URI
```

### 4. 检查数据库内容

```bash
# 查询节点数量
cypher-shell -u $NEO4J_USERNAME -p $NEO4J_PASSWORD -a $NEO4J_URI \
  "MATCH (n) RETURN count(n) as nodeCount"

# 查询所有节点类型
cypher-shell -u $NEO4J_USERNAME -p $NEO4J_PASSWORD -a $NEO4J_URI \
  "MATCH (n) RETURN DISTINCT labels(n) as labels, count(n) as count"
```

### 5. 检查程序日志

查看程序启动时的日志，查找：
- `[DagStore] BuildSnapshotFromGraphAsync starting`
- `[DagStore] BuildSnapshotFromGraphAsync (global): knowledgeNodes=...`
- `[DagStore] Failed to query Neo4j for global DAG`

---

## 💡 常见场景

### 场景 1: 重启后 Neo4j 未启动

**症状**: DAG 返回 0 节点，日志显示连接错误

**解决**: 启动 Neo4j 服务
```bash
./start_neo4j.sh
# 或
neo4j start
```

---

### 场景 2: 环境变量未设置

**症状**: DAG 返回 0 节点，没有明显的错误日志

**解决**: 检查并设置环境变量
```bash
export NEO4J_URI=bolt://localhost:7687
export NEO4J_USERNAME=neo4j
export NEO4J_PASSWORD=<your_password>
export NEO4J_DATABASE=neo4j
```

---

### 场景 3: 数据库被清空

**症状**: Neo4j 连接正常，但查询返回 0 节点

**解决**: 
- 如果数据确实丢失，需要重新运行研究
- 如果有备份，可以恢复数据

---

### 场景 4: DAG ID 不匹配

**症状**: 之前的数据存储在特定 `dagId` 下，但现在使用 `global`

**解决**: 
- 检查 `ResearchSession` 的 `DagId` 属性
- 确保使用正确的 `dagId` 查询数据

---

## 📊 总结

**问题根源**:
1. **Neo4j 数据库为空** - 数据被清空或丢失
2. **Neo4j 连接失败** - 服务未启动或配置错误
3. **数据库查询失败** - 异常被捕获，返回空 snapshot
4. **DAG ID 不匹配** - 使用了错误的 `dagId`

**诊断步骤**:
1. ✅ 检查 Neo4j 服务状态
2. ✅ 检查环境变量配置
3. ✅ 测试 Neo4j 连接
4. ✅ 查询数据库内容
5. ✅ 检查程序日志

**解决方案**:
- **如果 Neo4j 未启动**: 启动 Neo4j 服务
- **如果连接失败**: 检查并修复环境变量
- **如果数据库为空**: 重新运行研究或恢复备份
- **如果 DAG ID 不匹配**: 使用正确的 `dagId`

---

*最后更新: 2025-01-28*
