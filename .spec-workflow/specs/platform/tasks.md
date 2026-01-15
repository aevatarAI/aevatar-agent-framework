# Tasks Document

- [x] 1. Create Platform solution skeleton under `platform/src/`
  - File: `platform/src/Aevatar.Platform.slnx`
  - File: `platform/src/Aevatar.Platform.Cli/Aevatar.Platform.Cli.csproj`
  - File: `platform/src/Aevatar.Platform.Core/Aevatar.Platform.Core.csproj`
  - Purpose: Establish buildable Platform projects (CLI + Core) following monorepo conventions and CPM (`Directory.Packages.props`)
  - _Leverage: `Directory.Packages.props`, `platform/docs/FEASIBILITY.md`_
  - _Requirements: 1, 2, 11_
  - _Prompt: Role: .NET Solution Architect | Task: Implement the task for spec platform, first run spec-workflow-guide to get the workflow guide then implement the task: Create the initial Platform project skeleton under platform/src (CLI + Core) and a solution (.slnx). Wire package references without adding version numbers in csproj (use CPM). Keep file count per folder sane (<=8) and keep each file <=800 lines. | Restrictions: Do not bind any default port to 5000; do not introduce non-Protobuf cross-boundary types; do not delete existing tests; avoid long-running background processes. | _Leverage: platform/docs/FEASIBILITY.md (proposed structure), existing repo .slnx patterns | _Requirements: Requirement 1, 2, 11 | Success: `dotnet build` can build the new Platform projects, and the repo structure matches the design.

- [x] 2. Define Protobuf contracts for Platform sessions/events (cross-boundary)
  - File: `platform/src/Aevatar.Platform.Contracts/platform_messages.proto`
  - Purpose: Define cross-boundary types (session state, session events, streaming deltas, tool/step events) as Protobuf per repo iron rule
  - _Leverage: `src/Aevatar.Agents.AI.Core/Messages/*.proto`, `src/Aevatar.Agents.Abstractions/*.proto`_
  - _Requirements: 2, 8, 11 (and Protobuf-first from steering)_
  - _Prompt: Role: Protobuf / Distributed Systems Engineer | Task: Implement the task for spec platform, first run spec-workflow-guide to get the workflow guide then implement the task: Create platform_messages.proto defining Platform session state + events needed for CLI/TUI/server/attach and event sourcing. Ensure evolution-safe field numbering, bounded payload guidance (no huge blobs), and compatibility with JSON export/import. | Restrictions: All cross-boundary types MUST be Protobuf; do not use decimal; do not reuse field numbers; do not embed secrets in messages. | _Leverage: existing proto patterns in src/Aevatar.Agents.* | _Requirements: 2, 8, 11 | Success: Protobuf codegen succeeds on build, and types cover all session/event flows referenced in requirements.

