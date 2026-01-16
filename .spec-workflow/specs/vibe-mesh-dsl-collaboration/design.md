# Design Document

## Overview

This design implements **Option B** for the Scientific Research Assistant (SRA) “vibe researching” platform:

- Use **Cognitive Mesh DSL** (`MeshDefinition`) as a **declarative collaboration topology** for the multi-agent worker phase.
- Keep existing SRA orchestration as the **fallback path**, and keep File-SSoT + snapshot-first SSE semantics unchanged.

Important clarification (scope control):
- The **outer round lifecycle** (materials load, research_assistant brief/plan/summary, DAG/trace persistence) remains owned by `VibeOrchestrator`.
- The **worker collaboration** (planner/reasoner/librarian/verifier/dag_builder and their data dependencies) becomes **mesh-driven** when enabled.

## Steering Document Alignment

### Technical Standards (tech.md)
- **.NET 10** service-side implementation under `scientific-research-assistant/src/ScientificResearchAssistant.Api`.
- **Protobuf-first** remains intact: all cross-boundary runtime messages (AG-UI events, DAG snapshots, etc.) stay Protobuf-defined.
- **Central package management** unchanged (no new packages required for MVP).

### Project Structure (structure.md)
- New code lives in `scientific-research-assistant/src/ScientificResearchAssistant.Api/Vibe/Mesh/` (API-side orchestration glue).
- DSL compiler remains in `cognitive-mesh/Aevatar.CognitiveMesh.Dsl` and is referenced/used as a library.
- Files remain small and single-responsibility (compiler wrapper, store, planner, runner).

## Code Reuse Analysis

### Existing Components to Leverage
- **`Aevatar.CognitiveMesh.Dsl.CognitiveDslCompiler`**: parse + validate + normalize `MeshDefinition`.
- **`WorkspaceService`**: deterministic session workspace paths and safe file IO boundaries.
- **`VibeOrchestrator.Workers`**: proven per-agent message projection and per-agent tool snapshot refresh patterns.
- **`ResearchRuntime`**: creates/initializes the actual agents and supports tool snapshots.
- **AG-UI event pattern**: `aevatar.vibe.message_meta`, `StepStartedEvent`/`StepFinishedEvent`, and custom `aevatar.vibe.*` signals.

### Integration Points
- **`VibeOrchestrator.ExecuteOneRoundAsync`**: entry point to choose “mesh-driven workers” vs “existing workers”.
- **Session workspace (File-SSoT)**: persist the mesh definition and compile results under `workspace/sessions/{sessionId}/...`.

## Architecture

### High-level flow

```mermaid
flowchart TD
    A[VibeOrchestrator.ExecuteOneRoundAsync] --> B{Mesh enabled?}
    B -->|No| C[Existing Workers Path]
    B -->|Yes| D[Load mesh.json (File-SSoT)]
    D --> E[Compile/Validate via CognitiveDslCompiler]
    E -->|fail| F{OnCompileError?}
    F -->|fallback| C
    F -->|fail-fast| X[Abort run with error artifact + events]
    E -->|ok| G[MeshExecutionPlanner: build executable plan]
    G --> H[MeshExecutionRunner: run nodes in topo order]
    H --> I[Return worker outputs to orchestrator]
    I --> J[Existing DAG consensus + trace + summary]
```

### Modular design principles
- **Store ≠ Compile ≠ Map ≠ Execute**: each stage has its own small type with a narrow interface.
- **Whitelists over “dynamic power”**: node types and channels are explicit allowlists to keep the system safe.
- **Fallback-first**: never crash the server due to mesh parsing; always persist artifacts + emit explainable events.

## Components and Interfaces

### 1) `IMeshDefinitionStore` (File-SSoT)
- **Purpose:** Load/save the mesh DSL JSON for a session.
- **Location:** `ScientificResearchAssistant.Api/Vibe/Mesh/MeshDefinitionStore.cs`
- **Storage paths (session-scoped):**
  - `workspace/sessions/{sessionId}/decisions/mesh.json` (SSoT for active mesh)
  - `workspace/sessions/{sessionId}/artifacts/mesh/compile_*.json` (audit artifacts)
- **Interface:**
  - `Task<string?> TryLoadRawAsync(sessionId)`
  - `Task SaveAsync(sessionId, rawJson)`

### 2) `MeshCompilerService`
- **Purpose:** Compile raw JSON into validated, normalized `MeshDefinition`, producing an error list on failure.
- **Reuses:** `CognitiveDslCompiler` + `DslCompilationException.Errors`
- **Important design choice:** The compiler is configured with **SRA-specific allowlists**:
  - Allowed node types MUST include the SRA roles we will execute.
  - Allowed constraint types MUST include the subset we implement (initially: `confidence_threshold`, `max_iterations`).

### 3) `MeshExecutionPlanner`
- **Purpose:** Convert `MeshDefinition` into a bounded, executable plan for SRA.
- **Responsibilities:**
  - Enforce supported `NodeSpec.Type` and supported `EdgeSpec.Channel` (reject unsupported).
  - Topologically sort nodes by edges.
  - Produce a per-node “input bindings” list based on incoming edges + channel semantics.
