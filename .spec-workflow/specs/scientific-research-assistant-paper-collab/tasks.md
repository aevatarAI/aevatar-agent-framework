# Tasks Document

- [x] 1. Add Protobuf contracts for file-mailbox + fact lifecycle + paper patches
  - File: scientific-research-assistant/src/ScientificResearchAssistant.Contracts/sra_collab.proto
  - Create new `.proto` defining:
    - `SraMailboxMessage` (envelope: session_id, message_id, from, to, type, correlation_id, payload Any)
    - `FactProposal`, `FactVote`, `FactVerification`, `FactDecision`
    - `PaperPatchProposal`
  - Purpose: Establish schema-first cross-agent boundary for file-only comm and durable decisions
  - _Requirements: 2, 3, 4_
  - _Prompt: Implement the task for spec scientific-research-assistant-paper-collab, first run spec-workflow-guide to get the workflow guide then implement the task: Role: Protobuf/API Contract Engineer | Task: Create a new Protobuf schema file `sra_collab.proto` that defines the mailbox envelope and the fact/paper collaboration messages needed by the spec (mailbox, facts_proposed lifecycle, paper patch proposal). Use `google.protobuf.Any` for payload where needed and keep messages small and evolvable. | Restrictions: All cross-agent boundary types MUST be Protobuf; do not embed large blobs in messages; do not reuse field numbers; follow existing package/namespace conventions in repo. | _Leverage: src/Aevatar.Agents.Abstractions/abstrations_messages.proto (Any + ContextValue patterns), execution_trace.proto (decision sessions inspiration) | Success: Protobuf compiles via dotnet build (Grpc.Tools), messages cover requirements 2/3/4 without ambiguity, schema is extensible (additive changes)._

