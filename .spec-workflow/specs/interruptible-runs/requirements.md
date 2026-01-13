# Requirements Document

## Introduction

当前科研平台（SRA）在“调整 plan / 多 agent 编排 / 工具调用”时存在明显等待时间：用户一旦发现方向要改，往往需要重新发起一次 run 才能继续推进，体验割裂且浪费成本。

本功能在**框架层**引入“可打断（Interruptible Runs）”能力：同一会话/同一 Agent 的长任务（LLM 流式输出、工具调用、编排步骤）可以被**新的用户消息**协作式取消，并以“Latest-wins”策略立刻切换到新 run。随后在 SRA 接入，确保不影响其他子系统与运行时。

## Alignment with Product Vision

该能力直接支撑框架产品原则与目标：
- **Events are Truth / Actor Model Encapsulation**：将“打断”建模为控制事件（control event），保持 Actor mailbox 语义与一致性。
- **Runtime Agnostic by Design**：在 Core/Runtime 适配层实现可打断 run，保证 Local/Orleans/ProtoActor 行为一致。
- **Protobuf-First Contracts**：跨边界的控制事件、状态与对外协议使用 Protobuf 定义，确保跨运行时兼容与可演进。

## Requirements

### Requirement 1 — Framework: Interruptible Run primitive

**User Story:** As a framework user, I want long-running agent work to be cancelable, so that new user intent can preempt old work without restarting the whole system.

#### Acceptance Criteria

1. WHEN a new run is started for the same logical scope (session/agent) THEN the framework SHALL provide a way to request cancellation of the previous run and mark it as “superseded”.
2. WHEN cancellation is requested THEN all cooperative work units (LLM streaming, tool calls, orchestration steps) SHALL observe a CancellationToken and stop producing new output within a bounded time.
3. WHEN a run is canceled THEN the framework SHALL emit a structured cancellation outcome (e.g., canceled/superseded/reason) that can be projected to UI/telemetry.

### Requirement 2 — Framework: Control event across boundaries (Protobuf)

**User Story:** As a runtime developer, I want cancellation/preemption to propagate across actor/stream boundaries consistently, so that Local/Orleans/ProtoActor runtimes can behave the same way.

#### Acceptance Criteria

1. WHEN a run cancellation is initiated across an actor boundary THEN the system SHALL use a Protobuf-defined control message/event (no ad-hoc C# DTO crossing boundaries).
2. IF the runtime receives a cancellation control event for an active run THEN the runtime SHALL cancel the corresponding run token and prevent further output from that run.
3. WHEN cancellation control is received THEN the runtime SHALL keep mailbox semantics (no concurrent processing of control + business messages for the same actor).

### Requirement 3 — Framework: Safe streaming + tool cancellation

**User Story:** As an end user, I want streaming responses and tool executions to stop quickly when interrupted, so that I can redirect the assistant without waiting.

#### Acceptance Criteria

1. WHEN a run is interrupted during LLM streaming THEN the streaming loop SHALL stop emitting deltas and close the stream cleanly (no “hanging spinner”).
2. WHEN a run is interrupted during tool execution THEN the tool pipeline SHALL stop further tool calls and return a cancellation outcome (best-effort for non-cancelable external calls).
3. IF a tool/provider cannot be canceled immediately THEN the system SHALL still stop UI output for the superseded run and surface a bounded “canceled” status (no unbounded background work).

### Requirement 4 — SRA: New message interrupts previous run (no “re-run from scratch”)

**User Story:** As an SRA user, I want to send a new message to interrupt the ongoing “plan edit / research run”, so that I can change direction instantly without losing session context.

#### Acceptance Criteria

1. WHEN the client posts a new input message to the same session while a run is active THEN SRA SHALL request cancellation of the active run and start a new run immediately (Latest-wins).
2. WHEN an active run is interrupted THEN SRA SHALL emit UI events indicating: interrupt requested, run canceled/superseded, and new run started.
3. WHEN interruption happens THEN SRA SHALL not require the user to refresh/recreate the session; after API key is configured (if needed) the new run SHALL continue as normal.

### Requirement 5 — Compatibility: No regression to existing apps and runtimes

**User Story:** As a platform maintainer, I want this feature to not break existing agents or runtimes, so that adoption is safe and incremental.

#### Acceptance Criteria

1. IF interruption is not used THEN existing behavior SHALL remain unchanged (backwards compatible defaults).
2. WHEN running non-SRA systems in the monorepo THEN they SHALL build and behave as before (no required config changes).
3. WHEN running Local/Orleans/ProtoActor runtimes THEN cancellation behavior SHALL be consistent and covered by tests.

## Non-Functional Requirements

### Code Architecture and Modularity
- **Single Responsibility Principle**: cancellation/run-preemption logic lives in framework/runtime layers, not in business agents.
- **Modular Design**: SRA integration should be thin (wire up run manager + pass CancellationToken), with most logic reusable by other apps.
- **Clear Interfaces**: define minimal contracts for run lifecycle, cancellation request, and outcome reporting.
- **Protobuf boundary compliance**: any control message crossing streams/actors MUST be Protobuf-defined.

### Performance
- Interruption request handling SHOULD be O(1) per session/actor (no scanning of history).
- Cancellation SHOULD stop streaming output within a short bound (target: < 1s typical, best-effort).

### Security
- Cancellation control MUST not allow remote arbitrary cancellation outside session scope; enforce existing session boundaries.
- Any UI/HTTP endpoints added MUST respect existing “local-only” constraints where applicable.

### Reliability
- Canceling a run MUST not corrupt agent state or event stores.
- The system MUST close message streams cleanly to prevent UI deadlocks.
- Cancellation should be idempotent (repeated cancel requests for same run should not crash).

### Usability
- User should see explicit “interrupted / superseded by new message” status rather than silent truncation.
- The system should encourage “keep typing to interrupt” UX without forcing user to restart sessions.


