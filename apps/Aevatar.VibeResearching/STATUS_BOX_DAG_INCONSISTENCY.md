# 状态框和 DAG 图不一致问题分析

## 🔍 问题描述

**现象**: 前端状态框内容和右边 DAG 图显示的内容不一致
- ✅ DAG 图已经更新增加了节点（显示最新状态）
- ❌ 状态框显示的还是 round1 的信息（显示旧状态）

---

## 📊 数据源对比

### DAG 图数据源

**API 端点**: `GET /api/sessions/{sessionId}/dag`

**更新机制**:
1. **自动刷新**: 每 10 秒自动刷新 (`WorkflowTopology.tsx` 第 462 行)
   ```typescript
   const intervalId = setInterval(silentRefresh, 10000)
   ```

2. **事件触发**: 监听 `aevatar.vibe.dag_updated` 事件立即刷新 (`useAxiomStream.ts` 第 803 行)
   ```typescript
   stream.onCustom("aevatar.vibe.dag_updated", async (event) => {
     const dagSnapshot = await getDagSnapshot(sessionId)
     setDag({ nodes, edges })
   })
   ```

**数据内容**: 
- 所有 Knowledge 节点（累积）
- 所有 Plan 节点（累积）
- 所有边（累积）

**特点**: 
- ✅ **累积显示** - 不会清空，所有 rounds 的节点都会显示
- ✅ **实时更新** - 有新节点时立即更新

---

### 状态框数据源

**API 端点**: `GET /api/sessions/{sessionId}/status`

**更新机制**:
1. **轮询刷新**: 每 3 秒轮询一次 (`useSessionStatus.ts` 第 78 行)
   ```typescript
   intervalRef.current = setInterval(fetchStatus, intervalMs)
   ```

2. **数据来源**: `SessionUiSnapshotStore` → `ui_snapshot.json`

**数据内容**:
- `RunSteps` - 当前 run 的 steps 信息
- `RunId` - 当前 run 的 ID
- `Steps.Order` - Steps 执行顺序
- `Steps.Map` - Steps 状态映射

**特点**:
- ⚠️ **只显示最后一个 run** - `UiRunStepsSnapshot` 只有一个 `RunId`
- ⚠️ **新 run 时清空** - 当 `RunStartedEvent` 触发时，会清空之前的 steps

---

## 🔍 根本原因

### 问题 1: RunSteps 只保存最后一个 Run

**代码位置**: `SessionUiTraceRecorder.cs` (第 154-160 行)

```csharp
case RunStartedEvent e:
    currentRunId = (e.RunId ?? string.Empty).Trim();
    runStepsOrder.Clear();  // ❌ 清空之前的 steps
    runStepsMap.Clear();     // ❌ 清空之前的 steps
    await FlushSnapshotAsync();
    break;
```

**问题**:
- 当新的 research round 开始时，会触发 `RunStartedEvent`
- `RunSteps` 会被清空，只保存新 round 的 steps
- 状态框显示的是**最后一个 run** 的 steps，而不是所有 rounds 的累积信息

---

### 问题 2: DAG 和 Status 数据源不同

| 数据 | DAG 图 | 状态框 |
|------|--------|--------|
| **数据源** | `/api/sessions/{id}/dag` | `/api/sessions/{id}/status` |
| **后端存储** | Neo4j（持久化） | `ui_snapshot.json`（文件） |
| **更新频率** | 10秒 + 事件触发 | 3秒轮询 |
| **数据范围** | 所有 rounds 累积 | 最后一个 run 的 steps |
| **清空机制** | 不清空 | 新 run 时清空 |

---

### 问题 3: 状态框显示的是 Workflow Steps，不是 Round 信息

**状态框显示内容** (`WorkflowSteps.tsx`):
- `vibe.brief` - Brief 生成步骤
- `vibe.plan_dag` - DAG 计划步骤
- `vibe.worker` - Worker 执行步骤
- `vibe.dag_consensus` - DAG Consensus 步骤

**这些是 workflow steps，不是 research rounds**:
- 每个 research round 都会执行相同的 workflow steps
- 状态框只显示**当前 run** 的 steps
- 如果当前 run 是 round1，状态框就显示 round1 的 steps
- 如果当前 run 是 round2，状态框就显示 round2 的 steps

---

