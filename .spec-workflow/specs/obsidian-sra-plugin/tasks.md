# Tasks Document

- [x] 1. Create Obsidian plugin skeleton (manifest + entrypoint)
  - Files:
    - `scientific-research-assistant/obsidian-plugin/manifest.json`
    - `scientific-research-assistant/obsidian-plugin/src/main.ts`
  - Create a minimal Obsidian Desktop plugin with a stable plugin id and a compilable TypeScript entrypoint.
  - Purpose: Establish an additive, isolated project that does not affect existing SRA web frontend/backend.
  - _Leverage: `scientific-research-assistant/docs/DEVELOPMENT.md` (ports), `scientific-research-assistant/docs/ARCHITECTURE.md` (module boundaries)_
  - _Requirements: 1, 2_
  - _Prompt: Implement the task for spec obsidian-sra-plugin, first run spec-workflow-guide to get the workflow guide then implement the task: Role: Obsidian Plugin Developer | Task: Create the minimal Obsidian plugin skeleton under `scientific-research-assistant/obsidian-plugin/` with a correct `manifest.json` and a TypeScript `src/main.ts` that loads without errors | Restrictions: Additive only; do not modify existing SRA frontend/backend; do not introduce port :5000 in any docs/config; keep files small and focused | _Leverage: Use existing monorepo conventions in `.spec-workflow/steering/structure.md` and SRA docs | _Requirements: Requirement 1, Requirement 2 | Success: Plugin can be loaded by Obsidian Desktop (after build in later tasks) and the entrypoint activates without runtime exceptions | Process: Mark this task as [-] before coding, log implementation with log-implementation (artifacts required), then mark as [x]_

- [x] 2. Add TypeScript build pipeline for the plugin (esbuild + scripts)
  - Files:
    - `scientific-research-assistant/obsidian-plugin/package.json`
    - `scientific-research-assistant/obsidian-plugin/tsconfig.json`
    - `scientific-research-assistant/obsidian-plugin/esbuild.mjs`
  - Add a minimal build pipeline that produces `dist/main.js` from `src/main.ts` and supports watch mode for desktop dev.
  - Purpose: Enable local iterative development without impacting other Node projects in the repo.
  - _Leverage: `scientific-research-assistant/frontend/` tooling patterns (Vite/esbuild concepts)_
  - _Requirements: Non-Functional (Code Architecture/Modularity, Performance)_
  - _Prompt: Implement the task for spec obsidian-sra-plugin, first run spec-workflow-guide to get the workflow guide then implement the task: Role: Tooling Engineer (TypeScript/Node) | Task: Add `package.json`, `tsconfig.json`, and an `esbuild.mjs` build script for an Obsidian plugin that outputs `dist/main.js` and supports `npm run build` + `npm run dev` (watch) | Restrictions: Keep dependencies minimal; do not touch existing repo-wide Node configs; avoid port 5000 in examples; ensure build output path is deterministic | _Leverage: SRA frontend patterns for TS build structure; Obsidian plugin conventions | _Requirements: NFR Code Architecture/Modularity, Performance | Success: `npm run build` produces `dist/main.js` and `npm run dev` watches/rebuilds without errors | Process: Mark [-] → implement → log-implementation → mark [x]_

- [x] 3. Implement plugin settings + connection test UI
  - Files:
    - `scientific-research-assistant/obsidian-plugin/src/settings.ts`
    - `scientific-research-assistant/obsidian-plugin/src/main.ts` (extend)
  - Add settings: `baseUrl` (default `http://localhost:5678`), `vaultRoot` (default `SRA`), `secretsUiUrl` (default `http://localhost:6677`), timeout.
  - Provide “Test Connection” action that calls backend `GET /health` or `GET /api/info`.
  - _Leverage: `scientific-research-assistant/README.md` (recommended ports), `src/ScientificResearchAssistant.Api/Program.cs` (`/health`, `/api/info`)_
  - _Requirements: 1, 7_
  - _Prompt: Implement the task for spec obsidian-sra-plugin, first run spec-workflow-guide to get the workflow guide then implement the task: Role: Obsidian UX Engineer | Task: Implement persistent plugin settings and a settings tab that can test connectivity to SRA backend; default baseUrl must be `http://localhost:5678` and vault root must be `SRA` | Restrictions: Do not store any API keys in plugin settings; avoid breaking changes to existing code; keep UI simple (MVP) | _Leverage: SRA backend endpoints `/health` and `/api/info` | _Requirements: Requirement 1, Requirement 7 | Success: Users can edit baseUrl, click Test Connection, and see a clear success/error result; settings persist across restarts | Process: Mark [-] → implement → log-implementation → mark [x]_

