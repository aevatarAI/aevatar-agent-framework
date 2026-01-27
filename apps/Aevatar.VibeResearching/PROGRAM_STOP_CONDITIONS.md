# 程序停止条件和显示信息

## 📋 概述

从用户在文本框中输入信息开始，程序会执行一系列研究步骤，直到满足停止条件。本文档详细说明程序在哪里停止，以及停止时显示的信息。

---

## 🔄 执行流程

### 1. 用户输入入口

**API 端点**: `POST /api/sessions/{sessionId}/input`

**代码位置**: `ResearchSessionsApi.InputAndFacts.cs`

```csharp
[HttpPost("{sessionId}/input")]
public async Task<IActionResult> PostInputAndFacts(
    string sessionId,
    [FromBody] SessionInputInDto input,
    CancellationToken ct)
{
    // Fire-and-forget: 立即返回，后台执行
    _ = Task.Run(async () => {
        await _executor.ExecuteAsync(session, runId, input, ct);
    }, ct);
    
    return Accepted(new { runId });
}
```

**特点**: Fire-and-forget 模式，API 立即返回，研究在后台执行。

---

### 2. 执行模式选择

**代码位置**: `ResearchRunExecutor.cs` → `ExecuteAsync`

**默认模式**: `"milestone"`（里程碑驱动的研究）

**模式列表**:
- `"milestone"` / `"vibe_milestone"` / `"research"` / `"vibe"` → 里程碑循环
- `"vibe_loop"` / `"vibe_goal_loop"` → 目标循环（已弃用）
- `"vibe_researching"` / `"axiom"` / `"single"` → 单轮研究
- `"chat"` → 纯聊天模式

---

### 3. 里程碑循环执行

**代码位置**: `ResearchRunExecutor.cs` → `ExecuteMilestoneLoopRunAsync`

**主要步骤**:

1. **Materials 加载** (`vibe.materials`)
   - 加载 DAG 快照
   - 构建 Materials Context

2. **里程碑循环** (`VibeMilestoneLoopRunner.ExecuteByMilestonesAsync`)
   - 遍历所有 milestones
   - 对每个 milestone 执行 deep research
   - 评估 milestone 完成度
   - 自动扩展（如果需要）

3. **完成处理**
   - 发布 `TextMessageEndEvent`
   - 发布 `RunFinishedEvent`

---

## 🛑 停止条件

### 停止位置

**代码位置**: `VibeMilestoneLoopRunner.cs` → `ExecuteByMilestonesAsync` (第 606-618 行)

```csharp
if (milestonesExecuted >= totalMilestones)
{
    emitAssistantDelta($"\n\n## Research Complete!\n\nAll {totalMilestones} milestones have been completed");
    if (extensionRound > 0)
    {
        emitAssistantDelta($" (including {extensionRound} auto-extension round{(extensionRound > 1 ? "s" : "")})");
    }
    emitAssistantDelta(".\n");
}
else
{
    emitAssistantDelta($"\n\n## Research Paused\n\nCompleted {milestonesExecuted}/{totalMilestones} milestones. Reason: {stopReason}\n");
}
```

### 停止条件

#### ✅ 条件 1: 所有 Milestones 完成

**条件**: `milestonesExecuted >= totalMilestones`

**显示信息**:
```
## Research Complete!

All {totalMilestones} milestones have been completed.
```

**如果有自动扩展**:
```
## Research Complete!

All {totalMilestones} milestones have been completed (including {extensionRound} auto-extension round(s)).
```

**stopReason**: `"completed"` 或 `"auto_extended"`

---

#### ⏸️ 条件 2: 研究暂停

**条件**: `milestonesExecuted < totalMilestones`

**显示信息**:
```
## Research Paused

Completed {milestonesExecuted}/{totalMilestones} milestones. Reason: {stopReason}
```

**可能的 stopReason**:
- `"completed"`: 所有 milestones 完成（正常情况下不会出现）
- `"cancelled"`: 用户取消或操作被中断
- `"error"`: 发生错误
- `"no_milestones"`: 没有生成 milestones
- `"interrupted"`: 被用户输入中断
- `"auto_extended"`: 自动扩展后完成（会重置为 "completed"）

---

### 其他停止场景

#### 场景 1: 用户取消

**代码位置**: `ResearchRunExecutor.cs` → `ExecuteMilestoneLoopRunAsync` (第 433-452 行)

