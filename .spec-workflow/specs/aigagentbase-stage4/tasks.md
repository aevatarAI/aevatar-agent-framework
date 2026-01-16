# Tasks Document

- [x] 1. Introduce ToolingRuntime core and delegate tool initialization
  - File: src/Aevatar.Agents.AI.Core/Tooling/ToolingRuntime.cs
  - File: src/Aevatar.Agents.AI.Core/AIGAgentBase.Tools.cs
  - Move tool manager initialization, registration, cache refresh into ToolingRuntime and keep AIGAgentBase as thin façade
  - Purpose: reduce AIGAgentBase platform aggregation while preserving behavior
  - _Leverage: src/Aevatar.Agents.AI.Core/AgentSkills/AgentSkillsRuntime.cs, src/Aevatar.Agents.AI.Core/AIGAgentBase.Tools.cs_
  - _Requirements: 1_
  - _Prompt: Implement the task for spec aigagentbase-stage4, first run spec-workflow-guide to get the workflow guide then implement the task: Role: C# Runtime Refactor Engineer | Task: Create internal ToolingRuntime to own tool manager initialization/registration/cache refresh and update AIGAgentBase.Tools.cs to delegate while keeping public/protected API stable | Restrictions: No behavior change, no new public APIs, keep best-effort semantics, folder file count ≤ 8, follow existing logging patterns | _Leverage: AgentSkillsRuntime as extraction pattern, AIGAgentBase.Tools.cs for source logic | _Requirements: 1 | Success: ToolingRuntime compiles, AIGAgentBase delegates to it, tool registration behavior matches pre-refactor, caches still refresh correctly | Instructions: Before coding, mark this task as [-] in tasks.md. After completion, log implementation with log-implementation tool including artifacts. Then mark task [x].

- [x] 2. Move tool loop and allowlist enforcement into ToolingRuntime
  - File: src/Aevatar.Agents.AI.Core/Tooling/ToolingRuntime.Loop.cs
  - File: src/Aevatar.Agents.AI.Core/AIGAgentBase.Tools.Loop.cs
  - File: src/Aevatar.Agents.AI.Core/AIGAgentBase.Tools.Policy.cs
  - Extract tool loop execution and allowlist enforcement into ToolingRuntime, keeping policy hooks in base
  - Purpose: isolate tool execution details from base class
  - _Leverage: src/Aevatar.Agents.AI.Core/AIGAgentBase.Tools.Loop.cs, src/Aevatar.Agents.AI.Core/AIGAgentBase.Tools.Policy.cs_
  - _Requirements: 1_
  - _Prompt: Implement the task for spec aigagentbase-stage4, first run spec-workflow-guide to get the workflow guide then implement the task: Role: C# Tooling Engineer | Task: Extract tool loop execution and ExecuteAllowedToolAsync into ToolingRuntime.Loop while preserving policy checks in AIGAgentBase.Tools.Policy.cs via internal wrappers | Restrictions: Keep error semantics identical, no public API changes, maintain defense-in-depth checks, avoid new locks | _Leverage: existing tool loop + policy code, ToolingRuntime created in task 1 | _Requirements: 1 | Success: Tool loop behavior unchanged, policy denial messages unchanged, ToolingRuntime owns execution path, AIGAgentBase remains façade | Instructions: Mark task [-] before work, log implementation with artifacts, then mark [x].

- [x] 3. Introduce LlmRequestRuntime and delegate BuildLLMRequest
  - File: src/Aevatar.Agents.AI.Core/Llm/LlmRequestRuntime.cs
  - File: src/Aevatar.Agents.AI.Core/AIGAgentBase.Chat.cs
  - File: src/Aevatar.Agents.AI.Core/AIGAgentBase.History.cs
  - Move LLM request assembly into runtime while keeping override points intact
  - Purpose: isolate request composition logic and shrink base responsibilities
  - _Leverage: src/Aevatar.Agents.AI.Core/AIGAgentBase.Chat.cs, src/Aevatar.Agents.AI.Core/AIGAgentBase.History.cs_
  - _Requirements: 2_
  - _Prompt: Implement the task for spec aigagentbase-stage4, first run spec-workflow-guide to get the workflow guide then implement the task: Role: C# LLM Integration Engineer | Task: Create internal LlmRequestRuntime that builds AevatarLLMRequest, move BuildLLMRequest implementation there, and expose minimal internal wrappers for prompt/history/tool allowlist composition | Restrictions: Preserve exact request composition order, keep overrides working, no new public APIs, keep history summary behavior unchanged | _Leverage: BuildLLMRequest and BuildEffectiveSystemPromptWithSummary implementations | _Requirements: 2 | Success: LlmRequestRuntime compiles, BuildLLMRequest delegates, prompt + tools + history unchanged in output | Instructions: Mark task [-], log implementation with artifacts, then mark [x].

