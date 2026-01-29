# DAG Consensus 被阻止文件解释

## 📄 文件说明

**文件路径**: `workspace/sessions/fc284a36a5ac4950bba7a08bf96dc1d2/artifacts/dag/consensus/20260129_031051_72b3d352760942ff961006be135b0864.json`

**文件类型**: Maker Workflow Consensus Artifact（被阻止）

**时间戳**: 2026-01-29 03:10:51 UTC

**状态**: ❌ **被阻止** (`blocked`)

---

## 🔍 文件内容解释

### 基本信息 (第 2-9 行)

```json
{
  "sessionId": "fc284a36a5ac4950bba7a08bf96dc1d2",
  "runId": "fc284a36a5ac4950bba7a08bf96dc1d2:1",
  "workflow": "maker",
  "status": "blocked",
  "error": "Direct State modification is not allowed when Event Sourcing is active (Version > 0). Use RaiseEvent to modify state, or clear EventStore to reset version.",
  "redFlags": [
    "maker_failed"
  ]
}
```

**字段说明**:

| 字段 | 值 | 说明 |
|------|-----|------|
| **sessionId** | `fc284a36a5ac4950bba7a08bf96dc1d2` | 研究会话的唯一标识符 |
| **runId** | `fc284a36a5ac4950bba7a08bf96dc1d2:1` | 研究轮次的标识符 |
| **workflow** | `"maker"` | 使用的共识工作流类型（Maker Workflow） |
| **status** | `"blocked"` | ❌ **被阻止** - Mutation 未被接受 |
| **error** | `"Direct State modification..."` | 🔴 **错误信息** - Event Sourcing 相关错误 |
| **redFlags** | `["maker_failed"]` | 红色警告标志：`maker_failed` 表示 Maker Workflow 执行失败 |

---

### ⚠️ 关键错误信息

**错误消息**:
```
Direct State modification is not allowed when Event Sourcing is active (Version > 0). 
Use RaiseEvent to modify state, or clear EventStore to reset version.
```

**错误含义**:
- 🔴 **Event Sourcing 冲突**: Maker Workflow 尝试直接修改 Agent 状态
- 🔴 **版本冲突**: Agent 的 Event Store 版本 > 0，表示已有事件历史
- 🔴 **操作不允许**: 当 Event Sourcing 激活时，不能直接修改状态，必须使用 `RaiseEvent`

**可能原因**:
1. Maker Workflow 中的 Agent 尝试直接修改状态（而不是通过事件）
2. Agent 的 Event Store 已经初始化并包含事件历史
3. Maker Workflow 配置不正确，没有正确处理 Event Sourcing

---

### 统计信息 (第 10-17 行)

```json
"stats": {
  "success": false,
  "durationMs": 6,
  "llmCalls": 0,
  "promptTokens": 0,
  "completionTokens": 0,
  "totalTokens": 0
}
```

**字段说明**:

| 字段 | 值 | 说明 |
|------|-----|------|
| **success** | `false` | ❌ 共识过程失败 |
| **durationMs** | `6` | 耗时仅 **6 毫秒** - 说明失败发生得非常快 |
| **llmCalls** | `0` | **0 次** LLM 调用 - 说明在调用 LLM 之前就失败了 |
| **promptTokens** | `0` | 没有消耗任何输入 tokens |
| **completionTokens** | `0` | 没有消耗任何输出 tokens |
| **totalTokens** | `0` | 总 token 消耗为 0 |

**分析**:
- ⚠️ **快速失败**: 仅 6 毫秒就失败，说明是在初始化阶段就出错
- ⚠️ **未调用 LLM**: 没有进行任何 LLM 调用，说明错误发生在准备阶段
- ⚠️ **无 Token 消耗**: 没有产生任何成本

---

### 候选 Mutation 信息 (第 18-23 行)

```json
"candidate": {
  "mutationId": "hpa_verification_round1",
  "authorAgent": "dag_builder",
  "nodeCount": 7,
  "edgeCount": 10
}
```

**字段说明**:

| 字段 | 值 | 说明 |
|------|-----|------|
| **mutationId** | `"hpa_verification_round1"` | Mutation 的唯一标识符（HPA 验证第一轮） |
| **authorAgent** | `"dag_builder"` | 生成这个 mutation 的 Agent |
| **nodeCount** | `7` | 要添加/更新的节点数量 |
| **edgeCount** | `10` | 要添加/更新的边数量 |

**观察**:
- 这个 mutation 包含 **7 个节点**和 **10 条边**
- 由 `dag_builder` agent 生成
- 是 HPA（Holographic Phase Amplitude）验证的第一轮

---

### 解析结果 (第 24-27 行)

```json
"extractedJson": null,
"parsed": null,
"rejectionReason": null,
"validationOutcome": null
```

**说明**:
- ❌ **所有解析字段都为 `null`**
- 说明 Maker Workflow 在解析之前就失败了
- 没有提取到任何 JSON 数据
- 没有验证结果

---

### 原始输出 (第 28 行)

```json
"rawOutput": ""
```

**说明**:
- ❌ **原始输出为空字符串**
- 说明 Maker Workflow 没有产生任何输出
- 失败发生在输出生成之前

---

## 🔄 与其他文件的对比

### 文件 1: 成功案例（已接受）

**文件**: `20260129_025057_cc58614865d741a79a59037f702410d8.json`

| 字段 | 值 |
|------|-----|
| **status** | ✅ `"accepted"` |
| **error** | `null` |
| **redFlags** | `[]` |
| **durationMs** | `134303` (134.3 秒) |
| **llmCalls** | `11` |
| **totalTokens** | `52624` |

