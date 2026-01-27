# Research Assistant 工作流程分析

## 📋 概述

`research_assistant` 是 VibeResearching 系统的**单一入口点（single authority）**，负责生成研究摘要（Brief）、执行计划（Plan）和轮次总结（Summary）。它根据 User Prompt 中的模式标记（`[MODE:BRIEF]`、`[MODE:PLAN]`、`[MODE:SUMMARY]`）来执行不同的任务。

## 🎯 核心职责

1. **理解用户意图**：将用户输入转换为可执行的研究问题
2. **制定研究计划**：为每个研究轮次生成详细的执行计划
3. **总结研究成果**：汇总每个轮次的研究成果和发现

## 🔄 三种工作模式

### 模式 1: [MODE:BRIEF] - 研究摘要生成

**位置**: `VibeOrchestrator.cs` → `TryGetBriefAsync` (第 179-243 行)

**触发时机**: 
- 每个 session 的第一次 research round
- 如果 brief 已存在（`Version > 0`），跳过此步骤

**输入**:
- **System Prompt**: 基础定义 + Materials Context + DAG Knowledge Grounding
- **User Prompt**: `[MODE:BRIEF]` + Question + Plan + DAG stats + Trace + 附件

**输出格式**: STRICT JSON ONLY
```json
{
  "rewrittenQuestion": "string",
  "scope": "string",
  "successCriteria": "string",
  "terms": [ { "term": "string", "meaning": "string" } ],
  "assumptions": ["string"],
  "risks": ["string"],
  "uncertainties": ["string"],
  "milestones": [ { "roundIndex": 1, "expectedOutput": "string" } ]
}
```

**处理流程**:
1. 调用 `BuildBriefMessage` 构建 user prompt
2. 添加 `[MODE:BRIEF]` 前缀
3. 合并 Materials Context 和 DAG Knowledge Grounding 到 system prompt
4. 调用 LLM（`ra.ChatAsync`）
5. 解析 JSON 输出
6. 转换为 `SraResearchBriefSnapshot`
7. 保存到 BriefStore
8. **创建 Plan 节点**：调用 `BuildMilestonesPlanDagMutation` 将 milestones 转换为 DAG Plan 节点

**关键输出**:
- `SraResearchBriefSnapshot`: 包含 milestones 列表
- Plan 节点：每个 milestone 对应一个 Plan 节点（用于后续 Knowledge 节点关联）

**Materials Context**: ✅ 包含（最多 200 索引 + 32 fact + 80 DAG 节点）

---

### 模式 2: [MODE:PLAN] - 执行计划生成

**位置**: `VibeOrchestrator.cs` → `TryGetPlanAsync` (第 245-252 行)

**触发时机**: 
- 每个 research round 开始时
- 在 Brief 阶段之后

**输入**:
- **System Prompt**: 基础定义 + Materials Context + DAG Knowledge Grounding
- **User Prompt**: `[MODE:PLAN]` + Question + Plan + DAG stats + Trace + 附件

**输出格式**: STRICT JSON ONLY
```json
{
  "roundTitle": "string",
  "goalsInit": [
    { "goalId": "string (optional)", "text": "string", "priority": 0 }
  ],
  "workers": [
    { 
      "agent": "planner|reasoner|librarian|verifier|dag_builder",
      "task": "string",
      "inputs": { 
        "useGoals": true|false, 
        "useDag": true|false, 
        "useTrace": true|false, 
        "useMaterials": true|false 
      }
    }
  ],
  "notes": ["string"]
}
```

**处理流程**:
1. 调用 `BuildPlanMessage` 构建 user prompt
2. 添加 `[MODE:PLAN]` 前缀
3. 合并 Materials Context 和 DAG Knowledge Grounding 到 system prompt
4. 调用 LLM（`ra.ChatAsync`）
5. 解析 JSON 输出
6. 转换为 `PlanResult`（包含 workers 列表）
7. 返回给 orchestrator，用于后续 worker 执行

**关键输出**:
- `PlanResult`: 包含 `workers` 列表，指定要执行的 agent 和任务
- 如果 `goalsInit` 非空，会自动持久化到 GoalsStore

**Materials Context**: ✅ 包含（最多 200 索引 + 32 fact + 80 DAG 节点）

---

### 模式 3: [MODE:SUMMARY] - 轮次总结生成

**位置**: `VibeOrchestrator.cs` → `TryGetSummaryAsync` (第 342-359 行)

**触发时机**: 
- 每个 research round 结束时
- 在所有 worker 执行完成后

**输入**:
- **System Prompt**: 基础定义（**不包含** Materials Context）
- **User Prompt**: `[MODE:SUMMARY]` + Question + DAG outcome + Worker outputs + Facts written

