# Session 持久化和恢复说明

## 📋 概述

停止程序后重新运行，**Session 会部分恢复**，但**不会继续运行**。

---

## ✅ 会保留的内容

### 1. Session 元数据

**位置**: `workspace/.data/vibe_sessions.json`

**内容**:
- Session ID
- Provider Name（LLM 提供者）
- DAG ID
- 创建时间

**恢复时机**: 程序启动时自动恢复

**代码位置**: `Program.cs` (第 338-347 行)

```csharp
// Best-effort: restore persisted session registry (if any)
try
{
    var sessions = app.Services.GetRequiredService<ResearchSessionManager>();
    await sessions.LoadPersistedSessionsAsync(CancellationToken.None);
}
catch
{
    // best-effort only
}
```

---

### 2. DAG 数据（Knowledge Graph）

**位置**: Neo4j 数据库

**内容**:
- 所有 Knowledge 节点
- 所有 Plan 节点
- 节点之间的关系（边）
- 节点的时间戳和元数据

**恢复方式**: 
- 重启后从 Neo4j 重新加载
- 通过 `/api/dag/{dagId}` API 查询

**重要**: DAG 数据**完全保留**，不受程序重启影响。

---

### 3. UI Snapshot（浏览器刷新恢复）

**位置**: `workspace/sessions/{sessionId}/artifacts/ui/ui_snapshot.json`

**内容**:
- 消息快照（bounded）
- 消息元数据（agent/stepName）
- Tool 输出（bounded）
- Run 步骤（最后一个 run）

**用途**: 浏览器刷新时恢复 UI 状态

**代码位置**: `SessionUiSnapshotStore.cs`

---

### 4. Workspace 文件

**位置**: `workspace/sessions/{sessionId}/`

**内容**:
- 上传的文件（`artifacts/uploads/`）
- 生成的文件
- DAG artifacts（`artifacts/dag/`）
- UI artifacts（`artifacts/ui/`）

**恢复方式**: 文件系统保留，重启后仍可访问

---

## ❌ 会丢失的内容

### 1. Session 运行状态

**丢失内容**:
- 当前正在执行的 run
- Agent 的内存状态
- 未完成的步骤
- 流式输出连接（SSE）

**原因**: Session 是 **in-memory** 的（内存中）

**代码说明**:
```csharp
// ResearchSessionManager.cs 第 134-141 行
/// <summary>
/// Get an existing session or create a new in-memory session with the specified id.
///
/// Why:
/// - Sessions are ephemeral in this MVP (in-memory only).
/// - Tool calls may arrive after restart, or from UI that only persisted File-SSoT workspace.
/// - We still want to allow edits (DAG/plan/mesh) for an existing sessionId.
/// </summary>
```

---

### 2. SSE 连接

**丢失内容**:
- 浏览器与后端的 SSE 连接
- 流式消息推送

**恢复方式**: 
- 浏览器刷新或重新连接时会自动恢复
- UI Snapshot 会通过 bootstrap 事件恢复部分状态

---

### 3. 正在执行的 Agent 任务

**丢失内容**:
- 正在运行的 Planner/Reasoner/Verifier 任务
- 未完成的 LLM 调用
- 中间计算结果

**影响**: 如果程序重启时正在执行任务，该任务会中断

---

## 🔄 恢复流程

### 程序启动时

1. **加载 Session 索引** (`LoadPersistedSessionsAsync`)
   - 从 `workspace/.data/vibe_sessions.json` 读取
   - 恢复 Session 列表
   - 创建 in-memory Session 对象

2. **恢复 DAG 数据**
   - 从 Neo4j 查询 DAG 快照
   - 重建 DAG 图结构

3. **恢复 UI Snapshot**（浏览器连接时）
   - 读取 `artifacts/ui/ui_snapshot.json`
   - 通过 SSE bootstrap 事件发送给前端

---

### 浏览器连接时

1. **建立 SSE 连接**
2. **接收 bootstrap 事件**:
   - `aevatar.vibe.message_meta_snapshot`
   - `aevatar.ui.tools_snapshot`
   - `aevatar.ui.run_steps_snapshot`
3. **恢复 UI 状态**

---

## 📊 数据持久化位置

| 数据类型 | 存储位置 | 是否保留 | 恢复方式 |
|---------|---------|---------|---------|
| **Session 元数据** | `workspace/.data/vibe_sessions.json` | ✅ | 自动恢复 |
| **DAG 数据** | Neo4j 数据库 | ✅ | 自动恢复 |
| **UI Snapshot** | `workspace/sessions/{id}/artifacts/ui/ui_snapshot.json` | ✅ | 浏览器连接时恢复 |
| **上传文件** | `workspace/sessions/{id}/artifacts/uploads/` | ✅ | 文件系统保留 |
| **Session 运行状态** | 内存 | ❌ | 丢失 |
| **SSE 连接** | 内存 | ❌ | 需要重新连接 |
| **正在执行的任务** | 内存 | ❌ | 中断 |

---

## 💡 实际行为

### 场景 1: 程序重启，Session 未在执行任务

**结果**:
- ✅ Session 列表恢复
- ✅ DAG 数据恢复
- ✅ 可以继续使用该 Session
- ✅ 可以查看历史数据

**操作**:
- 前端会自动显示恢复的 Session
- 可以继续发送新消息
- 可以查看 DAG 图

