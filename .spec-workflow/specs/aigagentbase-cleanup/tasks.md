# Tasks Document

- [x] 1. Split Chat/Streaming into `AIGAgentBase.Chat.cs`
  - File: src/Aevatar.Agents.AI.Core/AIGAgentBase.Chat.cs
  - Move `ChatAsync/ChatStreamAsync/BuildLLMRequest/GetLLMSettings/...` out of `AIGAgentBase.cs` into a dedicated partial file
  - Update AI.Core architecture docs to reflect the new file
  - Purpose: Reduce monolithic file size and isolate chat concerns
  - _Leverage: existing `partial` split pattern in AI.Core_
  - _Requirements: 1_
  - _Prompt: Role: C# Engineer (Aevatar AI.Core maintainer) | Task: Extract Chat/Streaming-related methods from `AIGAgentBase.cs` into `AIGAgentBase.Chat.cs` as a `partial` class, without changing behavior; update `docs/ARCHITECTURE.md` accordingly | Restrictions: No behavior changes, no public API breaking changes, keep thread-safety assumptions intact, keep each file < 800 lines | Success: `dotnet test test/Aevatar.Agents.AI.Core.Tests/...` passes; file responsibilities are clearly separated; docs updated_

- [x] 2. Split History/Compaction/Summary into `AIGAgentBase.History.cs`
  - File: src/Aevatar.Agents.AI.Core/AIGAgentBase.History.cs
  - Move history state switches, locks, compaction and summary generation out of `AIGAgentBase.cs`
  - Ensure `BuildEffectiveSystemPromptWithSummary` remains available to `BuildLLMRequest` (cross-partial call)
  - Purpose: Isolate history management, reduce coupling, and keep core base file small
  - _Leverage: `ConversationHistoryManager`, existing summary logic_
  - _Requirements: 1_
  - _Prompt: Role: C# Engineer (Aevatar AI.Core maintainer) | Task: Extract history-related logic (history switches, locking, compaction, summary prompt injection, summarization helper methods) from `AIGAgentBase.cs` into `AIGAgentBase.History.cs` as a `partial` class, preserving behavior | Restrictions: No behavior changes; preserve concurrency safety; keep summary key stable (`history_summary`); keep each file < 800 lines | Success: AI.Core tests pass; history behaviors unchanged; docs updated_

- [x] 3. Split `AIGAgentBase.Tools.cs` into smaller partials (registration/loop/policy)
  - File: src/Aevatar.Agents.AI.Core/AIGAgentBase.Tools.cs (refactor) + new partial files
  - Extract sub-domains into dedicated partials (suggested):
    - `AIGAgentBase.Tools.Registration.cs` (tool manager/init/register/cache/instruction block)
    - `AIGAgentBase.Tools.Loop.cs` (tool call loop + message append)
    - `AIGAgentBase.Tools.Policy.cs` (policy checks + allowlist helpers)
  - Update `docs/ARCHITECTURE.md`
  - Purpose: Reduce 791-line file risk, isolate responsibilities, unblock future changes without hitting 800-line limit
  - _Leverage: current code structure in `AIGAgentBase.Tools.cs`_
  - _Requirements: 1_
  - _Prompt: Role: C# Refactoring Specialist | Task: Split `AIGAgentBase.Tools.cs` into 2-3 smaller `partial` files by responsibility (registration/caches, tool loop, policy/allowlist) without behavior changes; update architecture docs | Restrictions: Keep method semantics identical; do not change public APIs; do not weaken tool safety switches; avoid introducing new allocations in hot path | Success: Compilation succeeds; AI.Core tests pass; each new file < 800 lines; docs reflect new layout_

