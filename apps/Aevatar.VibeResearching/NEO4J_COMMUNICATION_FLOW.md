# Neo4j 通信流程和 DAG 更新机制

## 📋 概述

本文档详细说明程序如何与 Neo4j 通信，以及 DAG 图是如何更新到 Neo4j 的。

---

## 🔌 Neo4j 通信方式

### ✅ **通过后端通信（不是前端）**

**重要**: 程序与 Neo4j 的**所有通信都通过后端**进行，前端**不直接**连接 Neo4j。

---

## 🏗️ 架构图

```
┌─────────────┐         ┌──────────────┐         ┌─────────────┐
│   Frontend  │  HTTP   │   Backend    │  Bolt   │    Neo4j    │
│  (React)    │ ──────> │  (.NET API)  │ ──────> │  Database   │
└─────────────┘         └──────────────┘         └─────────────┘
     │                        │                        │
     │                        │                        │
     │  1. GET /api/dag/*    │                        │
     │  <────────────────────│                        │
     │                        │                        │
     │                        │  2. Query Neo4j       │
     │                        │ ──────────────────────>│
     │                        │                        │
     │                        │  3. Return DAG        │
     │                        │ <─────────────────────│
     │                        │                        │
     │  4. Return JSON        │                        │
     │ <──────────────────────│                        │
     │                        │                        │
     │                        │  5. Apply Mutation    │
     │                        │ ──────────────────────>│
     │                        │                        │
```

---

## 🔧 后端 Neo4j 连接配置

### 1. 环境变量配置

**位置**: `boot.sh` (第 140-145 行)

```bash
export NEO4J_URI="${NEO4J_URI:-bolt://localhost:7687}"
export NEO4J_USERNAME="${NEO4J_USERNAME:-neo4j}"
export NEO4J_PASSWORD="${NEO4J_PASSWORD:-gVwt5cwZ-vKisdvGS6z6r94iE9DXE1yV_yvPL-vs5qI}"
export NEO4J_DATABASE="${NEO4J_DATABASE:-neo4j}"
```

**说明**:
- 后端启动时读取这些环境变量
- 用于创建 Neo4j Driver 连接

---

### 2. 服务注册

**位置**: `Program.cs` (第 247 行)

```csharp
builder.Services.AddAevatarGraphNeo4j();
```

**作用**:
- 注册 Neo4j Graph 服务
- 创建 `IKnowledgeGraphClientFactory`
- 配置 Neo4j Driver

**代码位置**: `Aevatar.Agents.Persistence.Neo4j.Graph`

---

### 3. Neo4j 客户端创建

**位置**: `DagStore.cs` (第 128 行)

```csharp
var client = _graphFactory.CreateClient(writeSessionId);
```

**说明**:
- `_graphFactory` 是 `IKnowledgeGraphClientFactory` 实例
- 为每个 session 创建独立的 Neo4j 客户端
- 客户端通过 Bolt 协议连接 Neo4j (端口 7687)

---

## 📊 DAG 图更新流程

### 完整更新流程

```
┌─────────────────────────────────────────────────────────────────┐
│  1. Agent 生成 DAG Mutation                                      │
│     - dag_builder agent 提取知识节点                             │
│     - research_assistant agent 创建 Plan 节点                    │
└─────────────────────────────────────────────────────────────────┘
                            │
                            ▼
┌─────────────────────────────────────────────────────────────────┐
│  2. DAG Consensus 验证                                           │
│     - DagConsensusRunner.RunAsync()                             │
│     - 验证节点和边的有效性                                        │
│     - 检查 Red Flags                                             │
└─────────────────────────────────────────────────────────────────┘
                            │
                            ▼
┌─────────────────────────────────────────────────────────────────┐
│  3. 应用 Mutation                                                │
│     - VibeOrchestrator.DagConsensus.cs                          │
│     - DagStore.ApplyMutationAsync()                             │
└─────────────────────────────────────────────────────────────────┘
                            │
                            ▼
┌─────────────────────────────────────────────────────────────────┐
│  4. 写入 Neo4j                                                   │
│     - client.CreatePlanNodeAsync() (Plan 节点)                   │
│     - client.UpsertNodeAsync() (Knowledge 节点)                 │
│     - client.CreateEdgeAsync() (边)                              │
└─────────────────────────────────────────────────────────────────┘
                            │
                            ▼
┌─────────────────────────────────────────────────────────────────┐
│  5. 保存快照到文件                                                │
│     - artifacts/dag/snapshot.json                               │
│     - 作为备份和恢复机制                                          │
└─────────────────────────────────────────────────────────────────┘
```

