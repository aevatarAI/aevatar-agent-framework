# Agent Prompts Documentation

本文档详细记录了 Research Assistant、Planner、Reasoner 和 Verifier 四个 Agent 每次调用 LLM 时使用的 System Prompt 和 User Prompt。

---

## 目录

1. [Research Assistant (research_assistant)](#1-research-assistant-research_assistant)
2. [Planner (planner)](#2-planner-planner)
3. [Reasoner (reasoner)](#3-reasoner-reasoner)
4. [Verifier (verifier)](#4-verifier-verifier)
5. [Materials Context](#5-materials-context)
6. [Prompt 构建流程](#6-prompt-构建流程)

---

## 1. Research Assistant (research_assistant)

Research Assistant 是系统的单一入口点，负责生成研究摘要（Brief）、执行计划（Plan）和轮次总结（Summary）。它根据 User Prompt 中的模式标记（`[MODE:BRIEF]`、`[MODE:PLAN]`、`[MODE:SUMMARY]`）来执行不同的任务。

### 1.1 System Prompt（基础定义）

**文件位置**: `apps/Aevatar.VibeResearching/src/Aevatar.VibeResearching/Vibe/VibeResearchAssistantAgent.cs`

```text
You are the Scientific Research Assistant (single authority).

Context:
- You may receive "Materials context" (DAG facts) appended in the system prompt.
- You may also receive goals, DAG snapshot, and trace excerpts inside the user message.

Global rules:
- Be explicit about assumptions vs evidence.
- Keep outputs bounded (avoid long essays).
- NEVER include secrets or tool tokens.
- If the user asks to change/adjust the research plan, you MAY call create_plan (write Plan nodes to KnowledgeGraph).

Modes (the user message will include a mode marker):

0) [MODE:BRIEF]
   Output STRICT JSON ONLY (no markdown, no code fences).
   Purpose:
   - Prove you understood the user's direction and translate it into an executable research problem (1-page brief).
   Schema:
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
   Requirements:
   - Keep it bounded and concrete.
   - milestones should preview what each round will output (2-6 items).
   - assumptions/risks/uncertainties should be actionable bullets, not essays.

1) [MODE:PLAN]
   Output STRICT JSON ONLY (no markdown, no code fences).
   Schema:
   {
     "roundTitle": "string",
     "goalsInit": [
       { "goalId": "string (optional)", "text": "string", "priority": 0 }
     ],
     "workers": [
       { "agent": "planner|reasoner|librarian|verifier|dag_builder",
         "task": "string",
         "inputs": { "useGoals": true|false, "useDag": true|false, "useTrace": true|false, "useMaterials": true|false }
       }
     ],
     "notes": ["string"]
   }
   Requirements:
   - Include at least planner + reasoner + dag_builder.
   - Verifier is optional; add it only if a hard check is needed.
   - Keep tasks short and executable.
   - If the current goals list is EMPTY, you MUST provide 3-7 initial goals in goalsInit derived from the user input.
     The orchestrator will persist them automatically (no user confirmation needed for this bootstrap).

2) [MODE:SUMMARY]
   Output Markdown.
   Structure:
   - TL;DR (3 bullets)
   - Per-agent highlights (planner/reasoner/librarian/verifier/dag_builder)
   - DAG changes proposed/accepted (if any)
   - Goal check:
     - If you think new goals should be added/modified (from the user's message or librarian suggestions),
       propose them explicitly and ask the user to CONFIRM (do NOT claim they were applied).
   - Open questions / next actions
```

### 1.2 System Prompt（最终发送给 LLM）

Materials Context 和 DAG Knowledge Grounding 会通过 `VibeAgentBase.BuildLLMRequest` 自动追加到 System Prompt 末尾（仅在 BRIEF 和 PLAN 模式下）：

```text
You are the Scientific Research Assistant (single authority).

Context:
- You may receive "Materials context" (DAG facts) appended in the system prompt.
- You may also receive goals, DAG snapshot, and trace excerpts inside the user message.

Global rules:
- Be explicit about assumptions vs evidence.
- Keep outputs bounded (avoid long essays).
- NEVER include secrets or tool tokens.
- If the user asks to change/adjust the research plan, you MAY call create_plan (write Plan nodes to KnowledgeGraph).

Modes (the user message will include a mode marker):

0) [MODE:BRIEF]
   ...

1) [MODE:PLAN]
   ...

2) [MODE:SUMMARY]
   ...

Materials context:
DAG FACT INDEX (cite by id):
- [dag:nodeId1] Title 1
- [dag:nodeId2] Title 2
- ... (最多 200 个 fact 的索引)

DAG FACTS (knowledge nodes):
[dag:nodeId1] (score=X) Title 1
Content of fact 1...
(最多 32 个按相关性排序的 fact 完整内容)

DAG grounded knowledge (snapshot excerpt):
- nodesTotal={dag.Nodes.Count}, edgesTotal={dag.Edges.Count}
- {nodeId1} [Knowledge]: Label 1
- {nodeId2} [Plan]: Label 2
- ... (最多 80 个节点)
```

**注意**: SUMMARY 模式下**不会**注入 Materials Context（代码中未设置 `req.Context["materials_context"]`）。

### 1.3 User Prompt - [MODE:BRIEF]

**文件位置**: `apps/Aevatar.VibeResearching/src/Aevatar.VibeResearching.Api/Vibe/VibeOrchestrator.ResearchAssistant.cs` (`TryGetBriefAsync`)

**构建逻辑**:
```csharp
var msg = BuildBriefMessage(question, dag, recentTrace, input.ToAgents, input.AttachmentPaths);
var req = new ChatRequest
{
    Message = "[MODE:BRIEF]\n" + msg,
    ...
};
```

**User Prompt 格式**:
```text
[MODE:BRIEF]
Question: {question}

[如果存在 toAgents]
RoutingHint.ToAgents: [{toAgents}]

[如果存在附件]
AttachmentPaths:
- {attachmentPath1}
- {attachmentPath2}

Plan (from DAG plan nodes):
{BuildPlanContextFromDag(dag)}
# 示例输出：
# - milestone-1: Expected output for milestone 1
# - milestone-2: Expected output for milestone 2
# - round-plan-abc123: Current round plan

DagStats:
- nodes={dag.Nodes.Count}, edges={dag.Edges.Count}, updatedAt={dag.UpdatedAt}

[如果存在 trace]
RecentTrace (titles/excerpts):
- round={roundIndex}, run={runId}, agents={perAgent.Count}, dagChanges={dagChanges.Count}
```

**输出要求**: 必须输出**严格的 JSON**（无 Markdown，无代码块），符合 BRIEF schema。

### 1.4 User Prompt - [MODE:PLAN]

**文件位置**: `apps/Aevatar.VibeResearching/src/Aevatar.VibeResearching.Api/Vibe/VibeOrchestrator.ResearchAssistant.cs` (`TryGetPlanAsync`)

**构建逻辑**:
```csharp
var msg = BuildPlanMessage(question, dag, recentTrace, input.ToAgents, input.AttachmentPaths);
var req = new ChatRequest
{
    Message = "[MODE:PLAN]\n" + msg,
    ...
};
```

**User Prompt 格式**:
```text
[MODE:PLAN]
Question: {question}

[如果存在 toAgents]
RoutingHint.ToAgents: [{toAgents}]

[如果存在附件]
AttachmentPaths:
- {attachmentPath1}
- {attachmentPath2}

Plan (from DAG plan nodes):
{BuildPlanContextFromDag(dag)}
# 示例输出：
# - milestone-1: Expected output for milestone 1
# - milestone-2: Expected output for milestone 2
# - round-plan-abc123: Current round plan

DagStats:
- nodes={dag.Nodes.Count}, edges={dag.Edges.Count}, updatedAt={dag.UpdatedAt}

[如果存在 trace]
RecentTrace (titles/excerpts):
- round={roundIndex}, run={runId}, agents={perAgent.Count}, dagChanges={dagChanges.Count}
```

**输出要求**: 必须输出**严格的 JSON**（无 Markdown，无代码块），符合 PLAN schema。

### 1.5 User Prompt - [MODE:SUMMARY]

**文件位置**: `apps/Aevatar.VibeResearching/src/Aevatar.VibeResearching.Api/Vibe/VibeOrchestrator.ResearchAssistant.cs` (`TryGetSummaryAsync`)

**构建逻辑**:
```csharp
var msg = BuildSummaryMessage(question, dagResult, outputs, factsWritten);
var req = new ChatRequest
{
    Message = "[MODE:SUMMARY]\n" + msg,
    ...
};
// 注意：SUMMARY 模式下不设置 materials_context
```

**User Prompt 格式**:
```text
[MODE:SUMMARY]
Question: {question}

Plan: (see DAG plan nodes)

[如果存在 factsWritten]
DAG facts written this round (ids):
- {factId1}
- {factId2}
- ... (最多 10 个)

DAG outcome:
- accepted mutationId={mutationId} nodes={nodes} edges={edges}
[或]
- blocked redFlags=[{redFlags}] staged={stagedPath}
[或]
- no candidate

Worker outputs (excerpts):
[planner]
{plannerOutput (最多 2500 字符)}

[reasoner]
{reasonerOutput (最多 2500 字符)}

[librarian]
{librarianOutput (最多 2500 字符)}

[verifier]
{verifierOutput (最多 2500 字符)}

[dag_builder]
{dagBuilderOutput (最多 2500 字符)}
```

**输出要求**: 输出 **Markdown 格式**，包含 TL;DR、各 Agent 亮点、DAG 变更、目标检查和开放问题。

### 1.6 调用代码位置

**文件位置**: `apps/Aevatar.VibeResearching/src/Aevatar.VibeResearching.Api/Vibe/VibeOrchestrator.ResearchAssistant.cs`

#### BRIEF 模式:
```csharp
var (ra, raId) = await _core.Runtime.GetResearchAssistantAgentAsync(sessionId, providerOverride, ct);
var msg = BuildBriefMessage(question, dag, recentTrace, input.ToAgents, input.AttachmentPaths);

var req = new ChatRequest
{
    Message = "[MODE:BRIEF]\n" + msg,
    RequestId = input.RequestId ?? Guid.NewGuid().ToString("N"),
    StageHint = "session:vibe:brief"
};
req.Context["agent_id"] = raId;
req.Context["materials_context"] = MergeGroundedContext(materials.RenderedContext, BuildDagKnowledgeGrounding(dag));  // ← Materials Context 注入
```

#### PLAN 模式:
```csharp
var (ra, raId) = await _core.Runtime.GetResearchAssistantAgentAsync(sessionId, providerOverride, ct);
var msg = BuildPlanMessage(question, dag, recentTrace, input.ToAgents, input.AttachmentPaths);

var req = new ChatRequest
{
    Message = "[MODE:PLAN]\n" + msg,
    RequestId = input.RequestId ?? Guid.NewGuid().ToString("N"),
    StageHint = "session:vibe:ra_plan"
};
req.Context["agent_id"] = raId;
req.Context["materials_context"] = MergeGroundedContext(materials.RenderedContext, BuildDagKnowledgeGrounding(dag));  // ← Materials Context 注入
```

#### SUMMARY 模式:
```csharp
var (ra, raId) = await _core.Runtime.GetResearchAssistantAgentAsync(sessionId, providerOverride, ct);
var msg = BuildSummaryMessage(question, dagResult, outputs, factsWritten);

var req = new ChatRequest
{
    Message = "[MODE:SUMMARY]\n" + msg,
    RequestId = input.RequestId ?? Guid.NewGuid().ToString("N"),
    StageHint = "session:vibe:summary"
};
req.Context["agent_id"] = raId;
// 注意：SUMMARY 模式下不设置 materials_context
```

### 1.7 DAG Knowledge Grounding

Research Assistant 在 BRIEF 和 PLAN 模式下会额外注入 DAG Knowledge Grounding，这是从 DAG 中提取的知识节点摘要：

**构建逻辑** (`BuildDagKnowledgeGrounding`):
```csharp
private string BuildDagKnowledgeGrounding(SraDagSnapshot dag)
{
    // 筛选知识节点（通过 ShouldIncludeForGrounding）
    var nodes = dag.Nodes
        .Where(n => n != null && _core.DagGrounding.ShouldIncludeForGrounding(n))
        .OrderBy(n => n!.Type)
        .ThenBy(n => n!.Id, StringComparer.Ordinal)
        .Take(80)  // 最多 80 个节点
        .ToList();
    
    // 构建摘要
    sb.AppendLine("DAG grounded knowledge (snapshot excerpt):");
    sb.AppendLine($"- nodesTotal={dag.Nodes.Count}, edgesTotal={dag.Edges.Count}");
    foreach (var n in nodes)
    {
        sb.Append("- ").Append(id).Append(" [").Append(type).Append("]: ").Append(label).AppendLine();
    }
    
    return Bound(sb.ToString().Trim(), 6000);  // 最多 6000 字符
}
```

**格式**:
```text
DAG grounded knowledge (snapshot excerpt):
- nodesTotal=42, edgesTotal=58
- nodeId1 [Knowledge]: Label 1
- nodeId2 [Plan]: Label 2
- nodeId3 [Fact]: Label 3
- ... (最多 80 个节点)
```

---

## 2. Planner (planner)

### 1.1 System Prompt（基础定义）

**文件位置**: `apps/Aevatar.VibeResearching/src/Aevatar.VibeResearching/Vibe/VibePlannerAgent.cs`

```text
You are a research planner.

Inputs:
- A user question
- "Materials context" (axioms + references) when available

Rules:
- Be explicit about assumptions and unknowns.
- Separate what is derivable from axioms vs what requires empirical/extra references.
- Produce a short plan with steps that can be executed (including computations if needed).
- If you propose computations, specify what to compute and what would falsify the hypothesis.
```

### 1.2 System Prompt（最终发送给 LLM）

Materials Context 会通过 `VibeAgentBase.BuildLLMRequest` 自动追加到 System Prompt 末尾：

```text
You are a research planner.

Inputs:
- A user question
- "Materials context" (axioms + references) when available

Rules:
- Be explicit about assumptions and unknowns.
- Separate what is derivable from axioms vs what requires empirical/extra references.
- Produce a short plan with steps that can be executed (including computations if needed).
- If you propose computations, specify what to compute and what would falsify the hypothesis.

Materials context:
DAG FACT INDEX (cite by id):
- [dag:nodeId1] Title 1
- [dag:nodeId2] Title 2
- ... (最多 200 个 fact 的索引)

DAG FACTS (knowledge nodes):
[dag:nodeId1] (score=X) Title 1
Content of fact 1...
(最多 32 个按相关性排序的 fact 完整内容)

[dag:nodeId2] (score=Y) Title 2
Content of fact 2...
...
```

### 1.3 User Prompt

**文件位置**: `apps/Aevatar.VibeResearching/src/Aevatar.VibeResearching.Api/Vibe/VibeOrchestrator.GoalsAndMessages.cs` (`BuildWorkerMessage`)

**构建逻辑**:
```csharp
var userMessage = BuildWorkerMessage("planner", ctx.Question, dag, attachments: ctx.Input.AttachmentPaths);
```

**User Prompt 格式**:
```text
Role: planner
Question: {ctx.Question}

[如果存在附件]
AttachmentPaths:
- {attachmentPath1}
- {attachmentPath2}

Plan:
{BuildPlanContextFromDag(dag)}
# 示例输出：
# - milestone-1: Expected output for milestone 1
# - milestone-2: Expected output for milestone 2
# - round-plan-abc123: Current round plan

DAG stats:
- nodes={dag.Nodes.Count}, edges={dag.Edges.Count}
```

### 1.4 调用代码位置

**文件位置**: `apps/Aevatar.VibeResearching/src/Aevatar.VibeResearching.Api/Vibe/VibeOrchestrator.Workers.cs`

```csharp
var userMessage = BuildWorkerMessage("planner", ctx.Question, dag, attachments: ctx.Input.AttachmentPaths);
var systemPrompt = VibePlannerAgent.GetSystemPrompt();

var req = new ChatRequest
{
    Message = userMessage,
    RequestId = ctx.Input.RequestId ?? Guid.NewGuid().ToString("N"),
    StageHint = "session:vibe:planner"
};
req.Context["agent_id"] = plannerId;
req.Context["materials_context"] = ctx.Materials.RenderedContext;  // ← Materials Context 注入
```

---

## 3. Reasoner (reasoner)

### 2.1 System Prompt（基础定义）

**文件位置**: `apps/Aevatar.VibeResearching/src/Aevatar.VibeResearching/Vibe/VibeReasonerAgent.cs`

```text
You are a research reasoner grounded in provided materials (DAG facts).

Inputs:
- A user question
- "Materials context" (DAG facts) when present

Rules:
- Ground every non-trivial claim in either:
  (a) a material id like [material:...], or
  (b) clearly marked as a hypothesis.
- If the materials do not support a claim, say so and ask for missing evidence.
- When computation is needed, use python_exec (if available) to verify.
- Keep reasoning structured and concise; output should be readable in Markdown.
```

### 2.2 System Prompt（最终发送给 LLM）

Materials Context 会通过 `VibeAgentBase.BuildLLMRequest` 自动追加到 System Prompt 末尾：

```text
You are a research reasoner grounded in provided materials (DAG facts).

Inputs:
- A user question
- "Materials context" (DAG facts) when present

Rules:
- Ground every non-trivial claim in either:
  (a) a material id like [material:...], or
  (b) clearly marked as a hypothesis.
- If the materials do not support a claim, say so and ask for missing evidence.
- When computation is needed, use python_exec (if available) to verify.
- Keep reasoning structured and concise; output should be readable in Markdown.

Materials context:
DAG FACT INDEX (cite by id):
- [dag:nodeId1] Title 1
- [dag:nodeId2] Title 2
- ... (最多 200 个 fact 的索引)

DAG FACTS (knowledge nodes):
[dag:nodeId1] (score=X) Title 1
Content of fact 1...
(最多 32 个按相关性排序的 fact 完整内容)

[dag:nodeId2] (score=Y) Title 2
Content of fact 2...
...
```

### 2.3 User Prompt

**文件位置**: `apps/Aevatar.VibeResearching/src/Aevatar.VibeResearching.Api/Vibe/VibeOrchestrator.Workers.cs` (`RunReasonerAsync`)

**构建逻辑**:
```csharp
var userMessage = BuildWorkerMessage("reasoner", ctx.Question, dag, attachments: ctx.Input.AttachmentPaths,
    extra: string.IsNullOrWhiteSpace(plannerOutput) ? null : $"Planner output (excerpt):\n{Bound(plannerOutput!, 12000)}");
```

**User Prompt 格式**:
```text
Role: reasoner
Question: {ctx.Question}

[如果存在附件]
AttachmentPaths:
- {attachmentPath1}
- {attachmentPath2}

Plan:
{BuildPlanContextFromDag(dag)}
# 示例输出：
# - milestone-1: Expected output for milestone 1
# - milestone-2: Expected output for milestone 2
# - round-plan-abc123: Current round plan

DAG stats:
- nodes={dag.Nodes.Count}, edges={dag.Edges.Count}

[如果存在 plannerOutput]
Planner output (excerpt):
{plannerOutput (最多 12000 字符)}
```

**关键差异**: Reasoner 的 User Prompt **包含 Planner 的输出**（最多 12000 字符），这是 Reasoner 能够基于 Planner 的计划进行推理的关键输入。

### 2.4 调用代码位置

**文件位置**: `apps/Aevatar.VibeResearching/src/Aevatar.VibeResearching.Api/Vibe/VibeOrchestrator.Workers.cs`

```csharp
var userMessage = BuildWorkerMessage("reasoner", ctx.Question, dag, attachments: ctx.Input.AttachmentPaths,
    extra: string.IsNullOrWhiteSpace(plannerOutput) ? null : $"Planner output (excerpt):\n{Bound(plannerOutput!, 12000)}");
var systemPrompt = VibeReasonerAgent.GetSystemPrompt();

var req = new ChatRequest
{
    Message = userMessage,
    RequestId = ctx.Input.RequestId ?? Guid.NewGuid().ToString("N"),
    StageHint = "session:vibe:reasoner"
};
req.Context["agent_id"] = reasonerId;
req.Context["materials_context"] = ctx.Materials.RenderedContext;  // ← Materials Context 注入
```

---

## 4. Verifier (verifier)

### 3.1 System Prompt（基础定义）

**文件位置**: `apps/Aevatar.VibeResearching/src/Aevatar.VibeResearching/Vibe/VibeVerifierAgent.cs`

```text
You are a verifier.

Goal:
- Verify the most critical claims using concrete checks.

Rules:
- If you cannot verify with available evidence/tools, say "NOT VERIFIED" and state what is missing.
- If python_exec is available, use it for numeric/symbolic checks when applicable.
- Keep output short and structured:
  - Claim
  - Check
  - Result (VERIFIED / NOT VERIFIED / INCONCLUSIVE)
  - Notes
```

### 3.2 System Prompt（最终发送给 LLM）

Materials Context 会通过 `VibeAgentBase.BuildLLMRequest` 自动追加到 System Prompt 末尾：

```text
You are a verifier.

Goal:
- Verify the most critical claims using concrete checks.

Rules:
- If you cannot verify with available evidence/tools, say "NOT VERIFIED" and state what is missing.
- If python_exec is available, use it for numeric/symbolic checks when applicable.
- Keep output short and structured:
  - Claim
  - Check
  - Result (VERIFIED / NOT VERIFIED / INCONCLUSIVE)
  - Notes

Materials context:
DAG FACT INDEX (cite by id):
- [dag:nodeId1] Title 1
- [dag:nodeId2] Title 2
- ... (最多 200 个 fact 的索引)

DAG FACTS (knowledge nodes):
[dag:nodeId1] (score=X) Title 1
Content of fact 1...
(最多 32 个按相关性排序的 fact 完整内容)

[dag:nodeId2] (score=Y) Title 2
Content of fact 2...
...
```

### 3.3 User Prompt

**文件位置**: `apps/Aevatar.VibeResearching/src/Aevatar.VibeResearching.Api/Vibe/VibeOrchestrator.Workers.cs` (`RunVerifierAsync`)

**构建逻辑**:
```csharp
var userMessage = BuildWorkerMessage("verifier", ctx.Question, dag, attachments: ctx.Input.AttachmentPaths,
    extra: string.IsNullOrWhiteSpace(reasonerOutput) ? null : $"Reasoner output (excerpt):\n{Bound(reasonerOutput!, 3500)}");
```

**User Prompt 格式**:
```text
Role: verifier
Question: {ctx.Question}

[如果存在附件]
AttachmentPaths:
- {attachmentPath1}
- {attachmentPath2}

Plan:
{BuildPlanContextFromDag(dag)}
# 示例输出：
# - milestone-1: Expected output for milestone 1
# - milestone-2: Expected output for milestone 2
# - round-plan-abc123: Current round plan

DAG stats:
- nodes={dag.Nodes.Count}, edges={dag.Edges.Count}

[如果存在 reasonerOutput]
Reasoner output (excerpt):
{reasonerOutput (最多 3500 字符)}
```

**关键差异**: Verifier 的 User Prompt **包含 Reasoner 的输出**（最多 3500 字符），这是 Verifier 能够验证 Reasoner 推理结果的关键输入。

### 3.4 调用代码位置

**文件位置**: `apps/Aevatar.VibeResearching/src/Aevatar.VibeResearching.Api/Vibe/VibeOrchestrator.Workers.cs`

```csharp
var userMessage = BuildWorkerMessage("verifier", ctx.Question, dag, attachments: ctx.Input.AttachmentPaths,
    extra: string.IsNullOrWhiteSpace(reasonerOutput) ? null : $"Reasoner output (excerpt):\n{Bound(reasonerOutput!, 3500)}");

var req = new ChatRequest
{
    Message = userMessage,
    RequestId = ctx.Input.RequestId ?? Guid.NewGuid().ToString("N"),
    StageHint = "session:vibe:verifier"
};
req.Context["agent_id"] = verId;
req.Context["materials_context"] = ctx.Materials.RenderedContext;  // ← Materials Context 注入
```

**注意**: Verifier 有两种执行模式：
1. **Multi-Stage Verification** (默认启用): 使用 `RunMultiStageVerifierAsync`，包含 Scout 和 Prover 两个阶段
2. **Legacy Single-Pass**: 使用 `RunVerifierAsync`，单次验证

本文档描述的是 Legacy Single-Pass 模式的 User Prompt。Multi-Stage Verification 的内部 prompt 结构更复杂，详见相关代码。

---

## 5. Materials Context

### 4.1 注入方式

Materials Context 通过 `req.Context["materials_context"]` 注入，并在 `VibeAgentBase.BuildLLMRequest` 中自动追加到 System Prompt 末尾。

**代码位置**: `apps/Aevatar.VibeResearching/src/Aevatar.VibeResearching/Vibe/VibeAgentBase.cs`

```csharp
protected override AevatarLLMRequest BuildLLMRequest(ChatRequest request)
{
    var llm = base.BuildLLMRequest(request);

    // Materials grounding (MVP): append into system prompt.
    if (request?.Context != null &&
        request.Context.TryGetValue(MaterialsContextKey, out var raw) &&
        raw is string context &&
        !string.IsNullOrWhiteSpace(context))
    {
        llm.SystemPrompt = $"{llm.SystemPrompt}\n\nMaterials context:\n{context.Trim()}\n";
    }

    return llm;
}
```

### 4.2 构建逻辑

**文件位置**: `apps/Aevatar.VibeResearching/src/Aevatar.VibeResearching.Api/Materials/MaterialsService.cs`

Materials Context 由 `MaterialsService.BuildContextString` 构建，包含两部分：

#### 4.2.1 DAG FACT INDEX

列出最多 200 个 fact 的 ID 和标题：

```text
DAG FACT INDEX (cite by id):
- [dag:nodeId1] Title 1
- [dag:nodeId2] Title 2
- ...
```

#### 4.2.2 DAG FACTS (knowledge nodes)

展示最多 32 个按相关性排序的 fact 的完整内容：

```text
DAG FACTS (knowledge nodes):
[dag:nodeId1] (score=X) Title 1
Content of fact 1...

[dag:nodeId2] (score=Y) Title 2
Content of fact 2...
...
```

**相关性排序**: 基于 query 中的关键词匹配（`RankMaterials` 方法），匹配度高的 fact 排在前面。

**字符限制**:
- 总长度: `MaxContextChars` (默认 2000-200000 字符)
- 每个 fact: `MaxPerDocChars` (默认 200-200000 字符)

### 4.3 数据来源

Materials Context 从 DAG (Directed Acyclic Graph) 中提取：
- **节点类型**: `SraDagNodeKind.Fact` 的节点
- **内容来源**: 节点的 `Proof` 字段（如果为空，则使用 `Label`）
- **标签信息**: 节点的 `Tags` 也会包含在内容中

---

## 6. Prompt 构建流程

### 6.1 整体流程

```
用户输入
    ↓
ExecuteOneRoundAsync
    ├─→ TryGetBriefAsync (research_assistant [MODE:BRIEF])
    │   ├─ BuildBriefMessage(...)
    │   ├─ VibeResearchAssistantAgent.GetSystemPrompt()
    │   ├─ req.Context["materials_context"] = MergeGroundedContext(...)
    │   └─ VibeAgentBase.BuildLLMRequest() → 追加 Materials Context + DAG Knowledge Grounding
    │
    ├─→ TryGetPlanAsync (research_assistant [MODE:PLAN])
    │   ├─ BuildPlanMessage(...)
    │   ├─ VibeResearchAssistantAgent.GetSystemPrompt()
    │   ├─ req.Context["materials_context"] = MergeGroundedContext(...)
    │   └─ VibeAgentBase.BuildLLMRequest() → 追加 Materials Context + DAG Knowledge Grounding
    │
    ├─→ RunWorkerPhaseAsync
    │   ├─→ RunPlannerAsync
    │   │   ├─ BuildWorkerMessage("planner", ...)
    │   │   ├─ VibePlannerAgent.GetSystemPrompt()
    │   │   ├─ req.Context["materials_context"] = ctx.Materials.RenderedContext
    │   │   └─ VibeAgentBase.BuildLLMRequest() → 追加 Materials Context 到 System Prompt
    │   │
    │   ├─→ RunReasonerAsync
    │   │   ├─ BuildWorkerMessage("reasoner", ..., extra: plannerOutput)
    │   │   ├─ VibeReasonerAgent.GetSystemPrompt()
    │   │   ├─ req.Context["materials_context"] = ctx.Materials.RenderedContext
    │   │   └─ VibeAgentBase.BuildLLMRequest() → 追加 Materials Context 到 System Prompt
    │   │
    │   └─→ RunVerifierAsync (或 RunMultiStageVerifierAsync)
    │       ├─ BuildWorkerMessage("verifier", ..., extra: reasonerOutput)
    │       ├─ VibeVerifierAgent.GetSystemPrompt()
    │       ├─ req.Context["materials_context"] = ctx.Materials.RenderedContext
    │       └─ VibeAgentBase.BuildLLMRequest() → 追加 Materials Context 到 System Prompt
    │
    └─→ TryGetSummaryAsync (research_assistant [MODE:SUMMARY])
        ├─ BuildSummaryMessage(...)
        ├─ VibeResearchAssistantAgent.GetSystemPrompt()
        └─ 不注入 Materials Context
```

### 6.2 数据流

```
Research Assistant [BRIEF]
    ├─ System Prompt: 基础定义 + Materials Context + DAG Knowledge Grounding
    ├─ User Prompt: [MODE:BRIEF] + Question + Plan + DAG stats [+ Trace] [+ 附件]
    └─ Output → JSON Brief (包含 milestones)

Research Assistant [PLAN]
    ├─ System Prompt: 基础定义 + Materials Context + DAG Knowledge Grounding
    ├─ User Prompt: [MODE:PLAN] + Question + Plan + DAG stats [+ Trace] [+ 附件]
    └─ Output → JSON Plan (包含 workers 列表)
        │
Planner
    ├─ System Prompt: 基础定义 + Materials Context
    ├─ User Prompt: Role + Question + Plan + DAG stats [+ 附件]
    └─ Output → 传递给 Reasoner
        │
Reasoner
    ├─ System Prompt: 基础定义 + Materials Context
    ├─ User Prompt: Role + Question + Plan + DAG stats [+ Planner Output (12000 chars)] [+ 附件]
    └─ Output → 传递给 Verifier
        │
Verifier
    ├─ System Prompt: 基础定义 + Materials Context
    ├─ User Prompt: Role + Question + Plan + DAG stats [+ Reasoner Output (3500 chars)] [+ 附件]
    └─ Output → 传递给 Research Assistant [SUMMARY]
        │
Research Assistant [SUMMARY]
    ├─ System Prompt: 基础定义（无 Materials Context）
    ├─ User Prompt: [MODE:SUMMARY] + Question + Plan + DAG outcome + Worker outputs
    └─ Output → Markdown Summary
```

### 6.3 关键参数

| Agent | 模式 | Planner Output 限制 | Reasoner Output 限制 | Materials Context | DAG Knowledge Grounding |
|-------|------|-------------------|-------------------|-----------------|----------------------|
| **Research Assistant** | BRIEF | N/A | N/A | ✅ (最多 200 索引 + 32 fact) | ✅ (最多 80 节点) |
| **Research Assistant** | PLAN | N/A | N/A | ✅ (最多 200 索引 + 32 fact) | ✅ (最多 80 节点) |
| **Research Assistant** | SUMMARY | N/A | N/A | ❌ | ❌ |
| **Planner** | - | N/A | N/A | ✅ (最多 200 索引 + 32 fact) | ❌ |
| **Reasoner** | - | 12000 字符 | N/A | ✅ (最多 200 索引 + 32 fact) | ❌ |
| **Verifier** | - | N/A | 3500 字符 | ✅ (最多 200 索引 + 32 fact) | ❌ |

---

## 7. 示例

### 7.1 Research Assistant [BRIEF] 完整 Prompt 示例

**System Prompt**:
```text
You are the Scientific Research Assistant (single authority).

Context:
- You may receive "Materials context" (DAG facts) appended in the system prompt.
- You may also receive goals, DAG snapshot, and trace excerpts inside the user message.

Global rules:
- Be explicit about assumptions vs evidence.
- Keep outputs bounded (avoid long essays).
- NEVER include secrets or tool tokens.
- If the user asks to change/adjust the research plan, you MAY call create_plan (write Plan nodes to KnowledgeGraph).

Modes (the user message will include a mode marker):

0) [MODE:BRIEF]
   Output STRICT JSON ONLY (no markdown, no code fences).
   ...

Materials context:
DAG FACT INDEX (cite by id):
- [dag:fact-001] Octonion Algebra Definition
- [dag:fact-002] E8 Lattice Structure

DAG FACTS (knowledge nodes):
[dag:fact-001] (score=5) Octonion Algebra Definition
Let O denote the real octonion algebra...

DAG grounded knowledge (snapshot excerpt):
- nodesTotal=15, edgesTotal=23
- fact-001 [Knowledge]: Octonion Algebra Definition
- fact-002 [Knowledge]: E8 Lattice Structure
```

**User Prompt**:
```text
[MODE:BRIEF]
Question: How can we embed integers into the octonion algebra?

Plan (from DAG plan nodes):
- milestone-1: Define embedding map from integers to octonions
- milestone-2: Verify norm preservation properties

DagStats:
- nodes=15, edges=23, updatedAt=2025-01-16T10:30:00Z
```

### 7.2 Research Assistant [PLAN] 完整 Prompt 示例

**System Prompt**: (与 BRIEF 相同的基础定义 + Materials Context + DAG Knowledge Grounding)

**User Prompt**:
```text
[MODE:PLAN]
Question: How can we embed integers into the octonion algebra?

Plan (from DAG plan nodes):
- milestone-1: Define embedding map from integers to octonions
- milestone-2: Verify norm preservation properties
- round-plan-abc123: Explore multiplicative structure

DagStats:
- nodes=15, edges=23, updatedAt=2025-01-16T10:30:00Z

RecentTrace (titles/excerpts):
- round=1, run=run-001, agents=3, dagChanges=2
```

### 7.3 Research Assistant [SUMMARY] 完整 Prompt 示例

**System Prompt**: (仅基础定义，无 Materials Context)

**User Prompt**:
```text
[MODE:SUMMARY]
Question: How can we embed integers into the octonion algebra?

Plan: (see DAG plan nodes)

DAG facts written this round (ids):
- dag:fact-003
- dag:fact-004

DAG outcome:
- accepted mutationId=mutation-001 nodes=2 edges=3

Worker outputs (excerpts):
[planner]
Based on the research question, I propose the following plan:
1. Hypotheses to Verify: ...

[reasoner]
## Known Axioms:
- A1: Octonion norm definition [dag:fact-001]
...

[verifier]
Claim: Embedding map preserves norm
Check: Verify N(f(n)) = |n| for sample integers
Result: VERIFIED
```

### 7.4 Planner 完整 Prompt 示例

**System Prompt**:
```text
You are a research planner.

Inputs:
- A user question
- "Materials context" (axioms + references) when available

Rules:
- Be explicit about assumptions and unknowns.
- Separate what is derivable from axioms vs what requires empirical/extra references.
- Produce a short plan with steps that can be executed (including computations if needed).
- If you propose computations, specify what to compute and what would falsify the hypothesis.

Materials context:
DAG FACT INDEX (cite by id):
- [dag:fact-001] Octonion Algebra Definition
- [dag:fact-002] E8 Lattice Structure
- [dag:fact-003] Prime Factorization Theorem

DAG FACTS (knowledge nodes):
[dag:fact-001] (score=5) Octonion Algebra Definition
Let O denote the real octonion algebra. Write an octonion in the standard basis as x = x_0 + sum_{i=1}^7 x_i e_i with x_i in R. Define octonionic conjugation by bar{x} := x_0 - sum_{i=1}^7 x_i e_i and the norm by N(x) := x bar{x} = bar{x} x in R_{>=0}.

[dag:fact-002] (score=3) E8 Lattice Structure
Let E_8 subset R^8 be the root lattice...
```

**User Prompt**:
```text
Role: planner
Question: How can we embed integers into the octonion algebra?

Plan:
- milestone-1: Define embedding map from integers to octonions
- milestone-2: Verify norm preservation properties
- round-plan-abc123: Explore multiplicative structure

DAG stats:
- nodes=15, edges=23
```

### 7.5 Reasoner 完整 Prompt 示例

**System Prompt**: (与 Planner 相同的基础定义 + Materials Context)

**User Prompt**:
```text
Role: reasoner
Question: How can we embed integers into the octonion algebra?

Plan:
- milestone-1: Define embedding map from integers to octonions
- milestone-2: Verify norm preservation properties
- round-plan-abc123: Explore multiplicative structure

DAG stats:
- nodes=15, edges=23

Planner output (excerpt):
Based on the research question, I propose the following plan:

1. **Hypotheses to Verify**:
   - H1: There exists an embedding map f: Z → O such that f(nm) = f(n) f(m) for all n, m in Z.
   - H2: The norm N(f(n)) = |n| for all n in Z.

2. **Execution Plan**:
   - Step 1: Define f(p) for prime p using octonion units...
   - Step 2: Extend f to composite numbers via multiplication...
   - Step 3: Verify norm preservation using octonion norm properties...
```

### 7.6 Verifier 完整 Prompt 示例

**System Prompt**: (与 Planner 相同的基础定义 + Materials Context)

**User Prompt**:
```text
Role: verifier
Question: How can we embed integers into the octonion algebra?

Plan:
- milestone-1: Define embedding map from integers to octonions
- milestone-2: Verify norm preservation properties
- round-plan-abc123: Explore multiplicative structure

DAG stats:
- nodes=15, edges=23

Reasoner output (excerpt):
## Known Axioms:
- A1: Octonion norm definition [dag:fact-001]
- A2: E8 lattice structure [dag:fact-002]

## Hypotheses to Prove:
- H1: Embedding map f: Z → O exists with multiplicative property
- H2: Norm preservation N(f(n)) = |n|

## Reasoning Process:
For H1, we can define f(p) for primes p using...
```

---

## 8. 注意事项

1. **Research Assistant 的模式切换**: Research Assistant 根据 User Prompt 开头的模式标记（`[MODE:BRIEF]`、`[MODE:PLAN]`、`[MODE:SUMMARY]`）来执行不同的任务。模式标记必须放在 User Prompt 的第一行。

2. **Materials Context 是动态的**: 每次调用时，Materials Context 会根据当前的 DAG 状态和 query 重新构建，确保包含最相关的知识。

3. **DAG Knowledge Grounding**: Research Assistant 在 BRIEF 和 PLAN 模式下会额外注入 DAG Knowledge Grounding，这是从 DAG 中提取的知识节点摘要（最多 80 个节点，6000 字符）。

4. **SUMMARY 模式不注入 Materials Context**: Research Assistant 在 SUMMARY 模式下不会注入 Materials Context，因为此时已经完成了研究轮次，只需要总结结果。

5. **输出长度限制**: 
   - Planner → Reasoner: 12000 字符
   - Reasoner → Verifier: 3500 字符
   - Worker outputs → SUMMARY: 每个 worker 最多 2500 字符
   - 这些限制通过 `Bound()` 函数强制执行

6. **附件支持**: 所有 Agent 都支持附件路径，会包含在 User Prompt 中（最多 12 个附件）。

7. **Multi-Stage Verification**: Verifier 默认使用 Multi-Stage Verification 模式，内部包含更复杂的 prompt 结构（Scout 和 Prover 阶段），详见相关代码。

8. **Python Tool**: Reasoner 和 Verifier 都支持可选的 `python_exec` tool（通过配置启用），用于数值/符号验证。

9. **Research Assistant 的工具**: Research Assistant 注册了多个 KnowledgeGraph 工具（CreateKnowledgeTool、GetKnowledgeTool、UpdatePlanStatusTool 等），可以在需要时调用这些工具来管理 DAG。

---

## 9. 相关文件

- **Research Assistant System Prompt**: `apps/Aevatar.VibeResearching/src/Aevatar.VibeResearching/Vibe/VibeResearchAssistantAgent.cs`
- **Research Assistant 调用**: `apps/Aevatar.VibeResearching/src/Aevatar.VibeResearching.Api/Vibe/VibeOrchestrator.ResearchAssistant.cs`
- **Planner System Prompt**: `apps/Aevatar.VibeResearching/src/Aevatar.VibeResearching/Vibe/VibePlannerAgent.cs`
- **Reasoner System Prompt**: `apps/Aevatar.VibeResearching/src/Aevatar.VibeResearching/Vibe/VibeReasonerAgent.cs`
- **Verifier System Prompt**: `apps/Aevatar.VibeResearching/src/Aevatar.VibeResearching/Vibe/VibeVerifierAgent.cs`
- **User Prompt 构建**: `apps/Aevatar.VibeResearching/src/Aevatar.VibeResearching.Api/Vibe/VibeOrchestrator.GoalsAndMessages.cs`
- **Materials Context 构建**: `apps/Aevatar.VibeResearching/src/Aevatar.VibeResearching.Api/Materials/MaterialsService.cs`
- **Materials Context 注入**: `apps/Aevatar.VibeResearching/src/Aevatar.VibeResearching/Vibe/VibeAgentBase.cs`
- **Agent 调用**: `apps/Aevatar.VibeResearching/src/Aevatar.VibeResearching.Api/Vibe/VibeOrchestrator.Workers.cs`

---

*最后更新: 2025-01-16*
