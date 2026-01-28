# 程序一直运行不停的问题分析

## 📋 问题描述

当 `research_assistant` 中所有要证明的假设都已经验证并且 DAG 图也被更新了，程序却一直在不停的运行。

---

## 🔍 问题原因分析

### 1. Auto-Extension（自动扩展）机制

**代码位置**: `VibeMilestoneLoopRunner.cs` → `ExecuteByMilestonesAsync` (第 438-500 行)

**问题根源**:
当所有 milestone 完成后，程序会进入 **Auto-Extension Loop**，这个循环会：
1. 调用 `TryAutoExtendAsync` 分析是否需要扩展研究
2. 如果 LLM 返回 `shouldExtend: true` 并生成新的 milestones，程序会继续执行
3. 这个过程会重复，直到达到限制或 LLM 返回 `shouldExtend: false`

**关键代码**:
```csharp
// Auto-Extension Loop: Continue extending until done or limits reached
while (milestonesExecuted >= totalMilestones && stopReason == "completed")
{
    // Check if we can extend
    var canExtend = totalMilestones < MaxMilestones && extensionRound < MaxExtensionRounds;
    
    if (!canExtend)
    {
        break;  // 达到限制，退出循环
    }
    
    // 调用 LLM 分析是否需要扩展
    var autoExtensionResult = await TryAutoExtendAsync(...);
    
    if (autoExtensionResult.ExtendedMilestones.Count == 0)
    {
        break;  // LLM 返回不需要扩展，退出循环
    }
    
    // 添加新的 milestones 并继续执行
    milestones.AddRange(newMilestones);
    totalMilestones = milestones.Count;
    
    // 执行新的 milestones...
}
```

**限制参数**:
- `MaxMilestones = 20`: 最大 milestones 数（包括自动扩展）
- `MaxExtensionRounds = 3`: 最大自动扩展轮数

---

### 2. Milestone 完成评估过于严格

**代码位置**: `VibeMilestoneLoopRunner.cs` → `EvaluateMilestoneCompletionAsync` (第 850-950 行)

**问题根源**:
`EvaluateMilestoneCompletionAsync` 使用 LLM 来评估 milestone 是否完成，评估标准非常严格：

```csharp
Be strict in your evaluation. Only mark isComplete=true if ALL of the following are satisfied:
1. The core question/goal has been thoroughly addressed
2. Key derivations or proofs have been completed (if applicable)
3. Findings are supported by credible evidence
4. Knowledge has been properly synthesized
```

**可能导致的问题**:
- 即使假设已经验证并且 DAG 已更新，LLM 可能仍然认为 milestone 未完成
- 如果 `IsComplete` 一直返回 `false`，程序会在每个 milestone 上循环最多 `AbsoluteMaxIterationsPerMilestone = 10` 次
- 即使达到迭代限制，程序也会继续下一个 milestone，而不是停止

**关键代码**:
```csharp
while (!milestoneAchieved && iterationCount < AbsoluteMaxIterationsPerMilestone)
{
    // 执行研究轮次
    await _vibe.ExecuteOneRoundAsync(...);
    
    // 评估 milestone 是否完成
    var evaluation = await EvaluateMilestoneCompletionAsync(...);
    
    if (evaluation.IsComplete)
    {
        milestoneAchieved = true;  // 完成，退出循环
    }
    else if (iterationCount >= AbsoluteMaxIterationsPerMilestone)
    {
        // 达到迭代限制，继续下一个 milestone
        break;
    }
    else
    {
        // 继续研究...
    }
}
```

---

### 3. Auto-Extension 的 LLM 决策

**代码位置**: `VibeMilestoneLoopRunner.cs` → `TryAutoExtendAsync` (第 1470-1540 行)

**问题根源**:
`TryAutoExtendAsync` 使用 LLM 来决定是否需要扩展研究：

```csharp
Consider:
1. Are there unexplored aspects of the original question?
2. Did the completed milestones reveal new interesting directions?
3. Would additional research deepen understanding significantly?
4. Is the research scope sufficiently covered?
```