---

### 文件 2: 失败案例（当前文件）

**文件**: `20260129_031051_72b3d352760942ff961006be135b0864.json`

| 字段 | 值 |
|------|-----|
| **status** | ❌ `"blocked"` |
| **error** | `"Direct State modification..."` |
| **redFlags** | `["maker_failed"]` |
| **durationMs** | `6` (6 毫秒) |
| **llmCalls** | `0` |
| **totalTokens** | `0` |

---

### 文件 3: 另一个失败案例

**文件**: `20260129_032232_fdab4f7ae9e64375ad5fd3926f9f3436.json`

| 字段 | 值 |
|------|-----|
| **status** | ❌ `"blocked"` |
| **error** | `"Direct State modification..."` |
| **redFlags** | `["maker_failed"]` |
| **durationMs** | `3` (3 毫秒) |
| **candidate.nodeCount** | `12` |
| **candidate.edgeCount** | `25` |

---

### 文件 4: 第三个失败案例

**文件**: `20260129_033459_aacdb757df604e3bbce18cbe1bdf3fa9.json`

| 字段 | 值 |
|------|-----|
| **status** | ❌ `"blocked"` |
| **error** | `"Direct State modification..."` |
| **redFlags** | `["maker_failed"]` |
| **durationMs** | `1` (1 毫秒) |
| **candidate.nodeCount** | `9` |
| **candidate.edgeCount** | `13` |

---

## 🔍 错误分析

### 错误模式

所有失败的 consensus artifacts 都显示相同的错误：

```
Direct State modification is not allowed when Event Sourcing is active (Version > 0). 
Use RaiseEvent to modify state, or clear EventStore to reset version.
```

**共同特征**:
- ✅ 相同的错误消息
- ✅ 相同的 `redFlags: ["maker_failed"]`
- ✅ 极短的执行时间（1-6 毫秒）
- ✅ 没有 LLM 调用
- ✅ 没有 Token 消耗

---

### 错误发生时机

**时间线**:
1. **02:50:57** - ✅ 成功（`accepted`）
2. **03:10:51** - ❌ 失败（`blocked`）- 当前文件
3. **03:22:32** - ❌ 失败（`blocked`）
4. **03:34:59** - ❌ 失败（`blocked`）

**观察**:
- 第一次共识成功
- 后续三次都失败
- 失败发生在成功之后约 20 分钟

---

### 可能原因

#### 1. Event Sourcing 状态变化

**假设**:
- 第一次共识成功后，Agent 的 Event Store 被初始化
- 后续共识尝试时，Agent 已经有事件历史（Version > 0）
- Maker Workflow 中的 Agent 尝试直接修改状态，导致错误

---

#### 2. Maker Workflow 配置问题

**假设**:
- Maker Workflow 中的 Agent 没有正确配置 Event Sourcing
- Agent 尝试直接修改状态而不是通过事件
- 需要修改 Maker Workflow 配置

---

#### 3. Agent 生命周期问题

**假设**:
- Agent 在第一次共识后被激活
- 后续共识时，Agent 状态已经存在
- Maker Workflow 没有正确处理已存在的 Agent 状态

---

## 💡 解决方案建议

### 方案 1: 修改 Maker Workflow

**检查 Maker Workflow 配置**:
- 确保 Agent 使用 `RaiseEvent` 而不是直接修改状态
- 检查 Event Sourcing 配置

**代码位置**: `workflows/maker.yaml`

---

### 方案 2: 重置 Agent 状态

**如果不需要保留状态**:
- 清除 Event Store
- 重置 Agent 版本

**注意**: ⚠️ 这会丢失 Agent 的历史状态

---

### 方案 3: 使用不同的共识工作流

**临时解决方案**:
- 使用 `verifier-quorum` 工作流代替 `maker`
- `verifier-quorum` 可能不会遇到这个问题

**配置位置**: `appsettings.json`
```json
{
  "Vibe": {
    "DagConsensus": {
      "Mode": "verifier-quorum"  // 改为 verifier-quorum
    }
  }
}
```

---

## 📊 总结

### 文件状态

| 项目 | 值 |
|------|-----|
| **共识结果** | ❌ **被阻止** (`blocked`) |
| **错误类型** | Event Sourcing 冲突 |
| **执行时间** | 6 毫秒（快速失败） |
| **LLM 调用** | 0 次 |
| **Token 消耗** | 0 tokens |
| **Red Flags** | `["maker_failed"]` |

---

### Mutation 信息

| 项目 | 值 |
|------|-----|
| **Mutation ID** | `hpa_verification_round1` |
| **作者 Agent** | `dag_builder` |
| **节点数量** | 7 个 |
| **边数量** | 10 条 |

---

### 关键发现

1. ✅ **第一次共识成功** - 说明系统基本功能正常
2. ❌ **后续共识失败** - 说明存在状态管理问题
3. ⚠️ **快速失败** - 错误发生在初始化阶段
4. 🔴 **Event Sourcing 冲突** - 需要修复 Maker Workflow 配置

---

## 🔗 相关文档

- `DAG_CONSENSUS_FILE_EXPLANATION.md` - 成功案例解释
- `DAG_CONSENSUS_WORKFLOW.md` - DAG Consensus 工作流说明
- `DAG_CONSENSUS_UPDATE_ISSUE.md` - Consensus 更新问题诊断

---

*最后更新: 2025-01-29*
