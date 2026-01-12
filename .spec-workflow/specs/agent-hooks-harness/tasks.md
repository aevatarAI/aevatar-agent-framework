# Tasks Document

- [x] 1. Define Hook abstractions (`IAevatarAgentHook`, stages, context)
  - File: `src/Aevatar.Agents.AI.Core/Hooks/IAevatarAgentHook.cs`
  - Introduce the hook interface with stage methods: BeforeLLMRequest / AfterLLMResponse / BeforeToolExecute / AfterToolExecute / OnError
  - Include deterministic ordering via `Priority` and stable `Name`
  - Purpose: Establish the minimal contract for a composable hook/harness layer
  - _Leverage: `src/Aevatar.Agents.AI.Core/AIGAgentBase.Tools.cs` (existing tool loop stages), `src/Aevatar.Agents.AI.Core/docs/ARCHITECTURE.md`_
  - _Requirements: 1, 2_
  - _Prompt: Implement the task for spec agent-hooks-harness, first run spec-workflow-guide to get the workflow guide then implement the task: Role: C# framework engineer | Task: Add `IAevatarAgentHook` defining hook stages and ordering fields (`Name`, `Priority`). Keep signatures async (`Task`) and best-effort friendly. | Restrictions: Do not introduce breaking changes to existing public APIs; do not add runtime-boundary types unless defined in proto; keep file under 300 lines; no new ports usage. | _Leverage: AIGAgentBase.Tools.cs stage boundaries | _Requirements: 1,2 | Success: Hook interface compiles, is minimal, and cleanly maps to LLM/tool lifecycle stages.

- [x] 2. Implement Hook context + policy snapshot (read-only safety boundary)
  - File: `src/Aevatar.Agents.AI.Core/Hooks/AevatarAgentHookContext.cs`
  - Add a context object carrying: AgentId/AgentType/RequestId, LLM request/response, tool name/args/result, metadata, and policy snapshot (AllowInternalTools/AllowDangerousTools + budgets)
  - Ensure policy is read-only so hooks cannot widen permissions
  - Purpose: Provide a safe data plane between core agent flow and hooks
  - _Leverage: `src/Aevatar.Agents.AI.Core/Tool/Abstractions/ToolExecutionContext.cs`, `src/Aevatar.Agents.AI.Core/AIGAgentBase.Tools.cs`_
  - _Requirements: 1, 4, 5_
  - _Prompt: Implement the task for spec agent-hooks-harness, first run spec-workflow-guide to get the workflow guide then implement the task: Role: C# API designer | Task: Create `AevatarAgentHookContext` with minimal fields needed by hooks, plus a read-only `Policy` snapshot to enforce “hooks can only restrict”. Include a `Metadata` dictionary for traceable flags. | Restrictions: No cross-boundary config/state unless proto; keep allocations bounded; keep file under 400 lines. | _Leverage: ToolExecutionContext semantics for Allow* flags | _Requirements: 1,4,5 | Success: Context compiles, is easy to consume, and makes it impossible (by design) for hooks to bypass safety flags.

- [x] 3. Add Hook options + DI-friendly configuration (disabled hooks, budgets)
  - File: `src/Aevatar.Agents.AI.Core/Hooks/AevatarAgentHookOptions.cs`
  - Implement options: `DisabledHooks`, `MaxToolOutputChars`, `ContextMessageWarn`, `ContextCharsWarn`
  - Default values: safe and conservative (e.g., MaxToolOutputChars=16000)
  - Purpose: Provide an oh-my-opencode-like toggling mechanism (`disabled_hooks`) without hard-coding behavior
  - _Leverage: Existing options patterns in repo (search for `Options` classes), `AGENTS.md` security defaults_
  - _Requirements: 2, 3_
  - _Prompt: Implement the task for spec agent-hooks-harness, first run spec-workflow-guide to get the workflow guide then implement the task: Role: .NET DI/config engineer | Task: Add `AevatarAgentHookOptions` with disabled list + budgets. Keep names stable and self-explanatory. | Restrictions: No ports; no secrets in code; keep simple. | _Requirements: 2,3 | Success: Options compile and can be bound from configuration, enabling hook disable/budget tuning.

- [x] 4. Implement Hook pipeline executor (ordering, disable, best-effort, observability)
  - File: `src/Aevatar.Agents.AI.Core/Hooks/AevatarAgentHookPipeline.cs`
  - Load hooks (constructor injection as `IEnumerable<IAevatarAgentHook>`)
  - Apply disable list from options; order by `Priority` then `Name`
  - Provide stage runners that:
    - Execute hooks sequentially (deterministic)
    - Catch/log exceptions and continue (best-effort)
    - Record per-hook duration (log)
  - Purpose: The central harness layer that makes hooks practical and safe
  - _Leverage: Logging style from `AevatarToolManager` and `AIGAgentBase.Tools.cs`_
  - _Requirements: 1, 2, 5_
  - _Prompt: Implement the task for spec agent-hooks-harness, first run spec-workflow-guide to get the workflow guide then implement the task: Role: C# systems engineer | Task: Create `AevatarAgentHookPipeline` executing hooks with disable + ordering + best-effort exception handling + logging (hook name, stage, requestId, elapsed). | Restrictions: Deterministic; no parallelism in MVP; do not block main flow on hook failure; keep file under 500 lines. | _Leverage: AevatarToolManager best-effort patterns | _Requirements: 1,2,5 | Success: Pipeline compiles and can run hooks safely with clear logs.

