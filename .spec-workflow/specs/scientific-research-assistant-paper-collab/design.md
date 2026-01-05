# Design Document

## Overview

本设计把 `@scientific-research-assistant` 从“内存驱动的 chat/vibe”升级到“**文件驱动的论文协作系统**”：

- **File-SSoT**：会话工作区（paper、facts_proposed、decisions、mailbox、runs、artifacts）是唯一真相；UI/内存只做投影
- **facts lifecycle**：任何候选事实先落 `facts_proposed/`，经 vote + 可执行验证后 promote 到 `facts/`
- **file-only agent comm**：agents 之间只通过 `mailbox/` 交换 Protobuf schema 的消息文件（可审计、可重放）
- **Markdown paper + single writer**：多 agent 只提交 patch proposal；单写者应用 patch，避免并发冲突
- `sources/` 继续保留为 **可选**证据库；缺省也不影响 facts-only 工作流

## Steering Document Alignment

### Technical Standards (tech.md)

- **Protobuf schema-first**：跨 agent 边界消息（mailbox、fact record、decision）全部由 `.proto` 定义
- **Atomic write + idempotency**：tmp → rename 投递；in → processing → archive；失败进 _dead
- **Dangerous tools default-off**：Python 验证必须显式开启

### Project Structure (structure.md)

- 使用 `workspace/sessions/{sessionId}/` 作为 session scope 的黑板
- `paper/`、`facts_proposed/`、`decisions/`、`mailbox/` 分离，避免 transient 数据污染 `facts/`

## Code Reuse Analysis

### Existing Components to Leverage

- **`scientific-research-assistant/src/ScientificResearchAssistant.Api/Sessions/ResearchRunExecutor.cs`**
  - 现有 run 生命周期（RUN/STEP/TEXT）与 best-effort 投影约束可复用
- **`scientific-research-assistant/src/ScientificResearchAssistant.Api/Sessions/ResearchSessionManager.cs`**
  - 现有 sessionId/runId 生成与 SSE hub，可迁移为“投影层”
- **`scientific-research-assistant/src/ScientificResearchAssistant.Api/Materials/MaterialsService.cs`**
  - 现有 `facts/ + sources/` bounded context 注入逻辑可复用（sources 缺省视为空）
- **`scientific-research-assistant/src/ScientificResearchAssistant/Vibe/Tools/PythonExecTool.cs`**
  - 作为硬验证工具（hard verification）复用
- **`src/Aevatar.Agents.Abstractions/execution_trace.proto`**
  - 用于将共识/验证过程记录为可导出的 Trace（可选）

### Integration Points

- **AG-UI SSE**：继续用 `/api/sessions/{id}/agui/events`，但 snapshot 构建改为读取文件
- **Facts写回 API**：现有 `/api/sessions/{id}/facts` 调整为写入 `facts_proposed/`（不直接落 `facts/`）

## Architecture

核心思想：把“推理/写作/验证”都投影为**文件操作**，并围绕 mailbox 做编排。

```mermaid
flowchart TD
  UI[UI /api/sessions/{id}/input] --> ORCH[Run Orchestrator]
  ORCH --> WS[WorkspaceService]
  WS -->|create/run meta| RUNS[runs/{runId}/...]

  ORCH -->|mailbox msg| MB_IN[mailbox/{agent}/in]
  MB_IN --> AG[Agent Worker Loop]
  AG -->|move| MB_PROC[mailbox/{agent}/processing]
  AG -->|result msg| MB_OUT[mailbox/{agent}/out]
  AG -->|archive| MB_ARC[mailbox/{agent}/archive]
  AG -->|fail| MB_DEAD[mailbox/_dead]

  AG --> FP[facts_proposed/{factId}.*]
  RV[Reviewer Agents] --> VOTES[decisions/votes/{factId}/...]
  VF[Verifier Agent (python)] --> VERIF[decisions/verifications/{factId}/...]
  PROM[Promoter/Editor] --> FINAL[decisions/final/{factId}.json]
  PROM --> FACTS[facts/{factId}.*]

  PROM --> PAPER[paper/draft.md]
  UI <-->|STATE_SNAPSHOT/DELTA| PROJ[WorkspaceProjection]
  PROJ -->|scan| WS
```

## Components and Interfaces

### Component 1 — WorkspaceService

- **Purpose:** 负责 session workspace 路径、目录初始化、run metadata 落盘、扫描构建 snapshot。
- **Interfaces:**
  - `EnsureSessionWorkspace(sessionId) -> WorkspacePaths`
  - `CreateRun(runId, metadata) -> RunPaths`
  - `ScanWorkspace(sessionId) -> WorkspaceSnapshot`（有界扫描）
