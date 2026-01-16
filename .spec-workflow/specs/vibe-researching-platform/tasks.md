# Tasks Document

> 说明：任务按“每个任务修改 1–3 个文件”为原则拆分；每个任务都包含 `_Prompt`，用于后续实现时按 spec-workflow 的 Implementation 流程执行（标记 [-] / 实现 / log-implementation / 标记 [x]）。

- [x] 1. 扩展 Protobuf 契约（Goals / AgentComm / DAG / Trace）
  - File: `scientific-research-assistant/src/ScientificResearchAssistant.Contracts/sra_collab.proto`
  - Add: `SraGoalItem/SraGoalsSnapshot/SraGoalsUpdated`, `SraAgentUserMessage/SraAgentTask/SraAgentStatus`, `SraDag*`（Node/Edge/Mutation/Snapshot/Explain）, `SraRoundSummary`
  - Purpose: 确保所有跨边界数据（mailbox / file-based 知识库）schema-first 且可演进
  - _Leverage: `scientific-research-assistant/src/ScientificResearchAssistant.Api/Workspace/FileMailboxService.cs`（Any TypeRegistry 已引用 `SraCollabReflection`）
  - _Requirements: Requirement 2, 3, 5, 6
  - _Prompt: Implement the task for spec vibe-researching-platform, first run spec-workflow-guide to get the workflow guide then implement the task: Role: Protocol Buffers + C# contracts engineer | Task: Extend sra_collab.proto with messages for goals, agent routed messages/tasks/status, DAG node/edge/mutation/snapshot/explain, and per-round trace summary; ensure no field-number reuse and future compatibility | Restrictions: Do not change existing field numbers/types; only add new messages/fields; keep payloads bounded; anything crossing mailbox boundary must be proto | _Leverage: sra_collab.proto, FileMailboxService TypeRegistry usage | _Requirements: Requirement 2/3/5/6 | Success: `dotnet build` succeeds and generated C# types compile; new messages are usable via Any.Pack/Unpack | Process: mark this task [-] in tasks.md; search existing Implementation Logs for proto patterns; implement; build; log-implementation; mark [x].

- [x] 2. 扩展单入口输入 DTO（支持路由与附件引用）
  - File: `scientific-research-assistant/src/ScientificResearchAssistant.Api/Sessions/ResearchApiDtos.cs`
  - Add: `ToAgents`（string[]?）与 `AttachmentPaths`（string[]?）字段到 `SessionInputInDto`
  - Purpose: 保持单入口 `/api/sessions/{id}/input`，同时允许“同入口路由”给后台 agents
  - _Leverage: `scientific-research-assistant/frontend/src/App.tsx`（发送 input 的调用点）
  - _Requirements: Requirement 3, 7
  - _Prompt: Implement the task for spec vibe-researching-platform, first run spec-workflow-guide to get the workflow guide then implement the task: Role: Backend API engineer (.NET Minimal API) | Task: Extend SessionInputInDto with optional toAgents and attachmentPaths fields, documented as “single chat routing hints” for research_assistant; keep backward compatibility for existing clients | Restrictions: Do not break existing JSON shape; keep naming camelCase via existing JSON options; no business logic here | _Leverage: ResearchApiDtos.cs, existing frontend usage | _Requirements: Requirement 3/7 | Success: API accepts old and new payloads; build passes | Process: mark [-]; search logs; implement; build; log-implementation; mark [x].

