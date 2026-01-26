# VibeResearching Agent Prompts

本文档列出了 `research_assistant`, `planner`, `reasoner`, `verifier` 四个核心 agent 的系统提示词和用户提示词。

---

## 1. Research Assistant (research_assistant)

### 系统提示词 (System Prompt)

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

### 用户提示词 (User Prompt)

**文件位置**: `apps/Aevatar.VibeResearching/src/Aevatar.VibeResearching.Api/Vibe/VibeOrchestrator.ResearchAssistant.cs`

#### [MODE:BRIEF] 用户提示词

```text
Question: {question}

[如果存在 toAgents]
RoutingHint.ToAgents: [{toAgents}]

[如果存在附件]
AttachmentPaths:
- {attachmentPath1}
- {attachmentPath2}

Plan (from DAG plan nodes):
{BuildPlanContextFromDag(dag)}

DagStats:
- nodes={dag.Nodes.Count}, edges={dag.Edges.Count}, updatedAt={dag.UpdatedAt}

[如果存在 trace]
RecentTrace (titles/excerpts):
- round={roundIndex}, run={runId}, agents={perAgent.Count}, dagChanges={dagChanges.Count}
```

#### [MODE:PLAN] 用户提示词

```text
Question: {question}

[如果存在 toAgents]
RoutingHint.ToAgents: [{toAgents}]

[如果存在附件]
AttachmentPaths:
- {attachmentPath1}
- {attachmentPath2}

Plan (from DAG plan nodes):
{BuildPlanContextFromDag(dag)}

DagStats:
- nodes={dag.Nodes.Count}, edges={dag.Edges.Count}, updatedAt={dag.UpdatedAt}

[如果存在 trace]
RecentTrace (titles/excerpts):
- round={roundIndex}, run={runId}, agents={perAgent.Count}, dagChanges={dagChanges.Count}
```

#### [MODE:SUMMARY] 用户提示词

```text
Question: {question}

Plan: (see DAG plan nodes)

[如果存在 factsWritten]
DAG facts written this round (ids):
- {factId1}
- {factId2}

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

[verifier]
{verifierOutput (最多 2500 字符)}

...
```

### Materials Context

**注入方式**: 通过 `req.Context["materials_context"]` 注入，会自动追加到系统提示词末尾：

```text
{SystemPrompt}

Materials context:
{materials.RenderedContext}
```

---

## 2. Planner (planner)

### 系统提示词 (System Prompt)

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

### 用户提示词 (User Prompt)

**文件位置**: `apps/Aevatar.VibeResearching/src/Aevatar.VibeResearching.Api/Vibe/VibeOrchestrator.GoalsAndMessages.cs` (BuildWorkerMessage)

```text
Role: planner
Question: {question}

[如果存在附件]
AttachmentPaths:
- {attachmentPath1}
- {attachmentPath2}

Plan:
{BuildPlanContextFromDag(dag)}

DAG stats:
- nodes={dag.Nodes.Count}, edges={dag.Edges.Count}
```

### Materials Context

**注入方式**: 通过 `req.Context["materials_context"]` 注入，会自动追加到系统提示词末尾：

```text
{SystemPrompt}

Materials context:
{materials.RenderedContext}
```

---

## 3. Reasoner (reasoner)

### 系统提示词 (System Prompt)

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

### 用户提示词 (User Prompt)

**文件位置**: `apps/Aevatar.VibeResearching/src/Aevatar.VibeResearching.Api/Vibe/VibeOrchestrator.Workers.cs` (RunReasonerAsync)

```text
Role: reasoner
Question: {question}

[如果存在附件]
AttachmentPaths:
- {attachmentPath1}
- {attachmentPath2}

Plan:
{BuildPlanContextFromDag(dag)}

DAG stats:
- nodes={dag.Nodes.Count}, edges={dag.Edges.Count}

[如果存在 plannerOutput]
Planner output (excerpt):
{plannerOutput (最多 12000 字符)}
```

### Materials Context

**注入方式**: 通过 `req.Context["materials_context"]` 注入，会自动追加到系统提示词末尾：

```text
{SystemPrompt}

Materials context:
{materials.RenderedContext}
```

---

## 4. Verifier (verifier)

### 系统提示词 (System Prompt)

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

**特殊配置**:
- `EnableChatHistoryInState = false` (无状态，避免跨轮次污染)
- `EnableChatHistoryCompaction = false`
- `ChatHistoryMaxMessages = 0`
- `ChatHistorySummaryMaxChars = 0`

### 用户提示词 (User Prompt)

**文件位置**: `apps/Aevatar.VibeResearching/src/Aevatar.VibeResearching.Api/Vibe/VibeOrchestrator.Workers.cs` (RunVerifierAsync)

```text
Role: verifier
Question: {question}

[如果存在附件]
AttachmentPaths:
- {attachmentPath1}
- {attachmentPath2}

Plan:
{BuildPlanContextFromDag(dag)}

DAG stats:
- nodes={dag.Nodes.Count}, edges={dag.Edges.Count}

[如果存在 reasonerOutput]
Reasoner output (excerpt):
{reasonerOutput (最多 3500 字符)}
```

