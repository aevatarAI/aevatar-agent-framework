# 状态框与DAG图信息同步分析

## 概述

本文档分析前端左侧状态框（WorkflowSteps）和右侧DAG图（WorkflowTopology）的数据同步情况。

## 数据源分析

### 1. 右侧DAG图 (`WorkflowTopology`)

**数据来源：**
- Store: `useSisyphusStore().dag` (类型: `DAGGraph | null`)
- 更新机制：
  1. **SSE流更新**: `useAxiomStream` hook 监听 `aevatar.vibe.dag_snapshot` 事件
  2. **手动刷新**: 调用 `getDagSnapshot(sessionId)` API
  3. **DAG更新事件**: 监听 `aevatar.vibe.dag_updated` 事件后自动刷新

**显示统计信息：**
- `nodeCount`: `dag.nodes.length`
- `edgeCount`: `dag.edges.length`
- `planCount`: `dag.nodes.filter(n => n.kind === 'Plan').length`
- `knowledgeCount`: `dag.nodes.filter(n => n.kind === 'Knowledge').length`

**位置：** `TopologyHeader` 组件（第52-64行）

```typescript
// workflow-topology/topology-header.tsx
<p className="text-[10px] text-text-muted font-mono tracking-wide text-pretty">
  {nodeCount > 0 ? (
    <span>
      n={nodeCount} e={edgeCount}
      {(planCount > 0 || knowledgeCount > 0) && (
        <span className="ml-2">
          (<span className="text-blue-400">P:{planCount}</span>{' '}
          <span className="text-green-400">K:{knowledgeCount}</span>)
        </span>
      )}
    </span>
  ) : 'Workflow Dependency Graph'}
</p>
```

### 2. 左侧状态框 (`WorkflowSteps`)

**数据来源：**
- Store: `useSisyphusStore().sessionStatus` (类型: `SessionStatus | null`)
- 更新机制：
  1. **轮询**: `useSessionStatus` hook 每3秒轮询 `/api/sessions/{sessionId}/status`
  2. **SSE流更新**: `useAxiomStream` hook 监听 `RUN_STARTED` 事件后立即刷新

**显示信息：**
- `runningSteps.size`: 运行中的步骤数量
- `doneSteps.size`: 完成的步骤数量
- 步骤列表：显示运行中、最近完成的、待执行的步骤

**位置：** `WorkflowSteps` 组件（第189-192行）

```typescript
// workflow-steps.tsx
<div className="text-[10px] font-mono text-text-muted">
  <span className="text-neon-cyan">{runningSteps.size}</span> running · 
  <span className="text-neon-green ml-1">{doneSteps.size}</span> done
</div>
```

## 同步机制分析

### ✅ 已实现的同步机制

1. **RUN_STARTED 事件同步**
   - 位置: `use-axiom-stream.ts` 第214-248行
   - 机制: 当新研究轮次开始时，立即刷新 `sessionStatus`
   - 注释说明: "This ensures status box updates when a new research round starts"

```typescript
// RUN_STARTED 事件处理
stream.on("RUN_STARTED", async (event) => {
  // ...
  // Immediately refresh session status to show new run's steps
  // This ensures status box updates when a new research round starts
  // Fixes issue where status box shows old round's steps while DAG shows new nodes
  if (sessionId) {
    const status = await getSessionStatus(sessionId)
    // 更新 sessionStatus
  }
})
```

2. **DAG更新事件同步**
   - 位置: `use-axiom-stream.ts` 第830-850行
   - 机制: 监听 `aevatar.vibe.dag_updated` 事件后自动获取最新DAG

### ⚠️ 潜在的同步问题

1. **数据源不同**
   - DAG图: 从 `dag` store 获取（通过SSE流或API）
   - 状态框: 从 `sessionStatus` store 获取（通过轮询或API）
   - **问题**: 两个数据源可能不同步，导致显示不一致

2. **更新频率不同**
   - DAG图: 实时更新（SSE流）或手动刷新
   - 状态框: 每3秒轮询一次
   - **问题**: 状态框可能有最多3秒的延迟

3. **更新时机不同**
   - DAG图: 在 `dag_snapshot` 或 `dag_updated` 事件时更新
   - 状态框: 在 `RUN_STARTED` 事件时更新，或每3秒轮询
   - **问题**: 如果DAG更新但未触发 `RUN_STARTED`，状态框可能不会立即更新

## 建议的改进方案

