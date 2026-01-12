## Vibe Researching Platform（单入口科研工作台）

本文件描述 `scientific-research-assistant` 子系统扩展后的 **Vibe Researching 平台**：用户默认只与一个 `research_assistant` 对话（单入口），由它在后台调度多个辅助 agents，产出可解释的 DAG（推导图）与可回放的 Derivation Trace（推导轨迹）。

---

### 1) 核心理念：File-SSoT + Snapshot-first SSE

- **File-SSoT**：goals / DAG / trace / uploads / mailbox 等关键数据都落盘为文件，UI 与内存仅作为投影。
- **Snapshot-first SSE (AG-UI)**：前端每次 reconnect 先拿到“快照事件”，再接入 live stream，避免依赖 replay。

---

### 2) 运行时结构（Round Lifecycle）

一次 `mode=vibe` 的 run（单轮）由 `VibeOrchestrator` 执行：

1. **materials**：加载 `facts/` + `sources/` 形成 bounded context（注入 system prompt）
2. **research_assistant（plan）**：生成本轮计划（STRICT JSON）
3. **workers**：按计划运行（MVP 顺序/可并行）
   - `planner`：可执行计划/证据需求
   - `reasoner`：基于 materials 推理（可选 python_exec）
   - `librarian`：证据/资料整理与缺口
   - `verifier`：硬验证（可选 python_exec）
   - `dag_builder`：产出 DAG mutation candidate（STRICT JSON）
4. **DAG apply（当前实现：no verification / no consensus）**：
   - 解析 `dag_builder` 产出的 candidate（JSON）并直接 apply 到 DAG
   - 若 candidate 引用不存在的 `sources/*.md|txt`，会自动创建 placeholder source 文件（避免引用断裂）
   - apply 成功后发 `aevatar.vibe.dag_updated`
5. **research_assistant（summary）**：生成本轮总结（Markdown），并落盘为 Derivation Trace

补充：更细的“谁在什么时候更新 brief / plan nodes / trace / UI message_meta”等，见：
- `docs/VIBE_VIBE_ORCHESTRATION_CODEWALK.md`

---

### 3) Session Workspace 目录约定（File-SSoT）

会话根目录：`workspace/sessions/{sessionId}/`

```
paper/
  outline.md                         # PaperService scaffold（人读）
  draft.md                           # PaperService scaffold（人读）

decisions/
  goals.json                         # Protobuf-JSON: SraGoalsSnapshot

deliverables/
  brief.json                         # Protobuf-JSON: SraResearchBriefSnapshot（1-page brief）
  conclusions.json                   # Protobuf-JSON: SraConclusionCardsSnapshot
  evidence.json                      # Protobuf-JSON: SraEvidenceTableSnapshot
  tasks.json                         # Protobuf-JSON: SraNextTasksSnapshot
  delivery_snapshot.json             # Protobuf-JSON: SraDeliveryCenterSnapshot（paths + changedSummary）

artifacts/
  uploads/                           # 用户上传附件（返回相对路径）
  ui/
    ui_snapshot.json                 # UI 快照（messages/meta/tools/run-steps），用于刷新/重连恢复（best-effort）
    agent_providers.json             # per-agent LLM provider mapping（agent -> providerName）
  compute/
    decisions/                       # 用户 compute 决策记录（json, MVP）
  dag/
    snapshot.json                    # Protobuf-JSON: SraDagSnapshot
    staged/                          # 未通过共识的 candidate (json)
    consensus/                       # DAG 共识 artifacts (json)
  trace/
    trace.jsonl                      # 逐行 Protobuf-JSON: SraRoundSummary

runs/
  {runId}/
    summary.md                       # 本轮总结（人读）
    ui_events.jsonl                  # UI 工作痕迹（RUN/STEP/TOOL/META/MESSAGE_END，best-effort）
```

说明：
- **不要把大文本塞进 proto**：大段材料/证明/输出优先落盘，消息里传路径引用。
- `trace.jsonl` 为 append-only；UI 通过 `trace_snapshot` + `round_summary` 增量更新。

---

### 4) AG-UI SSE 事件命名（CUSTOM aevatar.vibe.*）

SSE endpoint：`GET /api/sessions/{sessionId}/agui/events`