- [x] 3. GoalsStore（File-SSoT）+ Goals API（GET/PUT）
  - Files:
    - `scientific-research-assistant/src/ScientificResearchAssistant.Api/Vibe/Goals/GoalsStore.cs` (new)
    - `scientific-research-assistant/src/ScientificResearchAssistant.Api/Sessions/ResearchSessionsApi.cs` (update)
  - Add endpoints:
    - `GET /api/sessions/{id}/goals`
    - `PUT /api/sessions/{id}/goals`
  - Behavior: 写入 `workspace/sessions/{id}/decisions/goals.json`（proto-json 原子写）；发布 `CUSTOM aevatar.vibe.goals_updated`；并向 mailbox 广播 `SraGoalsUpdated`（给所有已知 agentName）
  - _Leverage: `WorkspaceService`（within-root + paths）, `FileMailboxService`（Send）
  - _Requirements: Requirement 2, 8
  - _Prompt: Implement the task for spec vibe-researching-platform, first run spec-workflow-guide to get the workflow guide then implement the task: Role: Backend engineer (file-backed state + SSE projection) | Task: Implement GoalsStore that loads/saves goals snapshot in decisions/goals.json using proto-json atomic writes; add GET/PUT goals endpoints; on update publish AG-UI CUSTOM goals_updated and broadcast SraGoalsUpdated via mailbox | Restrictions: Must enforce within-root; goals file must be deterministic and bounded; no long blocking on SSE path | _Leverage: WorkspaceService, FileMailboxService, ResearchSessionsApi patterns | _Requirements: Requirement 2/8 | Success: Goals persist to disk; reconnect bootstrap can read; SSE emits goals_updated; build passes | Process: mark [-]; search logs; implement; build; log-implementation; mark [x].

- [x] 4. UploadsStore + uploads API（multipart）
  - Files:
    - `scientific-research-assistant/src/ScientificResearchAssistant.Api/Vibe/Uploads/UploadsStore.cs` (new)
    - `scientific-research-assistant/src/ScientificResearchAssistant.Api/Sessions/ResearchSessionsApi.cs` (update)
  - Add endpoint: `POST /api/sessions/{id}/uploads`（multipart/form-data）
  - Behavior: 保存到 `workspace/sessions/{id}/artifacts/uploads/`，返回相对路径列表，供 `attachmentPaths` 引用
  - _Leverage: `WorkspaceService`（ArtifactsDir/TmpDir）, existing JSON conventions
  - _Requirements: Requirement 3, Security NFR
  - _Prompt: Implement the task for spec vibe-researching-platform, first run spec-workflow-guide to get the workflow guide then implement the task: Role: Backend engineer (safe uploads) | Task: Implement UploadsStore with allowlist extensions + size cap + atomic write; add uploads endpoint returning relative paths; ensure within-root and portable path normalization | Restrictions: No traversal; no executable extensions by default; keep response bounded | _Leverage: WorkspaceService, existing Minimal API patterns | _Requirements: Requirement 3, Security NFR | Success: Uploads saved under artifacts/uploads; returned paths work as attachmentPaths; build passes | Process: mark [-]; search logs; implement; build; log-implementation; mark [x].

- [x] 5. TraceStore（round summary 落盘 + 读取 latest N）
  - File: `scientific-research-assistant/src/ScientificResearchAssistant.Api/Vibe/Trace/TraceStore.cs` (new)
  - On-disk:
    - `workspace/sessions/{id}/artifacts/trace/trace.jsonl`
    - `workspace/sessions/{id}/runs/{runId}/summary.md`
  - _Leverage: `WorkspaceService`（RunsDir/ArtifactsDir/TmpDir）
  - _Requirements: Requirement 5, 8
  - _Prompt: Implement the task for spec vibe-researching-platform, first run spec-workflow-guide to get the workflow guide then implement the task: Role: Backend engineer (file-backed trace) | Task: Implement TraceStore to append SraRoundSummary as proto-json lines (jsonl) and write a bounded summary.md per run; implement method to read latest N summaries efficiently (bounded scanning) | Restrictions: Must be bounded; must not load entire file for large trace; atomic writes; within-root | _Leverage: WorkspaceService patterns, FactLifecycleService atomic proto-json writer (pattern) | _Requirements: Requirement 5/8 | Success: trace.jsonl grows append-only; latest N retrieval works; build passes | Process: mark [-]; search logs; implement; build; log-implementation; mark [x].