---

### 场景 2: 程序重启，Session 正在执行任务

**结果**:
- ✅ Session 列表恢复
- ✅ DAG 数据恢复（已完成的节点）
- ❌ 正在执行的任务中断
- ❌ 未完成的任务丢失

**影响**:
- 需要重新发送消息或重新开始任务
- 已完成的 DAG 节点保留

---

### 场景 3: 浏览器刷新（程序未重启）

**结果**:
- ✅ UI Snapshot 恢复
- ✅ 消息历史恢复
- ✅ Tool 输出恢复
- ❌ SSE 连接断开（会重新连接）

**操作**:
- 浏览器会自动重新连接 SSE
- UI 状态会通过 snapshot 恢复

---

## 🔍 验证 Session 恢复

### 方法 1: 查看 Session 列表

```bash
# 查看持久化的 Session 索引
cat workspace/.data/vibe_sessions.json

# 或通过 API
curl http://localhost:5678/api/sessions
```

---

### 方法 2: 检查 DAG 数据

```bash
# 查看 DAG 快照
cat workspace/dags/global/artifacts/dag/snapshot.json | jq '.nodes | length'

# 或通过 API
curl http://localhost:5678/api/dag/global | jq '.dag.nodes | length'
```

---

### 方法 3: 查看 UI Snapshot

```bash
# 查找 Session 的 UI Snapshot
find workspace/sessions -name "ui_snapshot.json" -type f

# 查看内容
cat workspace/sessions/{sessionId}/artifacts/ui/ui_snapshot.json | jq .
```

---

## 📝 代码实现

### Session 持久化

**代码位置**: `ResearchSessionManager.cs` → `PersistSessionAsync`

```csharp
public async Task PersistSessionAsync(ResearchSession session, CancellationToken ct = default)
{
    try
    {
        if (_sessionStore != null)
        {
            await _sessionStore.SaveAsync(BuildRecord(session), ct);
            return;
        }

        if (_indexStore != null)
        {
            await UpsertIndexEntryAsync(session, ct);
        }
    }
    catch (Exception ex)
    {
        _logger?.LogWarning(ex, "Failed to persist session index for {SessionId}", session.Id);
    }
}
```

---

### Session 恢复

**代码位置**: `ResearchSessionManager.cs` → `LoadPersistedSessionsAsync`

```csharp
public async Task LoadPersistedSessionsAsync(CancellationToken ct = default)
{
    try
    {
        var records = await LoadPersistedRecordsAsync(ct);
        if (records.Count == 0)
            return;

        foreach (var record in records)
        {
            // 恢复 Session 元数据
            var providerName = NormalizeOptional(record.ProviderName);
            var dagId = NormalizeOptional(record.DagId);
            var createdAt = TryReadTimestamp(record.CreatedAt);

            // 创建 in-memory Session（注意：是新的空 Session）
            GetOrCreate(record.SessionId, providerName, createdAt, dagId);
        }
    }
    catch (Exception ex)
    {
        _logger?.LogWarning(ex, "Failed to load persisted sessions index.");
    }
}
```

---

## ⚠️ 重要说明

### Session 是 Ephemeral（临时性的）

**代码注释**:
```csharp
/// <summary>
/// Get an existing session or create a new in-memory session with the specified id.
///
/// Why:
/// - Sessions are ephemeral in this MVP (in-memory only).
/// - Tool calls may arrive after restart, or from UI that only persisted File-SSoT workspace.
/// - We still want to allow edits (DAG/plan/mesh) for an existing sessionId.
/// </summary>
```

**含义**:
- Session 对象本身是内存中的
- 重启后会创建新的空 Session
- 但 Session ID 和 DAG 数据会保留

---

## 🚀 最佳实践

### 1. 避免在任务执行中重启

- 等待当前任务完成后再重启
- 或使用 `--no-kill` 选项避免意外停止

---

### 2. 定期检查 Session 状态

```bash
# 查看所有 Session
curl http://localhost:5678/api/sessions | jq .

# 查看特定 Session 的状态
curl http://localhost:5678/api/sessions/{sessionId}/status | jq .
```

---

### 3. 备份重要数据

虽然 DAG 数据在 Neo4j 中，但建议：
- 定期备份 Neo4j 数据库
- 备份 `workspace/` 目录
- 导出重要的 Session 数据

---

## 📋 总结

### ✅ 会保留

1. **Session 元数据** - Session ID、配置等
2. **DAG 数据** - 所有 Knowledge 和 Plan 节点
3. **文件** - 上传的文件和生成的文件
4. **UI Snapshot** - 用于浏览器刷新恢复

### ❌ 会丢失

1. **Session 运行状态** - 内存中的状态
2. **正在执行的任务** - 未完成的任务会中断
3. **SSE 连接** - 需要重新连接

### 🔄 恢复行为

- **程序重启**: Session 列表恢复，但运行状态丢失
- **浏览器刷新**: UI Snapshot 恢复，SSE 重新连接
- **DAG 数据**: 始终保留（Neo4j）

---

## 🔗 相关文档

- `docs/FRONTEND_SESSION_API.md` - 前端 Session API 文档
- `src/Aevatar.VibeResearching.Api/Sessions/docs/README.md` - Session 模块文档
- `DAG_EMPTY_ISSUE.md` - DAG 数据恢复问题

---

*最后更新: 2025-01-28*
