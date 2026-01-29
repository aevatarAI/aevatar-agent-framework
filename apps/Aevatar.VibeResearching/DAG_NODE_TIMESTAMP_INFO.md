# DAG 节点时间戳信息

## 📋 概述

当有新的节点添加到 DAG 图中时，系统**确实记录了时间**。本文档说明时间戳是如何记录的，以及可以从哪里查看这些信息。

---

## 🕐 时间戳记录机制

### 1. KnowledgeNode（知识节点）

**代码位置**: `DagStore.cs` → `BuildSnapshotFromGraphAsync` (第 713 行)

**时间戳字段**: `CreatedAt`

**记录时机**:
- 节点创建时，Neo4j 会自动记录 `CreatedAt` 时间戳
- 每次构建 snapshot 时，会从 Neo4j 读取这个时间戳

**代码**:
```csharp
if (n is KnowledgeNode kn)
{
    var ts = kn.CreatedAt;  // 从 Neo4j 读取创建时间
    // ...
    nodeList.Add(new SraDagNode
    {
        // ...
        UpdatedAt = Timestamp.FromDateTime(DateTime.SpecifyKind(ts.UtcDateTime, DateTimeKind.Utc))
    });
}
```

---

### 2. PlanNode（计划节点）

**代码位置**: `DagStore.cs` → `BuildSnapshotFromGraphAsync` (第 763 行)

**时间戳字段**: `UpdatedAt`

**记录时机**:
- PlanNode 使用 `UpdatedAt` 字段（因为计划节点可能会更新）
- 每次构建 snapshot 时，会从 Neo4j 读取这个时间戳

**代码**:
```csharp
else if (n is PlanNode pn)
{
    var ts = pn.UpdatedAt;  // 从 Neo4j 读取更新时间
    // ...
    nodeList.Add(new SraDagNode
    {
        // ...
        UpdatedAt = Timestamp.FromDateTime(DateTime.SpecifyKind(ts.UtcDateTime, DateTimeKind.Utc))
    });
}
```

---

### 3. Edge（边）

**代码位置**: `DagStore.cs` → `BuildSnapshotFromGraphAsync` (第 794 行)

**时间戳字段**: `CreatedAt`

**记录时机**:
- 边创建时，Neo4j 会自动记录 `CreatedAt` 时间戳
- 每次构建 snapshot 时，会从 Neo4j 读取这个时间戳

**代码**:
```csharp
foreach (var e in allEdges)
{
    var ts = e.CreatedAt;  // 从 Neo4j 读取创建时间
    // ...
    edgeList.Add(new SraDagEdge
    {
        // ...
        UpdatedAt = Timestamp.FromDateTime(DateTime.SpecifyKind(ts.UtcDateTime, DateTimeKind.Utc))
    });
}
```

---

### 4. Mutation（变更）

**代码位置**: `DagConsensusRunner.cs` → `BuildMutation` (第 376 行)

**时间戳字段**: `CreatedAt`

**记录时机**:
- 每次 DAG Consensus 创建 mutation 时，会记录当前时间

**代码**:
```csharp
var now = Timestamp.FromDateTime(DateTime.UtcNow);
var m = new SraDagMutation
{
    // ...
    CreatedAt = now
};
```

---

## 📊 时间戳格式

### API 返回格式

**代码位置**: `DagStore.cs` → `GetSnapshotForListAsync` (第 442 行)

**格式**: ISO 8601 (UTC)

**示例**: `2025-01-28T10:30:45.123Z`

**代码**:
```csharp
updatedAt = n.UpdatedAt?.ToDateTime().ToUniversalTime().ToString("O") ?? "",
```

**说明**:
- `ToString("O")` 生成 ISO 8601 格式
- `.ToUniversalTime()` 确保是 UTC 时间
- 如果时间戳为空，返回空字符串

---

## 🔍 查看时间戳的位置

### 1. DAG API 响应

**API 端点**: `GET /api/dag/global`