### Materials Context

**注入方式**: 通过 `req.Context["materials_context"]` 注入，会自动追加到系统提示词末尾：

```text
{SystemPrompt}

Materials context:
{materials.RenderedContext}
```

---

## 辅助函数说明

### BuildPlanContextFromDag

**文件位置**: `apps/Aevatar.VibeResearching/src/Aevatar.VibeResearching.Api/Vibe/VibeOrchestrator.GoalsAndMessages.cs`

构建 DAG Plan 节点的文本表示：

```text
- {planNodeId}: {planNodeLabel (最多 220 字符)}
- {planNodeId}: {planNodeLabel}
...
```

**逻辑**:
1. 筛选所有 `Kind == Plan` 的节点
2. 区分 `milestone` 和 `round` 类型的 plan
3. Milestones 按 `milestoneRoundIndex` 排序（最多 8 个）
4. Round plans 按更新时间倒序（取最新的 1 个）
5. 合并后输出

### BuildWorkerMessage

**文件位置**: `apps/Aevatar.VibeResearching/src/Aevatar.VibeResearching.Api/Vibe/VibeOrchestrator.GoalsAndMessages.cs`

通用的 worker 用户提示词构建函数，所有 worker（planner, reasoner, verifier, librarian）都使用此函数。

**参数**:
- `role`: agent 名称（"planner", "reasoner", "verifier" 等）
- `question`: 用户问题
- `dag`: DAG 快照
- `attachments`: 附件路径列表（可选）
- `extra`: 额外内容（可选，用于传递前一个 worker 的输出）

---

## Materials Context 注入机制

**文件位置**: `apps/Aevatar.VibeResearching/src/Aevatar.VibeResearching/Vibe/VibeAgentBase.cs`

所有继承自 `VibeAgentBase` 的 agent 都会自动将 `materials_context` 追加到系统提示词：

```csharp
protected override AevatarLLMRequest BuildLLMRequest(ChatRequest request)
{
    var llm = base.BuildLLMRequest(request);
    
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

**Materials Context 内容**:
- `materials.RenderedContext`: 从材料文件（PDF、Markdown 等）提取的文本内容
- 可能包含 DAG knowledge grounding（通过 `BuildDagKnowledgeGrounding` 构建）

---

## 提示词长度限制

| Agent | User Prompt 限制 | Extra Content 限制 | Output 限制 |
|-------|-----------------|-------------------|-------------|
| research_assistant | 无明确限制 | - | Brief: 各字段有字符限制<br>Plan: JSON<br>Summary: 40,000 字符 |
| planner | 无明确限制 | - | 20,000 字符 |
| reasoner | 无明确限制 | plannerOutput: 12,000 字符 | 40,000 字符 |
| verifier | 无明确限制 | reasonerOutput: 3,500 字符 | 20,000 字符 |

---

## 提示词构建流程

### Research Assistant

1. **BRIEF 模式**:
   ```
   System Prompt (固定)
   + Materials Context (自动追加)
   ↓
   User Prompt: [MODE:BRIEF]\n{BuildBriefMessage(...)}
   ```

2. **PLAN 模式**:
   ```
   System Prompt (固定)
   + Materials Context (自动追加)
   ↓
   User Prompt: [MODE:PLAN]\n{BuildPlanMessage(...)}
   ```

3. **SUMMARY 模式**:
   ```
   System Prompt (固定)
   (不包含 Materials Context)
   ↓
   User Prompt: [MODE:SUMMARY]\n{BuildSummaryMessage(...)}
   ```

### Planner

```
System Prompt (固定)
+ Materials Context (自动追加)
↓
User Prompt: BuildWorkerMessage("planner", question, dag, attachments)
```

### Reasoner

```
System Prompt (固定)
+ Materials Context (自动追加)
↓
User Prompt: BuildWorkerMessage("reasoner", question, dag, attachments, 
                                 extra: plannerOutput (最多 12000 字符))
```

### Verifier

```
System Prompt (固定)
+ Materials Context (自动追加)
↓
User Prompt: BuildWorkerMessage("verifier", question, dag, attachments,
                                 extra: reasonerOutput (最多 3500 字符))
```

---

## 注意事项

1. **Materials Context**: 除了 `[MODE:SUMMARY]` 模式外，所有 agent 都会接收 Materials Context
2. **DAG Context**: 所有 worker 的用户提示词都包含 DAG Plan 节点和统计信息
3. **前序输出传递**: 
   - `reasoner` 接收 `planner` 的输出（最多 12,000 字符）
   - `verifier` 接收 `reasoner` 的输出（最多 3,500 字符）
4. **Verifier 无状态**: `verifier` 被配置为无状态 agent，不会保留聊天历史
5. **输出格式**: 
   - `research_assistant [MODE:BRIEF]` 和 `[MODE:PLAN]` 必须输出严格 JSON
   - `research_assistant [MODE:SUMMARY]` 输出 Markdown
   - `reasoner` 输出 Markdown
   - `verifier` 输出结构化文本

---

*最后更新: 2025-01-16*