- [x] 4. Split `AIGAgentBase.AgentSkills.cs` into smaller partials and harden best-effort logging points
  - File: src/Aevatar.Agents.AI.Core/AIGAgentBase.AgentSkills.cs (refactor) + new partial files
  - Extract discovery/YAML parsing helpers from tool registration/execution
  - Add throttled Debug logs for swallowed exceptions (invalid roots, enumerate failures) to aid diagnosis without spam
  - Update `docs/ARCHITECTURE.md`
  - Purpose: Reduce coupling and remove “silent failure” best-effort behavior
  - _Leverage: existing minimal YAML parser + discovery code_
  - _Requirements: 1, 4_
  - _Prompt: Role: C# Engineer (Security-aware) | Task: Refactor `AIGAgentBase.AgentSkills.cs` into smaller partials (tools vs discovery/parsing) and add throttled Debug logging for best-effort swallowed failures; keep behavior stable | Restrictions: Do not change default security posture (skills tools remain disabled by default); avoid logging sensitive file contents; keep logs throttled | Success: AI.Core tests pass; no new behavior differences except improved debug visibility; docs updated_

- [x] 5. Introduce centralized key registry/helpers for context + metadata (reduce stringly-typed coupling)
  - File: src/Aevatar.Agents.AI.Core/Utils/AIGAgentKeys.cs (new) and call-site refactors
  - Centralize keys:
    - `history_summary`
    - `aevatar.allowed_tools` (+ `aevatar.allowed_tools.source_skill`)
    - hook deny keys (`deny_tool`, `deny_reason`) (either in same file or a dedicated hook-keys helper)
  - Refactor call-sites to use the registry (including any hard-coded `history_summary` usage in tools)
  - Add unit tests for allowlist parsing helper (multi-type value branches)
  - Purpose: Eliminate key drift and concentrate multi-branch parsing in one place
  - _Leverage: existing `TryGetToolAllowlist` logic in `AIGAgentBase.Tools.cs`_
  - _Requirements: 2_
  - _Prompt: Role: C# Engineer (Maintainability focus) | Task: Create a centralized key registry + helper methods for the most critical context/metadata keys (history summary, tool allowlist, hook deny), refactor usages to use it, and add unit tests for allowlist parsing branches | Restrictions: Keep key strings unchanged (wire compatibility); do not introduce cross-boundary non-proto types; keep helpers allocation-light | Success: No remaining hard-coded critical keys; tests cover parsing branches; AI.Core tests pass_

- [x] 6. Align Streaming with Hooks (minimum viable parity)
  - File: src/Aevatar.Agents.AI.Core/AIGAgentBase.Chat.cs, src/Aevatar.Agents.AI.Core/AIGAgentBase.Hooks.cs
  - Ensure streaming path runs hook pipeline at least for `BeforeLLMRequestAsync` (budget monitor, governance metadata), and calls `OnErrorAsync` on exceptions
  - Document any remaining intentional differences (e.g., no `AfterLLMResponseAsync` during streaming tokens) in `docs/HOOKS_HARNESS.md`
  - Purpose: Remove “sync vs stream behavior fork” and make governance consistent
  - _Leverage: existing `GenerateLLMWithHooksAsync` logic_
  - _Requirements: 3, 4_
  - _Prompt: Role: C# Engineer (Streaming + middleware) | Task: Make `ChatStreamAsync` run the hook pipeline (at minimum `BeforeLLMRequestAsync` and `OnErrorAsync`) without breaking provider streaming; update docs to describe streaming hook behavior and limitations | Restrictions: Do not change existing stream token output semantics; keep tool policy enforcement intact; avoid extra LLM calls | Success: Streaming behaves the same for callers but hooks observe the request; AI.Core tests pass; docs updated_

- [x] 7. Final regression + documentation sync
  - File: src/Aevatar.Agents.AI.Core/docs/ARCHITECTURE.md, src/Aevatar.Agents.AI.Core/docs/HOOKS_HARNESS.md, tests as needed
  - Run AI.Core test suite and fix any regressions introduced by refactor
  - Ensure docs reflect final file layout and streaming hook semantics
  - Purpose: Close the loop and ensure maintainers can navigate the new structure
  - _Leverage: existing test projects under `test/Aevatar.Agents.AI.Core.Tests`_
  - _Requirements: All_
  - _Prompt: Role: Senior Maintainer | Task: Run regression tests after all refactors, fix any issues, and ensure documentation accurately reflects architecture and behavior; keep changes minimal and focused | Restrictions: Do not delete failing tests; fix issues instead; no new features; no port 5000 references | Success: Tests pass; docs are accurate; no new lints introduced_


