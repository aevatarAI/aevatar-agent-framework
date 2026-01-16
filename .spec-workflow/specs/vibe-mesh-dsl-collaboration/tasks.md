# Tasks Document

- [x] 1. Add Mesh orchestration options + config wiring (feature flag + fallback mode)
  - File: `scientific-research-assistant/src/ScientificResearchAssistant.Api/appsettings.json`
  - File: `scientific-research-assistant/src/ScientificResearchAssistant.Api/Program.cs`
  - File: `scientific-research-assistant/src/ScientificResearchAssistant.Api/Vibe/Mesh/MeshOrchestrationOptions.cs` (new)
  - Purpose: Introduce `Vibe:MeshOrchestration` config (`Enabled`, `OnCompileError`) and DI registration
  - _Leverage: `scientific-research-assistant/src/ScientificResearchAssistant.Api/Vibe/Dag/DagConsensusRunner.cs` (pattern: mode + fallback), `WorkspaceService`
  - _Requirements: 6
  - _Prompt: Implement the task for spec vibe-mesh-dsl-collaboration, first run spec-workflow-guide to get the workflow guide then implement the task: Role: .NET Backend Engineer | Task: Add `MeshOrchestrationOptions` and wire configuration + DI in `ScientificResearchAssistant.Api` so mesh orchestration can be enabled/disabled and can choose fallback vs fail-fast on compile errors | Restrictions: Do not change default behavior (must remain disabled by default); do not introduce port :5000; keep files small and SRP | _Leverage: Existing pattern in `DagConsensusRunner` config resolution | _Requirements: Requirement 6 | Success: Options load correctly, defaults preserve current behavior, DI registers options, and code builds

- [x] 2. Implement session-scoped MeshDefinition File-SSoT store
  - File: `scientific-research-assistant/src/ScientificResearchAssistant.Api/Vibe/Mesh/MeshDefinitionStore.cs` (new)
  - Purpose: Load/save raw mesh JSON under session workspace, and mirror audit artifacts
  - _Leverage: `scientific-research-assistant/src/ScientificResearchAssistant.Api/Workspace/WorkspaceService.cs` (session paths + safety), `GoalsStore`/`ComputeDecisionStore` (file-backed patterns)
  - _Requirements: 1, 5
  - _Prompt: Implement the task for spec vibe-mesh-dsl-collaboration, first run spec-workflow-guide to get the workflow guide then implement the task: Role: .NET Backend Engineer (File-SSoT) | Task: Create a `MeshDefinitionStore` that persists the active mesh at `workspace/sessions/{sessionId}/decisions/mesh.json` and can best-effort write artifacts under `artifacts/mesh/` | Restrictions: No unbounded directory scans; never allow path traversal; keep writes atomic (write temp + move) | _Leverage: `WorkspaceService` conventions | _Requirements: Requirement 1, 5 | Success: Store can save and load raw JSON for a session and uses deterministic, safe paths

- [x] 3. Implement Mesh compiler wrapper with SRA-specific allowlists and structured error output
  - File: `scientific-research-assistant/src/ScientificResearchAssistant.Api/Vibe/Mesh/MeshCompilerService.cs` (new)
  - Purpose: Compile + validate `MeshDefinition` via `CognitiveDslCompiler`, returning `(ok, definition?, errors[])`
  - _Leverage: `cognitive-mesh/Aevatar.CognitiveMesh.Dsl/CognitiveDslCompiler.cs`, `cognitive-mesh/Aevatar.CognitiveMesh.Dsl/Options/CognitiveDslOptions.cs`, `DslCompilationException`
  - _Requirements: 2, 4, 5
  - _Prompt: Implement the task for spec vibe-mesh-dsl-collaboration, first run spec-workflow-guide to get the workflow guide then implement the task: Role: .NET Backend Engineer (validation) | Task: Build `MeshCompilerService` that compiles raw JSON into a normalized `MeshDefinition` using `CognitiveDslCompiler`, configured with SRA’s allowed node types and constraint types; surface actionable errors without throwing to callers | Restrictions: Do not weaken DSL validation; do not accept unknown node types/channels; keep error payload bounded and stable | _Leverage: `CognitiveDslOptions.With(...)` | _Requirements: Requirement 2, 4, 5 | Success: Valid DSL compiles; invalid DSL returns structured errors; unit tests can cover both paths