**响应格式**:
```json
{
  "ok": true,
  "dagId": "global",
  "dag": {
    "sessionId": "global",
    "updatedAt": "2025-01-28T10:30:45.123Z",
    "nodes": [
      {
        "id": "thm_pythagoras_v1",
        "type": "theorem",
        "kind": "Knowledge",
        "label": "Pythagorean Theorem",
        "proof": "...",
        "updatedAt": "2025-01-28T10:30:45.123Z",  // ← 节点创建/更新时间
        "sessionId": "session_123",
        // ...
      }
    ],
    "edges": [
      {
        "fromId": "def_triangle_v1",
        "toId": "thm_pythagoras_v1",
        "type": "depends_on",
        "updatedAt": "2025-01-28T10:30:45.123Z"  // ← 边创建时间
      }
    ]
  }
}
```

**查看方法**:
```bash
# 使用 curl
curl http://localhost:5678/api/dag/global | jq '.dag.nodes[] | {id, label, updatedAt}'

# 查看特定节点的时间戳
curl http://localhost:5678/api/dag/global | jq '.dag.nodes[] | select(.id == "thm_pythagoras_v1") | .updatedAt'
```

---

### 2. DAG Snapshot 文件

**文件位置**: `workspace/dags/{dagId}/artifacts/dag/snapshot.json`

**格式**: Protobuf JSON

**内容示例**:
```json
{
  "sessionId": "global",
  "updatedAt": "2025-01-28T10:30:45.123Z",
  "nodes": [
    {
      "id": "thm_pythagoras_v1",
      "type": "THEOREM",
      "kind": "KNOWLEDGE",
      "label": "Pythagorean Theorem",
      "proof": "...",
      "updatedAt": "2025-01-28T10:30:45.123Z",  // ← 节点时间戳
      "sessionId": "session_123"
    }
  ],
  "edges": [
    {
      "fromId": "def_triangle_v1",
      "toId": "thm_pythagoras_v1",
      "type": "depends_on",
      "updatedAt": "2025-01-28T10:30:45.123Z"  // ← 边时间戳
    }
  ]
}
```

**查看方法**:
```bash
# 查看 snapshot 文件
cat workspace/dags/global/artifacts/dag/snapshot.json | jq '.nodes[] | {id, label, updatedAt}'

# 查看最新创建的节点
cat workspace/dags/global/artifacts/dag/snapshot.json | jq '.nodes | sort_by(.updatedAt) | reverse | .[0:5] | .[] | {id, label, updatedAt}'
```

---

### 3. Neo4j 数据库（直接查询）

**查询 KnowledgeNode**:
```cypher
MATCH (n:KnowledgeNode)
RETURN n.Id as id, n.CoreDescription as label, n.CreatedAt as createdAt
ORDER BY n.CreatedAt DESC
LIMIT 10
```

**查询 PlanNode**:
```cypher
MATCH (n:PlanNode)
RETURN n.Id as id, n.CoreDescription as label, n.UpdatedAt as updatedAt
ORDER BY n.UpdatedAt DESC
LIMIT 10
```

**查询 Edge**:
```cypher
MATCH (a)-[e:KNOWLEDGE_EDGE]->(b)
RETURN a.Id as from, b.Id as to, e.CreatedAt as createdAt
ORDER BY e.CreatedAt DESC
LIMIT 10
```

**使用 cypher-shell**:
```bash
cypher-shell -u neo4j -p <password> -a bolt://localhost:7687 \
  "MATCH (n:KnowledgeNode) RETURN n.Id, n.CoreDescription, n.CreatedAt ORDER BY n.CreatedAt DESC LIMIT 10"
```

---

### 4. 前端 UI（当前状态）

**代码位置**: `landing-dag-viewer.tsx` → `transformApiData` (第 24-34 行)

**当前实现**:
```typescript
const nodes: DAGNode[] = (snapshot.nodes || []).map((node: ApiDagNode) => ({
  id: node.id,
  label: node.label || node.id,
  kind: (node.kind as 'Plan' | 'Knowledge') || 'Knowledge',
  status: 'completed',
  type: node.type || 'Knowledge',
  proof: node.proof,
  attestationsCount: node.attestationsCount,
  planStatus: node.planStatus,
  sessionId: node.sessionId,
  // ⚠️ updatedAt 字段没有被映射！
}))
```