- [x] 3. Implement ConfigLoader for `~/.aevatar` (config.yaml + secrets.yaml + env overrides)
  - File: `platform/src/Aevatar.Platform.Core/Config/AevatarConfigLoader.cs`
  - File: `platform/src/Aevatar.Platform.Core/Config/AevatarConfigModels.cs`
  - Purpose: Single source of truth for config/secrets; supports OpenCode-like env overrides; never logs secrets
  - _Leverage: `platform/docs/PRD.md` (config structure), `src/Aevatar.Agents.AI.Core/Configuration/*`_
  - _Requirements: 2, 9, 11_
  - _Prompt: Role: Backend Engineer (C#) specializing in configuration systems | Task: Implement the task for spec platform, first run spec-workflow-guide to get the workflow guide then implement the task: Build a config loader that reads ~/.aevatar/config.yaml and ~/.aevatar/secrets.yaml, merges safe defaults, and applies environment-variable overrides (OpenCode parity semantics). Ensure secrets never get serialized to logs/events. | Restrictions: No default port 5000; no secrets in logs; keep code modular; avoid deep nesting. | _Leverage: platform/docs/PRD.md, AI.Core GlobalAgentYamlRegistry conventions | _Requirements: 2, 9, 11 | Success: Config loads with missing-file fallbacks, env overrides work, and unit tests cover merge behavior.

- [x] 4. Implement Profile/Pack resolution (multi-domain assistant selection)
  - File: `platform/src/Aevatar.Platform.Core/Packs/ProfileResolver.cs`
  - File: `platform/src/Aevatar.Platform.Core/Packs/PackRegistry.cs`
  - Purpose: Map `--profile` / config default profile to a selected pack (workflows + role templates + tool policy presets)
  - _Leverage: `platform/docs/PRD.md` (workflows/agents dirs), `scientific-research-assistant/.../Vibe/docs/README.md` (dynamic roles)_
  - _Requirements: 11, 4, 3_
  - _Prompt: Role: Software Architect | Task: Implement the task for spec platform, first run spec-workflow-guide to get the workflow guide then implement the task: Create ProfileResolver/PackRegistry that discovers packs and resolves an active profile (coding/vibe/worldbuilding). Packs should be data-first: workflows + role YAML templates + policy presets. Define deterministic precedence: CLI flag > session state > config default. | Restrictions: Do not couple to a single domain; keep packs composable; no secrets in pack definitions. | _Leverage: design.md Profiles/Packs section | _Requirements: 11, 4, 3 | Success: Profiles can switch default workflow/roles/tools policy without changing CLI/TUI interface.

- [x] 5. Implement WorkflowEngine using Cognitive Mesh DSL compiler (JSON/YAML) and allowlists
  - File: `platform/src/Aevatar.Platform.Core/Workflow/PlatformMeshCompiler.cs`
  - File: `platform/src/Aevatar.Platform.Core/Workflow/WorkflowEngine.cs`
  - Purpose: Compile/validate DSL and drive execution plan; merge global roles into allowedAgentTypes; structured errors (no throw to UI)
  - _Leverage: `cognitive-mesh/Aevatar.CognitiveMesh.Dsl/CognitiveDslCompiler.cs`, `scientific-research-assistant/.../MeshCompilerService.cs`_
  - _Requirements: 4, 11_
  - _Prompt: Role: Distributed Systems Engineer | Task: Implement the task for spec platform, first run spec-workflow-guide to get the workflow guide then implement the task: Build PlatformMeshCompiler that accepts JSON or YAML (deterministic YAML->JSON), configures CognitiveDslCompiler with allowlists + global roles from ~/.aevatar/agents, and returns structured compile errors. Implement WorkflowEngine that takes a compiled MeshDefinition and orchestrates role nodes and edges (MVP: cot linear + maker loop stub). | Restrictions: No unbounded loops; keep outputs bounded; do not crash on invalid DSL; cross-boundary messages must be Protobuf. | _Leverage: SRA MeshCompilerService patterns | _Requirements: 4, 11 | Success: DSL can be compiled/validated, errors render nicely, and engine can start a minimal execution for at least one workflow.

- [x] 6. Implement RoleAgentFactory that creates AIGAgentBase-backed role agents and applies role YAML
  - File: `platform/src/Aevatar.Platform.Core/Agents/RoleAgentFactory.cs`
  - File: `platform/src/Aevatar.Platform.Core/Agents/RoleAIGAgent.cs`
  - Purpose: Default “universal agent” for most roles; apply `~/.aevatar/agents/{role}.yaml` to each instance (tools/skills/system prompt/model)
  - _Leverage: `src/Aevatar.Agents.AI.Core/AIGAgentBase.cs`, `src/Aevatar.Agents.AI.Core/Configuration/AgentYamlConfigApplier.cs`_
  - _Requirements: 3, 5, 11_
  - _Prompt: Role: AI Platform Engineer (.NET) | Task: Implement the task for spec platform, first run spec-workflow-guide to get the workflow guide then implement the task: Create a RoleAIGAgent derived from AIGAgentBase and a factory that instantiates agents per role and applies YAML config via AgentYamlConfigApplier. Ensure baseline tool allowlist is enforced and skills are enabled only when YAML declares skills. | Restrictions: Agents must have parameterless constructors; state/config types crossing boundaries must be Protobuf; do not bypass tool safety policies. | _Leverage: AI.Core GlobalAgentYamlRegistry + AgentYamlConfigApplier | _Requirements: 3, 5, 11 | Success: Role agents start, load YAML, and tool visibility matches allowlist.

- [x] 7. Implement ToolRegistry and security policy layer (dangerous tools, path allowlists, timeouts)
  - File: `platform/src/Aevatar.Platform.Core/Tools/PlatformToolRegistry.cs`
  - File: `platform/src/Aevatar.Platform.Core/Tools/PlatformToolPolicy.cs`
  - Purpose: Provide OpenCode-parity tool capabilities while keeping a “default safe” policy; enforce repo port policy for server defaults
  - _Leverage: `src/Aevatar.Agents.AI.Core/docs/HOOKS_HARNESS.md`, `src/Aevatar.Agents.AI.Core/docs/WEB_SEARCH_TOOL.md`_
  - _Requirements: 6, 1, 2_
  - _Prompt: Role: Security-minded Backend Engineer | Task: Implement the task for spec platform, first run spec-workflow-guide to get the workflow guide then implement the task: Implement a tool registry and policy layer that controls dangerous tools (shell/web_search/etc), path allowlists, command allowlists, and timeouts. Integrate with AI.Core tool system as the execution backend. | Restrictions: Default must be safe; no privilege escalation via hooks; do not allow port 5000 defaults anywhere. | _Leverage: AI.Core tool policy patterns, HOOKS_HARNESS.md | _Requirements: 6, 1, 2 | Success: Tools are discoverable, policy blocks unsafe actions by default, and logs/events explain denials.

- [x] 8. Implement file-backed EventStore for sessions (Event Sourcing + export/import)
  - File: `platform/src/Aevatar.Platform.Core/Sessions/FileEventStore.cs`
  - File: `platform/src/Aevatar.Platform.Core/Sessions/SessionService.cs`
  - Purpose: `session list/show/resume/export/import` behavior and crash recovery
  - _Leverage: `src/Aevatar.Agents.Core/EventSourcing/InMemoryEventStore.cs`, `src/Aevatar.Agents.Abstractions/EventSourcing/IEventStore.cs`_
  - _Requirements: 8, 1_
  - _Prompt: Role: Backend Engineer (Event Sourcing) | Task: Implement the task for spec platform, first run spec-workflow-guide to get the workflow guide then implement the task: Build a file-backed IEventStore and SessionService for Platform sessions. Events must be Protobuf, storage must be bounded and append-only, and export/import must use a stable JSON format (no secrets). | Restrictions: No non-Protobuf event payloads; ensure replay is deterministic; do not delete tests. | _Leverage: existing IEventStore abstractions | _Requirements: 8, 1 | Success: Sessions can be created/resumed/listed/exported/imported, and unit tests cover basic replay.

- [x] 9. Implement CLI command surface (OpenCode parity) backed by PlatformCore
  - File: `platform/src/Aevatar.Platform.Cli/Program.cs`
  - File: `platform/src/Aevatar.Platform.Cli/Commands/RootCommands.cs`
  - Purpose: Wire `tui/run/serve/web/attach/...` commands and global flags; route to Core services
  - _Leverage: [OpenCode CLI](https://opencode.ai/docs/cli/), `platform/docs/FEASIBILITY.md`_
  - _Requirements: 1, 8, 11_
  - _Prompt: Role: CLI Developer (.NET) | Task: Implement the task for spec platform, first run spec-workflow-guide to get the workflow guide then implement the task: Implement OpenCode-parity CLI command surface using System.CommandLine: default starts TUI, plus subcommands run/serve/web/attach/agent/auth/mcp/models/session/stats/export/import/acp/uninstall/upgrade. Wire global flags. Ensure default ports are not 5000. | Restrictions: Keep command parsing deterministic; avoid long files; do not start long-running server in foreground in tests. | _Leverage: OpenCode CLI docs, FEASIBILITY.md | _Requirements: 1, 8, 11 | Success: `aevatar --help` shows expected commands/flags; basic commands invoke Core handlers.

- [x] 10. Implement TUI (OpenCode parity) with Spectre.Console + streaming rendering
  - File: `platform/src/Aevatar.Platform.Cli/Tui/TuiApp.cs`
  - File: `platform/src/Aevatar.Platform.Cli/Tui/InputParser.cs`
  - Purpose: Provide `@` file attach, `!` shell, `/` commands, `/editor`, and streaming output display
  - _Leverage: [OpenCode TUI](https://opencode.ai/docs/tui/), `platform/docs/FEASIBILITY.md`_
  - _Requirements: 1, 7, 6_
  - _Prompt: Role: Terminal UI Engineer | Task: Implement the task for spec platform, first run spec-workflow-guide to get the workflow guide then implement the task: Build a Spectre.Console-based TUI that matches OpenCode behaviors: @ fuzzy file attach, ! shell run (policy controlled), / commands (help/sessions/themes/editor/etc), /editor uses EDITOR env, and streaming output rendering. | Restrictions: No secrets in UI logs; bounded rendering (truncate long outputs); avoid tight loops; keep files small. | _Leverage: OpenCode TUI docs, FEASIBILITY.md | _Requirements: 1, 7, 6 | Success: TUI can run a prompt through WorkflowEngine and stream outputs; input syntaxes work.

- [x] 11. Implement `serve/web/attach` backend (HTTP + streaming) with authentication
  - File: `platform/src/Aevatar.Platform.Server/Aevatar.Platform.Server.csproj`
  - File: `platform/src/Aevatar.Platform.Server/Program.cs`
  - File: `platform/src/Aevatar.Platform.Server/Endpoints/SessionsEndpoints.cs`
  - Purpose: Remote backend for attach/web; streaming events; basic auth; CORS
  - _Leverage: [OpenCode CLI](https://opencode.ai/docs/cli/) (serve/web/attach), `scientific-research-assistant/.../ResearchSessionsApi.cs` (SSE snapshot-first patterns)_
  - _Requirements: 1, 7, 8_
  - _Prompt: Role: Backend Engineer (ASP.NET Core) | Task: Implement the task for spec platform, first run spec-workflow-guide to get the workflow guide then implement the task: Create a minimal ASP.NET Core server project that provides endpoints needed for serve/web/attach (session create/list/input, stream events snapshot-first via SSE, attach handshake). Support basic auth via env/config, and ensure default port is not 5000. | Restrictions: No port 5000 defaults; streaming must be snapshot-first; keep payloads bounded; avoid storing secrets. | _Leverage: SRA ResearchSessionsApi SSE patterns | _Requirements: 1, 7, 8 | Success: Server can stream session events and TUI can attach to it.

- [x] 12. Add automated tests: CLI parsing, DSL compile, session replay, and policy denials
  - File: `platform/test/Aevatar.Platform.Tests/Aevatar.Platform.Tests.csproj`
  - File: `platform/test/Aevatar.Platform.Tests/PlatformSmokeTests.cs`
  - Purpose: Lock in parity-critical behaviors and prevent regressions
  - _Leverage: existing test patterns under `test/`, `cognitive-mesh` DSL tests_
  - _Requirements: All (focus: 1, 4, 8, 11)_
  - _Prompt: Role: Test Engineer (.NET) | Task: Implement the task for spec platform, first run spec-workflow-guide to get the workflow guide then implement the task: Add test project and write tests for: CLI command parsing (help surface), DSL compilation success/failure with structured errors, file event store replay determinism, and tool policy denials. | Restrictions: Never delete failing tests; keep tests deterministic and fast; do not depend on external network. | _Leverage: existing xUnit patterns, CognitiveDslCompilerTests | _Requirements: All | Success: `dotnet test` passes for the new Platform test project with meaningful coverage of critical flows.