**输出格式**: Markdown
```
- TL;DR (3 bullets)
- Per-agent highlights (planner/reasoner/librarian/verifier/dag_builder)
- DAG changes proposed/accepted (if any)
- Goal check:
  - If you think new goals should be added/modified (from the user's message or librarian suggestions),
    propose them explicitly and ask the user to CONFIRM (do NOT claim they were applied).
- Open questions / next actions
```

**处理流程**:
1. 调用 `BuildSummaryMessage` 构建 user prompt
2. 添加 `[MODE:SUMMARY]` 前缀
3. **不注入** Materials Context（SUMMARY 模式特殊）
4. 调用 LLM（`ra.ChatAsync`）
5. 返回 Markdown 文本
6. 保存到 TraceStore

**关键输出**:
- Markdown 总结文本
- 用于 UI 显示和 trace 记录

**Materials Context**: ❌ **不包含**（SUMMARY 模式特殊设计）

---

## 📊 完整工作流程

### 在一个 Research Round 中的执行顺序

```
ExecuteOneRoundAsync
    │
    ├─> [MODE:BRIEF] (如果 brief 不存在)
    │   ├─ TryGetBriefAsync
    │   ├─ 生成 Brief JSON
    │   ├─ 保存到 BriefStore
    │   └─ 创建 Plan 节点（milestones）
    │
    ├─> [MODE:PLAN]
    │   ├─ TryGetPlanAsync
    │   ├─ 生成 Plan JSON
    │   └─ 返回 workers 列表
    │
    ├─> Worker Phase
    │   ├─ planner
    │   ├─ reasoner
    │   ├─ verifier
    │   ├─ librarian
    │   └─ dag_builder
    │
    ├─> DAG Consensus
    │   └─ 应用 DAG mutations
    │
    └─> [MODE:SUMMARY]
        ├─ TryGetSummaryAsync
        ├─ 生成 Markdown 总结
        └─ 保存到 TraceStore
```

## 🔍 详细流程分析

### 1. Brief 生成流程

**代码位置**: `VibeOrchestrator.ResearchAssistant.cs` → `TryGetBriefAsync`

**步骤详解**:

1. **检查现有 Brief**
   ```csharp
   var existing = await _core.Brief.LoadAsync(session.Id, innerCt);
   if (existing.Version > 0)
       return; // 已存在，跳过
   ```

2. **构建 User Prompt**
   ```csharp
   var msg = BuildBriefMessage(question, dag, recentTrace, input.ToAgents, input.AttachmentPaths);
   var userMessage = "[MODE:BRIEF]\n" + msg;
   ```

3. **构建 System Prompt**
   ```csharp
   var baseSystemPrompt = VibeResearchAssistantAgent.GetSystemPrompt();
   var materialsContext = MergeGroundedContext(
       materials.RenderedContext, 
       BuildDagKnowledgeGrounding(dag)
   );
   var finalSystemPrompt = string.IsNullOrWhiteSpace(materialsContext)
       ? baseSystemPrompt
       : $"{baseSystemPrompt}\n\nMaterials context:\n{materialsContext.Trim()}\n";
   ```

4. **调用 LLM**
   ```csharp
   var (ra, raId) = await _core.Runtime.GetResearchAssistantAgentAsync(sessionId, providerOverride, ct);
   var resp = await ra.ChatAsync(req, ct);
   ```

5. **解析 JSON**
   ```csharp
   if (!TryExtractJson(raw, out var json))
       return (null, promptRecord);
   var parsed = JsonSerializer.Deserialize<BriefJson>(json!, Json);
   ```

6. **转换为 Snapshot**
   ```csharp
   var snap = new SraResearchBriefSnapshot { ... };
   snap.RewrittenQuestion = Bound(parsed.RewrittenQuestion ?? string.Empty, 1200);
   snap.Milestones.Add(...);
   ```

7. **保存 Brief**
   ```csharp
   brief.Version = 1;
   var saved = await _core.Brief.SaveAsync(session.Id, brief, innerCt);
   ```

8. **创建 Plan 节点**
   ```csharp
   var mm = BuildMilestonesPlanDagMutation(session.Id, runId, question, saved);
   if (mm != null)
       dagSnap = await _core.Dag.ApplyMutationAsync(dagId, mm, innerCt);
   ```

**关键点**:
- Brief 只在 session 第一次运行时生成
- Milestones 会转换为 DAG Plan 节点
- Plan 节点用于后续 Knowledge 节点的 `motivatedByPlanNodeId` 关联

---

### 2. Plan 生成流程

**代码位置**: `VibeOrchestrator.ResearchAssistant.cs` → `TryGetPlanAsync`

**步骤详解**:

1. **构建 User Prompt**
   ```csharp
   var msg = BuildPlanMessage(question, dag, recentTrace, input.ToAgents, input.AttachmentPaths);
   var userMessage = "[MODE:PLAN]\n" + msg;
   ```