- [x] 4. Define supported SRA node types + channel semantics mapping (documented mapping table)
  - File: `scientific-research-assistant/src/ScientificResearchAssistant.Api/Vibe/Mesh/SraMeshMappings.cs` (new)
  - Purpose: Single source of truth for supported `NodeSpec.Type` and `EdgeSpec.Channel` semantics
  - _Leverage: Existing SRA roles: `planner`, `reasoner`, `librarian`, `verifier`, `dag_builder` (see `VibeOrchestrator.Workers.cs`)
  - _Requirements: 3
  - _Prompt: Implement the task for spec vibe-mesh-dsl-collaboration, first run spec-workflow-guide to get the workflow guide then implement the task: Role: Software Architect | Task: Create a small mapping module that defines allowed node types and allowed edge channels for mesh-driven orchestration, including human-readable descriptions used in errors and docs | Restrictions: Keep it minimal (only what we execute in v1); no dynamic reflection discovery in v1 | _Leverage: existing worker step names and message meta patterns | _Requirements: Requirement 3 | Success: There is a central allowlist + description table used by planner and error reporting

- [x] 5. Implement MeshExecutionPlanner (topo order + bindings + bounds)
  - File: `scientific-research-assistant/src/ScientificResearchAssistant.Api/Vibe/Mesh/MeshExecutionPlanner.cs` (new)
  - File: `scientific-research-assistant/src/ScientificResearchAssistant.Api/Vibe/Mesh/MeshExecutionPlan.cs` (new)
  - Purpose: Convert `MeshDefinition` into an executable plan; reject unsupported constructs before execution
  - _Leverage: `Aevatar.CognitiveMesh.Dsl.Models.MeshDefinition` (nodes/edges/constraints), `SraMeshMappings`
  - _Requirements: 3, 4
  - _Prompt: Implement the task for spec vibe-mesh-dsl-collaboration, first run spec-workflow-guide to get the workflow guide then implement the task: Role: Backend Engineer (algorithms) | Task: Create `MeshExecutionPlanner` that validates node types and edge channels, computes topo order, and produces per-node inbound bindings; enforce budget bounds (max_steps/token_limit) at plan level | Restrictions: No cycles allowed; cycle detection must produce actionable error; keep algorithm deterministic | _Leverage: existing DAG explain/topo patterns if helpful | _Requirements: Requirement 3, 4 | Success: Planner returns a stable plan or a bounded list of errors without partial execution

- [x] 6. Implement MeshExecutionRunner (exec nodes via ResearchRuntime + AG-UI events)
  - File: `scientific-research-assistant/src/ScientificResearchAssistant.Api/Vibe/Mesh/MeshExecutionRunner.cs` (new)
  - Purpose: Execute plan nodes and capture outputs, while emitting observability events and per-agent messages
  - _Leverage: `VibeOrchestrator.Workers.cs` (StartAgentMessage/EmitAgentDelta/EndAgentMessage patterns), `ResearchRuntime` agent getters, `RefreshToolsSnapshotAsync`
  - _Requirements: 3, 4, 5
  - _Prompt: Implement the task for spec vibe-mesh-dsl-collaboration, first run spec-workflow-guide to get the workflow guide then implement the task: Role: .NET Backend Engineer (orchestration) | Task: Build `MeshExecutionRunner` that executes nodes in topo order using existing SRA agents (planner/reasoner/librarian/verifier/dag_builder), routes bounded outputs according to channel semantics, and emits AG-UI custom events (`aevatar.vibe.mesh_*`) | Restrictions: Must not change existing message id conventions; must keep outputs bounded; best-effort: don’t crash server on tool/LLM errors | _Leverage: `VibeOrchestrator.Workers` helper methods (copy or refactor minimally) | _Requirements: Requirement 3, 4, 5 | Success: Runner executes a standard mesh equivalent to today’s pipeline and produces per-agent messages/steps/events