---

## 🔍 详细步骤说明

### 步骤 1: Agent 生成 DAG Mutation

**代码位置**: `VibeDagBuilderAgent.cs`

**过程**:
- `dag_builder` agent 分析 Verifier 输出
- 提取知识节点（theorem, axiom, hypothesis 等）
- 生成 `SraDagMutation` 对象
- 包含 `UpsertNodes` 和 `UpsertEdges`

**示例**:
```csharp
var mutation = new SraDagMutation
{
    SessionId = sessionId,
    UpsertNodes = new List<SraDagNode>
    {
        new SraDagNode
        {
            Id = "thm_example_v1",
            Kind = SraDagNodeKind.Knowledge,
            Type = SraDagNodeType.Theorem,
            Label = "Example Theorem",
            Proof = "Proof content..."
        }
    },
    UpsertEdges = new List<SraDagEdge> { ... }
};
```

---

### 步骤 2: DAG Consensus 验证

**代码位置**: `DagConsensusRunner.cs` → `RunAsync()`

**过程**:
1. **加载当前 DAG 快照**
   ```csharp
   var current = await _core.Dag.LoadSnapshotAsync(dagId, ct);
   ```

2. **运行 Consensus**
   - 使用 LLM 验证 mutation 的有效性
   - 检查节点类型、边关系
   - 识别 Red Flags

3. **返回结果**
   ```csharp
   if (!consensus.Ok || consensus.Mutation == null)
   {
       // Blocked - 不会更新 DAG
       return new DagRoundResult(false, true, ...);
   }
   ```

---

### 步骤 3: 应用 Mutation

**代码位置**: `VibeOrchestrator.DagConsensus.cs` → `RunDagApplyAsync()` (第 128 行)

**过程**:
```csharp
var dagId = session.EffectiveDagId;
var accepted = consensus.Mutation;

// 添加共识元数据
if (!string.IsNullOrWhiteSpace(consensus.Workflow))
    accepted.Labels["consensus_workflow"] = consensus.Workflow;

// 应用到 DAG
var applied = await _core.Dag.ApplyMutationAsync(dagId, accepted, ct);
```

---

### 步骤 4: 写入 Neo4j

**代码位置**: `DagStore.cs` → `ApplyMutationAsync()` (第 109-320 行)

#### 4.1 创建 Neo4j 客户端

```csharp
var client = _graphFactory.CreateClient(writeSessionId);
```

#### 4.2 写入节点

**Plan 节点** (第 169-187 行):
```csharp
if (n.Kind == SraDagNodeKind.Plan && isMilestoneMutation)
{
    await client.CreatePlanNodeAsync(
        nodeId: id,
        coreDescription: label,
        detailedDescription: detail,
        methodology: proof,
        cancellationToken: ct);
}
```

**Knowledge 节点** (第 194-209 行):
```csharp
else
{
    await client.UpsertNodeAsync(
        nodeId: id,
        nodeType: MapDagNodeType(n.Type),
        owner: owner,
        coreDescription: label,
        detailedDescription: detail,
        proof: proof,
        resourceFolderPath: null,
        cancellationToken: ct);
}
```

#### 4.3 写入边

**代码位置**: `DagStore.cs` (第 270-304 行)

```csharp
foreach (var e in mutation.UpsertEdges)
{
    await client.CreateEdgeAsync(
        fromNodeId: e.FromId,
        toNodeId: e.ToId,
        edgeType: MapEdgeType(e.Type),
        cancellationToken: ct);
}
```

---

### 步骤 5: 保存快照到文件

**代码位置**: `DagStore.cs` → `SaveSnapshotAsync()` (第 915 行)

