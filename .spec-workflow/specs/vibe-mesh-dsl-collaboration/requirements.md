# Requirements Document

## Introduction

The Scientific Research Assistant (SRA) “vibe researching” platform currently orchestrates multiple agents (planner/reasoner/librarian/verifier/dag_builder/research_assistant) with a mostly hard-coded control flow. This spec introduces **Option B**: use the **Cognitive Mesh DSL (MeshDefinition)** as a **declarative collaboration topology** to define agent roles, connections (channels), and constraints, and have the platform execute it.

Primary value:
- Make multi-agent collaboration **configurable, reusable, and evolvable** without rewriting orchestration code.
- Keep SRA aligned with the framework vision: **Actor model + event-driven + File-SSoT + Protobuf boundary types**.

Spec scope: only one feature at a time — **Mesh DSL–driven orchestration for SRA (mode=vibe)**.

## Alignment with Product Vision

This feature supports the product principles in `.spec-workflow/steering/product.md`:
- **Events are Truth**: orchestration decisions and progress will be surfaced via structured events (AG-UI custom events) and persisted artifacts.
- **Runtime Agnostic by Design**: “what runs” is described in DSL; runtime/actor infrastructure remains behind existing abstractions.
- **Protobuf-First Contracts**: cross-boundary types remain Protobuf; the DSL is stored as a file artifact and compiled/validated server-side.

## Requirements

### Requirement 1 — Session-scoped MeshDefinition storage (File-SSoT)

**User Story:** As a platform developer, I want to store a per-session collaboration mesh definition, so that different sessions can use different agent topologies safely.

#### Acceptance Criteria
1. WHEN a session is created THEN the system SHALL support a default mesh definition (either embedded default or absent -> fallback to current orchestrator).
2. WHEN a user (or admin) submits a mesh definition THEN the system SHALL persist it under the session workspace as a file (File-SSoT).
3. IF the mesh definition file is missing THEN the system SHALL fall back to the existing non-DSL orchestration path (no runtime crash).

### Requirement 2 — Compile + validate MeshDefinition with actionable errors

**User Story:** As a platform developer, I want the server to compile and validate the MeshDefinition before execution, so that invalid topologies fail early with clear feedback.

#### Acceptance Criteria
1. WHEN a mesh definition is loaded THEN the system SHALL compile it using `Aevatar.CognitiveMesh.Dsl.CognitiveDslCompiler`.
2. IF compilation/validation fails THEN the system SHALL return a structured error payload including all validation errors (node id conflicts, invalid edges, disallowed agent types/constraints).
3. WHEN compilation succeeds THEN the system SHALL produce a normalized immutable `MeshDefinition` that the runtime uses for execution.

### Requirement 3 — Map MeshDefinition to SRA agent collaboration (execution plan)

**User Story:** As a platform developer, I want a deterministic mapping from MeshDefinition nodes/edges into SRA’s agent execution plan, so that the system can run the collaboration reliably.

#### Acceptance Criteria
1. WHEN the mesh is compiled THEN the system SHALL map `NodeSpec` entries to an executable set of SRA agent roles (or adapters) using a documented mapping table.
2. WHEN edges declare `channel` THEN the system SHALL route messages/data according to the channel semantics (e.g., “context handoff”, “vote input”, “artifact reference”), using existing platform primitives (events/files/tools) whenever possible.
3. IF the mesh requests unsupported node types or channels THEN the system SHALL reject execution with an explicit error describing unsupported elements.

### Requirement 4 — Constraints and budgets are enforced

**User Story:** As a platform owner, I want mesh constraints and budgets to be enforced, so that runs are bounded, safe, and predictable.

#### Acceptance Criteria
1. WHEN a mesh specifies `budget.max_steps` or `budget.token_limit` THEN the system SHALL enforce these bounds during execution (best-effort, with clear termination reason).
2. WHEN a mesh specifies constraints (e.g., allowed agent types, allowed constraints) THEN the system SHALL validate them during compilation and reject invalid ones.
3. IF a run exceeds a configured bound THEN the system SHALL stop execution and persist a trace artifact explaining the stop reason.

### Requirement 5 — Observability: AG-UI events + persisted artifacts

**User Story:** As a user, I want to see what the system is doing and why, so that multi-agent orchestration is transparent and debuggable.

#### Acceptance Criteria
1. WHEN a mesh is loaded/compiled THEN the system SHALL publish an AG-UI custom event including mesh identity (sessionId, mesh version/hash, ok/error).
2. WHEN mesh-driven orchestration executes THEN the system SHALL publish step-level progress events that identify which node/agent is running and which artifacts were produced/consumed.
3. WHEN a run completes or fails THEN the system SHALL persist artifacts (mesh file, compiled/normalized summary, error report if any) under the session workspace.

### Requirement 6 — Backward compatible fallback path

**User Story:** As a platform developer, I want to roll out Mesh DSL gradually, so that existing runs continue working while the new path is tested.

#### Acceptance Criteria
1. WHEN mesh orchestration is disabled by configuration THEN the system SHALL use the existing orchestrator logic.
2. WHEN mesh orchestration is enabled but compilation fails THEN the system SHALL either (a) fail fast with a clear error, or (b) fall back to the existing orchestrator based on a configuration flag.
3. WHEN mesh orchestration succeeds THEN the system SHALL produce the same externally visible deliverables (DAG/trace/deliverables) as the existing orchestration, within the same session workspace conventions.

## Non-Functional Requirements

### Code Architecture and Modularity
- **Single Responsibility Principle**: compilation, validation, mapping, and execution MUST be separated (no monolithic “do everything” class).
- **Modular Design**: introduce clear interfaces (e.g., `IMeshDefinitionStore`, `IMeshExecutionPlanner`, `IMeshExecutionRunner`) to keep the system testable.
- **Clear Interfaces**: the mapping from DSL to SRA semantics MUST be documented and versioned.

### Performance
- Mesh compilation and validation SHOULD be fast (< 100ms for typical meshes) and bounded.
- Execution orchestration MUST avoid unbounded fan-out; parallelism must be explicitly controlled.

### Security
- Mesh execution MUST NOT allow arbitrary code execution.
- Mesh-defined actions MUST be restricted to approved primitives (LLM calls, tool calls, file artifacts) and existing safety gates.

### Reliability
- The system MUST persist enough artifacts for postmortem/debug (mesh source, validation result, execution trace references).
- Failures MUST be contained per-session and not corrupt other sessions.

### Usability
- Error messages MUST be actionable (point to node ids, edge endpoints, invalid fields).
- The system SHOULD provide a simple way to view the active mesh for a session (API/UI in future specs).