### 方案1: 统一数据源（推荐）

让 `WorkflowSteps` 也使用 `dag` store 的数据来计算统计信息：

```typescript
// workflow-steps.tsx
const dag = useSisyphusStore((s) => s.dag)
const dagStats = useMemo(() => {
  if (!dag?.nodes) return { planCount: 0, knowledgeCount: 0, totalCount: 0 }
  const planNodes = dag.nodes.filter((n) => n.kind === 'Plan')
  const knowledgeNodes = dag.nodes.filter((n) => n.kind === 'Knowledge')
  return {
    planCount: planNodes.length,
    knowledgeCount: knowledgeNodes.length,
    totalCount: dag.nodes.length,
  }
}, [dag])

// 在显示中添加DAG统计
<div className="text-[10px] font-mono text-text-muted">
  <span className="text-neon-cyan">{runningSteps.size}</span> running · 
  <span className="text-neon-green ml-1">{doneSteps.size}</span> done
  {dagStats.totalCount > 0 && (
    <>
      {' · '}
      <span className="text-blue-400">P:{dagStats.planCount}</span>
      {' '}
      <span className="text-green-400">K:{dagStats.knowledgeCount}</span>
    </>
  )}
</div>
```

### 方案2: 增强同步机制

在 `dag_updated` 事件处理中也刷新 `sessionStatus`：

```typescript
// use-axiom-stream.ts
stream.onCustom("aevatar.vibe.dag_updated", async (event) => {
  // ... 现有DAG刷新逻辑 ...
  
  // 同时刷新 sessionStatus 以保持同步
  if (sessionId) {
    try {
      const status = await getSessionStatus(sessionId)
      if (status) {
        const { setSessionStatus } = useSisyphusStore.getState()
        setSessionStatus(status)
      }
    } catch (err) {
      console.debug("[AxiomStream] Failed to refresh status on DAG update:", err)
    }
  }
})
```

### 方案3: 使用统一的更新事件

确保后端在DAG更新时也发送 `RUN_STARTED` 或类似的同步事件，让前端知道需要同时更新两个数据源。

## 验证方法

1. **检查数据一致性**
   - 打开浏览器开发者工具
   - 观察 `dag` 和 `sessionStatus` 的更新时机
   - 对比左侧状态框和右侧DAG图的显示是否一致

2. **测试场景**
   - 创建新会话
   - 上传文件创建新节点
   - 观察状态框和DAG图是否同步更新

3. **日志检查**
   - 查看控制台日志中的 `[AxiomStream]` 相关日志
   - 确认 `dag_snapshot` 和 `sessionStatus` 的更新时机

## 相关文件

- `apps/Aevatar.VibeResearching/sisyphus-frontend/src/components/sisyphus/workflow-steps.tsx`
- `apps/Aevatar.VibeResearching/sisyphus-frontend/src/components/sisyphus/workflow-topology/workflow-topology.tsx`
- `apps/Aevatar.VibeResearching/sisyphus-frontend/src/components/sisyphus/workflow-topology/topology-header.tsx`
- `apps/Aevatar.VibeResearching/sisyphus-frontend/src/hooks/use-axiom-stream.ts`
- `apps/Aevatar.VibeResearching/sisyphus-frontend/src/hooks/use-dag-interactions.ts`
- `apps/Aevatar.VibeResearching/sisyphus-frontend/src/store/sisyphus-store.ts`

## 结论

**当前状态：**
- ✅ 右侧DAG图使用 `dag` store，实时更新
- ✅ 左侧状态框使用 `sessionStatus` store，每3秒轮询
- ✅ **已实现方案1**：左侧状态框现在也显示DAG统计信息（Plan和Knowledge节点数量），确保数据一致性

**实现状态：**
- ✅ 方案1已实现：`WorkflowSteps` 组件已集成 `useDagInteractions` hook，显示DAG统计信息
- 修改文件：`apps/Aevatar.VibeResearching/sisyphus-frontend/src/components/sisyphus/workflow-steps.tsx`
- 修改内容：
  1. 导入 `useDagInteractions` hook
  2. 获取 `dagStats` 统计数据
  3. 在状态框摘要中添加 Plan 和 Knowledge 节点数量显示

**效果：**
- 左侧状态框现在显示：`X running · Y done · P:Z K:W`（当有DAG节点时）
- 与右侧DAG图的统计信息保持一致，使用相同的数据源（`dag` store）