- [x] 6. File-based DagStore（snapshot/staged/consensus logs）+ Explain
  - Files:
    - `scientific-research-assistant/src/ScientificResearchAssistant.Api/Vibe/Dag/DagStore.cs` (new)
    - `scientific-research-assistant/src/ScientificResearchAssistant.Api/Vibe/Dag/DagExplain.cs` (new)
    - `scientific-research-assistant/src/ScientificResearchAssistant.Api/Sessions/ResearchSessionsApi.cs` (update: DAG endpoints)
  - Add endpoints:
    - `GET /api/sessions/{id}/dag`
    - `GET /api/sessions/{id}/dag/{nodeId}/explain`
    - `GET /api/sessions/{id}/dag/staged`（optional, debug）
  - Algorithm: 迁移 `cognitive-mesh/Aevatar.AxiomReasoning/Graph/InMemoryGraphStore.cs` 的 explain 语义
  - _Leverage: AxiomReasoning explain algorithm; WorkspaceService paths
  - _Requirements: Requirement 6
  - _Prompt: Implement the task for spec vibe-researching-platform, first run spec-workflow-guide to get the workflow guide then implement the task: Role: Backend engineer (graph/DAG) | Task: Implement file-based DagStore managing snapshot.json plus staged candidates and consensus logs; implement Explain (closure/cycle/missing/topo/provable) aligned with AxiomReasoning semantics; expose GET dag + explain endpoints | Restrictions: Must be deterministic and bounded; explain must not be O(N^2) for common cases; no manual approval endpoints | _Leverage: Aevatar.AxiomReasoning InMemoryGraphStore Explain logic, WorkspaceService | _Requirements: Requirement 6 | Success: Dag snapshot loads/saves; explain works; endpoints return stable JSON; build passes | Process: mark [-]; search logs; implement; build; log-implementation; mark [x].

- [x] 7. 接入 CognitiveStrategy 并实现 DagConsensusRunner（maker-v2 共识门控）
  - Files:
    - `scientific-research-assistant/src/ScientificResearchAssistant.Api/ScientificResearchAssistant.Api.csproj` (update: add project refs/packages as needed)
    - `scientific-research-assistant/src/ScientificResearchAssistant.Api/Program.cs` (update: DI 注册 CognitiveStrategy)
    - `scientific-research-assistant/src/ScientificResearchAssistant.Api/Vibe/Dag/DagConsensusRunner.cs` (new)
  - Behavior: 使用 `Aevatar.CognitiveMesh.Strategies.CognitiveStrategy.ExecuteAsync` 运行 workflow `maker-v2`（文件：`src/Aevatar.Agents.Cognitive/workflows/maker-v2.yaml`）对 DAG mutation 做共识生成/校验；输出解析为 `SraDagMutation`；写 `artifacts/dag/consensus/*.json`
  - _Leverage: `cognitive-mesh/Aevatar.CognitiveMesh/Strategies/CognitiveStrategy.cs`（workflows 搜索路径已支持 source path）
  - _Requirements: Requirement 6, Reliability NFR
  - _Prompt: Implement the task for spec vibe-researching-platform, first run spec-workflow-guide to get the workflow guide then implement the task: Role: Cognitive workflow integration engineer | Task: Add CognitiveStrategy to SRA API (DI + references) and implement DagConsensusRunner that executes maker-v2 workflow to produce a strict JSON DAG mutation, validates/parses it into SraDagMutation, and records consensus artifacts/logs; expose a clean async API for orchestrator usage | Restrictions: Must not block SSE bootstrap; must enforce output format (JSON-only) and red-flag handling; keep budgets configurable (k/max_rounds) | _Leverage: CognitiveStrategy.ExecuteAsync, maker-v2.yaml, existing LLM provider config | _Requirements: Requirement 6, Reliability NFR | Success: maker-v2 can be executed from SRA API; consensus result parsed and persisted; build passes | Process: mark [-]; search logs; implement; build; log-implementation; mark [x].