- [x] 4. Implement `SraApiClient` (HTTP) for sessions/input/deliverables
  - Files:
    - `scientific-research-assistant/obsidian-plugin/src/api/SraApiClient.ts`
    - `scientific-research-assistant/obsidian-plugin/src/api/types.ts`
  - Implement:
    - `GET /health` + `GET /api/info`
    - `POST /api/sessions`, `GET /api/sessions`
    - `POST /api/sessions/{id}/input`
    - `GET /api/sessions/{id}/deliverables`
  - Keep `baseUrl` configurable; prepare for future remote (headers injection, timeouts).
  - _Leverage: `scientific-research-assistant/src/ScientificResearchAssistant.Api/Sessions/ResearchSessionsApi.cs`_
  - _Requirements: 1, 2, 3, 6_
  - _Prompt: Implement the task for spec obsidian-sra-plugin, first run spec-workflow-guide to get the workflow guide then implement the task: Role: API Client Engineer | Task: Implement a typed HTTP client for the SRA backend covering session lifecycle, input submission, and deliverables retrieval; all requests must be based on configurable `baseUrl` | Restrictions: Additive only; do not change backend APIs; handle non-2xx with clear typed errors; do not assume CORS allowances (prefer Obsidian-safe request APIs) | _Leverage: `ResearchSessionsApi` routes and DTOs | _Requirements: Requirement 1, Requirement 2, Requirement 3, Requirement 6 | Success: Client functions can be called from plugin code to create/list sessions, send input, and pull deliverables with robust error reporting | Process: Mark [-] → implement → log-implementation → mark [x]_

- [x] 5. Implement AG-UI SSE client for Obsidian Desktop (Node stream)
  - Files:
    - `scientific-research-assistant/obsidian-plugin/src/sse/SraSseClient.ts`
    - `scientific-research-assistant/obsidian-plugin/src/sse/sseParser.ts`
  - Implement a Node `http/https` based SSE connection to:
    - `GET /api/sessions/{id}/agui/events`
  - Parse `data: <json>` frames, emit typed events, expose connection status, and support reconnection backoff.
  - _Leverage: `scientific-research-assistant/frontend/src/lib/agui-sdk.ts`, `scientific-research-assistant/src/ScientificResearchAssistant.Api/Sessions/ResearchSessionsApi.cs` (SSE format)_
  - _Requirements: 4_
  - _Prompt: Implement the task for spec obsidian-sra-plugin, first run spec-workflow-guide to get the workflow guide then implement the task: Role: Streaming/Realtime Engineer | Task: Build a robust SSE client suitable for Obsidian Desktop that connects to SRA AG-UI endpoint, parses `data:` JSON messages, dispatches by `evt.type`, and reconnects with backoff on disconnect | Restrictions: Do not rely on browser `EventSource` (CORS risk); keep parsing bounded and resilient; never block the UI thread | _Leverage: SRA web frontend AG-UI shim, backend SSE writer logic | _Requirements: Requirement 4 | Success: Given a running backend, client connects and yields decoded AG-UI events; disconnect triggers retry and status transitions are observable | Process: Mark [-] → implement → log-implementation → mark [x]_