- [x] 2. Create Contracts project to host generated Protobuf code
  - File: scientific-research-assistant/src/ScientificResearchAssistant.Contracts/ScientificResearchAssistant.Contracts.csproj
  - Add project that generates C# types from `sra_collab.proto`
  - Reference Google.Protobuf/Grpc.Tools consistent with repo package management
  - Purpose: Keep cross-boundary contracts isolated and reusable by API + agent runtime
  - _Requirements: 2, 4_
  - _Prompt: Implement the task for spec scientific-research-assistant-paper-collab, first run spec-workflow-guide to get the workflow guide then implement the task: Role: .NET Build/SDK Engineer | Task: Create `ScientificResearchAssistant.Contracts` project and wire protobuf code generation for `sra_collab.proto`. Ensure it builds under .NET 10 and follows repo conventions (Directory.Packages.props for versions). | Restrictions: Do not hardcode package versions in csproj; do not introduce new build systems; keep project minimal. | _Leverage: Existing csproj patterns in scientific-research-assistant/src/*, repo Directory.Packages.props | Success: `dotnet build` generates C# types for the proto and the project can be referenced by API without warnings/errors._

- [x] 3. Implement WorkspaceService (file-backed session root + bounded scan)
  - File: scientific-research-assistant/src/ScientificResearchAssistant.Api/Workspace/WorkspaceService.cs
  - Create deterministic paths:
    - `workspace/sessions/{sessionId}/paper/`
    - `workspace/sessions/{sessionId}/facts_proposed/`
    - `workspace/sessions/{sessionId}/decisions/`
    - `workspace/sessions/{sessionId}/mailbox/`
    - `workspace/sessions/{sessionId}/runs/`
    - `workspace/sessions/{sessionId}/artifacts/`
    - `workspace/sessions/{sessionId}/tmp/`
  - Add: ensure folder creation + safe within-root validation + bounded scanning for snapshot metadata
  - Purpose: Provide File-SSoT primitive used by all other components
  - _Requirements: 1, 5, 6_
  - _Prompt: Implement the task for spec scientific-research-assistant-paper-collab, first run spec-workflow-guide to get the workflow guide then implement the task: Role: Backend Engineer (Filesystem + reliability) | Task: Add `WorkspaceService` to create and manage session workspace directories and to scan them in a bounded way for UI snapshots. Enforce within-root safety and deterministic folder names. | Restrictions: No large file reads during scan; do not depend on in-memory sessions as truth; avoid >3 nesting levels; keep file operations best-effort and resilient. | _Leverage: Existing path resolution patterns in MaterialsService, steering/structure.md directory tree | Success: Session workspace tree is created on session creation; scan returns stable metadata even if directories are missing; handles `sources/` missing gracefully._

- [x] 4. Implement FileMailboxService (atomic send + in→processing lock + archive + dead-letter)
  - File: scientific-research-assistant/src/ScientificResearchAssistant.Api/Workspace/FileMailboxService.cs
  - Uses `WorkspaceService` to resolve session mailbox paths
  - Stores messages as Protobuf-JSON (or `.pb`) using the generated contracts
  - Purpose: Enforce “file-only agent comm” protocol with idempotency
  - _Requirements: 2, 5_
  - _Prompt: Implement the task for spec scientific-research-assistant-paper-collab, first run spec-workflow-guide to get the workflow guide then implement the task: Role: Distributed Systems Engineer | Task: Implement a file-backed mailbox with atomic writes and single-consumer processing using directory moves. Provide send/dequeue/ack/dead-letter operations and ensure idempotency by archiving processed messages. | Restrictions: Must use tmp→rename for send; must use move to processing for lock; never delete unarchived messages; errors go to `_dead/` with readable reason; schema is Protobuf-defined. | _Leverage: WorkspaceService paths; Aevatar `EventEnvelope` patterns for correlation ids | Success: Parallel consumers do not double-process; messages survive restart; dead-letter contains enough debugging info._

- [x] 5. Implement PaperService + PaperEditor flow (Markdown single-writer)
  - File: scientific-research-assistant/src/ScientificResearchAssistant.Api/Paper/PaperService.cs
  - Ensure `paper/outline.md` and `paper/draft.md`
  - Define patch proposal format (start with replace-span patch; keep it deterministic)
  - Provide: `ProposePatch()` writes mailbox message to `paper_editor`; `ApplyPatch()` updates files and logs action in `runs/{runId}/`
  - Purpose: Enable multi-agent collaboration without merge conflicts
  - _Requirements: 3, 5_
  - _Prompt: Implement the task for spec scientific-research-assistant-paper-collab, first run spec-workflow-guide to get the workflow guide then implement the task: Role: Product-focused Backend Engineer | Task: Implement Markdown paper primitives and a single-writer patch workflow. Non-writer agents can only propose patches via mailbox; the writer applies patches deterministically and records the application. | Restrictions: Do not allow multiple writers; patch application must be deterministic and bounded; no complex merge algorithms in MVP. | _Leverage: FileMailboxService for proposals; WorkspaceService for paths; existing runId scheme | Success: Paper files exist; patch proposals are represented as durable files; applying patch updates draft and is auditable via run logs._

- [x] 6. Implement FactLifecycleService (facts_proposed + decisions + promote)
  - File: scientific-research-assistant/src/ScientificResearchAssistant.Api/Facts/FactLifecycleService.cs
  - Create proposal files under `facts_proposed/`
  - Persist votes/verifications/final decisions under `decisions/`
  - Promotion policy:
    - soft consensus: configurable threshold
    - hard verification: python verification result=true promotes
  - Promote by moving/copying to `facts/` with stable id
  - Purpose: Keep “facts” clean and trusted
  - _Requirements: 4, 5, 6_
  - _Prompt: Implement the task for spec scientific-research-assistant-paper-collab, first run spec-workflow-guide to get the workflow guide then implement the task: Role: Backend Engineer (workflow + correctness) | Task: Implement the file-backed fact lifecycle: proposal → votes/verifications → final decision → promote to facts. Ensure all records are durable, readable, and reconstructable. | Restrictions: Must not write directly to `facts/` without a final decision; promote must be idempotent; evidence may point to sources or artifacts; keep files small. | _Leverage: Contracts messages; WorkspaceService; PythonExecTool output as verification evidence | Success: Given votes/verification files, service can decide and promote; restarts do not lose state; facts remain clean._

- [x] 7. Wire Workspace/Proposed Facts into API (session create + facts endpoint behavior change)
  - File: scientific-research-assistant/src/ScientificResearchAssistant.Api/Sessions/ResearchSessionsApi.cs
  - On session create: ensure workspace tree + paper files
  - Change `POST /api/sessions/{id}/facts` to create a proposal in `facts_proposed/` (not writing into `facts/` directly)
  - Add endpoints (MVP):
    - `GET /api/sessions/{id}/workspace` (bounded snapshot)
    - `POST /api/sessions/{id}/facts/{factId}/votes`
    - `POST /api/sessions/{id}/facts/{factId}/verifications`
    - `POST /api/sessions/{id}/facts/{factId}/promote` (server-side evaluate+promote)
  - Purpose: Expose file-backed collaboration primitives
  - _Requirements: 1, 4, 5_
  - _Prompt: Implement the task for spec scientific-research-assistant-paper-collab, first run spec-workflow-guide to get the workflow guide then implement the task: Role: API Engineer | Task: Update the sessions API to initialize file-backed workspace on session creation and to route facts/votes/verifications/promote through the filesystem services (WorkspaceService + FactLifecycleService). Provide a bounded workspace snapshot endpoint. | Restrictions: Keep endpoints minimal; do not break AG-UI SSE; do not bind to port 5000; do not expose full file contents in snapshot. | _Leverage: Existing ResearchSessionsApi patterns; current /facts endpoint; ResearchWorkspaceState | Success: Endpoints exist, return stable JSON, and drive file-backed state transitions correctly._

- [x] 8. Convert AG-UI STATE_SNAPSHOT to be file-backed
  - File: scientific-research-assistant/src/ScientificResearchAssistant.Api/Sessions/ResearchRunExecutor.cs
  - Replace/augment in-memory workspace hydration with `WorkspaceService.ScanWorkspace()`
  - Emit snapshot on connect and after key actions (proposal/vote/verify/promote/paper patch apply)
  - Purpose: UI reflects file truth and survives restart
  - _Requirements: 1, 5_
  - _Prompt: Implement the task for spec scientific-research-assistant-paper-collab, first run spec-workflow-guide to get the workflow guide then implement the task: Role: Real-time Systems Engineer | Task: Ensure AG-UI snapshot and subsequent updates are derived from file state, not in-memory state. Wire snapshot emission into the SSE connect path and into write actions. | Restrictions: No huge dumps; keep snapshots bounded; best-effort—never crash the run; ensure reconnect works after restart. | _Leverage: MapAgUiEvents snapshot logic; existing ResearchWorkspaceState; WorkspaceService scan | Success: Reconnect shows correct counts/metadata based on files even after restart; UI updates on file changes._

- [x] 9. Add minimal integration tests for the file-backed workflow
  - File: scientific-research-assistant/test/ScientificResearchAssistant.Api.Tests/PaperCollabWorkflowTests.cs
  - Cover:
    - session create → workspace tree exists
    - propose fact → file in facts_proposed
    - vote/verification → decision files exist
    - promote → fact appears in facts/
    - paper patch proposal + apply updates draft
  - Purpose: Guardrails against regressions
  - _Requirements: All_
  - _Prompt: Implement the task for spec scientific-research-assistant-paper-collab, first run spec-workflow-guide to get the workflow guide then implement the task: Role: Test Engineer (integration) | Task: Add integration tests that exercise the filesystem-backed collaboration flow end-to-end. Use temporary directories and avoid touching developer machines. | Restrictions: Never delete failing tests; keep tests deterministic; do not require network. | _Leverage: Existing test patterns under test/; dotnet test conventions | Success: Tests pass on CI/local; failures are informative; core file-backed invariants are enforced._


