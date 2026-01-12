## Vibe Researching：多 Agent 编排与数据更新时机（按代码走读）

本文档面向维护者，目标是回答一个具体问题：
**当用户提出一个研究方向/问题后，各个 AI agents 分别做什么？什么时候会更新 Brief / DAG（plan/knowledge）/ Trace / UI 消息？**

内容基于当前代码实现（以 `scientific-research-assistant/src/ScientificResearchAssistant.Api/Vibe/VibeOrchestrator*.cs` 为准）。

---

### 1) 名词对齐：一次 run / 一次 round / File-SSoT

- **run**：一次用户输入触发的后端执行单元（`runId` 通常形如 `{sessionId}:{seq}`）。
- **round**：Trace 视角的一轮总结条目（`SraRoundSummary.RoundIndex` 单调递增，best-effort）。
- **File-SSoT**：brief/DAG/trace/deliverables 等关键状态都以文件为真源，内存与 UI 是投影。

---

### 2) 入口：用户“提出研究方向”后发生什么？

用户在 UI 中提交输入（`POST /api/sessions/{id}/input`，`mode` 为 `vibe|vibe_researching`）后：

- `ResearchRunExecutor.ExecuteVibeResearchingRunAsync(...)` 会先：
  - 发 `RunStartedEvent`
  - 发一条 `user` 消息到 AG-UI（messageId：`msg:{sessionId}:user:{runId}`）
  - 开始一条合并的主 assistant 流（messageId：`msg:{sessionId}:assistant:{runId}`）
  - **Step: `vibe.materials`**：加载 `facts/ + sources/`，构建 bounded materials context，并发 `StateSnapshotEvent`（Workspace 面板刷新）
  - 调用 `VibeOrchestrator.ExecuteOneRoundAsync(...)` 执行这一轮
  - 结束主 assistant 流并发 `RunFinishedEvent`

---

### 3) `VibeOrchestrator.ExecuteOneRoundAsync`：一轮的真实执行顺序

核心顺序（**按当前代码**）：

1. **加载上下文（best-effort）**
   - `DagStore.LoadSnapshotAsync(dagId)`：拿最新 DAG snapshot（可能是跨 session 共享）
   - `TraceStore.LoadLatestAsync(sessionId, max:5)`：拿最近 trace 作为回忆

2. **（可选）生成 Brief：仅当 session 还没有 brief 时**
   - 条件：`BriefStore.LoadAsync(sessionId).Version <= 0`
   - 调用 `research_assistant` 的 `[MODE:BRIEF]`（输出严格 JSON）
   - 保存到 `deliverables/brief.json`（version=1）
   - 发 `CustomEvent aevatar.vibe.brief_updated`
   - 同时把 brief 里 `milestones[]` **落到 DAG 的 plan nodes**（见第 4 节）

3. **每轮必跑：`research_assistant` 生成本轮 Plan（JSON）**
   - 调用 `research_assistant` 的 `[MODE:PLAN]`
   - 将 Plan JSON 的主要内容以小节形式写入主 assistant 流（用于可视化/回放）
   - 把这份 plan **落到 DAG 的 plan node**（见第 4 节）

4. **运行 workers（按确定性顺序）**
   - 默认顺序：`planner → reasoner → librarian → verifier(可选) → dag_builder`
   - 每个 worker 会有独立的 AG-UI 消息流（见第 6 节）
   - `librarian` 的输出可能触发副作用：写 facts、提供“可信 axiom 候选”（给 dag_builder 参考）
   - `dag_builder` 运行前会再刷新一次 DAG snapshot（避免共享 DAG 被其他 session 改动导致 id 冲突）

5. **DAG apply（当前实现：no verification / no consensus）**
   - 解析 `dag_builder` 输出的 JSON candidate mutation（nodes/edges）
   - 若候选 mutation 引用缺失的 `sources/*.md|txt`，会自动创建 placeholder source 文件
   - 若候选非空：直接 `DagStore.ApplyMutationAsync(...)` 并发 `CustomEvent aevatar.vibe.dag_updated`

6. **（可选）Delivery center / paper_editor**
   - 条件：仅当 DAG apply 成功且有 accepted mutation（MVP）
   - `paper_editor` 产出 patch + delivery lists，写入 deliverables（以及 paper scaffold）

7. **每轮必跑：`research_assistant` Summary（Markdown）→ TraceStore 落盘**
   - 调用 `research_assistant` 的 `[MODE:SUMMARY]`
   - 写入主 assistant 流
   - `PersistTraceAsync(...)` 把本轮摘要落盘（trace.jsonl + summary.md）
   - 发 `CustomEvent aevatar.vibe.round_summary`（包含 preview + summaryPath）

---

### 4) DAG：什么时候更新 plan node？什么时候更新 knowledge node？

#### 4.1 Plan nodes（用于 roadmap / 回放 / 可视化，不参与 grounded knowledge）