- **Outputs:**
  - `MeshExecutionPlan` (internal model): nodes, topo order, bindings, budgets.

### 4) `MeshExecutionRunner`
- **Purpose:** Execute the plan against real SRA agents using `ResearchRuntime`.
- **Reuses:**
  - `VibeOrchestrator.Workers` patterns: per-agent messages, step events, tool refresh.
- **Execution model (MVP):**
  - Execute nodes in topo order (no general loops in v1).
  - Each node executes exactly one agent call (streaming where available).
  - Node output is captured (bounded string) and routed to downstream nodes per channel.

### 5) `MeshOrchestrationOptions`
- **Purpose:** feature flags + fallback behavior.
- **Example config keys (API `appsettings.json`):**
  - `Vibe:MeshOrchestration:Enabled` (bool, default false)
  - `Vibe:MeshOrchestration:OnCompileError` = `fallback|fail` (default fallback)

## Data Models

### `MeshDefinition` (existing, from Cognitive Mesh DSL)
- Source: `Aevatar.CognitiveMesh.Dsl.Models.MeshDefinition`
- Stored as JSON, compiled/validated server-side.

### `MeshExecutionPlan` (new, API-internal)
Minimal internal shape:

```
MeshExecutionPlan
- sessionId: string
- runId: string
- budgetMaxSteps: int
- tokenLimit: int
- nodes: MeshPlanNode[]
- topoOrder: string[] (node ids)

MeshPlanNode
- id: string
- type: string (SRA role type)
- params: map<string, json>
- inbound: MeshBinding[] (channel + fromNodeId)

MeshBinding
- from: string
- channel: string
```

### Supported node types (MVP)
We start with a conservative allowlist that maps directly to existing SRA agents:
- `planner`
- `reasoner`
- `librarian`
- `verifier`
- `dag_builder`

Notes:
- The `research_assistant` role stays in the outer orchestrator for v1 to keep plan/summary behavior stable.
- Future extension can include `paper_editor` and other system agents once the mapping is proven.

### Supported channel semantics (MVP)
Channels are explicitly whitelisted to avoid accidental “action at a distance”:
- `question`: the user question text
- `materials`: the rendered materials context
- `dag_snapshot`: current DAG snapshot summary/bounded JSON
- `upstream_output`: raw bounded output from the upstream node
- `planner_output`: output from the node whose id is `planner` (if present)

Unsupported channels cause validation failure (fail-fast or fallback based on config).

## Error Handling

### Error Scenarios
1. **Mesh file missing**
   - **Handling:** treat as “disabled” and run existing worker orchestration.
   - **User Impact:** no behavior change; emit a best-effort event `aevatar.vibe.mesh_missing`.

2. **Compile/validation error (`DslCompilationException`)**
   - **Handling:** persist compile error artifact under `artifacts/mesh/`; behavior controlled by `OnCompileError`.
   - **User Impact:** either fallback silently (with a visible event) or fail-fast with a clear error message.

3. **Unsupported node type/channel**
   - **Handling:** treated as compile-time error at planning stage (not during execution).
   - **User Impact:** actionable error pointing to `nodes[i].type` or `edges[i].channel`.

4. **Agent execution throws**
   - **Handling:** mark node as failed, emit per-node step finish with error content; overall behavior for v1 is “best-effort continue” unless configured to stop.
   - **User Impact:** the run completes with partial outputs; trace includes node errors.

## Observability (AG-UI + artifacts)

### Custom events (proposed)
- `aevatar.vibe.mesh_loaded` { sessionId, ok, path?, hash? }
- `aevatar.vibe.mesh_compiled` { sessionId, ok, nodeCount, edgeCount, errors? }
- `aevatar.vibe.mesh_node_started` { sessionId, runId, nodeId, nodeType }
- `aevatar.vibe.mesh_node_finished` { sessionId, runId, nodeId, ok }

### Artifacts (File-SSoT)
Under `workspace/sessions/{sessionId}/artifacts/mesh/`:
- `mesh.json` (optional mirror copy for audit)
- `compile_{timestamp}.json` (normalized summary + hash + errors if any)
- `run_{runId}.json` (node execution summary: timings, ok/error, output byte sizes)

## Testing Strategy

### Unit Testing
- Compiler wrapper:
  - Valid mesh compiles and normalizes.
  - Invalid mesh yields all errors and stable paths.
- Planner:
  - Topological sort correctness.
  - Unsupported channel/type rejected.
  - Budget bounds applied.

### Integration Testing
- API orchestrator integration:
  - Mesh enabled -> uses mesh path for workers.
  - Compile error with `OnCompileError=fallback` -> uses existing workers.
  - Emits expected AG-UI custom events.

### End-to-End Testing
- Run a `mode=vibe` session with a mesh matching the standard pipeline and verify:
  - Per-agent messages appear (message_meta + streaming deltas).
  - DAG/trace outputs are produced as before.
  - No port `:5000` is introduced in docs/configs.