**可能导致的问题**:
- LLM 可能认为还有未探索的方面，即使所有假设都已验证
- LLM 可能认为已完成的研究揭示了新的有趣方向，需要进一步探索
- LLM 可能认为额外的研究会显著加深理解，即使当前研究已经足够

**关键代码**:
```csharp
var prompt = $$"""
    # Auto-Extension Analysis
    
    The research has completed all planned milestones. Analyze whether further exploration would be valuable.
    
    **Task**: Determine if the research would benefit from additional milestones.
    
    Respond with a JSON object:
    {
        "shouldExtend": true/false,
        "reason": "Brief explanation for the decision",
        "newMilestones": [...]
    }
""";
```

---

## 🔧 解决方案

### 方案 1: 禁用或限制 Auto-Extension

**修改位置**: `VibeMilestoneLoopRunner.cs`

**选项 A: 完全禁用 Auto-Extension**
```csharp
// 注释掉 Auto-Extension Loop
// while (milestonesExecuted >= totalMilestones && stopReason == "completed")
// {
//     ...
// }
```

**选项 B: 降低 Auto-Extension 的限制**
```csharp
private const int MaxMilestones = 10;  // 从 20 降低到 10
private const int MaxExtensionRounds = 1;  // 从 3 降低到 1
```

**选项 C: 添加用户配置选项**
```csharp
// 在 SessionInputInDto 中添加字段
public bool? DisableAutoExtension { get; set; }

// 在 ExecuteByMilestonesAsync 中检查
if (input.DisableAutoExtension == true)
{
    // 跳过 Auto-Extension Loop
}
```

---

### 方案 2: 放宽 Milestone 完成评估标准

**修改位置**: `VibeMilestoneLoopRunner.cs` → `EvaluateMilestoneCompletionAsync`

**选项 A: 添加基于 DAG 状态的自动完成逻辑**
```csharp
// 在评估之前，检查 DAG 状态
var dagSnapshot = await _dagStore.LoadSnapshotAsync(session.EffectiveDagId, ct);
var verifiedHypotheses = dagSnapshot.Nodes
    .Where(n => n.Type == DagNodeType.Hypothesis && n.Proof?.Status == "VERIFIED")
    .ToList();

// 如果所有假设都已验证，自动标记为完成
if (verifiedHypotheses.Count > 0 && 
    verifiedHypotheses.All(h => h.Proof?.Status == "VERIFIED"))
{
    return new MilestoneEvaluation
    {
        IsComplete = true,
        Summary = "All hypotheses have been verified and DAG has been updated.",
        CompletionPercentage = 100
    };
}
```

**选项 B: 修改评估提示词，使其更宽松**
```csharp
// 修改评估提示词
sb.AppendLine("Be pragmatic in your evaluation. Mark isComplete=true if:");
sb.AppendLine("1. The core question/goal has been addressed (doesn't need to be 'thoroughly' addressed)");
sb.AppendLine("2. Key findings have been documented in the DAG");
sb.AppendLine("3. If applicable, proofs or verifications have been completed");
sb.AppendLine("4. Knowledge has been synthesized (doesn't need to be 'properly' synthesized)");
```

---

### 方案 3: 改进 Auto-Extension 的决策逻辑

**修改位置**: `VibeMilestoneLoopRunner.cs` → `TryAutoExtendAsync`

**选项 A: 添加基于 DAG 状态的检查**
```csharp
// 在调用 LLM 之前，检查 DAG 状态
var dagSnapshot = await _dagStore.LoadSnapshotAsync(session.EffectiveDagId, ct);
var allHypotheses = dagSnapshot.Nodes
    .Where(n => n.Type == DagNodeType.Hypothesis)
    .ToList();
var verifiedHypotheses = allHypotheses
    .Where(h => h.Proof?.Status == "VERIFIED")
    .ToList();

// 如果所有假设都已验证，直接返回不需要扩展
if (allHypotheses.Count > 0 && 
    verifiedHypotheses.Count == allHypotheses.Count)
{
    return new AutoExtensionResult
    {
        Reason = "All hypotheses have been verified. No further extension needed.",
        ExtendedMilestones = new List<SraResearchMilestone>()
    };
}
```