- [x] 5. Wire pipeline into `AIGAgentBase` tool loop and LLM call points
  - Files:
    - `src/Aevatar.Agents.AI.Core/AIGAgentBase.Tools.cs`
    - (if needed) `src/Aevatar.Agents.AI.Core/AIGAgentBase.cs`
  - Instantiate pipeline (lazy) or allow DI injection (preferred) without breaking existing behavior
  - Call hook stages:
    - BeforeLLMRequest / AfterLLMResponse around `LLMProvider.GenerateAsync`
    - BeforeToolExecute / AfterToolExecute around `ExecuteAllowedToolAsync` + tool result message creation
    - OnError when LLM/tool failures occur
  - Purpose: Make the harness real—hooks must actually run in production flow
  - _Leverage: Existing tool loop in `ExecuteToolCallLoopAsync`, safety policy checks, and tool transcript publishing_
  - _Requirements: 1, 4, 5_
  - _Prompt: Implement the task for spec agent-hooks-harness, first run spec-workflow-guide to get the workflow guide then implement the task: Role: Aevatar AI.Core maintainer | Task: Integrate hook pipeline into the existing tool loop + LLM call. Ensure hooks cannot bypass safety flags and failures are best-effort. | Restrictions: No behavior change when no hooks registered; keep diffs minimal; do not expose dangerous tools by default. | _Leverage: ExecuteToolCallLoopAsync boundaries and policy checks | _Requirements: 1,4,5 | Success: Hooks run at the right phases, and existing agents still function unchanged when pipeline is empty.

- [x] 6. Implement built-in Hook: tool output truncation
  - File: `src/Aevatar.Agents.AI.Core/Hooks/BuiltIn/ToolOutputTruncationHook.cs`
  - In `AfterToolExecute`, truncate `ToolResult.Content` beyond `MaxToolOutputChars`
  - Preserve head/tail and add clear truncation marker; annotate metadata flags
  - Purpose: Equivalent to oh-my-opencode tool-output-truncator, preventing context bloat
  - _Leverage: `AevatarAgentHookOptions`, tool result handling in `AIGAgentBase.Tools.cs`_
  - _Requirements: 3, 5_
  - _Prompt: Implement the task for spec agent-hooks-harness, first run spec-workflow-guide to get the workflow guide then implement the task: Role: C# library developer | Task: Add `ToolOutputTruncationHook` honoring options and marking metadata. Ensure it is purely restrictive and best-effort. | Restrictions: No secrets; bounded work; keep file small. | _Requirements: 3,5 | Success: Oversized tool outputs are predictably truncated and traceable.

- [x] 7. Implement built-in Hook: context budget monitor (warn-only MVP)
  - File: `src/Aevatar.Agents.AI.Core/Hooks/BuiltIn/ContextBudgetMonitorHook.cs`
  - In `BeforeLLMRequest`, compute message count + total chars; annotate metadata warnings when thresholds exceeded
  - Purpose: Equivalent to context-window-monitor signal without adding compaction complexity in MVP
  - _Leverage: `AevatarAgentHookOptions`, llmRequest messages list_
  - _Requirements: 3, 5_
  - _Prompt: Implement the task for spec agent-hooks-harness, first run spec-workflow-guide to get the workflow guide then implement the task: Role: Observability-focused engineer | Task: Add a warn-only context budget hook that tags metadata and logs warnings when thresholds are exceeded. | Restrictions: Must not mutate messages in MVP; bounded computation. | _Requirements: 3,5 | Success: Warnings fire deterministically and can be observed without affecting behavior.

- [x] 8. Unit tests for pipeline + built-in hooks
  - Files:
    - `test/Aevatar.Agents.AI.Core.Tests/Hooks/AevatarAgentHookPipelineTests.cs` (new)
    - `test/Aevatar.Agents.AI.Core.Tests/Hooks/ToolOutputTruncationHookTests.cs` (new)
    - `test/Aevatar.Agents.AI.Core.Tests/Hooks/ContextBudgetMonitorHookTests.cs` (new)
  - Cover:
    - ordering, disabling, exception isolation
    - truncation behavior and metadata flags
    - context warning flags
  - Purpose: Lock down behavior and prevent regressions
  - _Leverage: Existing test patterns under `test/` (fixtures, xUnit usage)_
  - _Requirements: 1, 2, 3, 5_
  - _Prompt: Implement the task for spec agent-hooks-harness, first run spec-workflow-guide to get the workflow guide then implement the task: Role: QA engineer for .NET libraries | Task: Add xUnit tests for hook pipeline and built-in hooks. Ensure both happy and error paths. | Restrictions: Do not delete tests; keep deterministic; no network. | _Requirements: 1,2,3,5 | Success: Tests pass and cover the critical behavior of hooks/harness.

- [x] 9. Documentation: how to enable/disable hooks and default behavior
  - File: `.spec-workflow/specs/agent-hooks-harness/README.md` (new) OR add a short section to existing docs (pick one)
  - Include:
    - how to register hooks (DI or override)
    - how to disable hooks (DisabledHooks)
    - safety notes (hooks cannot widen permissions)
  - Purpose: Make the feature discoverable and usable
  - _Leverage: `docs/OH_MY_OPENCODE_RESEARCH.md` mapping, `src/Aevatar.Agents.AI.Core/docs/ARCHITECTURE.md`_
  - _Requirements: 2, 4, 5_
  - _Prompt: Implement the task for spec agent-hooks-harness, first run spec-workflow-guide to get the workflow guide then implement the task: Role: Technical writer for framework docs | Task: Document hook pipeline usage and safety model concisely with code snippets and config examples. | Restrictions: Avoid port 5000; no secrets. | _Requirements: 2,4,5 | Success: A developer can enable/disable hooks and understand safety boundaries in <5 minutes.