**触发条件**: `OperationCanceledException` 被捕获

**显示信息**:
- 发布 `CustomEvent`: `"aevatar.scientific.run_canceled"`
- 发布 `RunFinishedEvent`: `{ ok: false, canceled: true, error: "run canceled" }`

**日志**:
```
[Scientific] Milestone loop run canceled: {RunId}
```

---

#### 场景 2: 发生错误

**代码位置**: `ResearchRunExecutor.cs` → `ExecuteMilestoneLoopRunAsync` (第 454-473 行)

**触发条件**: 任何 `Exception` 被捕获

**显示信息**:
- 发布 `RunErrorEvent`: `{ Message: error, Code: "SRA_MILESTONE_LOOP_RUN_ERROR" }`
- 发布 `RunFinishedEvent`: `{ ok: false, error: ex.Message }`

**日志**:
```
[Scientific] Milestone loop run failed: {Message}
```

---

#### 场景 3: 没有 Milestones

**代码位置**: `VibeMilestoneLoopRunner.cs` → `ExecuteByMilestonesAsync` (第 138-185 行)

**触发条件**: `totalMilestones == 0`

**显示信息**:
```
## Research complete.

No milestones were generated. The research question may already be fully answered or requires clarification.
```

**stopReason**: `"no_milestones"`

---

#### 场景 4: 用户中断（Interruption）

**代码位置**: `VibeMilestoneLoopRunner.cs` → `ExecuteByMilestonesAsync` (第 93-124 行)

**触发条件**: 检测到用户输入中断

**处理流程**:
1. 分析用户意图 (`AnalyzeUserIntentAsync`)
2. 处理用户意图 (`HandleUserIntentAsync`)
3. 如果只是进度查询，返回早期结果

**stopReason**: `"progress_inquiry_handled"` 或 `"interrupted"`

---

## 📊 停止信息结构

### RunFinishedEvent

**代码位置**: `ResearchRunExecutor.cs` → `ExecuteMilestoneLoopRunAsync` (第 408-426 行)

```csharp
session.Events.Publish(new RunFinishedEvent
{
    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
    ThreadId = session.Id,
    RunId = runId,
    Result = new
    {
        ok = loopResult.Ok,
        assistantMessageId,
        assistant = assistant.ToString(),
        mode = "milestone",
        milestones = new
        {
            executed = loopResult.MilestonesExecuted,
            total = loopResult.TotalMilestones,
            stopReason = loopResult.StopReason
        }
    }
});
```

**Result 结构**:
```json
{
  "ok": true/false,
  "assistantMessageId": "msg:{sessionId}:assistant:{runId}",
  "assistant": "完整的助手回复文本",
  "mode": "milestone",
  "milestones": {
    "executed": 3,
    "total": 5,
    "stopReason": "completed" | "cancelled" | "error" | "no_milestones" | "interrupted" | "auto_extended"
  }
}
```

---

### MilestoneLoopResult

**代码位置**: `VibeMilestoneLoopRunner.cs` → `ExecuteByMilestonesAsync` (第 631-639 行)

```csharp
return new MilestoneLoopResult
{
    Ok = stopReason == "completed" || stopReason == "auto_extended",
    StopReason = stopReason,
    MilestonesExecuted = milestonesExecuted,
    TotalMilestones = totalMilestones,
    MilestonesSkipped = milestonesSkipped,
    ExtensionRound = extensionRound
};
```

**属性说明**:
- `Ok`: `true` 当 `stopReason` 为 `"completed"` 或 `"auto_extended"`
- `StopReason`: 停止原因（见上方）
- `MilestonesExecuted`: 已执行的 milestones 数量
- `TotalMilestones`: 总 milestones 数量
- `MilestonesSkipped`: 跳过的 milestones 数量（已完成的）
- `ExtensionRound`: 自动扩展轮数

---

## 🎯 停止信息显示位置

### 1. 前端 UI（SSE 流）

**事件流**: `GET /api/sessions/{sessionId}/events`

**关键事件**:
- `TEXT_MESSAGE_CONTENT`: 包含停止信息（`## Research Complete!` 或 `## Research Paused`）
- `RUN_FINISHED`: 包含完整的停止结果

**代码位置**: `ResearchSessionsApi.AgUiEvents.cs`

---

### 2. 日志文件

**日志级别**: `Information` 或 `Error`

