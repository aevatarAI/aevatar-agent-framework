# Tasks Document

- [x] 1. Add Claude Agent SDK provider config parser + validation
  - Files:
    - `src/Aevatar.Agents.AI.LLMTornado/ClaudeAgentSdk/ClaudeAgentSdkProviderConfig.cs` (new)
  - Implement:
    - Parse `LLMProviderConfig.ProviderSpecificSettings` into a strongly-typed config object
    - Validate required fields (runner command/args, projectRoot, timeouts, output caps)
    - Safe defaults (no secrets logged; minimal permissions)
  - Purpose: Single source of truth for how `claude_agent_sdk` provider is configured
  - _Leverage: `src/Aevatar.Agents.AI.Abstractions/Configuration/LLMProviderConfig.cs` (ProviderSpecificSettings), config validation patterns in `src/Aevatar.Agents.AI.MEAI/MEAILLMProviderFactory.cs`_
  - _Requirements: 1, 2, 5, 6_
  - _Prompt: Implement the task for spec llmtornado-claude-agent-sdk, first run spec-workflow-guide to get the workflow guide then implement the task: Role: .NET configuration engineer | Task: Create `ClaudeAgentSdkProviderConfig` that robustly parses `ProviderSpecificSettings` (handle string/int/bool/JsonElement/list), validates mandatory settings, and provides safe defaults. | Restrictions: Do not add new external dependencies; do not log secrets; keep file < 400 lines; no port 5000 examples. | _Leverage: LLMProviderConfig + MEAI factory config validation style | _Requirements: 1,2,5,6 | Success: Config parsing is deterministic, helpful errors are thrown for missing required fields, and defaults are safe. (Workflow: mark task [-] in tasks.md before coding; after completion use log-implementation with artifacts; then mark [x].)

- [x] 2. Implement Claude Agent SDK runner (process execution + bounded I/O + cancellation)
  - Files:
    - `src/Aevatar.Agents.AI.LLMTornado/ClaudeAgentSdk/ClaudeAgentSdkRunner.cs` (new)
    - `src/Aevatar.Agents.AI.LLMTornado/ClaudeAgentSdk/ClaudeAgentSdkProtocol.cs` (new)
  - Implement:
    - Spawn external runner process via `ProcessStartInfo` with stdin/stdout/stderr redirected
    - Hard timeout + linked cancellation; best-effort `Kill(entireProcessTree:true)`
    - Output reading that keeps draining even after cap (avoid pipe deadlock)
    - Parse output markers:
      - `AEVATAR_AGENT_SDK_OUTPUT:{json}`
      - `AEVATAR_AGENT_SDK_STREAM:{text}` (optional streaming lines)
  - Purpose: Provide a reliable, bounded integration layer for headless Claude Agent SDK execution
  - _Leverage: `src/Aevatar.Agents.AI.Core/Tool/Tools/CustomTools/DotNetFileSkillTool.cs` (DotNetFileSkillRunner process + drain patterns)_
  - _Requirements: 2, 4, 6, 7_
  - _Prompt: Implement the task for spec llmtornado-claude-agent-sdk, first run spec-workflow-guide to get the workflow guide then implement the task: Role: C# systems engineer | Task: Build `ClaudeAgentSdkRunner` + protocol helpers to execute an external runner process safely (timeouts, cancellation, bounded output, continuous drain). Add marker-based parsing for final JSON + optional streaming text lines. | Restrictions: No networking inside runner layer; no new deps; must avoid deadlocks (always drain pipes); do not leak secrets in logs; keep each file < 500 lines. | _Leverage: DotNetFileSkillRunner’s WaitForExitAsync + drain pattern | _Requirements: 2,4,6,7 | Success: Runner executes reliably, cancels/timeout cleanly, and outputs are parsed deterministically (including noisy stdout). (Workflow: mark task [-] in tasks.md before coding; after completion use log-implementation with artifacts; then mark [x].)

- [x] 3. Implement `ClaudeAgentSdkProvider` (IAevatarLLMProvider) on top of the runner
  - Files:
    - `src/Aevatar.Agents.AI.LLMTornado/ClaudeAgentSdk/ClaudeAgentSdkProvider.cs` (new)
  - Implement:
    - Inherit `AevatarLLMProviderBase`
    - Map `AevatarLLMRequest` (system prompt + messages + user prompt + settings) into runner request JSON
    - **Ignore** `AevatarLLMRequest.Functions` and **never** return `AevatarFunctionCall` (avoid Aevatar tool-loop)
    - Best-effort streaming: yield tokens from `AEVATAR_AGENT_SDK_STREAM:*` lines; otherwise degrade to one chunk
  - Purpose: Make Claude Agent SDK callable as a first-class Aevatar LLM provider without tool-loop coupling
  - _Leverage: `src/Aevatar.Agents.AI.Abstractions/LLMProvider/AevatarLLMProviderBase.cs`, `src/Aevatar.Agents.AI.Abstractions/LLMProvider/AevatarLLMRequest.cs`, `src/Aevatar.Agents.AI.Abstractions/LLMProvider/AevatarLLMResponse.cs`_
  - _Requirements: 2, 3, 4, 5, 6_
  - _Prompt: Implement the task for spec llmtornado-claude-agent-sdk, first run spec-workflow-guide to get the workflow guide then implement the task: Role: Aevatar provider maintainer | Task: Add `ClaudeAgentSdkProvider` inheriting `AevatarLLMProviderBase`, mapping Aevatar requests to runner JSON and mapping runner output to `AevatarLLMResponse` / `AevatarLLMToken`. Ensure functions/tool calls are ignored and `AevatarFunctionCall` is always null. | Restrictions: Must not trigger AIGAgentBase tool loop; must respect cancellation/timeout; no secrets in logs; keep file < 600 lines. | _Leverage: AevatarLLMProviderBase resilience wrapper + new runner | _Requirements: 2,3,4,5,6 | Success: Provider compiles, returns text responses, streams best-effort, and never returns tool calls. (Workflow: mark task [-] in tasks.md before coding; after completion use log-implementation with artifacts; then mark [x].)

