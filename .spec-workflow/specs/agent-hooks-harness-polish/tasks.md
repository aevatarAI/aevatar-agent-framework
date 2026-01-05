# Tasks Document

- [x] 1. Typed DenyTool API on `AevatarAgentHookContext`
  - Files:
    - `src/Aevatar.Agents.AI.Core/Hooks/AevatarAgentHookContext.cs`
    - `src/Aevatar.Agents.AI.Core/AIGAgentBase.Hooks.cs` (use the typed API as primary source; keep backward compatibility)
  - Implement `DenyTool(string? reason = null)` helper on context (writes deny flag + reason via `AIGAgentKeys`).
  - (Optional) Implement `TryGetDeniedToolReason(out string? reason)` for readers/tests (still metadata-backed).
  - Ensure existing hooks using `context.Metadata["deny_tool"/"deny_reason"]` continue to work.
  - Add/adjust unit tests to cover typed API path.
  - _Leverage: `src/Aevatar.Agents.AI.Core/Utils/AIGAgentKeys.cs`, existing deny logic in `AIGAgentBase.Hooks.cs`_
  - _Requirements: 1_
  - _Prompt: Implement the task for spec agent-hooks-harness-polish, first run spec-workflow-guide to get the workflow guide then implement the task: Role: C# framework engineer | Task: Add a typed deny-tool API to `AevatarAgentHookContext` (e.g., `DenyTool(reason)`), update tool wrapper to prefer typed API while remaining backward compatible with existing metadata keys. Add unit tests to prevent regressions. | Restrictions: No breaking changes; no new cross-boundary types unless proto; keep files small; best-effort only; do not widen tool permissions. | _Leverage: `AIGAgentKeys.HookDenyTool/HookDenyReason`, existing deny block in `AIGAgentBase.Hooks.cs` | _Requirements: R1 | Success: Hook authors can call `ctx.DenyTool(reason)` and tool execution is denied with clear reason; old metadata-based hooks still work; tests pass.

- [x] 2. Replace reflection-based hook injection with explicit injection in `AIGAgentFactory`
  - Files:
    - `src/Aevatar.Agents.AI.Core/AIGAgentFactory.cs`
    - `src/Aevatar.Agents.AI.Core/AIGAgentBase.Hooks.cs` (add internal setters/injection methods + invalidate `_hookPipeline`)
    - `src/Aevatar.Agents.AI.Core/Helpers/AIAgentHookInjector.cs` (remove usage; keep file as no-op shim or delete if not referenced)
  - Add explicit injection in `AIGAgentFactory`:
    - Resolve `IOptions<AevatarAgentHookOptions>` (preferred) or `AevatarAgentHookOptions`
    - Resolve `IEnumerable<IAevatarAgentHook>` (may be empty)
    - Inject into `AIGAgentBase` via a typed method (no reflection)
  - Ensure pipeline cache is invalidated on injection so changes take effect.
  - Keep best-effort: if DI resolution fails, proceed with defaults.
  - Update/adjust unit tests (or add new) to ensure DI-registered hooks are observed.
  - _Leverage: existing injection style in `AIGAgentFactory` (ToolManager/MemoryStore injectors), existing hook properties in `AIGAgentBase.Hooks.cs`_
  - _Requirements: 2_
  - _Prompt: Implement the task for spec agent-hooks-harness-polish, first run spec-workflow-guide to get the workflow guide then implement the task: Role: .NET DI/config engineer | Task: Remove reflection-based hook injection and replace with explicit, typed injection in `AIGAgentFactory` for `AevatarAgentHookOptions` and `IEnumerable<IAevatarAgentHook>`. Add internal injection methods on `AIGAgentBase` that invalidate the hook pipeline cache. Keep best-effort behavior and backward compatibility. | Restrictions: No breaking changes; no reflection-based property scanning; keep injection safe and deterministic; do not fail agent creation on injection issues. | _Leverage: existing injectors in `AIGAgentFactory`, `AIGAgentBase.Hooks.cs` | _Requirements: R2 | Success: Hook/options injection is discoverable and type-safe; pipeline reflects injected values; tests pass.

- [x] 3. Docs update: typed deny API + explicit injection
  - Files:
    - `src/Aevatar.Agents.AI.Core/docs/HOOKS_HARNESS.md`
    - `docs/AI_HOOKS_HARNESS_GUIDE_zh.md`
  - Update deny-tool example to `ctx.DenyTool("reason")`.
  - Update injection description: hooks/options injected explicitly by `AIGAgentFactory` (not reflection).
  - _Leverage: existing doc sections, `AIGAgentFactory.cs`_
  - _Requirements: 2_
  - _Prompt: Implement the task for spec agent-hooks-harness-polish, first run spec-workflow-guide to get the workflow guide then implement the task: Role: Technical writer for framework docs | Task: Update docs to reflect the new typed deny API (`ctx.DenyTool`) and explicit injection path via `AIGAgentFactory`. Ensure examples are accurate and consistent. | Restrictions: Keep docs concise; avoid secrets; avoid port 5000. | _Requirements: R2 | Success: Docs match implementation and reduce user confusion.


