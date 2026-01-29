# 状态框更新修复

## 🔴 问题

前端状态框内容和右边 DAG 图显示的内容不一致：
- ✅ DAG 图已经更新增加了节点（显示最新状态）
- ❌ 状态框显示的还是 round1 的信息（显示旧状态）

---

## 🔍 根本原因

### 数据源不同

| 组件 | 数据源 | 更新机制 | 数据范围 |
|------|--------|---------|---------|
| **DAG 图** | `/api/sessions/{id}/dag` | 10秒轮询 + 事件触发 | 所有 rounds 累积 |
| **状态框** | `/api/sessions/{id}/status` | 3秒轮询 | 最后一个 run 的 steps |

### 关键问题

1. **状态框只显示最后一个 run 的 steps**
   - `SessionUiSnapshotStore` 的 `RunSteps` 只保存一个 `RunId`
   - 新 run 开始时，会清空之前的 steps

2. **状态框更新延迟**
   - 状态框每 3 秒轮询一次
   - 如果新 run 开始后 3 秒内没有轮询，状态框会显示旧信息

3. **DAG 图更新更快**
   - DAG 图监听 `aevatar.vibe.dag_updated` 事件，立即更新
   - 状态框依赖轮询，可能有延迟

---

## ✅ 修复方案

### 修复内容

**文件**: `sisyphus-frontend/src/hooks/use-axiom-stream.ts`

**修改**: 在 `RUN_STARTED` 事件时立即刷新状态框

**代码位置**: 第 213-244 行

**修改前**:
```typescript
stream.on("RUN_STARTED", (event) => {
  markConnected()
  addRawEvent(event)
  const runId = (event as { runId?: string }).runId || `run-${Date.now()}`
  setCurrentRun(runId)
  addMessage({...})
})
```

**修改后**:
```typescript
stream.on("RUN_STARTED", async (event) => {
  markConnected()
  addRawEvent(event)
  const runId = (event as { runId?: string }).runId || `run-${Date.now()}`
  setCurrentRun(runId)
  
  // Immediately refresh session status to show new run's steps
  // This ensures status box updates when a new research round starts
  if (sessionId) {
    try {
      const status = await getSessionStatus(sessionId)
      if (status) {
        const { setSessionStatus } = useSisyphusStore.getState()
        const sessionStatus: SessionStatus = {
          runId: status.runId,
          updatedAt: status.updatedAt,
          steps: {
            order: status.steps.order,
            map: status.steps.map,
            running: status.steps.running,
            done: status.steps.done,
          },
          agents: status.agents,
          runningTools: status.runningTools,
        }
        setSessionStatus(sessionStatus)
        console.debug("[AxiomStream] Status refreshed immediately on run start")
      }
    } catch (err) {
      console.debug("[AxiomStream] Failed to refresh status on run start:", err)
    }
  }
  
  addMessage({...})
})
```

---

## 💡 修复原理

### 问题流程（修复前）

```
新 Run 开始
  ↓
触发 RUN_STARTED 事件
  ↓
状态框等待 3 秒轮询 ⏳
  ↓
状态框显示新 run 的 steps ✅
```

**问题**: 在 3 秒轮询间隔内，状态框显示的是旧 run 的信息。

---

### 修复后流程

```
新 Run 开始
  ↓
触发 RUN_STARTED 事件
  ↓
立即调用 getSessionStatus() ⚡
  ↓
立即更新状态框 ✅
```

**优势**: 状态框在新 run 开始时立即更新，不需要等待轮询。

---

## 🧪 验证修复

### 测试步骤

1. **启动应用**:
   ```bash
   cd apps/Aevatar.VibeResearching
   ./boot.sh
   ```

2. **触发新的 research round**:
   - 发送新的研究问题
   - 观察状态框和 DAG 图

3. **验证状态框更新**:
   - 状态框应该立即显示新 round 的 steps
   - DAG 图应该显示累积的节点
   - 两者应该同步更新

---

### 预期行为

| 时间点 | DAG 图 | 状态框 |
|--------|--------|--------|
| Round 1 开始 | 0 个节点 | Round 1 steps (立即显示) ✅ |
| Round 1 完成 | 6 个节点 | Round 1 steps (done) ✅ |
| Round 2 开始 | 6 个节点 | Round 2 steps (立即显示) ✅ |
| Round 2 完成 | 13 个节点 | Round 2 steps (done) ✅ |

---

## 📋 相关修改

### 修改的文件

1. **`sisyphus-frontend/src/hooks/use-axiom-stream.ts`**
   - 添加 `getSessionStatus` 导入
   - 添加 `SessionStatus` 类型导入
   - 在 `RUN_STARTED` 事件处理中立即刷新状态框

---

## 🔗 相关文档

- `STATUS_BOX_DAG_INCONSISTENCY.md` - 问题详细分析
- `SESSION_PERSISTENCE.md` - Session 持久化说明
- `NEO4J_COMMUNICATION_FLOW.md` - Neo4j 通信流程

---

## ✅ 修复完成

**修复时间**: 2025-01-29

**修复文件**: 
- `sisyphus-frontend/src/hooks/use-axiom-stream.ts`

**修复方法**: 
- 在 `RUN_STARTED` 事件时立即调用 `getSessionStatus()` 刷新状态框

**状态**: ✅ 已修复，等待测试验证

---

*最后更新: 2025-01-29*