2. **构建 System Prompt**（与 BRIEF 相同）
   ```csharp
   var materialsContext = MergeGroundedContext(
       materials.RenderedContext, 
       BuildDagKnowledgeGrounding(dag)
   );
   ```

3. **调用 LLM**
   ```csharp
   var resp = await ra.ChatAsync(req, ct);
   ```

4. **解析 JSON**
   ```csharp
   var parsed = JsonSerializer.Deserialize<PlanJson>(json!, Json);
   var workers = parsed?.Workers?
       .Where(w => !string.IsNullOrWhiteSpace(w.Agent))
       .Select(w => new PlanWorker { Agent = w.Agent, Task = w.Task })
       .ToList();
   ```

5. **返回 PlanResult**
   ```csharp
   return (new PlanResult(json, parsed?.RoundTitle, workers), promptRecord);
   ```

**关键点**:
- Plan 在每个 round 开始时生成
- Workers 列表决定了后续执行哪些 agent
- 如果 `goalsInit` 非空，会自动持久化

---

### 3. Summary 生成流程

**代码位置**: `VibeOrchestrator.ResearchAssistant.cs` → `TryGetSummaryAsync`

**步骤详解**:

1. **构建 User Prompt**
   ```csharp
   var msg = BuildSummaryMessage(question, dagResult, outputs, factsWritten);
   var userMessage = "[MODE:SUMMARY]\n" + msg;
   ```

2. **构建 System Prompt**（**不包含** Materials Context）
   ```csharp
   // Note: SUMMARY mode does NOT include Materials Context
   var finalSystemPrompt = baseSystemPrompt;
   ```

3. **调用 LLM**
   ```csharp
   var resp = await ra.ChatAsync(req, ct);
   ```

4. **返回 Markdown**
   ```csharp
   var md = (resp.Content ?? string.Empty).Replace("\r", "").Trim();
   var output = md.Length == 0 ? null : Bound(md, 40_000);
   return (output, promptRecord);
   ```

**关键点**:
- Summary 在每个 round 结束时生成
- **不包含** Materials Context（设计决策）
- 输出是 Markdown 格式，用于 UI 显示

---

## 🔗 与其他组件的交互

### 1. 与 Materials Service 的交互

**Materials Context 构建**:
```csharp
var materialsContext = MergeGroundedContext(
    materials.RenderedContext,  // 来自 MaterialsService
    BuildDagKnowledgeGrounding(dag)  // 来自 DAG
);
```

**包含内容**:
- DAG FACT INDEX（最多 200 条）
- DAG FACTS（最多 32 条）
- DAG Knowledge Grounding（最多 80 个节点）

### 2. 与 DAG Store 的交互

**Brief 阶段**:
- 读取 DAG snapshot（用于 grounding）
- 创建 Plan 节点（milestones）

**Plan 阶段**:
- 读取 DAG snapshot（用于 grounding）
- 读取 Plan 节点（用于 context）

**Summary 阶段**:
- 读取 DAG mutation 结果（用于总结）

### 3. 与 Trace Store 的交互

**Brief/Plan 阶段**:
- 读取 `recentTrace`（最近的研究历史）

**Summary 阶段**:
- 保存总结到 TraceStore

### 4. 与 Worker Agents 的交互

**Plan 阶段**:
- 生成 workers 列表，指定要执行的 agent

**Summary 阶段**:
- 接收所有 worker 的输出，用于总结

---

## 📝 User Prompt 构建详解

### BuildBriefMessage

**位置**: `VibeOrchestrator.GoalsAndMessages.cs`

**包含内容**:
- Question（用户问题）
- Plan（DAG 中的 Plan 节点摘要）
- DAG stats（节点数、边数）
- Trace（最近的研究历史，如果有）
- AttachmentPaths（附件路径）

### BuildPlanMessage

**位置**: `VibeOrchestrator.GoalsAndMessages.cs`

**包含内容**:
- Question（用户问题）
- Plan（DAG 中的 Plan 节点摘要）
- DAG stats（节点数、边数）
- Trace（最近的研究历史）
- AttachmentPaths（附件路径）

**特殊要求**:
- 如果当前 goals 列表为空，必须提供 3-7 个初始 goals

### BuildSummaryMessage

**位置**: `VibeOrchestrator.GoalsAndMessages.cs`

**包含内容**:
- Question（用户问题）
- DAG outcome（DAG mutation 结果）
- Worker outputs（所有 worker 的输出摘要）
- Facts written（librarian 写入的事实）

---

## 🎯 关键设计决策

### 1. 单一入口点

`research_assistant` 是系统的单一入口点，所有高级决策都由它做出。

### 2. 模式驱动

通过 `[MODE:...]` 标记明确指定任务类型，避免 LLM 混淆。

### 3. Materials Context 策略