## 💡 为什么会出现不一致？

### 场景示例

**Round 1**:
1. 执行 workflow steps (`vibe.brief`, `vibe.plan_dag`, `vibe.worker`, `vibe.dag_consensus`)
2. 创建了 6 个 Knowledge 节点
3. DAG 图显示 6 个节点 ✅
4. 状态框显示 round1 的 steps ✅

**Round 2 开始**:
1. 触发 `RunStartedEvent`
2. `RunSteps` 被清空
3. 开始执行新的 workflow steps
4. DAG 图显示 6 个节点（round1 的节点）✅
5. 状态框显示 round2 的 steps（但可能还在执行中）⚠️

**Round 2 完成**:
1. 创建了 7 个新的 Knowledge 节点
2. DAG 图显示 13 个节点（6 + 7）✅
3. 状态框显示 round2 的 steps（已完成）✅

**问题**: 如果状态框没有及时更新，或者显示的是旧的 run 信息，就会出现不一致。

---

## 🔍 可能的具体原因

### 原因 1: UI Snapshot 没有及时更新

**问题**: `SessionUiTraceRecorder` 可能没有及时刷新 `ui_snapshot.json`

**检查点**:
- `FlushSnapshotAsync` 是否被正确调用
- `ui_snapshot.json` 文件是否及时更新
- 文件写入是否有延迟

---

### 原因 2: 状态框轮询的是旧的 Snapshot

**问题**: 前端轮询时，后端返回的是旧的 snapshot

**检查点**:
- `SessionUiSnapshotStore.LoadAsync` 是否返回最新数据
- 文件读取是否有缓存问题

---

### 原因 3: 多个 Runs 同时存在

**问题**: 如果有多个 runs 同时存在，状态框可能显示的是错误的 run

**检查点**:
- `RunSteps.RunId` 是否正确
- 前端是否根据 `runId` 过滤 steps

---

### 原因 4: 状态框显示的是历史 Run

**问题**: 状态框可能显示的是之前完成的 run，而不是当前正在执行的 run

**检查点**:
- `sessionStatus.runId` 是否是最新的
- 前端是否根据 `runId` 判断显示哪个 run 的 steps

---

## ✅ 解决方案

### 方案 1: 确保状态框显示当前 Run（推荐）

**修改**: 确保状态框只显示当前正在执行的 run 的 steps

**检查点**:
1. 后端返回的 `runId` 是否是最新的
2. 前端是否根据 `runId` 过滤 steps
3. `ui_snapshot.json` 是否及时更新

---

### 方案 2: 状态框显示所有 Runs 的累积信息

**修改**: 修改 `SessionUiTraceRecorder` 保存所有 runs 的 steps

**问题**: 
- 需要修改数据结构（从单个 `RunId` 改为多个 runs）
- 前端需要支持显示多个 runs 的 steps

**不推荐**: 这会导致状态框信息过多，难以阅读。

---

### 方案 3: 状态框显示当前 Round 的 Steps

**修改**: 状态框显示当前 research round 的所有 steps（包括所有 runs）

**实现**:
- 后端需要追踪当前 round 的所有 runs
- 前端需要根据 round 信息过滤 steps

**问题**: 
- 需要定义什么是"当前 round"
- 需要修改数据结构

---

### 方案 4: 状态框显示 DAG 相关的 Round 信息

**修改**: 状态框显示当前 DAG 对应的 round 信息

**实现**:
- 从 DAG 节点中提取 round 信息
- 显示当前 round 的步骤

**问题**: 
- DAG 节点可能不包含 round 信息
- 需要修改 DAG 数据结构

---

## 🔧 诊断步骤

### 1. 检查 UI Snapshot 文件

**文件位置**: `workspace/sessions/{sessionId}/artifacts/ui/ui_snapshot.json`

**检查内容**:
```bash
cat workspace/sessions/{sessionId}/artifacts/ui/ui_snapshot.json | jq '.runSteps'
```

**预期**:
- `runId` 应该是最新的 run ID
- `order` 应该包含当前 run 的所有 steps
- `map` 应该包含当前 run 的所有 steps 的状态

---

### 2. 检查后端 API 响应

**API**: `GET /api/sessions/{sessionId}/status`

**检查**:
```bash
curl http://localhost:5678/api/sessions/{sessionId}/status | jq '.runId, .steps'
```