计划会被写入 DAG（node.kind=PLAN），有两类：

- **Milestones plan nodes（来自 Brief，仅首次生成 Brief 时写入）**
  - mutationId：`milestones_plan_{runId}`
  - node.kind：PLAN
  - tags：`planKind=milestone`，`milestoneRoundIndex=...`
  - nodeId：`plan_{sessionId}_ms_r{roundIndex}`（同 session 下可复用更新，避免重复堆积）

- **Round plan node（来自本轮 PLAN，每轮写入 1 个）**
  - mutationId：`plan_{runId}`
  - node.kind：PLAN
  - tags：`planKind=round`，`runId=...`，`originSessionId=...`
  - nodeId：`plan_{runId}`（与 run 绑定，基本不复用）

#### 4.2 Knowledge nodes（来自 dag_builder candidate）

- 来源：`dag_builder` 输出的 candidate JSON → 解析为 `SraDagMutation` → `DagStore.ApplyMutationAsync`
- 当前实现没有写入 `attestations`（即：不会自动“变成可 grounding 的已验证知识”）

---

### 5) Grounded context：哪些 DAG 节点会被注入到 RA 的系统提示词？

RA 在生成 **BRIEF / PLAN** 时，会把：

- `materials.RenderedContext`（facts + sources 的 bounded 摘要）
- + `BuildDagKnowledgeGrounding(dagSnap)`（DAG snapshot 摘要）

合并成 `materials_context` 注入系统 prompt。

关键：`BuildDagKnowledgeGrounding(...)` 只会采纳满足 grounding policy 的 DAG nodes：

- 必须是 `node.kind == KNOWLEDGE`
- 且 `node.attestations.count >= Vibe:DagGrounding:MinAttestations`（默认 1）
- 可选：限定 pubkeys（`Vibe:DagGrounding:RequiredPubKeys`）

因此：

- **plan nodes 永远不会进入 grounded context**
- 由 `dag_builder` 写入的知识节点在默认配置下通常也不会进入 grounded context（因为没有 attestations）

---

### 6) UI（AG-UI）：每个 agent 的消息什么时候出现/更新？

在同一轮里，UI 会看到三类消息流：

- **用户消息**：`msg:{sessionId}:user:{runId}`
- **主 assistant 合并流**：`msg:{sessionId}:assistant:{runId}`
  - 含章节化输出：Brief（若首次生成）、Plan（RA）、Round Summary、以及一些系统式提示（如 librarian 写 facts、delivery center 更新等）
- **每个 worker 自己的卡片消息流**：`msg:{sessionId}:{agent}:{runId}`
  - agent ∈ `planner|reasoner|librarian|verifier|dag_builder|paper_editor`
  - 每个卡片会在开始时发 `TextMessageStartEvent`，运行中不断发 `TextMessageContentEvent`（流式），结束发 `TextMessageEndEvent`
  - 同时附带 `CustomEvent aevatar.vibe.message_meta`，供前端标注该 message 属于哪个 agent/step/provider

---

### 7) Trace：什么时候落盘？落到哪里？包含什么？

Trace 在每轮的最后写入（在 summary 生成之后）：

- `workspace/sessions/{sessionId}/artifacts/trace/trace.jsonl`
  - append-only，每行是 protobuf-json 的 `SraRoundSummary`
- `workspace/sessions/{sessionId}/runs/{runId}/summary.md`
  - 人读摘要（bounded）

Trace 条目内容（MVP）：

- `perAgent.highlights[]`：从每个 worker 的输出中抽取少量要点
- `dagChanges[]`：若本轮 DAG apply 成功，记录被接受的 nodeId/type（否则记录错误/未产出）

写完后会发：

- `CustomEvent aevatar.vibe.round_summary`：UI timeline 增量更新（包含 `summaryPath` 与 `preview`）

---

### 8) 相关代码入口（便于继续追）

- Orchestrator 总控：`src/ScientificResearchAssistant.Api/Vibe/VibeOrchestrator.cs`
- RA 的 BRIEF/PLAN/SUMMARY：`src/ScientificResearchAssistant.Api/Vibe/VibeOrchestrator.ResearchAssistant.cs`
- Workers（per-agent streaming + message_meta）：`src/ScientificResearchAssistant.Api/Vibe/VibeOrchestrator.Workers.cs`
- DAG candidate 解析 + apply：`src/ScientificResearchAssistant.Api/Vibe/VibeOrchestrator.Parsing.cs`、`src/ScientificResearchAssistant.Api/Vibe/VibeOrchestrator.DagConsensus.cs`
- Brief/Trace 的 File-SSoT：`src/ScientificResearchAssistant.Api/Vibe/Brief/BriefStore.cs`、`src/ScientificResearchAssistant.Api/Vibe/Trace/TraceStore.cs`
- Grounding policy：`src/ScientificResearchAssistant.Api/Vibe/Dag/DagGroundingPolicy.cs`