- [x] 8. 实现 `VibeResearchAssistantAgent`（唯一入口：调度 + 总结）
  - File: `scientific-research-assistant/src/ScientificResearchAssistant/Vibe/VibeResearchAssistantAgent.cs` (new)
  - Behavior:
    - 输入：用户消息 + goals/DAG/trace/materials context
    - 输出 A：本轮执行计划（JSON：要调用哪些 worker、各自任务）
    - 输出 B：本轮总结（Markdown + 可映射到 `SraRoundSummary`）
  - _Leverage: `VibeAgentBase`（materials 注入 + tool events），现有 `VibePlannerAgent/VibeReasonerAgent`
  - _Requirements: Requirement 3, 5
  - _Prompt: Implement the task for spec vibe-researching-platform, first run spec-workflow-guide to get the workflow guide then implement the task: Role: Agent engineer (AIGAgentBase prompts + structured outputs) | Task: Implement VibeResearchAssistantAgent as the single user-facing authority: produce (1) a structured per-round execution plan JSON for orchestrator, and (2) a per-round summary aligned with SraRoundSummary + human-readable markdown; ensure grounded/explicit assumptions and bounded outputs | Restrictions: Must be parameterless constructor or DI-safe per framework activation rules; do not leak tool secrets; must be robust to missing context | _Leverage: VibeAgentBase, ResearchToolManager wrapper | _Requirements: Requirement 3/5 | Success: Agent initializes, streams output, and can be called by orchestrator for plan+summary; build passes | Process: mark [-]; search logs; implement; build; log-implementation; mark [x].

- [x] 9. 新增后台岗位 agents：Librarian / Verifier / DagBuilder（最小可用）
  - Files:
    - `scientific-research-assistant/src/ScientificResearchAssistant/Vibe/VibeLibrarianAgent.cs` (new)
    - `scientific-research-assistant/src/ScientificResearchAssistant/Vibe/VibeVerifierAgent.cs` (new)
    - `scientific-research-assistant/src/ScientificResearchAssistant/Vibe/VibeDagBuilderAgent.cs` (new)
  - Purpose: 为 round 提供材料整理、硬验证、DAG 候选提取（staged）等能力
  - _Leverage: `VibeAgentBase`, `PythonExecTool`（Verifier 可选）
  - _Requirements: Requirement 4, 6
  - _Prompt: Implement the task for spec vibe-researching-platform, first run spec-workflow-guide to get the workflow guide then implement the task: Role: Multi-agent role designer | Task: Add minimal librarian/verifier/dag_builder agents as VibeAgentBase derivatives with focused prompts and bounded outputs; verifier optionally uses python_exec when enabled; dag_builder outputs a candidate DAG mutation JSON for consensus runner | Restrictions: Keep each agent single-responsibility; no direct writing to final DAG files; no unsafe tools by default | _Leverage: VibeAgentBase, PythonExecTool, existing planner/reasoner patterns | _Requirements: Requirement 4/6 | Success: Agents compile and can be instantiated by ResearchRuntime; outputs follow expected formats | Process: mark [-]; search logs; implement; build; log-implementation; mark [x].

- [x] 10. 扩展 ResearchRuntime：创建/缓存 research_assistant 与后台 agents Actor
  - File: `scientific-research-assistant/src/ScientificResearchAssistant.Api/ResearchRuntime.cs`
  - Add methods:
    - `GetResearchAssistantAgentAsync`
    - `GetLibrarianAgentAsync`
    - `GetVerifierAgentAsync`
    - `GetDagBuilderAgentAsync`
  - _Leverage: 现有 `GetPlannerAgentAsync/GetReasonerAgentAsync` 初始化模式（providerName、temperature、max tokens）
  - _Requirements: Requirement 4, 5
  - _Prompt: Implement the task for spec vibe-researching-platform, first run spec-workflow-guide to get the workflow guide then implement the task: Role: Backend engineer (agent runtime lifecycle) | Task: Extend ResearchRuntime session entry to create/cache additional agent actors (research_assistant/librarian/verifier/dag_builder) with deterministic ids and provider init; keep best-effort error handling consistent | Restrictions: Do not change existing agent ids; keep initialization bounded; avoid deadlocks with entry.Lock | _Leverage: ResearchRuntime existing patterns | _Requirements: Requirement 4/5 | Success: New getters work per session; build passes | Process: mark [-]; search logs; implement; build; log-implementation; mark [x].