**选项 B: 修改 Auto-Extension 提示词，使其更保守**
```csharp
// 修改提示词，强调只有在真正需要时才扩展
sb.AppendLine("**Important**: Only extend if there are CLEARLY unexplored aspects that are ESSENTIAL to the research question.");
sb.AppendLine("Do NOT extend if:");
sb.AppendLine("- All hypotheses have been verified");
sb.AppendLine("- The research scope has been sufficiently covered");
sb.AppendLine("- Additional research would be redundant or tangential");
```

---

### 方案 4: 添加显式的停止条件检查

**修改位置**: `VibeMilestoneLoopRunner.cs` → `ExecuteByMilestonesAsync`

**添加检查逻辑**:
```csharp
// 在执行每个 milestone 之前，检查是否所有假设都已验证
var dagSnapshot = await _dagStore.LoadSnapshotAsync(session.EffectiveDagId, ct);
var allHypotheses = dagSnapshot.Nodes
    .Where(n => n.Type == DagNodeType.Hypothesis)
    .ToList();
var verifiedHypotheses = allHypotheses
    .Where(h => h.Proof?.Status == "VERIFIED")
    .ToList();

// 如果所有假设都已验证，提前停止
if (allHypotheses.Count > 0 && 
    verifiedHypotheses.Count == allHypotheses.Count &&
    milestonesExecuted > 0)  // 至少执行了一个 milestone
{
    _logger.LogInformation(
        "[MilestoneLoop] All hypotheses verified. Stopping research early.");
    emitAssistantDelta(
        "\n\n## Research Complete!\n\n" +
        "All hypotheses have been verified and the DAG has been updated. " +
        $"Completed {milestonesExecuted}/{totalMilestones} milestones.\n");
    stopReason = "completed";
    break;  // 退出 milestone 循环
}
```

---

## 📊 推荐方案

### 短期解决方案（快速修复）

**推荐**: 方案 1 选项 B + 方案 4

1. **降低 Auto-Extension 限制**:
   ```csharp
   private const int MaxMilestones = 10;
   private const int MaxExtensionRounds = 1;
   ```

2. **添加显式的停止条件检查**:
   - 在执行每个 milestone 之前，检查是否所有假设都已验证
   - 如果都已验证，提前停止

### 长期解决方案（完善机制）

**推荐**: 方案 2 选项 A + 方案 3 选项 A

1. **改进 Milestone 完成评估**:
   - 添加基于 DAG 状态的自动完成逻辑
   - 如果所有假设都已验证，自动标记为完成

2. **改进 Auto-Extension 决策**:
   - 添加基于 DAG 状态的检查
   - 如果所有假设都已验证，直接返回不需要扩展

---

## 🔍 诊断步骤

### 1. 检查日志

查看以下日志信息：
```
[MilestoneLoop] All {Count} milestones completed. Attempting auto-extension (round {Round})
[MilestoneLoop] Extended milestone {NodeId} failed: {Msg}
```

### 2. 检查 DAG 状态

查看 DAG 中是否所有假设都已验证：
```bash
# 检查 DAG 快照
cat workspace/sessions/{sessionId}/dag/{dagId}/snapshot.json | jq '.nodes[] | select(.type == "hypothesis") | {id, label, proof}'
```

### 3. 检查 Milestone 评估结果

查看 milestone 评估的 prompt 和输出：
```bash
# 查看 prompt logs
cat ~/.aevatar/prompt_logs/{sessionId}_{runId}.md | grep -A 50 "verifier_milestone_evaluation"
```

---

## 💡 总结

**问题根源**:
1. **Auto-Extension 机制**会持续生成新的 milestones
2. **Milestone 完成评估过于严格**，即使假设已验证也可能不标记为完成
3. **Auto-Extension 的 LLM 决策**可能认为需要继续研究

**解决方案**:
- **短期**: 降低 Auto-Extension 限制 + 添加显式停止条件
- **长期**: 改进评估和决策逻辑，基于 DAG 状态自动判断

---

*最后更新: 2025-01-28*