**过程**:
1. 从 Neo4j 重新构建快照
   ```csharp
   var outSnap = await BuildSnapshotFromGraphAsync(ws.DagId, ct);
   ```

2. 保存到文件
   ```csharp
   await SaveSnapshotAsync(ws, outSnap, ct);
   ```

**文件位置**: `workspace/sessions/{sessionId}/artifacts/dag/snapshot.json`

**用途**:
- 备份和恢复
- 离线查看
- 审计追踪

---

## 🌐 前端如何获取 DAG 数据

### API 端点

#### 1. 获取全局 DAG

**端点**: `GET /api/dag/global`

**代码位置**: `ResearchSessionsApi.Runtime.cs` (第 169 行)

```csharp
app.MapGet("/api/dag/global", async (
    DagStore dag,
    CancellationToken ct) =>
{
    var snap = await dag.GetSnapshotForListAsync(ResearchSession.GlobalDagId, ct);
    return Results.Ok(new { dag = snap });
});
```

**前端调用**:
```typescript
// axiom-client.ts (第 464 行)
export async function getGlobalDagSnapshot(): Promise<DagSnapshot | null> {
  const result = await fetchJson<{ dag?: DagSnapshot }>("/api/dag/global")
  return result?.dag ?? null
}
```

---

#### 2. 获取 Session DAG

**端点**: `GET /api/sessions/{sessionId}/dag`

**代码位置**: `ResearchSessionsApi.Runtime.cs` (第 187 行)

```csharp
app.MapGet("/api/sessions/{sessionId}/dag", async (
    string sessionId,
    ResearchSessionManager sessions,
    DagStore dag,
    CancellationToken ct) =>
{
    var session = sessions.GetOrCreate(sessionId);
    var snap = await dag.GetSnapshotForListAsync(dagId, ct, currentSessionId: session.Id);
    return Results.Ok(new { dag = snap });
});
```

**前端调用**:
```typescript
// axiom-client.ts (第 451 行)
export async function getDagSnapshot(sessionId: string): Promise<DagSnapshot | null> {
  const result = await fetchJson<{ dag?: DagSnapshot }>(`/api/sessions/${sessionId}/dag`)
  return result?.dag ?? null
}
```

---

### 前端数据流

```
┌─────────────────────────────────────────────────────────────┐
│  1. 组件请求 DAG 数据                                          │
│     - LandingDagViewer.tsx (第 144 行)                        │
│     - WorkflowTopology.tsx                                    │
└─────────────────────────────────────────────────────────────┘
                            │
                            ▼
┌─────────────────────────────────────────────────────────────┐
│  2. 调用 API 客户端                                            │
│     - getGlobalDagSnapshot()                                 │
│     - getDagSnapshot(sessionId)                              │
└─────────────────────────────────────────────────────────────┘
                            │
                            ▼
┌─────────────────────────────────────────────────────────────┐
│  3. HTTP 请求到后端                                            │
│     - GET /api/dag/global                                    │
│     - GET /api/sessions/{id}/dag                             │
└─────────────────────────────────────────────────────────────┘
                            │
                            ▼
┌─────────────────────────────────────────────────────────────┐
│  4. 后端查询 Neo4j                                            │
│     - DagStore.GetSnapshotForListAsync()                     │
│     - BuildSnapshotFromGraphAsync()                          │
│     - client.GetKnowledgeNodesAsync()                        │
│     - client.GetPlanNodesAsync()                             │
└─────────────────────────────────────────────────────────────┘
                            │
                            ▼
┌─────────────────────────────────────────────────────────────┐
│  5. 返回 JSON 数据                                             │
│     - { dag: { nodes: [...], edges: [...] } }               │
└─────────────────────────────────────────────────────────────┘
                            │
                            ▼
┌─────────────────────────────────────────────────────────────┐
│  6. 前端渲染 DAG 图                                            │
│     - transformApiData()                                     │
│     - CanvasRenderer                                         │
└─────────────────────────────────────────────────────────────┘
```

---

## 🔄 DAG 更新触发时机

### 1. Research Round 结束后

**流程**:
```
Worker Phase 完成
    │
    ▼
dag_builder agent 提取知识
    │
    ▼
生成 DAG Mutation
    │
    ▼
DAG Consensus 验证
    │
    ▼
ApplyMutationAsync → Neo4j
```

