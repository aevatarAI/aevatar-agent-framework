# Requirements Document

## Introduction

This spec upgrades **AevatarKit** (`aevatar-kit/`) into an **official, optional Workbench kit** by fixing two concrete “bad smells” and ensuring the **Graph Editor → Run → UI Timeline** loop works end-to-end using a **single, standardized UI event protocol (AG-UI)**.

Scope focus (explicit):
- **Fix bad smell #1**: duplicated frontend shim implementations of `@agui/sdk` across multiple frontends.
- **Fix bad smell #2**: AevatarKit Run streaming uses a custom SSE format + custom event model, diverging from AG-UI and breaking SDK reuse.
- **Priority**: **fully run through Graph Editor/Run first**, Task/Board management is **out of scope** for this spec.

Non-goals:
- Introducing a “Weft-like Task Board” domain (Tasks/Boards/Assignments).
- Changing Aevatar core runtime semantics (Local/Orleans/ProtoActor internals).

## Alignment with Product Vision

This work directly supports the repo steering goals:
- **Collaboration & Visibility** (product.md): provide a reusable, standardized UI integration surface so multiple agent systems can share UI components and tooling.
- **Protobuf-First Contracts** (product.md, tech.md): keep AevatarKit cross-boundary contracts Protobuf-first; do not introduce new cross-boundary C# classes in the runtime path.
- **Runtime Agnostic by Design** (product.md): Workbench is an optional kit; the core framework remains clean and reusable across runtimes.

## Requirements

### Requirement 1 — Single Source of Truth for the AG-UI Frontend Client

**User Story:** As a frontend developer in this monorepo, I want a single shared implementation of `@agui/sdk` so that all Aevatar frontends reuse the same AG-UI client behavior and we avoid drift/duplication.

#### Acceptance Criteria

1. WHEN any frontend imports `@agui/sdk` THEN the system SHALL resolve it to a **single shared source file** in this repo (no duplicated per-app shim code).
2. WHEN the shared AG-UI client is updated THEN all consuming frontends SHALL automatically pick up the change without copying code.
3. IF the environment is offline or lacks the upstream `@agui/sdk` package THEN the system SHALL still work (the shared implementation is local and versioned in-repo).
4. WHEN running dev servers for the affected frontends THEN Vite SHALL be configured to allow importing the shared source from the monorepo filesystem (no “outside of root” errors).

### Requirement 2 — AevatarKit Run Streaming MUST Conform to AG-UI

**User Story:** As a user running workflows in AevatarKit, I want the Run Timeline to be driven by **AG-UI events** so that reconnection is instant (snapshot-first) and UI components/SDK code can be reused across systems.

#### Acceptance Criteria

1. WHEN a client connects to the AevatarKit run stream THEN the server SHALL emit AG-UI JSON events over SSE, where each message includes a `type` field (e.g., `MESSAGES_SNAPSHOT`, `TEXT_MESSAGE_CONTENT`).
2. WHEN a client connects/reconnects THEN the server SHALL send **snapshot-first** events:
   - `MESSAGES_SNAPSHOT` (and optionally `STATE_SNAPSHOT`) SHALL be sent before incremental events.
3. WHEN the Run produces streaming output THEN the server SHALL emit `TEXT_MESSAGE_START` / `TEXT_MESSAGE_CONTENT` / `TEXT_MESSAGE_END` events in a consistent, parseable way.
4. WHEN the Run advances steps THEN the server SHALL emit `STEP_STARTED` / `STEP_FINISHED` (and `RUN_*`) events so the UI can render a stable timeline.
5. IF a Run id is unknown THEN the server SHALL return HTTP 404 (no SSE stream).

### Requirement 3 — Graph Editor/Run “Happy Path” Must Remain Fully Functional

**User Story:** As a developer evaluating AevatarKit, I want to edit a graph, start a run, and see the run timeline + memory without protocol mismatches, so that the Workbench kit is demonstrably runnable.

#### Acceptance Criteria

1. WHEN I save a graph in the AevatarKit UI THEN the graph SHALL be persisted in the existing Graph Library (in-memory for MVP is acceptable).
2. WHEN I start a run THEN the UI SHALL show:
   - a connected run stream,
   - live timeline updates,
   - and the final output (at least the current MVP engine output).
3. WHEN I refresh the page during/after a run THEN the UI SHALL restore the latest view from snapshot-first events without replaying the full token/event history.

## Non-Functional Requirements

### Code Architecture and Modularity
- **Optional kit**: Workbench changes SHOULD stay under `aevatar-kit/` and shared frontend utilities, not pollute core framework APIs.
- **SRP**: protocol mapping logic MUST be isolated (e.g., a dedicated mapper/service) rather than embedded in endpoint lambdas.
- **No new boundary violations**: do not introduce new cross-boundary runtime contracts as ad-hoc C# classes; keep Protobuf-first for kit contracts.

### Performance
- SSE streaming MUST be lightweight and avoid “replay explosion” on reconnect (snapshot-first).
- Event mapping MUST be O(1) per event, and avoid unbounded in-memory growth beyond existing run history limits (MVP acceptable).

### Security
- Repository port policy MUST be respected (**no `:5000`** defaults in docs/config/examples).
- Shared AG-UI client MUST not assume privileged browser APIs; it should be a minimal EventSource-based client.

### Reliability
- Streaming endpoints MUST handle client disconnects without crashing the process.
- Snapshot generation MUST be best-effort: failures to build snapshots MUST not prevent streaming incremental events.

### Usability
- AevatarKit UI SHOULD clearly show connection state and run id, and should remain usable without extra setup steps beyond `dotnet run`.


