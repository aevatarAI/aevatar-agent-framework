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
4. **maker-v2 共识 gate**：使用 Cognitive DSL workflow `maker-v2.yaml` 对 DAG candidate 做 vote + red-flagging
   - **通过**：写入最终 DAG snapshot
   - **失败**：candidate 写入 staged（保留待后续再审/再跑）
5. **research_assistant（summary）**：生成本轮总结（Markdown），并落盘为 Derivation Trace

---

### 3) Session Workspace 目录约定（File-SSoT）

会话根目录：`workspace/sessions/{sessionId}/`

```
decisions/
  goals.json                         # Protobuf-JSON: SraGoalsSnapshot

artifacts/
  uploads/                           # 用户上传附件（返回相对路径）
  dag/
    snapshot.json                    # Protobuf-JSON: SraDagSnapshot
    staged/                          # 未通过共识的 candidate (json)
    consensus/                       # maker-v2 共识 artifacts (json)
  trace/
    trace.jsonl                      # 逐行 Protobuf-JSON: SraRoundSummary

runs/
  {runId}/
    summary.md                       # 本轮总结（人读）
```

说明：
- **不要把大文本塞进 proto**：大段材料/证明/输出优先落盘，消息里传路径引用。
- `trace.jsonl` 为 append-only；UI 通过 `trace_snapshot` + `round_summary` 增量更新。

---

### 4) AG-UI SSE 事件命名（CUSTOM aevatar.vibe.*）

SSE endpoint：`GET /api/sessions/{sessionId}/agui/events`

#### 4.1 Bootstrap（reconnect 时立即发送）
- `aevatar.vibe.goals_snapshot`
- `aevatar.vibe.dag_snapshot`
- `aevatar.vibe.trace_snapshot`
- `aevatar.vibe.agents_snapshot`（best-effort，仅确定性 roster/ids）

#### 4.2 Live updates（run 中/结束后）
- `aevatar.vibe.goals_updated`（目前作为 signal，前端会再 GET /goals 拉全量）
- `aevatar.vibe.dag_updated`（共识通过并写入 DAG 后）
- `aevatar.vibe.consensus_blocked`（共识失败，candidate 被 staged）
- `aevatar.vibe.round_summary`（本轮 trace entry：包含 preview + summaryPath）

---

### 5) 主要 API（MVP）

- **Goals**
  - `GET /api/sessions/{id}/goals`
  - `PUT /api/sessions/{id}/goals`
- **Uploads**
  - `POST /api/sessions/{id}/uploads`（multipart/form-data）
- **DAG**
  - `GET /api/sessions/{id}/dag`
  - `GET /api/sessions/{id}/dag/{nodeId}/explain`
  - `GET /api/sessions/{id}/dag/staged`
- **Single Entry Chat**
  - `POST /api/sessions/{id}/input`
    - 支持 `mode=vibe`
    - 支持可选 `toAgents` / `attachmentPaths`（作为路由 hint 与附件引用）

---

### 6) maker-v2 共识门控（自动，无人工审批）

- workflow：`src/Aevatar.Agents.Cognitive/workflows/maker-v2.yaml`
- 执行器：`DagConsensusRunner`（调用 `CognitiveStrategy.ExecuteAsync`）
- 产物落盘：`artifacts/dag/consensus/*.json`
- 输出约束：STRICT JSON；解析失败/超长/不一致等会被 **red-flagging** 阻断并 staged

---

### 7) 扩展点（下一步）

- **Agents 输出分流**：把每个 agent 的输出映射到独立消息流/面板（目前 MVP 合流到单一 assistant message）
- **Mailbox 驱动**：将 plan/worker outputs/interrupt 统一走 file mailbox（durable queue），实现可追踪的多轮协作
- **更强的 DAG 展示**：引入轻量图布局（先避免重 graph lib），再逐步增强交互
- **Trace 可回放**：支持从 `trace.jsonl` 重放 UI，并可跳转到 `runs/{runId}/summary.md`

---

### 8) 端口约束

仓库内示例/文档/默认配置 **不要使用 `:5000`**。如需本地默认端口，优先 `:5678`。