- [x] 4. Wire `claude_agent_sdk` into `LLMTornadoProviderFactory` selection logic
  - Files:
    - `src/Aevatar.Agents.AI.LLMTornado/LLMTornadoProviderFactory.cs` (modify)
  - Implement:
    - Branch on `ProviderType == "claude_agent_sdk"` to construct and return `ClaudeAgentSdkProvider`
    - Preserve existing provider type mapping for OpenAI/Anthropic/Gemini/etc
  - Purpose: Make the new provider selectable via `LLMProviders` configuration without breaking existing setups
  - _Leverage: Existing `LLMTornadoProviderFactory.CreateProvider` patterns_
  - _Requirements: 1_
  - _Prompt: Implement the task for spec llmtornado-claude-agent-sdk, first run spec-workflow-guide to get the workflow guide then implement the task: Role: .NET DI/integration engineer | Task: Update `LLMTornadoProviderFactory` so `ProviderType=claude_agent_sdk` returns `ClaudeAgentSdkProvider`, while all existing provider types keep the exact previous behavior. | Restrictions: No breaking changes; do not change default provider mapping; keep diff minimal; no new packages. | _Leverage: Existing LLMTornadoProviderFactory branching patterns | _Requirements: 1 | Success: Factory can create the new provider and all existing providers still work unchanged. (Workflow: mark task [-] in tasks.md before coding; after completion use log-implementation with artifacts; then mark [x].)

- [x] 5. Add unit tests for config parsing + factory selection + protocol parsing
  - Files:
    - `test/Aevatar.Agents.AI.LLMTornado.Tests/ClaudeAgentSdkConfigTests.cs` (new)
    - `test/Aevatar.Agents.AI.LLMTornado.Tests/ClaudeAgentSdkProtocolTests.cs` (new)
    - `test/Aevatar.Agents.AI.LLMTornado.Tests/ProviderFactorySelectionTests.cs` (new)
  - Cover:
    - Missing required settings -> helpful exceptions
    - JsonElement values in ProviderSpecificSettings (simulated) parse correctly
    - Marker parsing for stream/output is robust against noisy stdout
    - Provider factory returns `ClaudeAgentSdkProvider` when `ProviderType=claude_agent_sdk`
  - Purpose: Lock down behavior and prevent regressions without requiring real Claude calls
  - _Leverage: `test/Aevatar.Agents.AI.LLMTornado.Tests/DependencyInjectionTests.cs` style, xUnit + Shouldly_
  - _Requirements: 1, 2, 4, 6, 7_
  - _Prompt: Implement the task for spec llmtornado-claude-agent-sdk, first run spec-workflow-guide to get the workflow guide then implement the task: Role: QA engineer for .NET providers | Task: Add xUnit tests covering config validation, protocol marker parsing, and provider factory selection. Keep tests deterministic and offline. | Restrictions: Do not call external network; do not require Node/Python installed; do not delete failing tests—fix them. | _Leverage: existing LLMTornado test patterns (DependencyInjectionTests) | _Requirements: 1,2,4,6,7 | Success: Tests pass locally and validate the critical edge cases (missing config, noisy stdout, correct provider selection). (Workflow: mark task [-] in tasks.md before coding; after completion use log-implementation with artifacts; then mark [x].)

- [x] 6. Documentation + examples for enabling `claude_agent_sdk` provider
  - Files:
    - `src/Aevatar.Agents.AI.LLMTornado/docs/ClaudeAgentSdkProvider.md` (new)
    - `src/Aevatar.Agents.AI.LLMTornado/docs/README.md` (modify)
  - Include:
    - Sample `appsettings.json` snippet (no secrets) showing `ProviderType=claude_agent_sdk`
    - Explanation of `.claude/*` projectRoot usage, plugins, and permission defaults
    - Runner protocol (markers) and troubleshooting (exit code, stderr, missing runner)
    - Explicit note: no ports; avoid `:5000` in any example; default suggestion `:5678` if ever needed
  - Purpose: Make the integration discoverable and safely usable by repo consumers
  - _Leverage: Existing docs in `src/Aevatar.Agents.AI.LLMTornado/README.md` and repo `docs/CLAUDE_CODE_SDK_RESEARCH.md`_
  - _Requirements: 5, 6, 7_
  - _Prompt: Implement the task for spec llmtornado-claude-agent-sdk, first run spec-workflow-guide to get the workflow guide then implement the task: Role: Technical writer + maintainer | Task: Document how to configure and run the Claude Agent SDK provider, including security defaults, config schema, runner protocol, and troubleshooting. Update docs index. | Restrictions: No secrets; no port 5000; keep docs concise and actionable. | _Leverage: existing LLMTornado docs patterns | _Requirements: 5,6,7 | Success: A developer can enable the provider and debug failures in <10 minutes using the docs alone. (Workflow: mark task [-] in tasks.md before coding; after completion use log-implementation with artifacts; then mark [x].)


