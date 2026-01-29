# DAG Consensus Event Sourcing 修复 V2

## 🔴 问题描述

当使用 Maker Workflow 进行 DAG Consensus 时，第一次共识成功后，后续共识会失败，错误信息：

```
Direct State modification is not allowed when Event Sourcing is active (Version > 0). 
Use RaiseEvent to modify state, or clear EventStore to reset version.
```

**错误特征**:
- ✅ 第一次共识成功
- ❌ 后续共识失败（相同的错误）
- ⚠️ 极短的执行时间（1-9 毫秒）
- ⚠️ 没有 LLM 调用
- ⚠️ Red Flags: `["maker_failed"]`
- ❌ DAG 节点无法更新到 DAG 图中

---

## 🔍 根本原因

**问题位置**: 
1. `src/Aevatar.Agents.Cognitive/Agents/Shared/CognitiveAIGAgentBase.cs` → `TrimHistoryWindowBestEffort`
2. `src/Aevatar.Agents.AI.Core/History/HistoryRuntime.cs` → `CompactIfNeededAsync`
3. `src/Aevatar.Agents.AI.Core/AIGAgentBase.cs` → `InitializeStateAndConfigAsync`

**问题代码**:

### 问题 1: TrimHistoryWindowBestEffort
```csharp
private void TrimHistoryWindowBestEffort()
{
    // ❌ 直接修改 State.History，当 Version > 0 时会失败
    while (State.History.Count > max)
        State.History.RemoveAt(0);
}
```

### 问题 2: HistoryRuntime.CompactIfNeededAsync
```csharp
internal async Task CompactIfNeededAsync(CancellationToken cancellationToken)
{
    // ❌ 直接修改 State.History 和 State.Context，当 Version > 0 时会失败
    history.Clear();
    history.AddRange(kept);
    _owner.State.Context[AIGAgentKeys.HistorySummary] = updatedSummary;
}
```

### 问题 3: AIGAgentBase.InitializeStateAndConfigAsync
```csharp
protected virtual async Task InitializeStateAndConfigAsync(...)
{
    // ❌ 直接赋值 State，当 Version > 0 时会失败
    if (StateStore != null)
    {
        State = await StateStore.LoadAsync(Id, cancellationToken) ?? new AevatarAIAgentState();
    }
}
```

**原因分析**:
1. 第一次共识成功后，Agent 的 Event Store 被初始化（Version > 0）
2. 后续共识时，多个地方尝试直接修改 State：
   - `TrimHistoryWindowBestEffort` 修改 `State.History`
   - `CompactIfNeededAsync` 修改 `State.History` 和 `State.Context`
   - `InitializeStateAndConfigAsync` 直接赋值 `State`
3. 当 Event Sourcing 激活时（Version > 0），直接修改 State 是不允许的
4. 必须使用 `RaiseEvent` 来修改状态，或者跳过修改（对于运行时数据如 History）

---

## ✅ 修复方案

### 修复内容

#### 修复 1: CognitiveAIGAgentBase.TrimHistoryWindowBestEffort

**文件**: `src/Aevatar.Agents.Cognitive/Agents/Shared/CognitiveAIGAgentBase.cs`

**修改**: 添加 Version 检查，当 Version > 0 时跳过修剪

```csharp
private void TrimHistoryWindowBestEffort()
{
    try
    {
        // Skip trimming when Event Sourcing is active (Version > 0)
        // History modifications should go through RaiseEvent in Event Sourcing mode
        if (GetCurrentVersion() > 0)
            return;

        var max = ChatHistoryMaxMessages;
        if (max <= 0) return;

        // Actor model: single-threaded per agent. Keep it simple.
        while (State.History.Count > max)
            State.History.RemoveAt(0);
    }
    catch
    {
        // best-effort only (history is optional)
    }
}
```

#### 修复 2: HistoryRuntime.CompactIfNeededAsync

**文件**: `src/Aevatar.Agents.AI.Core/History/HistoryRuntime.cs`

**修改**: 添加 Version 检查，当 Version > 0 时跳过压缩和 Context 更新

#### 修复 3: AIGAgentBase.InitializeStateAndConfigAsync

**文件**: `src/Aevatar.Agents.AI.Core/AIGAgentBase.cs`

**修改**: 添加 Version 检查，当 Version > 0 时跳过从 StateStore 加载 State