#### 4.1 Bootstrap（reconnect 时立即发送）
- `aevatar.vibe.goals_snapshot`
- `aevatar.vibe.message_meta_snapshot`（messageId → agent/stepName；用于刷新恢复每个 agent 的标签）
- `aevatar.vibe.brief_snapshot`
- `aevatar.vibe.dag_snapshot`
- `aevatar.vibe.delivery_snapshot`
- `aevatar.vibe.trace_snapshot`
- `aevatar.vibe.agents_snapshot`（best-effort，仅确定性 roster/ids）
- `aevatar.vibe.agent_providers_snapshot`（agent → providerName；用于 Agents 面板配置）
- `aevatar.ui.tools_snapshot`（tool cards：messageId → toolCalls；用于刷新恢复）
- `aevatar.ui.run_steps_snapshot`（Run Steps 卡片恢复）

#### 4.2 Live updates（run 中/结束后）
- `aevatar.vibe.goals_updated`（目前作为 signal，前端会再 GET /goals 拉全量）
- `aevatar.vibe.brief_updated`（signal，前端会再 GET /deliverables 拉全量）
- `aevatar.vibe.dag_updated`（DAG apply 成功后）
- `aevatar.vibe.delivery_updated`（signal：delivery center 更新后）
- `aevatar.vibe.round_summary`（本轮 trace entry：包含 preview + summaryPath）
- `aevatar.vibe.compute_decision`（用户点击 execute/degrade/skip 后）

---

### 5) 主要 API（MVP）

- **Agent Providers (per-agent LLM)**
  - `GET /api/sessions/{id}/agent-providers`
  - `PUT /api/sessions/{id}/agent-providers`（body: `{ agent, providerName }`；`providerName=""` 清除映射）
- **Goals**
  - `GET /api/sessions/{id}/goals`
  - `PUT /api/sessions/{id}/goals`
- **Uploads**
  - `POST /api/sessions/{id}/uploads`（multipart/form-data）
- **DAG**
  - `GET /api/sessions/{id}/dag`
  - `GET /api/sessions/{id}/dag/{nodeId}/explain`
  - `GET /api/sessions/{id}/dag/staged`
- **Deliverables**
  - `GET /api/sessions/{id}/deliverables`（brief + delivery center 列表投影）
- **Compute（MVP）**
  - `POST /api/sessions/{id}/compute/decision`（execute/degrade/skip；写入 artifacts/compute/decisions）
- **Single Entry Chat**
  - `POST /api/sessions/{id}/input`
    - 支持 `mode=vibe`
    - 支持可选 `toAgents` / `attachmentPaths`（作为路由 hint 与附件引用）

### 6) DAG 共识门控（自动，无人工审批）

默认采用 **verifier-quorum**（轻量）：几个 `verifier` 同意即可落盘。

- 配置：`src/ScientificResearchAssistant.Api/appsettings.json` → `Vibe:DagConsensus`
  - `Mode`: `verifier-quorum` | `maker-v2`
  - `VerifierCount` / `Quorum`: 门限投票
- 执行器：`DagConsensusRunner`
  - `verifier-quorum`: 直接调用 `VibeVerifierAgent` 做投票（更轻）
  - `maker-v2`: 调用 `CognitiveStrategy.ExecuteAsync`（更重，作为可选模式）
- 产物落盘：`artifacts/dag/consensus/*.json`
- 输出约束：解析失败/超时/结构性问题会被阻断并 staged（保留候选以便后续再审）

#### Mode 切换（推荐写清楚）

- **长期切换**：修改 `src/ScientificResearchAssistant.Api/appsettings.json`：
  - 轻量：`"Mode": "verifier-quorum"`
  - 重：`"Mode": "maker-v2"`

- **临时切换（一次启动）**：用环境变量覆盖（`__` 表示层级）：

```bash
# 切换为 maker-v2（更重，但更强的“共识/审查”）
Vibe__DagConsensus__Mode=maker-v2 dotnet run --project scientific-research-assistant/src/ScientificResearchAssistant.Api/ScientificResearchAssistant.Api.csproj
```

---

### 7) 扩展点（下一步）

- **Agents 输出分流**：把每个 agent 的输出映射到独立消息流/面板（目前 MVP 合流到单一 assistant message）
- **Mailbox 驱动**：将 plan/worker outputs/interrupt 统一走 file mailbox（durable queue），实现可追踪的多轮协作
- **更强的 DAG 展示**：引入轻量图布局（先避免重 graph lib），再逐步增强交互
- **Trace 可回放**：支持从 `trace.jsonl` 重放 UI，并可跳转到 `runs/{runId}/summary.md`

---

### 8) 端口约束

仓库内示例/文档/默认配置 **不要使用 `:5000`**。如需本地默认端口，优先 `:5678`。