- [x] 6. Implement `VaultStore` to persist runs/events/deliverables under `Vault/SRA/`
  - Files:
    - `scientific-research-assistant/obsidian-plugin/src/vault/VaultStore.ts`
    - `scientific-research-assistant/obsidian-plugin/src/vault/paths.ts`
  - Implement stable layout:
    - `SRA/sessions/{sessionId}/runs/{runId}/events.jsonl` (append-only)
    - `SRA/sessions/{sessionId}/deliverables/*`
  - Enforce path safety: never allow writes outside configured vaultRoot (default `SRA/`).
  - _Leverage: design.md “Vault layout”, Obsidian Vault API_
  - _Requirements: 4, 6_
  - _Prompt: Implement the task for spec obsidian-sra-plugin, first run spec-workflow-guide to get the workflow guide then implement the task: Role: Obsidian Storage Engineer | Task: Implement a vault persistence layer that writes all plugin artifacts under `Vault/SRA/` with safe path joining, append-only JSONL event logs, and deterministic overwrites for deliverables | Restrictions: Do not write outside vaultRoot; treat all writes as best-effort; prefer atomic write patterns where possible; keep the layout stable and human navigable | _Leverage: design.md vault layout; Obsidian vault adapter APIs | _Requirements: Requirement 4, Requirement 6 | Success: Given events + deliverables snapshots, files appear under `SRA/` with correct structure and content; path traversal attempts are prevented | Process: Mark [-] → implement → log-implementation → mark [x]_

- [x] 7. Build the main UI view + commands (sessions, send input, stream status)
  - Files:
    - `scientific-research-assistant/obsidian-plugin/src/ui/SraView.ts`
    - `scientific-research-assistant/obsidian-plugin/src/main.ts` (extend)
  - Implement:
    - Sidebar view: connection status, active session, message input, mode selector (`chat`/`vibe`/`vibe_loop`), run status.
    - Commands: “SRA: New Session”, “SRA: Connect to Session…”, “SRA: Send Message”.
  - Wire UI → `SraApiClient` and UI → `SraSseClient`.
  - _Leverage: `scientific-research-assistant/src/ScientificResearchAssistant.Api/Sessions/docs/README.md`_
  - _Requirements: 2, 3, 4_
  - _Prompt: Implement the task for spec obsidian-sra-plugin, first run spec-workflow-guide to get the workflow guide then implement the task: Role: Obsidian UI Developer | Task: Implement a minimal but usable sidebar view and commands to manage sessions and send messages to SRA; show streaming connection/run status based on SSE events | Restrictions: Keep UX simple; do not depend on existing SRA web frontend; do not block UI during network/IO; additive only | _Leverage: SRA Sessions module docs and endpoints | _Requirements: Requirement 2, Requirement 3, Requirement 4 | Success: User can create/select a session, send a message, and see live status updates in the view | Process: Mark [-] → implement → log-implementation → mark [x]_

- [x] 8. Add attachments workflow (upload vault files → attachmentPaths → input)
  - Files:
    - `scientific-research-assistant/obsidian-plugin/src/api/multipart.ts`
    - `scientific-research-assistant/obsidian-plugin/src/api/SraApiClient.ts` (extend)
    - `scientific-research-assistant/obsidian-plugin/src/ui/SraView.ts` (extend)
  - Implement:
    - Select current note (and/or chosen files) as attachments
    - Upload via `POST /api/sessions/{id}/uploads`
    - Include returned `attachmentPaths` in `POST /input`
  - _Leverage: `src/ScientificResearchAssistant.Api/Vibe/Uploads/UploadsStore.cs` (limits), `ResearchApiDtos.cs` (attachmentPaths)_
  - _Requirements: 5_
  - _Prompt: Implement the task for spec obsidian-sra-plugin, first run spec-workflow-guide to get the workflow guide then implement the task: Role: Full-stack (Obsidian + HTTP multipart) | Task: Implement an attachments flow: read selected vault files, upload them to SRA uploads endpoint as multipart form data, then pass `attachmentPaths` into session input | Restrictions: Must respect backend limits (max files/size, allowed extensions) and show clear errors; do not rely on local-only backend file APIs; keep it compatible with future remote baseUrl | _Leverage: UploadsStore allowed extensions and size limits; input DTO fields | _Requirements: Requirement 5 | Success: User can attach a note/file, upload succeeds, and the subsequent run sees `AttachmentPaths` reflected in downstream worker messages | Process: Mark [-] → implement → log-implementation → mark [x]_