**预期**:
- `runId` 应该是最新的 run ID
- `steps.order` 应该包含当前 run 的所有 steps
- `steps.map` 应该包含当前 run 的所有 steps 的状态

---

### 3. 检查前端轮询

**检查点**:
- `useSessionStatus` hook 是否正常轮询
- 轮询间隔是否合理（3秒）
- 是否有错误阻止了更新

**调试**:
```typescript
// 在 useSessionStatus.ts 中添加日志
console.log('[useSessionStatus] Fetched status:', status)
```

---

### 4. 检查 DAG 更新事件

**检查点**:
- `aevatar.vibe.dag_updated` 事件是否正常触发
- DAG 更新后是否立即刷新

**调试**:
```typescript
// 在 useAxiomStream.ts 中添加日志
console.log('[AxiomStream] DAG updated event:', event)
```

---

## 📋 预期行为

### 正常情况

| 时间点 | DAG 图 | 状态框 |
|--------|--------|--------|
| Round 1 开始 | 0 个节点 | Round 1 steps (running) |
| Round 1 完成 | 6 个节点 | Round 1 steps (done) |
| Round 2 开始 | 6 个节点 | Round 2 steps (running) |
| Round 2 完成 | 13 个节点 | Round 2 steps (done) |

---

### 异常情况（当前问题）

| 时间点 | DAG 图 | 状态框 |
|--------|--------|--------|
| Round 1 开始 | 0 个节点 | Round 1 steps (running) |
| Round 1 完成 | 6 个节点 | Round 1 steps (done) |
| Round 2 开始 | 6 个节点 | Round 1 steps (done) ❌ |
| Round 2 完成 | 13 个节点 | Round 1 steps (done) ❌ |

**问题**: 状态框没有更新到 Round 2 的 steps

---

## 🔍 代码位置总结

| 组件 | 文件 | 关键代码 |
|------|------|---------|
| **DAG 图更新** | `workflow-topology.tsx` | 第 426-464 行 |
| **DAG 事件监听** | `use-axiom-stream.ts` | 第 803-835 行 |
| **状态框更新** | `use-session-status.ts` | 第 35-65 行 |
| **状态框显示** | `workflow-steps.tsx` | 第 126-210 行 |
| **后端 Status API** | `ResearchSessionsApi.StatusAndDeliverables.cs` | 第 13-98 行 |
| **UI Snapshot 更新** | `SessionUiTraceRecorder.cs` | 第 154-160 行 |

---

## 💡 建议的修复方案

### 方案 A: 确保状态框及时更新（最简单）

**修改**: 确保 `SessionUiTraceRecorder` 在 `RunStartedEvent` 时立即刷新 snapshot

**代码位置**: `SessionUiTraceRecorder.cs` (第 154-160 行)

**当前代码**:
```csharp
case RunStartedEvent e:
    currentRunId = (e.RunId ?? string.Empty).Trim();
    runStepsOrder.Clear();
    runStepsMap.Clear();
    await FlushSnapshotAsync();  // ✅ 已经调用了
    break;
```

**检查**: `FlushSnapshotAsync` 是否正常工作

---

### 方案 B: 增加状态框刷新频率

**修改**: 减少状态框轮询间隔（从 3 秒改为 1 秒）

**代码位置**: `App.tsx` (第 60-64 行)

**修改**:
```typescript
useSessionStatus({
  sessionId: currentSessionId,
  intervalMs: 1000,  // 改为 1 秒
  enabled: isConnected,
});
```

**注意**: 这会增加后端负载

---

### 方案 C: 状态框监听 RunStartedEvent

**修改**: 状态框监听 `RunStartedEvent`，立即刷新

**代码位置**: `use-axiom-stream.ts`

**添加**:
```typescript
stream.onCustom("aevatar.vibe.run_started", async (event) => {
  // 立即刷新状态框
  const { refresh } = useSessionStatus.getState()
  refresh()
})
```

---

## 🔗 相关文档

- `SESSION_PERSISTENCE.md` - Session 持久化说明
- `NEO4J_COMMUNICATION_FLOW.md` - Neo4j 通信流程
- `DAG_CONSENSUS_FILE_EXPLANATION.md` - DAG Consensus 文件解释

---

*最后更新: 2025-01-29*