- **BRIEF/PLAN**: 包含 Materials Context（需要 grounded knowledge）
- **SUMMARY**: 不包含 Materials Context（避免过度依赖，专注于总结）

### 4. JSON vs Markdown

- **BRIEF/PLAN**: STRICT JSON（结构化数据）
- **SUMMARY**: Markdown（人类可读）

### 5. Brief 只生成一次

Brief 在每个 session 中只生成一次，后续 round 复用。

---

## 🔄 数据流图

```
用户输入
    │
    ├─> [MODE:BRIEF]
    │   ├─ System Prompt: 基础 + Materials Context + DAG Knowledge
    │   ├─ User Prompt: [MODE:BRIEF] + Question + Plan + DAG + Trace
    │   └─ Output: JSON Brief (包含 milestones)
    │       │
    │       └─> 创建 Plan 节点（milestones）
    │
    ├─> [MODE:PLAN]
    │   ├─ System Prompt: 基础 + Materials Context + DAG Knowledge
    │   ├─ User Prompt: [MODE:PLAN] + Question + Plan + DAG + Trace
    │   └─ Output: JSON Plan (包含 workers 列表)
    │       │
    │       └─> 执行 Workers (planner, reasoner, verifier, ...)
    │
    └─> [MODE:SUMMARY]
        ├─ System Prompt: 基础（无 Materials Context）
        ├─ User Prompt: [MODE:SUMMARY] + Question + DAG outcome + Worker outputs
        └─ Output: Markdown Summary
            │
            └─> 保存到 TraceStore
```

---

## 🛠️ 工具注册

`research_assistant` 注册了以下工具（用于 KnowledgeGraph 操作）：

**Plan 管理工具**:
- `UpdatePlanStatusTool`: 更新 Plan 节点状态
- `GetPlanTool`: 获取 Plan 节点

**Knowledge 管理工具**:
- `CreateKnowledgeTool`: 创建 Knowledge 节点
- `GetKnowledgeTool`: 获取 Knowledge 节点
- `LinkKnowledgeToPlanTool`: 关联 Knowledge 到 Plan

**Pivot 工具**:
- `CreatePivotSnapshotTool`: 创建 pivot 快照
- `GetPivotSnapshotsTool`: 获取 pivot 快照

**注意**: `CreatePlanTool` 已禁用，因为 Plan 节点在 Brief 生成时创建，不在执行时创建。

---

## 📊 输入输出限制

| 模式 | System Prompt | User Prompt | Output | Materials Context |
|------|--------------|-------------|--------|------------------|
| BRIEF | 基础 + Materials + DAG | [MODE:BRIEF] + ... | JSON | ✅ |
| PLAN | 基础 + Materials + DAG | [MODE:PLAN] + ... | JSON | ✅ |
| SUMMARY | 基础（无 Materials） | [MODE:SUMMARY] + ... | Markdown | ❌ |

---

## 🔍 错误处理

所有三个方法都使用 `best-effort` 策略：

```csharp
try
{
    // ... LLM 调用和解析 ...
}
catch (Exception ex)
{
    if (ex is OperationCanceledException && ct.IsCancellationRequested)
        throw;
    _host.Logger.LogDebug(ex, "[VibeOrchestrator] research_assistant {mode} failed (best-effort).");
    // 返回 null 或空结果，但不中断整个流程
    return (null, errorPromptRecord);
}
```

**设计理念**: 即使 `research_assistant` 失败，系统仍可以继续运行（使用默认配置）。

---

## 💡 最佳实践

1. **Brief 应该简洁**: 1 页，2-6 个 milestones
2. **Plan 应该明确**: 指定要执行的 workers 和任务
3. **Summary 应该全面**: 涵盖所有 worker 的输出和 DAG 变化
4. **Materials Context 使用**: BRIEF/PLAN 使用，SUMMARY 不使用
5. **错误处理**: 使用 best-effort，不中断流程

---

## 🐛 常见问题

### 1. Brief 未生成

**原因**: Brief 已存在（`Version > 0`）

**解决**: 检查 `BriefStore` 中是否已有 brief

### 2. Plan 为空

**原因**: LLM 输出无法解析或 workers 列表为空

**解决**: 检查 LLM 输出格式，使用默认 workers 列表

### 3. Summary 不包含 Materials Context

**原因**: 设计决策（SUMMARY 模式不包含 Materials Context）

**解决**: 这是预期行为，无需修改

---

## 📚 相关文件

- `VibeResearchAssistantAgent.cs`: Agent 定义和 System Prompt
- `VibeOrchestrator.ResearchAssistant.cs`: BRIEF/PLAN/SUMMARY 实现
- `VibeOrchestrator.GoalsAndMessages.cs`: User Prompt 构建
- `VibeOrchestrator.cs`: 主流程调用
