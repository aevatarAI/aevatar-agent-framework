# Tasks Document

> Spec: `issue-67-research-brief-delivery-center`  
> Scope: `scientific-research-assistant/` (API + Agents + Contracts + Frontend)  
> Reference UX: [issue #67](https://github.com/aevatarAI/aevatar-agent-framework/issues/67)

- [x] 1. Extend Protobuf contracts for Brief/Compute/Delivery Center
  - File: `scientific-research-assistant/src/ScientificResearchAssistant.Contracts/sra_collab.proto`
  - Add messages:
    - `SraResearchBriefSnapshot`
    - `SraComputeRequest`, `SraComputePlan`, `SraComputeJobStatus`
    - `SraConclusionCard`, `SraEvidenceItem`, `SraNextTaskItem`, `SraDeliveryCenterSnapshot` (or per-snapshot messages)
  - Regenerate code via `dotnet build`
  - _Leverage: existing Vibe contracts in `sra_collab.proto` (Goals/DAG/Trace/PaperPatchProposal)_
  - _Requirements: 1, 3, 4, 5, 6_
  - _Prompt: Role: C# / Protobuf Contract Engineer | Task: Implement the task for spec issue-67-research-brief-delivery-center, first run spec-workflow-guide to get the workflow guide then implement the task: extend `sra_collab.proto` with Brief/Compute/Delivery contracts aligned to issue #67, ensuring bounded payloads and forward-compatible evolution rules | Restrictions: Any cross-boundary type MUST be Protobuf; do not introduce large blob fields—use file path references; do not reuse field numbers | _Leverage: `scientific-research-assistant/src/ScientificResearchAssistant.Contracts/sra_collab.proto` | _Requirements: R1-R6 | Success: `dotnet build` succeeds and generated C# types compile; messages cover required fields for brief/compute/delivery snapshots and events_

- [x] 2. Add Deliverables workspace folder + safe path helpers
  - File: `scientific-research-assistant/src/ScientificResearchAssistant.Api/Workspace/WorkspaceService.cs`
  - Add `DeliverablesDir` under `workspace/sessions/{id}/deliverables/`
  - Ensure directory is created in `EnsureSessionWorkspace`
  - _Leverage: existing `artifacts/`, `tmp/`, `runs/` folder setup_
  - _Requirements: 1, 5_
  - _Prompt: Role: Backend Infrastructure Engineer (.NET) | Task: Implement the task for spec issue-67-research-brief-delivery-center, first run spec-workflow-guide to get the workflow guide then implement the task: extend `WorkspaceService` to create and expose a deliverables folder for brief/delivery center snapshots with within-root safety | Restrictions: Keep file/folder counts bounded; preserve existing folder layout; do not exceed 800 lines per file | _Leverage: `WorkspaceService` patterns | _Requirements: R1,R5 | Success: Workspace is created with `deliverables/` and paths are stable; no traversal issues; build passes_

- [x] 3. Implement BriefStore (File-SSoT) for `deliverables/brief.json`
  - File: `scientific-research-assistant/src/ScientificResearchAssistant.Api/Vibe/Brief/BriefStore.cs` (new)
  - Persist `SraResearchBriefSnapshot` as Protobuf-JSON
  - Provide bounded read/write, atomic write via `tmp/` rename
  - _Leverage: `GoalsStore` Protobuf-JSON patterns_
  - _Requirements: 1_
  - _Prompt: Role: Backend Developer (.NET) | Task: Implement the task for spec issue-67-research-brief-delivery-center, first run spec-workflow-guide to get the workflow guide then implement the task: create `BriefStore` as File-SSoT under session deliverables, using Protobuf-JSON and atomic writes | Restrictions: No non-proto serialized types across boundaries; keep outputs bounded | _Leverage: `GoalsStore.cs` patterns | _Requirements: R1 | Success: `BriefStore.Load/Save` work, bounded and atomic; build passes_

- [x] 4. Add `VibePaperEditorAgent` (single writer role)
  - File: `scientific-research-assistant/src/ScientificResearchAssistant/Vibe/VibePaperEditorAgent.cs` (new)
  - Behavior: produce (a) paper patch proposal(s) and (b) delivery snapshots from round context
  - Output: Markdown + embedded STRICT JSON block for structured payloads (best-effort extraction)
  - _Leverage: existing `VibeAgentBase` and role prompt styles (`VibeLibrarianAgent`, `VibeDagBuilderAgent`)_
  - _Requirements: 5, 6_
  - _Prompt: Role: LLM Agent Prompt Engineer | Task: Implement the task for spec issue-67-research-brief-delivery-center, first run spec-workflow-guide to get the workflow guide then implement the task: add a `paper_editor` agent prompt that reliably emits bounded patch + structured delivery outputs for DAG↔paper conversion | Restrictions: Output must be bounded; structured part must be strict JSON (no code fences in JSON segment) | _Leverage: existing Vibe agents | _Requirements: R5,R6 | Success: Agent compiles; prompt enforces single-writer behavior; structured output is parseable best-effort_

- [x] 5. Extend ResearchRuntime to create/cache `paper_editor` agent
  - File: `scientific-research-assistant/src/ScientificResearchAssistant.Api/ResearchRuntime.cs`
  - Add `PaperEditorActor/PaperEditorAgent` to session entry + `GetPaperEditorAgentAsync`
  - _Leverage: existing `GetLibrarianAgentAsync`/`GetDagBuilderAgentAsync` patterns_
  - _Requirements: 6_
  - _Prompt: Role: Backend Developer (.NET) | Task: Implement the task for spec issue-67-research-brief-delivery-center, first run spec-workflow-guide to get the workflow guide then implement the task: wire `paper_editor` agent into `ResearchRuntime` with safe caching and initialization | Restrictions: Parameterless agent constructors; best-effort init; no breaking changes to existing agents | _Leverage: `ResearchRuntime` agent init methods | _Requirements: R6 | Success: `paper_editor` can be created per session; build passes_

- [x] 6. Implement DeliveryCenterStore (File-SSoT) for deliverables snapshots
  - File: `scientific-research-assistant/src/ScientificResearchAssistant.Api/Vibe/Delivery/DeliveryCenterStore.cs` (new)
  - Persist: `conclusions.json`, `evidence.json`, `tasks.json`, `delivery_snapshot.json`
  - Provide bounded tail reads for UI and stable versioning
  - _Leverage: `TraceStore` (JSONL + summary) and `DagStore` snapshot patterns_
  - _Requirements: 5_
  - _Prompt: Role: Backend Developer (.NET) | Task: Implement the task for spec issue-67-research-brief-delivery-center, first run spec-workflow-guide to get the workflow guide then implement the task: create a file-backed Delivery Center store under `deliverables/` with atomic writes and bounded loads | Restrictions: Keep each file human-reviewable; no large blobs in proto; keep store API minimal | _Leverage: `TraceStore`, `DagStore` | _Requirements: R5 | Success: store can Save/Load snapshot; files appear under deliverables; build passes_

- [x] 7. Implement Brief generation in Orchestrator (new stage before Round 1)
  - File: `scientific-research-assistant/src/ScientificResearchAssistant.Api/Vibe/VibeOrchestrator.cs` (or refactor into `BriefService`)
  - Add `[MODE:BRIEF]` call to `research_assistant` and persist via `BriefStore`
  - SSE: `aevatar.vibe.brief_updated` signal + bootstrap `brief_snapshot` in SSE connect
  - _Leverage: existing `TryGetPlanAsync`/`TryGetSummaryAsync` patterns_
  - _Requirements: 1_
  - _Prompt: Role: Backend Developer (.NET) | Task: Implement the task for spec issue-67-research-brief-delivery-center, first run spec-workflow-guide to get the workflow guide then implement the task: add a brief stage that runs before round planning, persists brief, and streams it to UI | Restrictions: Keep best-effort; do not break existing vibe runs; prefer small refactor over giant file growth | _Leverage: `VibeOrchestrator` + SSE custom events | _Requirements: R1 | Success: brief is generated and visible in UI; reconnect receives brief snapshot; build passes_

- [x] 8. Add Delivery update step after DAG consensus (invoke `paper_editor`)
  - Files:
    - `scientific-research-assistant/src/ScientificResearchAssistant.Api/Vibe/VibeOrchestrator.cs`
    - `scientific-research-assistant/src/ScientificResearchAssistant.Api/Paper/PaperService.cs` (reuse apply patch)
  - Flow: accepted DAG mutation → call `paper_editor` → apply patch(es) → write deliverables snapshots → emit SSE signal
  - _Leverage: existing paper single-writer patch pipeline and DAG consensus output_
  - _Requirements: 5, 6_
  - _Prompt: Role: Backend Developer (.NET) | Task: Implement the task for spec issue-67-research-brief-delivery-center, first run spec-workflow-guide to get the workflow guide then implement the task: integrate `paper_editor` into round end so delivery center updates happen every round with audit logs | Restrictions: Only `paper_editor` writes paper; keep patches bounded; fall back gracefully on parse failure | _Leverage: `PaperService`, `DagStore`, `TraceStore` | _Requirements: R5,R6 | Success: After a successful DAG consensus, deliverables update files exist and UI is notified; build passes_

- [x] 9. Introduce Compute contracts + API skeleton (no full runner yet)
  - Files:
    - `scientific-research-assistant/src/ScientificResearchAssistant.Api/Sessions/ResearchSessionsApi.cs`
    - `scientific-research-assistant/src/ScientificResearchAssistant.Api/Vibe/Compute/*` (new)
  - Add endpoints:
    - `GET /api/sessions/{id}/deliverables` (brief + delivery snapshot)
    - `POST /api/sessions/{id}/compute/decision` (execute/degrade/skip)
  - _Leverage: existing `MapDag`/`MapGoals` patterns_
  - _Requirements: 3, 4, 5_
  - _Prompt: Role: API Engineer (.NET Minimal API) | Task: Implement the task for spec issue-67-research-brief-delivery-center, first run spec-workflow-guide to get the workflow guide then implement the task: add minimal APIs and SSE signals for deliverables and compute decision flow aligned to issue #67 | Restrictions: Keep endpoints small; use DTOs; do not add port 5000; keep best-effort | _Leverage: `ResearchSessionsApi.cs` patterns | _Requirements: R3-R5 | Success: endpoints compile and return expected shapes; SSE bootstrap includes deliverables snapshot; build passes_

- [x] 10. Frontend: add Brief card + Delivery Center panel
  - File: `scientific-research-assistant/frontend/src/App.tsx`
  - New panels/components:
    - `frontend/src/panels/BriefPanel.tsx` (new)
    - `frontend/src/panels/DeliveryCenterPanel.tsx` (new)
  - Subscribe to SSE:
    - `aevatar.vibe.brief_snapshot` / `aevatar.vibe.brief_updated`
    - `aevatar.vibe.delivery_snapshot` / `aevatar.vibe.delivery_updated`
  - _Leverage: existing `GoalsPanel/DagPanel/TracePanel` patterns and right-side dashboard layout_
  - _Requirements: 1, 5_
  - _Prompt: Role: Frontend Developer (React) | Task: Implement the task for spec issue-67-research-brief-delivery-center, first run spec-workflow-guide to get the workflow guide then implement the task: add Brief and Delivery Center UI panels that stay visible, stream updates, and do not trigger full chat rerenders | Restrictions: Keep light theme and existing layout; streaming must not re-render whole message list | _Leverage: current `App.tsx` streaming map-ref pattern | _Requirements: R1,R5 | Success: brief and deliverables render correctly; updates appear via SSE without refresh; UI remains usable with long chat_

- [x] 11. Frontend: Compute plan/decision card (UX skeleton)
  - Files:
    - `scientific-research-assistant/frontend/src/panels/ComputePanel.tsx` (new)
    - `scientific-research-assistant/frontend/src/App.tsx` (wire)
  - UI: a single card with recommended option + 3 buttons (execute/degrade/skip)
  - _Leverage: issue #67 experience table; reuse existing button/card styles_
  - _Requirements: 3, 4_
  - _Prompt: Role: Frontend Developer (React/UX) | Task: Implement the task for spec issue-67-research-brief-delivery-center, first run spec-workflow-guide to get the workflow guide then implement the task: add Compute plan card UI and hook it to compute decision endpoint and SSE status updates | Restrictions: No heavy graph libs; keep card interaction minimal; do not break existing chat flow | _Leverage: existing dashboard cards | _Requirements: R3,R4 | Success: compute plan displays, decisions POST successfully, status updates stream in UI_

- [x] 12. Tests: brief/delivery stores + paper_editor integration (minimal)
  - Files:
    - `scientific-research-assistant/test/ScientificResearchAssistant.Tests/*` (add/extend)
  - Cover:
    - atomic writes + bounded reads for BriefStore/DeliveryCenterStore
    - paper patch application remains deterministic
  - _Leverage: existing TraceStore tests patterns_
  - _Requirements: All_
  - _Prompt: Role: Test Engineer (.NET/xUnit) | Task: Implement the task for spec issue-67-research-brief-delivery-center, first run spec-workflow-guide to get the workflow guide then implement the task: add unit tests for new File-SSoT stores and a small integration test for delivery update flow | Restrictions: Never delete failing tests; use deterministic temp workspaces; avoid network | _Leverage: existing SRA tests and stores | _Requirements: All | Success: tests pass locally; cover happy + error paths; no flaky timing dependence_


