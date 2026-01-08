# Design Document

## Overview

本设计在现有 `scientific-research-assistant` 的 Vibe 平台之上，补齐客户在 [issue #67](https://github.com/aevatarAI/aevatar-agent-framework/issues/67) 的体验闭环：

1. **Research Brief（1页）**：用户输入方向后立刻回传“研究问题改写 + 假设/风险/不确定点 + 里程碑路线图”，并支持“继续/纠偏”最小交互。
2. **Round N 并行推进**：planner/librarian/reasoner/verifier/dag_builder/paper_editor 并行工作，输出全量可见且 streaming，不刷新页面。
3. **Compute 分层确认 + 运行期输出**：当 verifier 判定需要程序运算时，生成执行计划卡；低风险可自动执行，高风险需用户一键选择；运行期持续输出状态/日志摘要/中间快照（若有）。
4. **Delivery Center（交付物中心）**：论文（主叙事）+ 三类清单（结论卡/证据对照/下一步任务）作为协作骨架，每轮结束自动更新，并可从 DAG/论文互相转换。
5. **paper_editor 单写入者**：负责 DAG ↔ 论文/清单互转与落盘（通过 patch 与 snapshot 写入），避免多人并发写同一份稿件失控。

## Steering Document Alignment

### Technical Standards (tech.md)
- **Protobuf-first**：Brief/Compute/Delivery/DAG↔Paper 的跨边界数据全部用 `.proto` 定义；落盘采用 Protobuf-JSON（可 review，可演进）。
- **Runtime agnostic**：编排与存储落在 `ScientificResearchAssistant.Api`（File-SSoT + SSE），agent 逻辑落在 `ScientificResearchAssistant`（AIGAgentBase）。
- **Dangerous tools default-off**：Compute 默认不执行危险代码；仅在策略允许且/或用户确认后执行。
- **Port policy**：默认配置不使用 `:5000`。

### Project Structure (structure.md)
- 新增能力集中在 `scientific-research-assistant/` 子系统内：
  - API：`src/ScientificResearchAssistant.Api/Vibe/*` 增加 Brief/Compute/Delivery 模块（每文件职责单一）
  - Contracts：扩展 `src/ScientificResearchAssistant.Contracts/sra_collab.proto`
  - Agents：在 `src/ScientificResearchAssistant/Vibe/` 新增 `VibePaperEditorAgent`（单写入者逻辑角色）
- 会话级 SSoT 落在 `workspace/sessions/{sessionId}/...`，避免把大文本塞进 proto。

## Code Reuse Analysis

### Existing Components to Leverage
- **`VibeOrchestrator`**：已有 “plan → workers → maker-v2 → summary → trace” 闭环；本 spec 将其拆分/扩展为 “brief → round → compute → delivery” 的可组合阶段。
- **`PaperService`**：已实现 paper/outline/draft 的 single-writer patch 机制；本 spec 扩展其“写入范围”到交付物中心清单（建议以 snapshot 文件写入，而非 span patch）。
- **`GoalsStore` / `DagStore` / `TraceStore`**：继续作为 goals/DAG/trace 的 canonical File-SSoT。
- **`FileMailboxService`**：用于广播 goals 更新、paper patch proposals 等跨 agent 消息（可审计）。
- **`MaterialsService`**：用于构建 bounded materials context；librarian 可写 facts（已在现有实现中落到 `facts/sra/{sessionId}/...`）。
- **AG-UI SSE**：继续使用 snapshot-first + streaming delta。

### Integration Points
- **maker-v2 workflow**：DAG mutation 仍走 maker-v2 vote/red-flag gate，产物落盘到 `artifacts/dag/consensus/`。
- **Paper collaboration contracts**：沿用 `PaperPatchProposal`（replace-span）用于 markdown；新增 delivery snapshots 用 Protobuf-JSON。

## Architecture

总体仍坚持 **File-SSoT + Snapshot-first SSE**，将 issue #67 的“体验阶段”映射为 **可落盘的状态机** 与 **可观测事件流**。

```mermaid
flowchart TD
  UI[Frontend (AG-UI SSE)] -->|POST /input| IN[SessionInput]
  IN --> LOOP[Session Loop Controller]

  LOOP --> BRIEF[BriefService]
  LOOP --> ROUND[RoundCoordinator]
  LOOP --> COMPUTE[ComputeService]
  LOOP --> DELIV[DeliveryCenterService]

  ROUND --> RA[research_assistant]
  ROUND --> P[planner]
  ROUND --> L[librarian]
  ROUND --> R[reasoner]
  ROUND --> V[verifier]
  ROUND --> D[dag_builder]

  DELIV --> PE[paper_editor]
  DELIV --> PAPER[PaperService apply patch]
  DELIV --> FILES[deliverables/*.json + paper/*.md]

  ROUND --> MAKER[maker-v2 consensus]
  MAKER --> DAG[DagStore snapshot/staged/consensus]

  BRIEF --> SSoT[workspace/sessions/{id}/...]
  COMPUTE --> SSoT
  DAG --> SSoT
  FILES --> SSoT
  LOOP --> SSE[AG-UI SSE events]
  SSE --> UI
```

### Modular Design Principles
- **单一职责拆分**：不继续膨胀 `VibeOrchestrator`；将 Brief/Compute/Delivery/Workers 拆为独立 service + 小文件。
- **文件为真相**：brief/compute/delivery 的 canonical 状态落盘；SSE 仅做投影与信号（前端必要时 GET 拉全量）。
- **有界输出**：大内容写文件，事件只传路径/摘要；扫描/读取必须 bounded。

## Components and Interfaces

### Component 1 — Session Loop Controller（阶段编排）
- **Purpose:** 维护 session 内的阶段状态：`brief_pending → brief_ready → round_running → compute_pending/running → delivery_updated`。
- **Interfaces:**
  - `HandleInputAsync(session, input)`：识别 intent（方向输入 / continue / correction / compute decision）
  - `StartRoundAsync(...)`：触发一轮并行调研
- **Dependencies:** `BriefService`, `RoundCoordinator`, `ComputeService`, `DeliveryCenterService`, `WorkspaceService`
- **Reuses:** 现有 `ResearchRunExecutor` 的 run/step/sse 投影约束（best-effort）

### Component 2 — BriefService（研究简报）
- **Purpose:** 生成并持久化 Research Brief（1页），并提供“继续/纠偏”所需的可追溯输入。
- **Interfaces:**
  - `GenerateAsync(sessionId, userDirection, boundaries, context) -> SraResearchBriefSnapshot`
  - `SaveAsync(sessionId, snapshot)` / `LoadAsync(sessionId)`
- **Dependencies:** `ResearchRuntime`（调用 `research_assistant` 新增 `[MODE:BRIEF]`）, `WorkspaceService`
- **SSE:**
  - `aevatar.vibe.brief_snapshot`（connect）
  - `aevatar.vibe.brief_updated`（signal）

### Component 3 — RoundCoordinator（并行调研）
- **Purpose:** 执行 Round N：plan + 并行 workers + DAG 共识 + summary。
- **Interfaces:**
  - `ExecuteRoundAsync(session, runId, input, ct) -> RoundResult`
- **Dependencies:** `research_assistant`, `planner/librarian/reasoner/verifier/dag_builder`
- **Notes:** worker 调度支持并行（`Task.WhenAll`），但输出仍以 agent-tagged streaming 方式进入同一消息流/可折叠面板。

### Component 4 — ComputeService（执行计划卡 + 运行期输出）
- **Purpose:** 接收 verifier 的 compute 请求，生成执行计划卡，按策略决定自动执行或请求用户确认，并运行 compute job（可取消）。
- **Interfaces:**
  - `EvaluateRequestAsync(request) -> ComputeDecisionPolicy`
  - `CreatePlanAsync(request) -> SraComputePlan`
  - `StartJobAsync(plan) -> SraComputeJobStatus`（后台执行 + 持续 SSE）
  - `CancelJobAsync(jobId)`
- **SSE:**
  - `aevatar.vibe.compute_requested`（执行计划卡）
  - `aevatar.vibe.compute_status`（queued/running/done/failed）
  - `aevatar.vibe.compute_log_delta`（日志增量，bounded）
  - `aevatar.vibe.compute_snapshot`（中间结果引用，若有）

### Component 5 — DeliveryCenterService（交付物中心）
- **Purpose:** 每轮结束更新“论文 + 清单”，并提供 DAG↔论文互转入口。
- **Interfaces:**
  - `UpdateFromRoundAsync(roundResult) -> SraDeliveryCenterSnapshot`
  - `GetSnapshotAsync(sessionId)`
- **Dependencies:** `paper_editor` agent（生成 patch + lists），`PaperService`（apply patch），`WorkspaceService`
- **Notes:** 论文使用 patch（replace-span），清单用 snapshot 文件整写（bounded、可 diff）。

### Agent — paper_editor（单写入者）
- **Purpose:** DAG ↔ 论文/清单互转的唯一写入者逻辑角色。
- **Inputs:** 本轮 DAG diff（accepted mutation）、现有论文/清单、round summary、证据引用。
- **Outputs:**
  - `PaperPatchProposal`（draft/outline）
  - `SraDeliveryCenterSnapshot`（结论卡/证据对照/下一步任务清单的结构化更新）
  - 可选：从论文/清单抽取 DAG mutation 候选（交给 dag_builder 或直接产出候选再走 maker-v2）

## Data Models

### Protobuf Contracts (new/extend)
在 `sra_collab.proto` 追加（示意）：

- `SraResearchBriefSnapshot`
  - `session_id`, `version`, `rewritten_question`, `scope`, `terms[]`, `assumptions[]`, `risks[]`, `uncertainties[]`, `milestones[]`, `updated_at`
- `SraComputeRequest` / `SraComputePlan` / `SraComputeJobStatus`
  - request: 为什么必须算、预期产物、成本/风险级别、替代方案
  - plan: 推荐选项、是否自动执行、需要用户确认的阈值
  - job: status/progress/log_excerpt/artifact_paths/error
- `SraConclusionCard`, `SraEvidenceItem`, `SraNextTaskItem`
- `SraDeliveryCenterSnapshot`
  - `paper_outline_path`, `paper_draft_path`
  - `conclusions_path`, `evidence_path`, `tasks_path`
  - `changed_summary`, `updated_at`, `version`

### On-disk Layout (session)

```
workspace/sessions/{sessionId}/
  paper/
    outline.md
    draft.md
  deliverables/
    brief.json                 # SraResearchBriefSnapshot (Protobuf-JSON)
    conclusions.json           # SraConclusionCardsSnapshot (Protobuf-JSON)
    evidence.json              # SraEvidenceTableSnapshot (Protobuf-JSON)
    tasks.json                 # SraNextTasksSnapshot (Protobuf-JSON)
  artifacts/
    compute/
      {jobId}/
        plan.json              # SraComputePlan
        status.json            # SraComputeJobStatus
        log.txt                # bounded rolling log
        snapshots/*            # optional intermediate outputs
    dag/
      snapshot.json
      staged/
      consensus/
```

说明：
- `deliverables/` 为交付物中心的 canonical 文件集合（论文在 `paper/`，清单在 `deliverables/`）。
- Compute 输出统一归档到 `artifacts/compute/{jobId}/`，便于审计与 UI 引用。

## Error Handling

### Error Scenarios
1. **Brief/Compute/Delivery JSON 解析失败**
   - **Handling:** best-effort：保留原始文本到 runs/ 下供排查；对 UI 发 `*_error` 事件并允许继续下一步（降级为纯文本）。
   - **User Impact:** 用户看到“解析失败/已降级”的提示，不会卡死 session。

2. **Compute 执行失败或超时**
   - **Handling:** job 标记为 failed；写入 error；提供“重跑/降级/放弃”三选项。
   - **User Impact:** 明确失败原因分类与替代路径（对齐 issue #67）。

3. **文件写入路径不安全/越界**
   - **Handling:** 严格 within-root 校验；拒绝并记录；不允许 agent 任意写 workspace 外路径。
   - **User Impact:** UI 显示“写入被拒绝（安全原因）”，并提示修正建议。

## Testing Strategy

### Unit Testing
- BriefStore/DeliveryStore/ComputeStore：原子写、版本递增、bounded 读写、within-root 校验。
- ComputePolicy：低风险自动执行 vs 高风险需要确认的阈值判断。
- paper_editor 输出解析：patch 与 snapshot 的结构化校验与降级路径。

### Integration Testing
- 初次输入方向 → brief 生成 → continue → Round N → delivery 更新（文件存在 + SSE 事件触发）。
- Compute 请求 → 生成执行计划卡 → 用户确认执行 → job status/log/snapshot 落盘 → verifier 复核 → delivery 更新。

### End-to-End Testing
- 前端：Brief 卡片继续/纠偏、Compute 卡片三按钮、交付物中心（论文 + 清单）可见且不被聊天淹没；全程 streaming 不刷新页面。