**关键日志**:
```
[MilestoneLoop] All {Count} milestones completed. Attempting auto-extension (round {Round})
[Scientific] Milestone loop run canceled: {RunId}
[Scientific] Milestone loop run failed: {Message}
```

---

### 3. 会话文件

**位置**: `workspace/sessions/{sessionId}/runs/{runId}.jsonl`

**内容**: 包含完整的执行轨迹和最终结果

---

## 🔍 详细停止流程

### 正常完成流程

```
用户输入
  ↓
Materials 加载
  ↓
里程碑循环开始
  ↓
Milestone 1 → 执行 → 评估 → 完成
  ↓
Milestone 2 → 执行 → 评估 → 完成
  ↓
...
  ↓
所有 Milestones 完成
  ↓
检查是否需要自动扩展
  ↓
如果需要 → 生成新 Milestones → 执行
  ↓
如果不需要 → 停止
  ↓
显示: "## Research Complete!"
  ↓
发布 RunFinishedEvent
  ↓
程序停止
```

---

### 暂停流程

```
用户输入
  ↓
Materials 加载
  ↓
里程碑循环开始
  ↓
Milestone 1 → 执行 → 评估 → 完成
  ↓
Milestone 2 → 执行 → 评估 → 完成
  ↓
...
  ↓
达到停止条件（错误/取消/限制）
  ↓
显示: "## Research Paused"
  ↓
显示: "Completed {executed}/{total} milestones. Reason: {stopReason}"
  ↓
发布 RunFinishedEvent
  ↓
程序停止
```

---

## 📝 停止信息示例

### 示例 1: 正常完成

**显示文本**:
```
## Research Complete!

All 5 milestones have been completed.
```

**RunFinishedEvent Result**:
```json
{
  "ok": true,
  "mode": "milestone",
  "milestones": {
    "executed": 5,
    "total": 5,
    "stopReason": "completed"
  }
}
```

---

### 示例 2: 带自动扩展的完成

**显示文本**:
```
## Research Complete!

All 7 milestones have been completed (including 1 auto-extension round).
```

**RunFinishedEvent Result**:
```json
{
  "ok": true,
  "mode": "milestone",
  "milestones": {
    "executed": 7,
    "total": 7,
    "stopReason": "completed"
  }
}
```

---

### 示例 3: 研究暂停

**显示文本**:
```
## Research Paused

Completed 3/5 milestones. Reason: cancelled
```

**RunFinishedEvent Result**:
```json
{
  "ok": false,
  "mode": "milestone",
  "milestones": {
    "executed": 3,
    "total": 5,
    "stopReason": "cancelled"
  }
}
```

---

### 示例 4: 发生错误

**显示文本**:
```
## Research Paused

Completed 2/5 milestones. Reason: error
```

**RunFinishedEvent Result**:
```json
{
  "ok": false,
  "mode": "milestone",
  "milestones": {
    "executed": 2,
    "total": 5,
    "stopReason": "error"
  }
}
```

**RunErrorEvent**:
```json
{
  "message": "具体错误信息",
  "code": "SRA_MILESTONE_LOOP_RUN_ERROR"
}
```

---

## 🔧 配置参数

### 最大 Milestones 数

**代码位置**: `VibeMilestoneLoopRunner.cs`

```csharp
private const int MaxMilestones = 20;  // 最大 milestones 数
private const int MaxExtensionRounds = 3;  // 最大自动扩展轮数
private const int AbsoluteMaxIterationsPerMilestone = 10;  // 每个 milestone 最大迭代次数
```

---

## 💡 总结

### 停止位置

**主要停止点**: `VibeMilestoneLoopRunner.ExecuteByMilestonesAsync` (第 606-618 行)

### 停止条件

1. **所有 Milestones 完成** → 显示 "Research Complete!"
2. **研究暂停** → 显示 "Research Paused" + 原因

### 停止信息

- **文本显示**: 通过 `emitAssistantDelta` 输出到前端
- **事件发布**: `RunFinishedEvent` 包含完整的停止结果
- **日志记录**: 服务器日志记录停止原因和详细信息

### 停止原因

- `"completed"`: 正常完成
- `"auto_extended"`: 自动扩展后完成
- `"cancelled"`: 用户取消
- `"error"`: 发生错误
- `"no_milestones"`: 没有生成 milestones
- `"interrupted"`: 被用户输入中断

---

*最后更新: 2025-01-16*