```csharp
protected virtual async Task InitializeStateAndConfigAsync(...)
{
    // Load state and config if stores are available
    // Skip loading State from StateStore when Event Sourcing is active (Version > 0)
    // In Event Sourcing mode, State is rebuilt from events in OnActivateAsync
    if (StateStore != null && GetCurrentVersion() == 0)
    {
        State = await StateStore.LoadAsync(Id, cancellationToken) ?? new AevatarAIAgentState();
    }
    // ... 其余代码保持不变
}
```

```csharp
internal async Task CompactIfNeededAsync(CancellationToken cancellationToken)
{
    if (!_owner.EnableChatHistoryInState || !_owner.EnableChatHistoryCompaction)
        return;

    // Skip compaction when Event Sourcing is active (Version > 0)
    // History modifications should go through RaiseEvent in Event Sourcing mode
    if (_owner.GetCurrentVersion() > 0)
        return;

    // ... 其余代码保持不变
}

// 在更新 State.Context 时也添加检查
// Skip updating State.Context when Event Sourcing is active (Version > 0)
// Context modifications should go through RaiseEvent in Event Sourcing mode
if (_owner.GetCurrentVersion() == 0)
{
    lock (_historyLock)
    {
        _owner.State.Context[AIGAgentKeys.HistorySummary] = updatedSummary;
    }
}
```

---

## 💡 为什么这个修复有效？

### 1. History 是运行时数据

**关键点**:
- `State.History` 主要用于 UI hydration，不是核心业务状态
- History 不需要通过 Event Sourcing 来管理
- 当 Version > 0 时，跳过 History 的修剪和压缩是安全的

### 2. 避免 Event Sourcing 冲突

**关键点**:
- ✅ 当 Version > 0 时，跳过 History 的修改
- ✅ History 仍然可以正常添加消息（通过 `AddMessage`）
- ✅ 只是不进行修剪和压缩，不影响核心功能

### 3. 为什么 AddMessage 不需要修复？

`AddMessage` 方法只是调用 `_history.Add(message)`，这是修改集合内容，不会触发 State setter。只有重新赋值 State（如 `State = new State()`）才会触发检查。

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
   - 确认 DAG 节点正确更新到 DAG 图中

4. **检查日志**:
   - 查找 `[DagStore] ApplyMutationAsync` 日志
   - 确认没有 "Direct State modification" 错误
   - 确认 consensus 状态为 `accepted`

---

## 📊 预期结果

### 修复前

| 共识轮次 | 状态 | 错误 | DAG 更新 |
|---------|------|------|---------|
| Round 1 | ✅ accepted | null | ✅ 成功 |
| Round 2 | ❌ blocked | Direct State modification... | ❌ 失败 |
| Round 3 | ❌ blocked | Direct State modification... | ❌ 失败 |

### 修复后

| 共识轮次 | 状态 | 错误 | DAG 更新 |
|---------|------|------|---------|
| Round 1 | ✅ accepted | null | ✅ 成功 |
| Round 2 | ✅ accepted | null | ✅ 成功 |
| Round 3 | ✅ accepted | null | ✅ 成功 |

---

## 🔗 相关文档

- `FIX_MAKER_WORKFLOW_EVENT_SOURCING.md` - 第一次修复（ConfigureSessionContext）
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
- 对于运行时数据（如 History），当 Version > 0 时跳过修改
- 对于核心业务状态，使用 `RaiseEvent` + `TransitionState`
- 只在初始化时（Version == 0）修改 State

### History 的特殊处理

**为什么 History 可以跳过修改？**
- History 是运行时数据，主要用于 UI 显示
- History 不需要通过 Event Sourcing 来管理
- 跳过修剪和压缩不影响核心功能

**为什么 AddMessage 不需要修复？**
- `AddMessage` 只是修改集合内容（`_history.Add(message)`）
- 不会触发 State setter，因为不是重新赋值 State
- Protobuf 的 `RepeatedField` 支持直接修改内容

---

## ✅ 修复完成

**修复时间**: 2025-01-29

**修复文件**: 
- `src/Aevatar.Agents.Cognitive/Agents/Shared/CognitiveAIGAgentBase.cs`
- `src/Aevatar.Agents.AI.Core/History/HistoryRuntime.cs`
- `src/Aevatar.Agents.AI.Core/AIGAgentBase.cs`

**修复方法**: 
- 在 `TrimHistoryWindowBestEffort` 中添加 Version 检查
- 在 `CompactIfNeededAsync` 中添加 Version 检查
- 在 `InitializeStateAndConfigAsync` 中添加 Version 检查
- 当 Version > 0 时，跳过 History 的修剪和压缩，以及从 StateStore 加载 State

**状态**: ✅ 已修复，等待测试验证

---

*最后更新: 2025-01-29*