- [x] 7. Integrate mesh-driven workers into `VibeOrchestrator` with fallback behavior
  - File: `scientific-research-assistant/src/ScientificResearchAssistant.Api/Vibe/VibeOrchestrator.cs` (modify)
  - Purpose: Choose between mesh path and existing worker path based on options + compile result
  - _Leverage: Existing worker loop + outputs dictionary + existing DAG/trace lifecycle
  - _Requirements: 6, 1, 2
  - _Prompt: Implement the task for spec vibe-mesh-dsl-collaboration, first run spec-workflow-guide to get the workflow guide then implement the task: Role: Senior Backend Engineer | Task: Wire `MeshDefinitionStore` + `MeshCompilerService` + `MeshExecutionPlanner/Runner` into `VibeOrchestrator.ExecuteOneRoundAsync` so the worker phase can be driven by mesh; implement `OnCompileError` fallback vs fail-fast; persist artifacts and emit events | Restrictions: Default behavior unchanged (mesh disabled); keep “brief/plan/summary” path intact; do not break SSE snapshot-first assumptions | _Leverage: `DagConsensusRunner` fallback patterns | _Requirements: Requirement 6, 1, 2 | Success: With mesh disabled everything behaves identically; with mesh enabled a valid mesh runs; invalid mesh follows configured fallback mode

- [x] 8. Add minimal API endpoint(s) to view/update the session mesh (MVP)
  - File: `scientific-research-assistant/src/ScientificResearchAssistant.Api/Sessions/ResearchSessionsApi.cs` (modify)
  - Purpose: Provide an API for setting the mesh JSON for a session (admin/dev workflow); UI integration is out of scope for v1
  - _Leverage: Existing session APIs patterns; File-SSoT stores; bounded payload handling
  - _Requirements: 1, 2
  - _Prompt: Implement the task for spec vibe-mesh-dsl-collaboration, first run spec-workflow-guide to get the workflow guide then implement the task: Role: API Developer | Task: Add `GET /api/sessions/{sessionId}/mesh` and `PUT /api/sessions/{sessionId}/mesh` to load/save mesh JSON; PUT should compile/validate and return errors on failure (without executing) | Restrictions: No secrets in responses; bound payload size; avoid port :5000 in docs | _Leverage: pattern from other session endpoints | _Requirements: Requirement 1, 2 | Success: Endpoint can save mesh, validate it, and retrieve it; invalid payload returns actionable error list

- [x] 9. Tests: compiler/planner + orchestrator integration fallback
  - File: `scientific-research-assistant/test/ScientificResearchAssistant.Api.Tests/VibeMeshOrchestrationTests.cs` (new or modify existing test project)
  - Purpose: Ensure mesh compilation errors and fallback behavior are correct and stable
  - _Leverage: existing test patterns in `test/` and SRA test projects (if present)
  - _Requirements: 2, 6
  - _Prompt: Implement the task for spec vibe-mesh-dsl-collaboration, first run spec-workflow-guide to get the workflow guide then implement the task: Role: Test Engineer | Task: Add unit tests for `MeshCompilerService` and `MeshExecutionPlanner` and an integration-style test that verifies `VibeOrchestrator` falls back (or fails) based on config when mesh compile fails | Restrictions: Never delete failing tests; keep tests bounded; prefer in-memory graph and local runtime | _Leverage: `CognitiveDslCompilerTests` patterns | _Requirements: Requirement 2, 6 | Success: Tests cover success/failure compile, unsupported channels, and fallback behavior deterministically

- [x] 10. Documentation: add a short developer guide for mesh-driven orchestration
  - File: `scientific-research-assistant/docs/VIBE_RESEARCHING_PLATFORM.md` (modify)
  - File: `scientific-research-assistant/src/ScientificResearchAssistant.Api/Vibe/docs/README.md` (modify)
  - Purpose: Document config, storage path, sample mesh JSON, and failure modes
  - _Leverage: existing docs style; keep concise
  - _Requirements: 5, 6
  - _Prompt: Implement the task for spec vibe-mesh-dsl-collaboration, first run spec-workflow-guide to get the workflow guide then implement the task: Role: Technical Writer + Engineer | Task: Update docs to explain how to enable mesh orchestration, where mesh.json is stored, provide a sample mesh equivalent to default worker pipeline, and describe fallback vs fail-fast behavior | Restrictions: No :5000 in examples; keep docs concise and accurate | _Leverage: existing Vibe docs sections | _Requirements: Requirement 5, 6 | Success: Docs allow a developer to enable mesh orchestration and understand artifacts/events