- [x] 11. 实现 VibeOrchestrator（单轮执行：plan→workers→maker共识→写DAG→写Trace）
  - Files:
    - `scientific-research-assistant/src/ScientificResearchAssistant.Api/Vibe/VibeOrchestrator.cs` (new)
    - `scientific-research-assistant/src/ScientificResearchAssistant.Api/Sessions/ResearchRunExecutor.cs` (update: vibe path uses orchestrator)
    - `scientific-research-assistant/src/ScientificResearchAssistant.Api/Program.cs` (update: DI 注册 orchestrator + stores)
  - Behavior:
    - 读取 materials/goals/dag/trace 上下文
    - 调用 `research_assistant` 生成计划（JSON）
    - 按计划调用后台 agents（planner/reasoner/librarian/verifier/dag_builder）
    - `DagConsensusRunner` 执行 maker-v2 共识得到最终 mutation
    - `DagStore.ApplyMutation` 写 snapshot；共识失败写 staged
    - `TraceStore.AppendRoundSummary` 写 summary + trace.jsonl\n+    - AG-UI 投影：TEXT_MESSAGE_*（按 agentName 分流）+ CUSTOM(round_summary/dag_updated/consensus_blocked)\n+  - _Requirements: Requirement 3, 4, 5, 6, 8
  - _Prompt: Implement the task for spec vibe-researching-platform, first run spec-workflow-guide to get the workflow guide then implement the task: Role: Systems engineer (orchestration + SSE projection) | Task: Implement VibeOrchestrator running one round end-to-end (plan → run worker agents → maker-v2 consensus → persist DAG/trace → emit AG-UI events); integrate into existing vibe run path in ResearchRunExecutor and wire DI in Program.cs | Restrictions: Must be best-effort (no process crash on projection); must serialize runs per session; do not require manual DAG approval; keep outputs bounded | _Leverage: ResearchRunExecutor vibe flow, ResearchStreamEventContext tool sink, ResearchRuntime agent getters, DagConsensusRunner, DagStore, TraceStore | _Requirements: Requirement 3/4/5/6/8 | Success: A vibe run produces agent outputs, updates DAG snapshot, appends trace summary, and streams events to frontend; build passes | Process: mark [-]; search logs; implement; build; log-implementation; mark [x].

- [x] 12. AG-UI SSE bootstrap 扩展（goals/dag/trace/agents 快照）
  - File: `scientific-research-assistant/src/ScientificResearchAssistant.Api/Sessions/ResearchSessionsApi.cs`
  - Add bootstrap events:
    - `CUSTOM aevatar.vibe.goals_snapshot`
    - `CUSTOM aevatar.vibe.dag_snapshot`
    - `CUSTOM aevatar.vibe.trace_snapshot`
    - `CUSTOM aevatar.vibe.agents_snapshot`（best-effort）
  - _Leverage: 现有 snapshot-first 逻辑（MessagesSnapshotEvent + StateSnapshotEvent）
  - _Requirements: Requirement 1, 8
  - _Prompt: Implement the task for spec vibe-researching-platform, first run spec-workflow-guide to get the workflow guide then implement the task: Role: Backend engineer (SSE + protocol) | Task: Extend AG-UI SSE bootstrap to send goals/dag/trace/agents snapshots before live stream; keep fast bootstrap (do not await slow init) and preserve polymorphic JSON serialization correctness | Restrictions: Must remain snapshot-first; do not add replay dependency; avoid blocking on agent/tool init | _Leverage: ResearchSessionsApi MapAgUiEvents, BroadcastEventHub patterns | _Requirements: Requirement 1/8 | Success: Reconnect shows goals/dag/trace immediately; build passes | Process: mark [-]; search logs; implement; build; log-implementation; mark [x].