**代码位置**: `VibeOrchestrator.DagConsensus.cs`

---

### 2. Milestone 创建时

**流程**:
```
Brief Generation 完成
    │
    ▼
research_assistant 创建 Plan 节点
    │
    ▼
ApplyMutationAsync → Neo4j
```

**代码位置**: `VibeMilestoneLoopRunner.cs` → `CreateMilestonePlanNodesAsync()`

---

### 3. 文件上传后（Knowledge Extraction）

**流程**:
```
文件上传
    │
    ▼
UploadExtractionService 提取知识
    │
    ▼
创建 Knowledge 节点
    │
    ▼
ApplyMutationAsync → Neo4j
```

**代码位置**: `UploadExtractionService.cs` (第 492 行)

---

## 📝 关键代码位置总结

| 功能 | 文件 | 关键方法 |
|------|------|---------|
| **Neo4j 连接配置** | `Program.cs` | `AddAevatarGraphNeo4j()` |
| **DAG 更新入口** | `DagStore.cs` | `ApplyMutationAsync()` |
| **写入 Plan 节点** | `DagStore.cs` | `client.CreatePlanNodeAsync()` |
| **写入 Knowledge 节点** | `DagStore.cs` | `client.UpsertNodeAsync()` |
| **写入边** | `DagStore.cs` | `client.CreateEdgeAsync()` |
| **查询 DAG** | `DagStore.cs` | `BuildSnapshotFromGraphAsync()` |
| **API 端点** | `ResearchSessionsApi.Runtime.cs` | `GET /api/dag/global` |
| **Consensus 验证** | `DagConsensusRunner.cs` | `RunAsync()` |
| **应用 Mutation** | `VibeOrchestrator.DagConsensus.cs` | `RunDagApplyAsync()` |

---

## 🔍 验证 Neo4j 连接

### 检查后端日志

**查找日志**:
```
[DagStore] ApplyMutationAsync starting: dagId=...
[DagStore] Creating PlanNode: nodeId=...
[DagStore] PlanNode created successfully: nodeId=...
[DagStore] Upserting KnowledgeNode: nodeId=...
[DagStore] KnowledgeNode upserted successfully: nodeId=...
```

**错误日志**:
```
[DagStore] Failed to query Neo4j for global DAG: dagId=..., error=...
[DagStore] Failed to upsert dag node {NodeId}: {Error}
```

---

### 使用 Neo4j Browser

**访问**: http://localhost:7474

**查询示例**:
```cypher
// 查看所有节点
MATCH (n) RETURN n LIMIT 50

// 查看 Plan 节点
MATCH (n:PlanNode) RETURN n

// 查看 Knowledge 节点
MATCH (n:KnowledgeNode) RETURN n LIMIT 50

// 查看边
MATCH (a)-[r]->(b) RETURN a, r, b LIMIT 50
```

---

## 💡 重要说明

### 1. 前端不直接连接 Neo4j

- ✅ **前端只通过 HTTP API 获取数据**
- ✅ **所有 Neo4j 操作都在后端**
- ✅ **前端无法直接修改 Neo4j**

---

### 2. DAG 更新是异步的

- DAG 更新发生在后端 Agent 执行过程中
- 前端通过轮询或 SSE 获取更新
- 更新可能延迟几秒到几分钟

---

### 3. 数据一致性

- **Neo4j 是 Single Source of Truth (SSoT)**
- 文件快照 (`snapshot.json`) 是备份
- 查询时优先从 Neo4j 读取

---

### 4. Session 隔离

- 每个 Session 有独立的 DAG
- Global DAG 包含所有 Session 的节点
- Session DAG 只包含当前 Session 的节点

---

## 🔗 相关文档

- `DAG_NODE_CREATION_RULES.md` - DAG 节点创建规则
- `DAG_CONSENSUS_UPDATE_ISSUE.md` - DAG Consensus 更新问题
- `NEO4J_AUTH_FIX.md` - Neo4j 认证问题修复
- `TEST_NEO4J.md` - Neo4j 测试指南

---

*最后更新: 2025-01-28*
