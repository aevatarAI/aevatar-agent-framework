# Maker Workflow Event Sourcing 修复验证

## ✅ 修复状态

**修复完成**: `CognitiveAIGAgentBase.cs` 中的 `ConfigureSessionContext` 方法已修复

**修复文件**: `src/Aevatar.Agents.Cognitive/Agents/Shared/CognitiveAIGAgentBase.cs`

**修复内容**: 移除了对 `State.Context` 的直接修改，改用私有字段 `_sessionId`

---

## 🔍 修复详情

### 修复前的问题代码

```csharp
public void ConfigureSessionContext(...)
{
    _sessionId = sessionId.Trim();
    State.Context[ChatRequest.SessionIdKey] = _sessionId;  // ❌ 直接修改 State
    ...
}
```

**问题**: 当 Event Sourcing 激活时（Version > 0），直接修改 `State.Context` 会触发错误：
```
Direct State modification is not allowed when Event Sourcing is active (Version > 0). 
Use RaiseEvent to modify state, or clear EventStore to reset version.
```

### 修复后的代码

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

**修复方法**: 
- ✅ 移除了 `State.Context[ChatRequest.SessionIdKey] = _sessionId;` 这行代码
- ✅ 只使用私有字段 `_sessionId` 存储 session ID
- ✅ `ApplySessionContext` 方法使用 `_sessionId` 字段，不依赖 `State.Context`
- ✅ `OnActivateAsync` 可以从 `State.Context` 恢复 `_sessionId`（如果存在）

---

## 🧪 验证步骤

### 1. 重新编译项目

```bash
cd /Users/chronoai/aevatar-agent-framework
dotnet build
```

**预期结果**: 编译成功，无错误

---

### 2. 重启应用

```bash
cd apps/Aevatar.VibeResearching
./boot.sh
```

**预期结果**: 应用正常启动

---

### 3. 运行 DAG Consensus 测试

#### 测试场景 1: 多次共识

1. **创建新会话**
2. **触发第一个 research round**
   - 观察共识结果：应该为 `accepted`
   - 检查共识文件：`workspace/sessions/{sessionId}/artifacts/dag/consensus/`
3. **触发第二个 research round**
   - 观察共识结果：应该为 `accepted`（不再失败）
   - 检查共识文件：确认没有 `blocked` 状态
4. **触发第三个 research round**
   - 观察共识结果：应该为 `accepted`
   - 确认所有共识都成功

#### 测试场景 2: 检查共识文件

查看共识文件内容：

```bash
# 查看最新的共识文件
ls -lt apps/Aevatar.VibeResearching/workspace/sessions/*/artifacts/dag/consensus/ | head -5

# 检查文件内容
cat apps/Aevatar.VibeResearching/workspace/sessions/{sessionId}/artifacts/dag/consensus/{latest_file}.json | jq '.status'
```

**预期结果**:
- ✅ `status: "accepted"`（所有共识都成功）
- ❌ 不应该有 `status: "blocked"`
- ❌ 不应该有 `error: "Direct State modification..."`

---

### 4. 检查日志

查看应用日志，确认没有错误：

```bash
# 查看日志中的错误
grep -i "Direct State modification" logs/*.log

# 查看共识相关日志
grep -i "DagConsensus\|Maker workflow" logs/*.log | tail -20
```

**预期结果**:
- ✅ 没有 "Direct State modification" 错误
- ✅ 共识状态为 `accepted`
- ✅ Maker workflow 执行成功

---

## 📊 预期结果对比

### 修复前

| 共识轮次 | 状态 | 错误 | 执行时间 | LLM 调用 |
|---------|------|------|---------|---------|
| Round 1 | ✅ accepted | null | ~30s | 5+ |
| Round 2 | ❌ blocked | Direct State modification... | ~14ms | 0 |
| Round 3 | ❌ blocked | Direct State modification... | ~14ms | 0 |

### 修复后

| 共识轮次 | 状态 | 错误 | 执行时间 | LLM 调用 |
|---------|------|------|---------|---------|
| Round 1 | ✅ accepted | null | ~30s | 5+ |
| Round 2 | ✅ accepted | null | ~30s | 5+ |
| Round 3 | ✅ accepted | null | ~30s | 5+ |

---

## 🔍 其他潜在问题

### 事件处理器中的状态修改

**位置**: `CognitiveWorkerGAgent.cs` 和 `CognitiveCoordinatorGAgent.cs`

**代码示例**:
```csharp
[EventHandler]
public async Task HandleExecuteStepRequest(ExecuteStepRequestEvent request)
{
    CustomState.Status = WorkerStatus.WsExecuting;  // ⚠️ 直接修改状态
    CustomState.CurrentStepId = request.StepId;
    ...
}
```

**状态**: ⚠️ **可能需要修复**

**说明**: 
- 这些修改发生在事件处理器中
- 如果 Event Sourcing 已激活，可能需要使用 `RaiseEvent` + `TransitionState`
- 但根据错误信息，主要问题在 `ConfigureSessionContext`，已修复

**建议**: 
- 如果后续仍然出现类似错误，需要修复这些事件处理器
- 目前先验证 `ConfigureSessionContext` 的修复是否解决了问题

---

## 📝 验证清单

- [ ] 重新编译项目成功
- [ ] 应用正常启动
- [ ] 第一个共识成功（`accepted`）
- [ ] 第二个共识成功（`accepted`，不再失败）
- [ ] 第三个共识成功（`accepted`）
- [ ] 日志中没有 "Direct State modification" 错误
- [ ] 共识文件中没有 `blocked` 状态
- [ ] Maker workflow 正常执行（有 LLM 调用和 token 消耗）

---

## 🔗 相关文档

- `FIX_MAKER_WORKFLOW_EVENT_SOURCING.md` - 修复说明
- `DAG_CONSENSUS_BLOCKED_EXPLANATION.md` - 被阻止文件解释
- `DAG_CONSENSUS_BLOCKED_FILE_EXPLANATION.md` - 具体文件解释

---

## ✅ 修复完成确认

**修复时间**: 2025-01-29

**修复文件**: 
- `src/Aevatar.Agents.Cognitive/Agents/Shared/CognitiveAIGAgentBase.cs`

**修复方法**: 
- 移除 `ConfigureSessionContext` 中对 `State.Context` 的直接修改
- 依赖 `_sessionId` 私有字段和 `ApplySessionContext` 方法

**状态**: ✅ **已修复，等待测试验证**

---

*最后更新: 2025-01-29*
