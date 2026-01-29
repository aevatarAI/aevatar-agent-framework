# Maker Workflow Event Sourcing 修复

## 🔴 问题描述

当使用 Maker Workflow 进行 DAG Consensus 时，第一次共识成功后，后续共识会失败，错误信息：

```
Direct State modification is not allowed when Event Sourcing is active (Version > 0). 
Use RaiseEvent to modify state, or clear EventStore to reset version.
```

**错误特征**:
- ✅ 第一次共识成功
- ❌ 后续共识失败（相同的错误）
- ⚠️ 极短的执行时间（1-6 毫秒）
- ⚠️ 没有 LLM 调用
- ⚠️ Red Flags: `["maker_failed"]`

---

## 🔍 根本原因

**问题位置**: `src/Aevatar.Agents.Cognitive/Agents/Shared/CognitiveAIGAgentBase.cs`

**问题代码** (第 77 行):
```csharp
public void ConfigureSessionContext(...)
{
    _sessionId = sessionId.Trim();
    State.Context[ChatRequest.SessionIdKey] = _sessionId;  // ❌ 直接修改 State
    ...
}
```

**原因分析**:
1. 第一次共识成功后，Agent 的 Event Store 被初始化（Version > 0）
2. 后续共识时，`ConfigureSessionContext` 尝试直接修改 `State.Context`
3. 当 Event Sourcing 激活时（Version > 0），直接修改 State 是不允许的
4. 必须使用 `RaiseEvent` 来修改状态，或者避免修改 State

---

## ✅ 修复方案

### 修复内容

**文件**: `src/Aevatar.Agents.Cognitive/Agents/Shared/CognitiveAIGAgentBase.cs`

**修改**: 移除了对 `State.Context` 的直接修改

**修复后的代码**:
```csharp
public void ConfigureSessionContext(
    string sessionId,
    bool enableSessionMemory,
    bool enableAgentMemory)
{
    if (string.IsNullOrWhiteSpace(sessionId))
        return;

    _sessionId = sessionId.Trim();
    
    // NOTE: Do NOT modify State.Context here to avoid Event Sourcing conflicts.
    // When Event Sourcing is active (Version > 0), direct State modification is not allowed.
    // The _sessionId field is sufficient - ApplySessionContext() uses it directly,
    // and OnActivateAsync() can restore it from State.Context if needed.
    // This fixes: "Direct State modification is not allowed when Event Sourcing is active (Version > 0)"

    EnableSessionMemoryStoreAppend = enableSessionMemory;
    EnableMemoryStoreAppend = enableAgentMemory;
}
```

---

## 💡 为什么这个修复有效？

### 1. `ApplySessionContext` 只使用私有字段

**代码位置**: `CognitiveAIGAgentBase.cs` (第 83-89 行)

```csharp
protected void ApplySessionContext(ChatRequest request)
{
    if (string.IsNullOrWhiteSpace(_sessionId))
        return;

    request.SetSessionId(_sessionId);  // ✅ 只使用 _sessionId 字段
}
```

**说明**: `ApplySessionContext` 方法只使用 `_sessionId` 私有字段，不依赖 `State.Context`。

---

### 2. `OnActivateAsync` 可以从 State.Context 恢复

**代码位置**: `CognitiveAIGAgentBase.cs` (第 48-63 行)

```csharp
protected override async Task OnActivateAsync(CancellationToken ct = default)
{
    await base.OnActivateAsync(ct);

    // Best-effort: restore session id from persisted state context.
    if (State.Context.TryGetValue(ChatRequest.SessionIdKey, out var raw) &&
        !string.IsNullOrWhiteSpace(raw))
    {
        _sessionId = raw.Trim();  // ✅ 从 State.Context 恢复
    }
    ...
}
```

**说明**: 如果 State.Context 中有 sessionId，会在初始化时恢复。如果没有，`_sessionId` 字段会在 `ConfigureSessionContext` 中设置。

---

### 3. 避免 Event Sourcing 冲突

**关键点**:
- ✅ `_sessionId` 字段不涉及 Event Sourcing
- ✅ `ApplySessionContext` 不修改 State
- ✅ 只在初始化时（Version == 0）从 State.Context 读取
- ✅ 运行时只使用 `_sessionId` 字段

---

## 🧪 验证修复

### 测试步骤

1. **重新编译项目**:
   ```bash
   dotnet build
   ```

2. **重启应用**:
   ```bash
   cd apps/Aevatar.VibeResearching
   ./boot.sh
   ```

3. **运行 DAG Consensus**:
   - 触发多个 research rounds
   - 观察 consensus artifacts
   - 确认后续共识不再失败

4. **检查日志**:
   - 查找 `[DagStore] ApplyMutationAsync` 日志
   - 确认没有 "Direct State modification" 错误
   - 确认 consensus 状态为 `accepted`

---

## 📊 预期结果

### 修复前

| 共识轮次 | 状态 | 错误 |
|---------|------|------|
| Round 1 | ✅ accepted | null |
| Round 2 | ❌ blocked | Direct State modification... |
| Round 3 | ❌ blocked | Direct State modification... |

---

### 修复后

| 共识轮次 | 状态 | 错误 |
|---------|------|------|
| Round 1 | ✅ accepted | null |
| Round 2 | ✅ accepted | null |
| Round 3 | ✅ accepted | null |

---

## 🔗 相关文档

- `DAG_CONSENSUS_BLOCKED_EXPLANATION.md` - 被阻止文件解释
- `DAG_CONSENSUS_FILE_EXPLANATION.md` - 成功案例解释
- `DAG_CONSENSUS_WORKFLOW.md` - DAG Consensus 工作流说明

---

## 📝 技术细节

### Event Sourcing 约束

**规则**: 当 Agent 的 Event Store 版本 > 0 时，不能直接修改 State。

**原因**: 
- Event Sourcing 要求所有状态变更都通过事件记录
- 直接修改 State 会破坏事件流的完整性
- 必须使用 `RaiseEvent` + `TransitionState` 来修改状态

**修复策略**:
- 避免在运行时修改 State
- 使用私有字段存储运行时数据
- 只在初始化时（Version == 0）修改 State

---

## ✅ 修复完成

**修复时间**: 2025-01-29

**修复文件**: 
- `src/Aevatar.Agents.Cognitive/Agents/Shared/CognitiveAIGAgentBase.cs`

**修复方法**: 
- 移除 `ConfigureSessionContext` 中对 `State.Context` 的直接修改
- 依赖 `_sessionId` 私有字段和 `ApplySessionContext` 方法

**状态**: ✅ 已修复，等待测试验证

---

*最后更新: 2025-01-29*