- [x] 13. 前端：接入 goals/dag/trace 的 CUSTOM 事件与最小展示
  - File: `scientific-research-assistant/frontend/src/App.tsx`
  - Add handlers for:
    - `aevatar.vibe.goals_snapshot/goals_updated`
    - `aevatar.vibe.dag_snapshot`
    - `aevatar.vibe.trace_snapshot/round_summary`
  - Purpose: 先把数据跑通（面板可先用 JSON 预览），确保端到端闭环可见
  - _Leverage: 现有 `CUSTOM` 分支处理、`workspaceOpen` JSON 预览模式
  - _Requirements: Requirement 7, 8
  - _Prompt: Implement the task for spec vibe-researching-platform, first run spec-workflow-guide to get the workflow guide then implement the task: Role: Frontend engineer (React + SSE state) | Task: Update App.tsx to consume new vibe custom events for goals/dag/trace and render minimal panels (JSON preview ok) without changing overall app structure yet | Restrictions: Keep single chat UX; no large refactor in this task; handle reconnect snapshot-first | _Leverage: App.tsx existing AgUiClient handlers | _Requirements: Requirement 7/8 | Success: UI shows live goals/dag/trace updates; build passes | Process: mark [-]; search logs; implement; build; log-implementation; mark [x].

- [x] 14. 前端：Goals 编辑面板（单入口 chat + goals CRUD）
  - Files:
    - `scientific-research-assistant/frontend/src/panels/GoalsPanel.tsx` (new)
    - `scientific-research-assistant/frontend/src/App.tsx` (update: mount panel + wire API calls)
  - Behavior: goals 列表增删改；保存走 `PUT /api/sessions/{id}/goals`
  - _Leverage: 现有 tailwind 样式与 sidebar patterns
  - _Requirements: Requirement 2, 7
  - _Prompt: Implement the task for spec vibe-researching-platform, first run spec-workflow-guide to get the workflow guide then implement the task: Role: Frontend engineer (React UI/UX) | Task: Add a GoalsPanel with CRUD editing; wire to goals endpoints; update App.tsx to mount the panel in the multi-panel layout region (can be simple) | Restrictions: Keep UI bounded and responsive; avoid deep refactor; do not add new dependencies unless necessary | _Leverage: App.tsx styling patterns | _Requirements: Requirement 2/7 | Success: Goals can be edited and updates reflect via SSE goals_updated | Process: mark [-]; search logs; implement; build; log-implementation; mark [x].

- [x] 15. 前端：上传 + 单入口路由发送（toAgents / attachmentPaths）
  - Files:
    - `scientific-research-assistant/frontend/src/panels/Composer.tsx` (new)
    - `scientific-research-assistant/frontend/src/App.tsx` (update: replace existing composer UI)
  - Behavior: 发送消息走 `/api/sessions/{id}/input`，支持可选 toAgents 多选与上传文件（先 `/uploads` 再带回 attachmentPaths）
  - _Leverage: 现有发送逻辑与 lucide icons
  - _Requirements: Requirement 3, 7
  - _Prompt: Implement the task for spec vibe-researching-platform, first run spec-workflow-guide to get the workflow guide then implement the task: Role: Frontend engineer (file upload + messaging UX) | Task: Implement a Composer component that supports (1) uploading files to /uploads and (2) sending a single chat message to /input with optional toAgents and attachmentPaths; keep the visible chat thread single (research_assistant) while routing hints go to backend | Restrictions: Must be non-blocking UX; handle errors gracefully; no new thread creation in UI | _Leverage: App.tsx send flow, new backend uploads API | _Requirements: Requirement 3/7 | Success: User can send message with files and routing hints; backend run starts; build passes | Process: mark [-]; search logs; implement; build; log-implementation; mark [x].