- **Dependencies:** `IHostEnvironment` + `IOptions<WorkspaceOptions>`
- **Reuses:** 现有 sessionId/runId 生成规则

### Component 2 — FileMailboxService

- **Purpose:** 以目录作为队列，实现 file-only agent comm（send/receive/ack/dead）。
- **Interfaces:**
  - `Send(toAgent, SraMailboxMessage msg)`
  - `TryDequeue(agent, ct) -> (filePath, msg)`（in→processing）
  - `Ack(filePath)`（processing→archive）
  - `DeadLetter(filePath, error)`（processing→_dead）
- **Dependencies:** `WorkspacePaths`, `ILogger`
- **Reuses:** Protobuf `Any`/envelope 思路（参考 `EventEnvelope`）

### Component 3 — PaperService (Markdown) + PaperEditor

- **Purpose:** 管理 `paper/*` 文件；非写者只提交 patch proposal；写者合并并记录日志。
- **Interfaces:**
  - `EnsurePaperFiles(sessionId)`
  - `ProposePatch(sessionId, patch)`（写 mailbox 给 `paper_editor`）
  - `ApplyPatch(sessionId, patch)`（仅 writer 调用，幂等）
- **Notes:** patch 格式先采用“replace-span”或“unified diff”两种之一（设计阶段定稿）

### Component 4 — FactLifecycleService

- **Purpose:** 维护事实从 proposed 到 final 的全流程（proposal/vote/verify/promote）。
- **Interfaces:**
  - `CreateProposal(fact) -> factId`
  - `RecordVote(factId, vote)`
  - `RecordVerification(factId, verification)`
  - `EvaluateAndPromote(factId) -> decision`
- **Promotion policy:**
  - Soft consensus（Maker 风格投票阈值）
  - Hard verification（Python 通过即 promote）

### Component 5 — WorkspaceProjection (AG-UI)

- **Purpose:** 从文件构建 bounded snapshot（counts + metadata），并在关键动作后发 STATE_*。
- **Interfaces:**
  - `BuildSnapshot(sessionId) -> ResearchWorkspaceState`
  - `EmitSnapshot(hub, state)`

## Data Models

### Protobuf Contracts (new)

> 需要新增一个 contracts 位置（建议新建 `ScientificResearchAssistant.Contracts` 项目）来放 `.proto`。

- `SraMailboxMessage`（envelope）
  - `session_id`, `message_id`, `from`, `to`, `type`, `correlation_id`
  - `google.protobuf.Any payload`
- `FactProposal`
  - `fact_id`, `title`, `content`, `evidence_paths[]`（可指向 `sources/*` 或 `artifacts/*`）
- `FactVote`
  - `fact_id`, `reviewer_id`, `vote`（approve/reject/needs_work）, `comment`
- `FactVerification`
  - `fact_id`, `verifier_id`, `tool`（python_exec）, `result`, `artifacts[]`, `log_excerpt`
- `FactDecision`
  - `fact_id`, `decision`（promote/reject）, `basis`（votes/verification）, `finalized_at`
- `PaperPatchProposal`
  - `target_file`（draft/outline）, `patch_format`, `patch_text`, `author_agent`

### On-disk Representation

- **Schema**：全部以 Protobuf 为唯一 schema
- **Files**：落盘可用 Protobuf-JSON（便于 review），必要时同时支持 `.pb`

## Error Handling

### Error Scenarios

1. **Path traversal / invalid paths**
   - **Handling:** 所有相对路径必须 normalize 并验证 “within root”
   - **User Impact:** API 返回 400；邮件进入 _dead 并给出错误摘要

2. **Mailbox race / double consume**
   - **Handling:** 通过 `in → processing` 的 move 锁定；move 失败即视为已被其他消费者占用
   - **User Impact:** 无（幂等），或在 trace/log 中可见

3. **Corrupt / unparsable message**
   - **Handling:** move 到 `_dead/` 并写入 `error.txt`/`error.json`
   - **User Impact:** UI 提示有 dead-letter

## Testing Strategy

### Unit Testing

- Workspace path resolver：跨平台路径、within-root 校验
- Mailbox：atomic send、processing lock、ack、dead-letter、幂等
- Fact lifecycle：vote 阈值、verification promote、decision 文件落盘

### Integration Testing

- Create session → ensure workspace tree
- Propose fact → vote/verify → promote → facts/ 产物存在且可追溯
- Paper patch proposal → writer apply → `paper/draft.md` 更新

### End-to-End Testing

- UI 发起 run → SSE 收到 snapshot → 看到 proposed facts / decisions / paper changes 的可视化反馈


