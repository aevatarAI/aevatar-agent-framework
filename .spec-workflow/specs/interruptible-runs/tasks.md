# Tasks Document

- [x] 1. Add Protobuf run control message for cancellation/preemption
  - File: `src/Aevatar.Agents.Abstractions/abstrations_messages.proto`
  - Add `RunControlEvent` (enum action + target_run_id + superseded_by_run_id + reason + timestamp)
  - Ensure it can be carried via `EventEnvelope.payload` and/or `context_metadata` keys
  - Purpose: Provide Protobuf-first cross-boundary control message for interrupt/cancel
  - _Leverage: `src/Aevatar.Agents.Abstractions/abstrations_messages.proto` (existing `EventEnvelope`, `ContextValue`)_
  - _Requirements: 2, 5_
  - _Prompt: Implement the task for spec interruptible-runs, first run spec-workflow-guide to get the workflow guide then implement the task: Role: Protobuf/Contracts Engineer | Task: Extend `abstrations_messages.proto` with a new Protobuf message `RunControlEvent` (and minimal enums) to represent cancellation/preemption across boundaries. Keep it forward/backward compatible and ensure it fits the existing `EventEnvelope` transport. | Restrictions: MUST use Protobuf (no C# DTOs). Do not break existing field numbers. Avoid increasing file count unless necessary. | _Leverage: Existing `EventEnvelope`, `ContextValue`, and repo Protobuf conventions | _Requirements: Requirement 2 + 5 | Success: Build generates code; message can be packed in Any; no breaking changes.

- [x] 2. Implement Core run context + run manager (Latest-wins) with AsyncLocal scope
  - File: `src/Aevatar.Agents.Core/Runtime/RunContext.cs` (new)
  - File: `src/Aevatar.Agents.Core/Runtime/RunManager.cs` (new)
  - Define `RunContext` (scopeId/runId/cts/startedAt/supersededBy/reason) and `IRunManager` API
  - Implement `RunContextScope` using `AsyncLocal<RunContext?>` to expose the current run to downstream logic
  - Purpose: Provide framework-level primitive to start/interrupt runs and bind cancellation to execution
  - _Leverage: existing patterns in `Aevatar.Agents.Core` for lightweight managers and best-effort logging_
  - _Requirements: 1, 5_
  - _Prompt: Implement the task for spec interruptible-runs, first run spec-workflow-guide to get the workflow guide then implement the task: Role: .NET Framework Engineer | Task: Create Core primitives for interruptible runs: `RunContext`, `IRunManager`, `RunManager` (Latest-wins), and `RunContextScope` (AsyncLocal). Provide idempotent interrupt and safe disposal of CTS. | Restrictions: Keep files small and single-purpose; no behavior changes unless feature is enabled/used. Avoid adding new dependencies. | _Leverage: Existing Core coding style and best-effort logging patterns | _Requirements: Requirement 1 + 5 | Success: Compiles; unit tests can deterministically start/interrupt runs; no side effects when unused.

- [x] 3. Runtime integration: propagate runId and process RunControlEvent cancellation
  - Files: `src/Aevatar.Agents.Runtime.Local/*`, `src/Aevatar.Agents.Runtime.Orleans/*`, `src/Aevatar.Agents.Runtime.ProtoActor/*`
  - Add a minimal “run binder” at runtime entrypoints (RPC/chat) to set `RunContextScope` and pass `CancellationToken`
  - Add handling for `RunControlEvent(CANCEL)` to cancel the active run in the same scope
  - Purpose: Make cancellation/preemption consistent across runtimes while keeping mailbox semantics
  - _Leverage: Local runtime actor mailbox semantics; Orleans single-thread grain execution; existing EventEnvelope handling_
  - _Requirements: 1, 2, 5_
  - _Prompt: Implement the task for spec interruptible-runs, first run spec-workflow-guide to get the workflow guide then implement the task: Role: Runtime Engineer (Local/Orleans/ProtoActor) | Task: Integrate runId propagation + cancellation control handling in runtimes. Ensure run cancellation is processed in mailbox order and is idempotent. | Restrictions: Do not break existing event routing. Keep default behavior unchanged unless runId/control event present. | _Leverage: Existing runtime message entrypoints and event processing flow | _Requirements: Requirements 1/2/5 | Success: A cancel control event stops further output for the active run; behavior consistent across runtimes; tests pass.

- [x] 4. AI/tool pipeline: ensure cancellation stops streaming + tool loops cleanly
  - Files: `src/Aevatar.Agents.AI.Core/AIGAgentBase.Chat.cs`, `src/Aevatar.Agents.AI.Core/AIGAgentBase.Hooks.cs`, tool loop components
  - Ensure `OperationCanceledException` is treated as expected cancellation (not error spam)
  - Ensure tool loop respects token and stops emitting output for superseded run
  - Purpose: Make cancellation effective for the user-visible slow path (LLM streaming + tools)
  - _Leverage: existing cancellation usage in `ChatStreamAsync` (`[EnumeratorCancellation]`) and tool loop structure_
  - _Requirements: 1, 3, 5_
  - _Prompt: Implement the task for spec interruptible-runs, first run spec-workflow-guide to get the workflow guide then implement the task: Role: AI Systems Engineer | Task: Harden cancellation semantics in the AI chat + tool execution pipeline: when token is canceled, stop streaming quickly, close message stream properly, and avoid logging cancellations as failures. | Restrictions: No behavior changes when token not canceled. Do not remove error logging for real exceptions. | _Leverage: `ChatStreamAsync` cancellation pattern and existing tool loop | _Requirements: Requirement 3 + 5 | Success: Canceling token stops stream; no hanging; cancellations don’t appear as errors.

- [x] 5. SRA integration: new input interrupts active run and starts new run
  - Files: `scientific-research-assistant/src/ScientificResearchAssistant.Api/Sessions/ResearchSessionManager.cs`, `ResearchRunExecutor.cs`, `ResearchSessionsApi.cs`
  - Store active run CTS in `ResearchSession`; on new `/input` cancel previous, emit UI interrupt events, and start new run with a real CancellationToken (not `CancellationToken.None`)
  - Ensure `plan_edit` path (`VibeOrchestrator.PlanEditing.cs`) and orchestrator paths receive token and can be canceled
  - Purpose: Achieve “type-to-interrupt” UX in SRA without recreating sessions
  - _Leverage: existing `RunLock`, AG-UI events, and SRA’s fire-and-forget run structure_
  - _Requirements: 4, 5_
  - _Prompt: Implement the task for spec interruptible-runs, first run spec-workflow-guide to get the workflow guide then implement the task: Role: SRA Backend Engineer | Task: Wire framework interruptible runs into SRA: new input cancels prior run and starts new run; propagate CancellationToken into executor/orchestrator/plan_edit; emit AG-UI events so UI reflects interruption. | Restrictions: Must not break existing modes (`chat`, `vibe`, `vibe_loop`). Keep behavior unchanged when only one run active. | _Leverage: `ResearchRunExecutor`, `ResearchSession.RunLock`, existing AG-UI run/message events | _Requirements: Requirement 4 + 5 | Success: Posting a second message cancels the first run promptly; UI shows canceled/superseded; new run proceeds.

- [x] 6. Tests: runtime + SRA interruption flows
  - Files: new/updated tests under `test/` and/or `scientific-research-assistant/test/`
  - Add Local runtime test: two runs started; cancel first; ensure no further output emitted after cancel
  - Add SRA integration test: trigger slow plan_edit; send second input; assert first run canceled and second starts
  - Purpose: Prevent regressions and ensure cross-runtime consistency
  - _Leverage: existing test patterns in repo; prefer deterministic delays with CancellationToken_
  - _Requirements: 1, 3, 4, 5_
  - _Prompt: Implement the task for spec interruptible-runs, first run spec-workflow-guide to get the workflow guide then implement the task: Role: Test Engineer | Task: Add unit/integration tests covering interruptible runs across runtime + SRA. Ensure tests are deterministic and validate both “stop output” and “new run starts” behaviors. | Restrictions: Do not delete failing tests. Avoid flaky timing; use controlled delays and cancellation. | _Leverage: existing test utilities and Local runtime in-memory components | _Requirements: Requirements 1/3/4/5 | Success: Tests reliably reproduce cancellation and pass in CI-like environment.


