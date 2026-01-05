# Requirements Document

## Introduction

本 spec 让 `@scientific-research-assistant` 具备“多智能体协作写论文”的基础设施能力：

- 稿件先支持 **Markdown**
- **文件系统是唯一事实来源（SSoT）**：事实、候选事实、决策、验证产物、稿件都落盘；内存与 UI 只做投影
- agents **只通过文件沟通**（file-mailbox），不允许跨 agent 的内存直连
- 事实进入 `facts/` 之前必须先进入 `facts_proposed/`，并经 **多 agent 共识**或**可执行验证一锤定音**后 promote
- `sources/` 保留为 **可选**证据库（不强制）

## Alignment with Product Vision

该能力直接落实 steering 中的三条铁律：

- **File-SSoT**：文件是唯一真相，系统可重建、可审计、可复现
- **facts_proposed → consensus/verify → facts**：把“推论”和“事实依赖”隔离，防止污染前提
- **Markdown paper + single writer**：多 agent 不并发写同一稿件，通过 patch proposal 实现协作且不冲突

## Requirements

### Requirement 1 — Workspace is Single Source of Truth (File-SSoT)

**User Story:** As a researcher, I want the session workspace to be fully file-backed, so that runs and writing are reproducible and survive restarts.

#### Acceptance Criteria

1. WHEN a session is created THEN the system SHALL create a workspace directory at `workspace/sessions/{sessionId}/` with required subfolders (`paper/`, `facts_proposed/`, `decisions/`, `mailbox/`, `runs/`, `artifacts/`, `tmp/`).
2. WHEN a run starts THEN the system SHALL create `runs/{runId}/` and persist run metadata (goal, timestamps, model/provider, etc.) as files.
3. IF the service restarts THEN the system SHALL reconstruct minimal session workspace state by scanning the session workspace directory (not by relying on in-memory objects).

### Requirement 2 — File-only Agent Communication (Mailbox)

**User Story:** As a system designer, I want agents to communicate only via files, so that coordination is auditable, replayable, and runtime-agnostic.

#### Acceptance Criteria

1. WHEN an agent sends a message to another agent THEN the system SHALL write a mailbox message file into `mailbox/{to}/in/` using atomic write (tmp → rename).
2. WHEN an agent starts processing a mailbox message THEN the system SHALL move the message from `in/` to `processing/` to establish a single consumer lock.
3. WHEN a mailbox message is processed successfully THEN the system SHALL move it into `archive/` (or another deterministic archive location) to guarantee idempotency.
4. IF a mailbox message cannot be processed THEN the system SHALL move it into `mailbox/_dead/` with an error summary for human inspection.
5. WHEN a mailbox message schema is introduced or changed THEN the schema SHALL be defined in `.proto` (cross-agent boundary rule).

### Requirement 3 — Markdown Paper Workspace + Single Writer

**User Story:** As an author, I want multiple agents to collaborate on a Markdown paper without merge conflicts, so that the paper evolves deterministically.

#### Acceptance Criteria

1. WHEN a session is created THEN the system SHALL ensure `paper/outline.md` and `paper/draft.md` exist (empty initial content is allowed).
2. WHEN a non-writer agent wants to change the paper THEN the system SHALL represent this as a patch proposal message/file (not a direct edit of `paper/*`).
3. WHEN the designated paper writer applies a patch proposal THEN the system SHALL update `paper/*` deterministically and record the action in `runs/{runId}/` (or another durable log).

### Requirement 4 — Facts Lifecycle: facts_proposed → consensus/verify → facts

**User Story:** As a researcher, I want proposed facts to be reviewed/verified before becoming facts, so that downstream reasoning uses only trusted premises.

#### Acceptance Criteria

1. WHEN a new conclusion is proposed THEN the system SHALL write a candidate record into `facts_proposed/` with stable id, content, and references to evidence (optional).
2. WHEN reviewers vote on a proposed fact THEN the system SHALL persist votes as files under `decisions/votes/{factId}/`.
3. WHEN a verifier runs executable verification (e.g., Python) THEN the system SHALL persist verification output under `decisions/verifications/{factId}/` and store produced artifacts under `artifacts/`.
4. WHEN the promotion criteria is met THEN the system SHALL:
   - write a final decision file under `decisions/final/{factId}.json`
   - promote the fact by moving/copying it from `facts_proposed/` to `facts/`
5. IF promotion criteria is not met THEN the system SHALL keep the fact in `facts_proposed/` and SHALL NOT place it into `facts/`.

### Requirement 5 — UI Projection (AG-UI) from File State

**User Story:** As a user, I want the UI to reflect the file-backed workspace state, so that I can see facts/paper progress live and after reconnect.

#### Acceptance Criteria

1. WHEN a client connects to the AG-UI event stream THEN the system SHALL send a snapshot that includes counts/metadata for `facts/`, `facts_proposed/` and (if present) `sources/`.
2. WHEN the workspace changes (new proposed fact, vote, verification, promotion, paper edit) THEN the system SHALL publish bounded UI updates (no huge file dumps) via AG-UI events.
3. IF a client reconnects THEN the system SHALL be able to rebuild the snapshot from files.

### Requirement 6 — Optional Sources

**User Story:** As a researcher, I want sources to be optional, so that I can run a “facts-only” workflow while still supporting citations when needed.

#### Acceptance Criteria

1. IF the `sources/` directory does not exist THEN the system SHALL treat sources as empty and SHALL continue to function.
2. WHEN a fact references evidence THEN the system SHALL allow evidence to be either `sources/*` paths OR `artifacts/*` paths.

## Non-Functional Requirements

### Code Architecture and Modularity

- **Single Responsibility Principle**: Each file should have a single, well-defined purpose
- **Modular Design**: Filesystem layer (mailbox/workspace) must be isolated from agent reasoning logic
- **Clear Interfaces**: Mailbox and workspace operations must have explicit service APIs

### Performance

- Workspace scanning SHALL be bounded (e.g., max files per folder / size limits) to avoid UI hangs.

### Security

- Dangerous tools (e.g., Python exec) SHALL remain default-off and require explicit enablement.

### Reliability

- All cross-agent communication SHALL be idempotent and recoverable after restart.

### Usability

- Paper/facts workflow SHALL be explainable to users via deterministic folder structure and readable files.