- [x] 16. 前端：DAG + Trace 面板（时间线 + explain）
  - Files:
    - `scientific-research-assistant/frontend/src/panels/DagPanel.tsx` (new)
    - `scientific-research-assistant/frontend/src/panels/TracePanel.tsx` (new)
    - `scientific-research-assistant/frontend/src/App.tsx` (update: mount panels + API calls for explain)
  - Behavior:
    - DagPanel：展示 nodes/edges，点击 node 调 `/dag/{nodeId}/explain`
    - TracePanel：展示 round summaries 时间线（来自 trace_snapshot + round_summary）
    - 可选展示 staged 列表（debug）
  - _Leverage: ReactMarkdown/remarkGfm（渲染 summary.md / markdown 摘要）
  - _Requirements: Requirement 5, 6, 7, 8
  - _Prompt: Implement the task for spec vibe-researching-platform, first run spec-workflow-guide to get the workflow guide then implement the task: Role: Frontend engineer (data visualization panels) | Task: Add DagPanel and TracePanel; wire to SSE state and backend endpoints for dag snapshot/explain; render per-round summaries and allow drilling into node explain; keep UI collapsible-ready | Restrictions: Avoid heavy graph libs initially; keep rendering bounded; handle missing data | _Leverage: ReactMarkdown usage in App.tsx | _Requirements: Requirement 5/6/7/8 | Success: User can browse DAG and trace timeline end-to-end | Process: mark [-]; search logs; implement; build; log-implementation; mark [x].

- [x] 17. 文档：更新子系统架构说明（单入口 + maker-v2 共识 + 文件布局 + 事件名）
  - File: `scientific-research-assistant/docs/VIBE_RESEARCHING_PLATFORM.md` (new)
  - Content: 目录树、File-SSoT 约定（goals/dag/trace/mailbox/uploads）、AG-UI CUSTOM 事件 `aevatar.vibe.*`、maker-v2 共识门控说明与 debug 路径
  - _Leverage: `cognitive-mesh/Aevatar.AxiomReasoning/docs/ARCHITECTURE.md`（写法）
  - _Requirements: Documentation protocol (architecture changes), Requirement 6/8
  - _Prompt: Implement the task for spec vibe-researching-platform, first run spec-workflow-guide to get the workflow guide then implement the task: Role: Technical writer/architect | Task: Create concise architecture doc for the new vibe researching workbench: single chat entrypoint, maker-v2 consensus gating for DAG, file layout, SSE event names, and extension points | Restrictions: Keep it short, precise, and aligned with repo doc tone; no outdated ports (avoid 5000) | _Leverage: AxiomReasoning ARCHITECTURE.md style | _Requirements: Requirement 6/8 | Success: Doc is accurate and helps new contributors navigate | Process: mark [-]; implement; log-implementation; mark [x].

- [x] 18. 测试：DagExplain 与 TraceStore 单元测试
  - Files:
    - `test/ScientificResearchAssistant.Tests/DagExplainTests.cs` (new)
    - `test/ScientificResearchAssistant.Tests/TraceStoreTests.cs` (new)
  - Purpose: 覆盖 explain correctness（cycle/missing/topo/provable）与 trace append/latest N 边界
  - _Leverage: AxiomReasoning explain cases as reference
  - _Requirements: Reliability NFR, Requirement 5/6
  - _Prompt: Implement the task for spec vibe-researching-platform, first run spec-workflow-guide to get the workflow guide then implement the task: Role: Test engineer (.NET) | Task: Add unit tests for DAG explain and trace store using deterministic fixtures; cover happy-path and edge cases (cycles, missing deps, bounded reads) | Restrictions: Do not delete failing tests; avoid flakiness; keep tests fast | _Leverage: AxiomReasoning explain semantics | _Requirements: Requirement 5/6, Reliability NFR | Success: Tests pass reliably and guard core logic | Process: mark [-]; implement; run tests; log-implementation; mark [x].