- [x] 4. Standardize YAML tool policy audit fields in code
  - File: src/Aevatar.Agents.AI.Core/AIGAgentBase.Tools.Policy.cs
  - Define fixed audit field set (counts/flags/hash or summary) and use it in LogYamlToolPolicyDecision
  - Purpose: ensure stable audit logs across versions
  - _Leverage: src/Aevatar.Agents.AI.Core/AIGAgentBase.Tools.Policy.cs_
  - _Requirements: 3_
  - _Prompt: Implement the task for spec aigagentbase-stage4, first run spec-workflow-guide to get the workflow guide then implement the task: Role: Observability Engineer | Task: Normalize YAML policy audit logging with fixed field names and a stable summary/hash of allowlist, keeping LogYamlToolPolicyDecision as the single hook | Restrictions: Do not change policy behavior, keep logging level default Debug, avoid heavy allocations in hot paths | _Leverage: existing LogYamlToolPolicyDecision hook | _Requirements: 3 | Success: Log output fields are stable and documented in code, no behavior change, no extra dependencies | Instructions: Mark task [-], log implementation with artifacts, then mark [x].

- [x] 5. Update architecture and AIGAgentBase docs for new runtimes
  - File: src/Aevatar.Agents.AI.Core/docs/ARCHITECTURE.md
  - File: src/Aevatar.Agents.AI.Core/docs/aigagentbase/AIGAGENTBASE_GUIDE.md
  - File: src/Aevatar.Agents.AI.Core/docs/aigagentbase/YAML_TOOL_POLICY.md
  - Describe ToolingRuntime/LlmRequestRuntime responsibilities and YAML audit field semantics
  - Purpose: keep docs aligned with refactor
  - _Leverage: existing docs in src/Aevatar.Agents.AI.Core/docs/_
  - _Requirements: 4, 3_
  - _Prompt: Implement the task for spec aigagentbase-stage4, first run spec-workflow-guide to get the workflow guide then implement the task: Role: Documentation Engineer | Task: Update architecture and AIGAgentBase docs to reflect ToolingRuntime/LlmRequestRuntime extraction and YAML audit field standardization | Restrictions: Keep docs concise, maintain directory file count ≤ 8, align with existing doc tone | _Leverage: ARCHITECTURE.md, AIGAGENTBASE_GUIDE.md, YAML_TOOL_POLICY.md | _Requirements: 4, 3 | Success: Docs reference new runtimes, audit fields documented, navigation intact | Instructions: Mark task [-], log implementation with artifacts, then mark [x].

- [x] 6. Run AI.Core test suite for regression
  - File: test/Aevatar.Agents.AI.Core.Tests/
  - Execute tests and record outcome in implementation log
  - Purpose: confirm behavior remains stable after refactor
  - _Leverage: test/Aevatar.Agents.AI.Core.Tests/_
  - _Requirements: 5_
  - _Prompt: Implement the task for spec aigagentbase-stage4, first run spec-workflow-guide to get the workflow guide then implement the task: Role: QA Engineer | Task: Run dotnet test for Aevatar.Agents.AI.Core.Tests in Release, capture results, and log in implementation log | Restrictions: Do not change tests, only run and record results | _Leverage: test/Aevatar.Agents.AI.Core.Tests | _Requirements: 5 | Success: Tests executed, results captured in log, failures surfaced clearly | Instructions: Mark task [-], log implementation with artifacts, then mark [x].

- [x] 7. Split runtime context into per-domain contexts
  - File: src/Aevatar.Agents.AI.Core/AIGAgentBase.RuntimeContext.cs
  - File: src/Aevatar.Agents.AI.Core/AIGAgentBase.Tools.cs
  - File: src/Aevatar.Agents.AI.Core/AIGAgentBase.Chat.cs
  - Replace the single AIGAgentRuntimeContext with two focused contexts for Tooling and LLM request composition
  - Purpose: avoid a new aggregation point and keep runtime contexts single-responsibility
  - _Leverage: src/Aevatar.Agents.AI.Core/Tooling/IToolingRuntimeHost.cs, src/Aevatar.Agents.AI.Core/Llm/ILlmRequestHost.cs_
  - _Requirements: 1, 2_
  - _Prompt: Implement the task for spec aigagentbase-stage4, first run spec-workflow-guide to get the workflow guide then implement the task: Role: C# Architecture Refactor Engineer | Task: Split AIGAgentRuntimeContext into two focused host contexts (Tooling + LLM request) and update runtime wiring to use per-domain contexts | Restrictions: No behavior change, no public API changes, keep explicit host interfaces, avoid new internal wrappers on AIGAgentBase | _Leverage: IToolingRuntimeHost, ILlmRequestHost, ToolingRuntime, LlmRequestRuntime | _Requirements: 1, 2 | Success: Runtime contexts are split, ToolingRuntime/LlmRequestRuntime use focused hosts, base remains thin | Instructions: Mark task [-], log implementation with artifacts, then mark [x].


