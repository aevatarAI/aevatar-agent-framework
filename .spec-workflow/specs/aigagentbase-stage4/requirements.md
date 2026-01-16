# Requirements Document

## Introduction

Stage 4 focuses on进一步收敛 `AIGAgentBase` 的平台聚合职责：把工具系统与 LLM Request 组装等“可替换能力”下沉到 runtime/facade 结构，同时保持当前行为与对外 API 不变，并补齐一致性与审计文档。

## Alignment with Product Vision

该阶段直接支持 “Runtime Agnostic by Design” 与 “Events are Truth” 原则：通过减少基座聚合点、增强可观测与一致性描述，让框架的可维护性、可测试性和演进路径更清晰，降低未来扩展/替换能力的代价。

## Requirements

### Requirement 1

**User Story:** As a framework maintainer, I want tooling logic to be moved into a dedicated runtime so that `AIGAgentBase` becomes a thin orchestration layer with reduced change radius.

#### Acceptance Criteria

1. WHEN tooling is initialized THEN the runtime SHALL own tool manager initialization, cache refresh, and tool execution policy checks without changing observable behavior.
2. WHEN tools are registered or refreshed THEN the resulting tool allowlist and function schema SHALL match the pre‑refactor behavior.
3. IF a tool is denied by policy THEN the system SHALL return the same error semantics and metadata as before.

### Requirement 2

**User Story:** As an agent developer, I want LLM request construction to be delegated while keeping override points, so that custom prompt/tool behavior remains stable.

#### Acceptance Criteria

1. WHEN `BuildLLMRequest(...)` is called THEN it SHALL produce the same prompt composition (system prompt + tool instruction block + history summary) as before.
2. WHEN a fixed tool allowlist is set THEN it SHALL still be applied to every request.
3. IF history compaction is enabled THEN the request SHALL still include history replay and summary injection as before.

### Requirement 3

**User Story:** As a platform operator, I want YAML tool policy decisions to be logged with consistent fields so that audits are stable across releases.

#### Acceptance Criteria

1. WHEN YAML tools/skills configuration is applied THEN the system SHALL log a structured decision record with fixed field names (counts/flags/allowlist hash or summary).
2. IF YAML is absent or empty THEN the system SHALL not emit misleading policy logs.

### Requirement 4

**User Story:** As a documentation reader, I want architecture and AIGAgentBase docs updated to reflect new runtime components so that the system is easy to understand and maintain.

#### Acceptance Criteria

1. WHEN the refactor completes THEN `docs/ARCHITECTURE.md` SHALL describe the new runtime components and their boundaries.
2. WHEN users read AIGAgentBase docs THEN the guide SHALL include updated component responsibilities and entry points.

### Requirement 5

**User Story:** As a QA engineer, I want existing tests to remain green after refactor so that behavior regressions are caught immediately.

#### Acceptance Criteria

1. WHEN running `dotnet test test/Aevatar.Agents.AI.Core.Tests/ -c Release` THEN all tests SHALL pass.

## Non-Functional Requirements

### Code Architecture and Modularity
- **Single Responsibility Principle**: tool system and LLM request assembly must be isolated from `AIGAgentBase`.
- **Modular Design**: new runtime components must be internal, thin, and discoverable.
- **Dependency Management**: no new external dependencies added; reuse existing abstractions.
- **Clear Interfaces**: keep existing protected override points stable.

### Performance
- No additional allocations or locks on the hot path beyond current behavior.

### Security
- Dangerous tool gating must remain enforced (defense‑in‑depth).

### Reliability
- Best‑effort semantics remain: optional integrations must not fail the chat path.

### Usability
- Docs must reflect new structure, with clear navigation and purpose statements.