- [x] 9. Implement “Pull Deliverables” sync into `Vault/SRA/` (render + overwrite)
  - Files:
    - `scientific-research-assistant/obsidian-plugin/src/vault/render.ts`
    - `scientific-research-assistant/obsidian-plugin/src/vault/VaultStore.ts` (extend)
    - `scientific-research-assistant/obsidian-plugin/src/ui/SraView.ts` (extend)
  - Implement:
    - `GET /api/sessions/{id}/deliverables` → write `deliverables.json`
    - Best-effort render to `brief.md` + `delivery.md`
    - Deterministic overwrite (no infinite duplicates)
  - _Leverage: `ResearchSessionsApi.cs` deliverables endpoint shape_
  - _Requirements: 6_
  - _Prompt: Implement the task for spec obsidian-sra-plugin, first run spec-workflow-guide to get the workflow guide then implement the task: Role: Integration Engineer (sync + markdown) | Task: Implement deliverables pulling and vault write-back under `Vault/SRA/`, including raw JSON persistence plus best-effort markdown rendering, with deterministic overwrites | Restrictions: Do not change backend; best-effort rendering must not block; never spam duplicate files; keep markdown minimal and readable | _Leverage: SRA deliverables API contract | _Requirements: Requirement 6 | Success: After a run, user can click Pull Deliverables and see updated `deliverables.json`, `brief.md`, and `delivery.md` under `SRA/` | Process: Mark [-] → implement → log-implementation → mark [x]_

- [x] 10. Add secrets handoff UX (no key storage) + plugin docs
  - Files:
    - `scientific-research-assistant/obsidian-plugin/README.md`
    - `scientific-research-assistant/docs/OBSIDIAN_PLUGIN.md`
  - Document:
    - How to run sidecar (`ASPNETCORE_URLS=http://localhost:5678 dotnet run`)
    - How to install plugin in Obsidian Desktop (copy to `.obsidian/plugins/...`)
    - How to configure keys via `Aevatar.Secrets.Api` (`http://localhost:6677`) / CLI
  - _Leverage: `scientific-research-assistant/README.md`, `apps/Aevatar.Secrets.Api/README.md`_
  - _Requirements: 7, Non-Functional (Usability)_
  - _Prompt: Implement the task for spec obsidian-sra-plugin, first run spec-workflow-guide to get the workflow guide then implement the task: Role: Documentation Engineer | Task: Add concise docs for the Obsidian plugin usage and secrets workflow handoff; include quickstart and troubleshooting without referencing port 5000 | Restrictions: Do not claim unsupported features (mobile/remote auth); keep docs precise; additive only | _Leverage: existing SRA quickstart and secrets app docs | _Requirements: Requirement 7, NFR Usability | Success: A new developer can run sidecar, install plugin, connect, and understand how to set API keys without storing them in the plugin | Process: Mark [-] → implement → log-implementation → mark [x]_

- [x] 11. Add minimal automated tests for SSE parsing + vault path safety
  - Files:
    - `scientific-research-assistant/obsidian-plugin/test/sseParser.test.mjs`
    - `scientific-research-assistant/obsidian-plugin/test/paths.test.mjs`
    - `scientific-research-assistant/obsidian-plugin/package.json` (extend scripts)
  - Use Node built-in `node:test` runner; keep tests pure and deterministic.
  - _Leverage: `src/sse/sseParser.ts`, `src/vault/paths.ts`_
  - _Requirements: Non-Functional (Reliability)_
  - _Prompt: Implement the task for spec obsidian-sra-plugin, first run spec-workflow-guide to get the workflow guide then implement the task: Role: Test Engineer | Task: Add a minimal automated test suite for SSE parsing and vault path safety using Node's built-in test runner; ensure `npm test` runs and validates key edge cases | Restrictions: No flaky network tests; keep tests fast; do not require Obsidian runtime; avoid heavy dependencies | _Leverage: Parser and paths helpers from plugin code | _Requirements: NFR Reliability | Success: `npm test` passes and covers core edge cases (split frames, invalid JSON, traversal segments) | Process: Mark [-] → implement → log-implementation → mark [x]_


