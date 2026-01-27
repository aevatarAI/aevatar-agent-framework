# Research Round 和 DAG 图显示流程

## 📋 目录

1. [Research Round 触发时机](#research-round-触发时机)
2. [Research Round 执行流程](#research-round-执行流程)
3. [DAG 节点创建时机](#dag-节点创建时机)
4. [DAG 图显示机制](#dag-图显示机制)
5. [完整流程图](#完整流程图)

---

## Research Round 触发时机

### 1. API 端点

**`POST /api/sessions/{sessionId}/input`**

- **位置**: `ResearchSessionsApi.InputAndFacts.cs` (第 18-97 行)
- **触发条件**: 
  - 用户在前端发送消息
  - 消息体包含 `{ "message": "...", "mode": "milestone" }`
  - `mode` 默认为 `"milestone"`（如果未指定）

### 2. 执行模式

根据 `input.Mode` 参数，系统会执行不同的研究流程：

| Mode | 执行方法 | 说明 |
|------|---------|------|
| `milestone` (默认) | `ExecuteMilestoneLoopRunAsync` | 里程碑驱动的完整研究流程 |
| `vibe_milestone` | `ExecuteMilestoneLoopRunAsync` | 同 milestone |
| `research` | `ExecuteMilestoneLoopRunAsync` | 同 milestone |
| `vibe` | `ExecuteMilestoneLoopRunAsync` | 同 milestone |
| `vibe_loop` | `ExecuteVibeGoalLoopRunAsync` | 迭代循环（旧版） |
| `vibe_researching` | `ExecuteVibeResearchingRunAsync` | 单轮研究（调试用） |
| `chat` | `ExecuteChatRunAsync` | 纯聊天模式（无研究编排） |

**代码位置**: `ResearchRunExecutor.cs` (第 68-106 行)

---

## Research Round 执行流程

### Milestone 模式（默认）

#### 1. 入口点

**`ExecuteMilestoneLoopRunAsync`** (`VibeMilestoneLoopRunner.cs`)

#### 2. 执行步骤

```
1. 检查是否已有 Brief（研究计划）
   ├─ 如果没有 → 执行 Planning Round 生成 Brief
   │   └─ 调用 ExecuteOneRoundAsync (Brief + Plan 生成)
   └─ 如果有 → 继续下一步

2. 遍历每个 Milestone（里程碑）
   ├─ 对每个 Milestone 执行 Deep Research Loop
   │   ├─ 迭代 1: ExecuteOneRoundAsync
   │   ├─ 迭代 2: ExecuteOneRoundAsync
   │   ├─ ...
   │   └─ 直到 Milestone 完成或达到最大迭代次数
   └─ 评估 Milestone 完成度 (EvaluateMilestoneCompletionAsync)

3. 所有 Milestones 完成后结束
```

#### 3. 单轮执行 (`ExecuteOneRoundAsync`)

**位置**: `VibeOrchestrator.cs` (第 57-393 行)

**执行顺序**:

```
1. Brief Generation (research_assistant [MODE:BRIEF])
   ├─ 生成研究计划
   └─ 创建 Plan 节点（Milestones）→ DAG

2. Plan Phase (research_assistant [MODE:PLAN])
   ├─ 生成执行计划
   └─ 创建 Plan 节点（Round Plans）→ DAG

3. Worker Phase
   ├─ Planner → 生成执行计划
   ├─ Reasoner → 生成推理过程
   ├─ Verifier → 验证假设
   └─ Dag_builder → 从 worker outputs 提取知识 → 创建 Knowledge 节点 → DAG

4. DAG Consensus Phase
   ├─ 应用 Dag_builder 的输出
   └─ 更新 DAG 节点

5. Summary Phase (research_assistant [MODE:SUMMARY])
   └─ 生成研究总结
```

---

## DAG 节点创建时机

### 1. Plan 节点创建

#### Brief Generation 阶段

**位置**: `VibeOrchestrator.cs` (第 200-250 行)

```csharp
// 生成 Brief 后
var mm = BuildMilestonesPlanDagMutation(session.Id, runId, question, saved);
if (mm != null)
{
    dagSnap = await _core.Dag.ApplyMutationAsync(dagId, mm, innerCt);
    // 创建 Plan 节点（Milestones）
}
```

**日志**: `[VibeOrchestrator] Applying milestones DAG mutation: sessionId=..., nodeCount=X`

#### Plan Phase 阶段

**位置**: `VibeOrchestrator.ResearchAssistant.cs`

- `research_assistant` 在 `[MODE:PLAN]` 模式下生成计划
- Plan 节点通过 `DagApplyAsync` 创建

### 2. Knowledge 节点创建

#### Dag Builder 阶段

**位置**: `VibeOrchestrator.ExecuteOneRound.Parts.cs` (第 600-700 行)

```csharp
// Dag_builder 从 worker outputs 提取知识
case "dag_builder":
    var dagBuilderOutput = await RunDagBuilderAsync(...);
    outputs["dag_builder"] = dagBuilderOutput;
    // Dag_builder 的输出包含 Knowledge 节点定义
    break;
```

#### DAG Consensus Phase

**位置**: `VibeOrchestrator.cs` (第 350-370 行)

```csharp
dagSnap = await _core.Dag.LoadSnapshotAsync(dagId, ct);
var dagResult = await RunDagApplyAsync(
    session, runId, materials, dagSnap, outputs,
    emitAssistantDelta, consensusProvider, ct);
// 应用 Dag_builder 的输出，创建 Knowledge 节点
```

**日志**: 
- `[DagStore] ApplyMutationAsync starting: dagId=global, nodeCount=X`
- `[DagStore] Creating PlanNode: nodeId=..., label=...`
- `[DagStore] Upserting KnowledgeNode: nodeId=..., type=..., label=...`
- `[DagStore] BuildSnapshotFromGraphAsync (global): knowledgeNodes=X, planNodes=Y, edges=Z`

---

## DAG 图显示机制

### 1. 前端组件

**`LandingDagViewer`** (`sisyphus-frontend/src/pages/landing/landing-dag-viewer.tsx`)

### 2. 数据获取

#### API 端点

**`GET /api/dag/global`**

- **位置**: `ResearchSessionsApi.Runtime.cs` (第 169-175 行)
- **返回**: 所有会话的所有节点（全局 DAG）
- **格式**: `{ ok: true, dagId: "global", dag: { nodes: [...], edges: [...] } }`

#### 前端调用

**`getGlobalDagSnapshot()`** (`sisyphus-frontend/src/lib/axiom-client.ts`)

```typescript
export async function getGlobalDagSnapshot(): Promise<DagSnapshot | null> {
  const result = await fetchJson<{ dag?: DagSnapshot }>("/api/dag/global")
  return result?.dag ?? null
}
```

### 3. 数据加载时机

#### 初始加载

**`useEffect`** (第 165-167 行)

```typescript
// Fetch on mount
useEffect(() => {
  fetchDagData()
}, [fetchDagData])
```

- 组件挂载时自动获取一次 DAG 数据

#### 手动刷新

**刷新按钮** (第 550-560 行)

```typescript
<button
  onClick={fetchDagData}
  className="..."
>
  <RefreshCw className="..." />
  Refresh
</button>
```

- 用户点击 "Refresh" 按钮时手动刷新

### 4. 自动刷新机制

**⚠️ 当前实现：无自动刷新**

- 前端只在组件挂载时获取一次数据
- 没有轮询（polling）或 SSE 推送机制
- 需要用户手动点击 "Refresh" 按钮才能看到最新数据

### 5. 数据转换

**`transformApiData()`** (第 23-43 行)

```typescript
function transformApiData(snapshot: DagSnapshot): DAGGraph {
  const nodes: DAGNode[] = (snapshot.nodes || []).map((node: ApiDagNode) => ({
    id: node.id,
    label: node.label || node.id,
    kind: (node.kind as 'Plan' | 'Knowledge') || 'Knowledge',
    status: 'completed',
    type: node.type || 'Knowledge',
    // ...
  }))
  
  const edges = (snapshot.edges || []).map((edge) => ({
    source: edge.fromId,
    target: edge.toId,
    type: edge.type,
  }))
  
  return { nodes, edges }
}
```

---

## 完整流程图

```
用户发送消息
    ↓
POST /api/sessions/{sessionId}/input
    ↓
ResearchRunExecutor.ExecuteAsync()
    ↓
ExecuteMilestoneLoopRunAsync()
    ↓
┌─────────────────────────────────────┐
│ ExecuteOneRoundAsync()              │
│                                     │
│ 1. Brief Generation                │
│    └─ 创建 Plan 节点 (Milestones) │
│                                     │
│ 2. Plan Phase                       │
│    └─ 创建 Plan 节点 (Round Plans) │
│                                     │
│ 3. Worker Phase                    │
│    ├─ Planner                      │
│    ├─ Reasoner                     │
│    ├─ Verifier                     │
│    └─ Dag_builder                  │
│       └─ 提取知识 → Knowledge 节点 │
│                                     │
│ 4. DAG Consensus Phase            │
│    └─ 应用节点到 Neo4j             │
│                                     │
│ 5. Summary Phase                   │
└─────────────────────────────────────┘
    ↓
Neo4j 数据库更新
    ↓
前端手动刷新 /api/dag/global
    ↓
LandingDagViewer 显示 DAG 图
```

---

## 🔍 关键发现

### 1. DAG 更新是实时的

- 节点创建后立即写入 Neo4j
- API `/api/dag/global` 实时查询 Neo4j

### 2. 前端显示不是实时的

- **问题**: 前端只在组件挂载时获取一次数据
- **解决方案**: 
  - 用户需要手动点击 "Refresh" 按钮
  - 或者实现自动刷新机制（轮询或 SSE）

### 3. 节点创建位置

- **Plan 节点**: Brief Generation 和 Plan Phase
- **Knowledge 节点**: Dag Builder 和 DAG Consensus Phase

---

## 💡 建议改进

### 1. 添加自动刷新机制

**选项 A: 轮询（Polling）**

```typescript
useEffect(() => {
  fetchDagData()
  const interval = setInterval(fetchDagData, 5000) // 每 5 秒刷新
  return () => clearInterval(interval)
}, [fetchDagData])
```

**选项 B: SSE 推送**

- 后端在节点创建后发送 SSE 事件
- 前端监听事件并自动刷新

### 2. 添加加载状态指示

- 显示 "正在加载..." 状态
- 显示最后更新时间

### 3. 添加错误处理

- 网络错误时显示友好提示
- 自动重试机制

---

## 📝 总结

1. **Research Round 触发**: `POST /api/sessions/{sessionId}/input` (默认 mode="milestone")
2. **执行流程**: Milestone Loop → ExecuteOneRoundAsync → Workers → DAG Consensus
3. **节点创建**: Brief/Plan 阶段创建 Plan 节点，Dag_builder 阶段创建 Knowledge 节点
4. **DAG 显示**: 前端通过 `GET /api/dag/global` 获取数据，**需要手动刷新**
5. **实时性**: 后端实时更新，前端需要手动刷新才能看到最新数据