**问题**: 
- API 返回了 `updatedAt` 字段
- 但前端 `transformApiData` 函数**没有映射**这个字段
- 因此前端 UI **目前不显示**时间戳

---

### 5. 服务器日志

**日志位置**: 程序运行时的控制台输出或日志文件

**日志示例**:
```
[DagStore] Upserting KnowledgeNode: nodeId=thm_pythagoras_v1, label=Pythagorean Theorem, type=THEOREM
[DagStore] KnowledgeNode upserted successfully: nodeId=thm_pythagoras_v1
[DagStore] BuildSnapshotFromGraphAsync (global): knowledgeNodes=10, planNodes=3, edges=15
```

**查看方法**:
```bash
# 查看节点创建日志
grep -i "Upserting KnowledgeNode\|Creating PlanNode" logs/*.log

# 查看特定节点的创建时间
grep -i "nodeId=thm_pythagoras_v1" logs/*.log
```

---

## 🔧 如何在前端显示时间戳

### 修改前端代码

**文件**: `sisyphus-frontend/src/pages/landing/landing-dag-viewer.tsx`

**修改位置**: `transformApiData` 函数 (第 24-34 行)

**修改前**:
```typescript
const nodes: DAGNode[] = (snapshot.nodes || []).map((node: ApiDagNode) => ({
  id: node.id,
  label: node.label || node.id,
  // ... 其他字段
  // ⚠️ 缺少 updatedAt
}))
```

**修改后**:
```typescript
const nodes: DAGNode[] = (snapshot.nodes || []).map((node: ApiDagNode) => ({
  id: node.id,
  label: node.label || node.id,
  // ... 其他字段
  updatedAt: node.updatedAt,  // ← 添加时间戳字段
}))
```

**然后更新类型定义**:

**文件**: `sisyphus-frontend/src/types/index.ts`

**修改**:
```typescript
export interface DAGNode {
  id: string
  label: string
  // ... 其他字段
  updatedAt?: string  // ← 添加时间戳字段
}
```

**在 UI 中显示**:

可以在节点详情面板或工具提示中显示时间戳：
```typescript
<div className="node-timestamp">
  Created: {new Date(node.updatedAt).toLocaleString()}
</div>
```

---

## 📊 时间戳用途

### 1. 节点排序

**按创建时间排序**:
```bash
# 查看最新创建的节点
curl http://localhost:5678/api/dag/global | \
  jq '.dag.nodes | sort_by(.updatedAt) | reverse | .[0:10] | .[] | {id, label, updatedAt}'
```

### 2. 节点过滤

**查找特定时间范围内的节点**:
```bash
# 查找今天创建的节点
curl http://localhost:5678/api/dag/global | \
  jq '.dag.nodes[] | select(.updatedAt | startswith("2025-01-28")) | {id, label, updatedAt}'
```

### 3. 变更追踪

**查看 DAG 快照的更新时间**:
```bash
# 查看 DAG 快照的最后更新时间
curl http://localhost:5678/api/dag/global | jq '.dag.updatedAt'
```

### 4. 审计和调试

**追踪节点创建历史**:
- 查看 snapshot 文件的时间戳
- 查看服务器日志中的创建时间
- 查询 Neo4j 数据库中的时间戳

---

## 💡 总结

### 时间戳记录

✅ **系统确实记录了时间**:
- KnowledgeNode: `CreatedAt`（创建时间）
- PlanNode: `UpdatedAt`（更新时间）
- Edge: `CreatedAt`（创建时间）
- Snapshot: `UpdatedAt`（最后更新时间）

### 查看位置

1. ✅ **DAG API**: `GET /api/dag/global` - 返回 `updatedAt` 字段
2. ✅ **Snapshot 文件**: `workspace/dags/{dagId}/artifacts/dag/snapshot.json`
3. ✅ **Neo4j 数据库**: 直接查询 `CreatedAt` 或 `UpdatedAt` 字段
4. ✅ **服务器日志**: 查看节点创建日志
5. ⚠️ **前端 UI**: 目前**不显示**时间戳（需要修改代码）

### 时间戳格式

- **格式**: ISO 8601 (UTC)
- **示例**: `2025-01-28T10:30:45.123Z`
- **时区**: UTC

---

*最后更新: 2025-01-28*
