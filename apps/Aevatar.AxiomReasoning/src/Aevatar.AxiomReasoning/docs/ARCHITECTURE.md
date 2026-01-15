# Aevatar.AxiomReasoning - Architecture

目标：复刻 `Aevatar.PaperReview` 的“Session + SSE + CognitiveStrategy”模式，实现 **定理发现循环**：

- Coordinator 提出一个新定理（theorem candidate）
- 所有 Workers 并行给出证明/反证/漏洞
- Coordinator 通过 vote 达成“是否证明成功 + 最终证明”的共识
- 证明成功后把定理加入已知集合，并基于“公理 + 已证定理”继续推导下一条

## 目录结构

```
Aevatar.AxiomReasoning/
├── EventStreaming/
│   ├── AgUi/
│   │   ├── AxiomAgUiEventStream.cs     # AxiomEvent → AG-UI 事件流投影
│   │   └── AxiomAgUiBootstrap.cs       # 重连快照：messages/status/graph（使用框架层 AgUiBootstrap）
│   └── Events/
│       ├── AxiomEvent.cs               # 领域事件
│       └── GraphEvent.cs               # 图快照事件
├── Framework/
│   └── Aevatar.Agents.Cognitive.Streaming/ # BroadcastEventHub (SSE 广播)
├── Program.cs
├── Aevatar.AxiomReasoning.csproj
├── Models/
│   └── AxiomSession.cs
├── Services/
│   ├── AxiomReasoningService.cs
│   ├── AxiomReasoningEventBridge.cs
│   ├── LlmTranscriptOptions.cs     # 本地 LLM 对话记录配置
│   ├── LlmTranscriptRecorder.cs    # JSONL + review.md 落盘（便于复盘/AI 分析）
│   ├── IGraphStore.cs
│   ├── AxiomDagService.cs          # InMemory graph store（默认）
│   ├── SupabaseGraphStore.cs       # Supabase graph store（可选落盘）
│   └── SupabaseService.cs          # 会话结果落盘（state/theorems）
└── wwwroot/
    ├── index.html
    ├── styles.css
    └── app.js
```

## 关键路径

1. 前端提交公理与目标（可选 workflow / language / budgets）→ `POST /api/sessions`
2. 启动推理 → `POST /api/sessions/{id}/run`
3. 后端调用 `CognitiveStrategy.ExecuteAsync`，使用 session 选择的 `CognitiveWorkflow`
4. 进度回调 `ReasoningProgress` 经 `AxiomReasoningEventBridge` 生成领域事件（ProgressEvent/GraphEvent/ResultEvent/ErrorEvent）
5. Session 使用 `BroadcastEventHub` fan-out 给所有 SSE 订阅者（避免多连接“抢消息”）
6. SSE 输出：
   - Legacy：`GET /api/sessions/{id}/events`（项目内 UI 兼容）
   - 标准化：`GET /api/sessions/{id}/agui/events`（AG-UI 协议 + CUSTOM 扩展）
   - 同时 `LlmTranscriptRecorder` 会把 llm_call/vote 的对话过程落到 `output/{sessionId}/llm/`
7. `update_state` 完成时：`AxiomReasoningEventBridge` 解析 `state`，发 `GraphEvent`，并写入 `IGraphStore`（InMemory 或 Supabase）
8. 完成后落盘 artifacts：`state.json / theorems.json`
9. （可选）完成后写入 Supabase：将 `state.json / theorems.json` 作为 JSON 字符串持久化到 Postgres

## AG-UI 对接策略（本项目落地版）

- **threadId/runId**：默认都使用 `sessionId`
- **连接时序（/agui/events）**：服务端优先发“快照”，不依赖 EventHub replay（避免断线重连时 token/progress 爆发）
  - `MESSAGES_SNAPSHOT`：user input + **Coordinator/Workers 的 `State.History`（短期窗口）**
    - 前提：CognitiveStrategy 使用 `session_id` 派生 **确定性 AgentId**（Coordinator + worker-0..N-1），因此服务端可以稳定定位到同一批 Actor
    - 同时发送 `CUSTOM(name="aevatar.axiom.message_meta")`，把 system/user prompts 绑定到每条 messageId（Workers 卡片刷新后能补齐提示词）
  - `CUSTOM(name="aevatar.axiom.status_snapshot")`：当前 status/phase/progress/tokens/llm 统计
  - `STATE_SNAPSHOT`（可选）：从 `IGraphStore.GetSnapshotAsync` best-effort 构造 GraphEvent 并下发（让新订阅者立刻拿到图）
  - `CUSTOM(name="aevatar.axiom.session")`：会话配置（workflow/budgets/hpa…）
  - `RUN_STARTED`：若 session 已在 Running
  - 之后进入 live stream（`SubscribeAsync(replay:false)`）
- **标准事件**：
  - `RUN_STARTED/RUN_FINISHED/RUN_ERROR`：对应 session 执行生命周期
  - `STEP_STARTED/STEP_FINISHED`：对应 DSL step 状态跃迁（Running → Finished）
  - `TEXT_MESSAGE_*`：对齐 `llm_call` 的 streaming（**token delta 主通道**；避免把每 token 都塞进 progress 事件）
  - `STATE_SNAPSHOT/STATE_DELTA`：GraphEvent 作为 state（首次 snapshot，后续尽量用 JSON Patch 增量）
- **CUSTOM 扩展**：为了让现有 Dashboard UI 零痛接入，继续发送：
  - `CUSTOM(name="aevatar.axiom.progress", value=ProgressEvent)`（**非 streaming token** 的状态/统计事件；token 走 TEXT_MESSAGE_CONTENT；并对 fan_out 的“空跑 tick”做去重）
  - `CUSTOM(name="aevatar.axiom.result", value=ResultEvent)`
  - `CUSTOM(name="aevatar.axiom.error", value=ErrorEvent)`
  - `CUSTOM(name="aevatar.axiom.message_meta", value={workerId/provider/prompts...})`

## 设计约束

- **不改动框架调用链**：沿用 `CognitiveStrategy` 的 Actor 并行与 vote 共识机制。
- **输入限制**：`CognitiveStrategy` 注入 `task/context`（以及少量 Context 变量），所以本项目把 `axioms + focus(可选) + seedHypothesis(可选)` 编码进 task 文本，由 workflow 在 init 步骤解析并写入 `state`。

### Goal / Seed Hypothesis（可选）

本项目把输入组织为两类：

- **Goal**：作为整个 loop 提出 hypotheses 的大方向（workflow 会把它写入 `state.focus` 并在后续 propose 阶段持续对齐）。
- **Seed Hypothesis（可选）**：用户给一个“第一轮就要尝试验证/修补”的起始假设（workflow 会优先把它作为 `current_hypothesis` 的 statement）。

实现上，这两者会被编码进 `task` 文本（`Focus (optional):` 与 `SeedHypothesis (optional):` 两段），由 workflow 的 `init_state` 步骤解析并写入 `state`。

## 多 Workflow / 多语言 / 长跑预算

### UI 参数（Create Session）

- `workflow`: 选择 Cognitive DSL workflow（默认：`hypothesis_promotion_loop`）
- `language`: 生成内容语言（例：`English` / `Chinese`；不影响 JSON keys）
- `maxDurationMinutes / maxLlmCalls / maxTokens`: 长跑预算
- `continueOnFailure`: `proved=false` 时是否继续探索
- `hpaEnabled`: 是否启用 HPA 透传（仅对支持 `hpa` 的 workflow 生效，例如 `hypothesis_promotion_loop_hpa`）
- `hpaAlpha / hpaSeedPhase`: Θ 扫描参数（默认黄金 α=φ^{-1}）
- `hpaBetaModel / hpaBeta0 / hpaBeta1 / hpaSeed`: embedding 参数（phase model + deterministic seed）
- `hpaRadialWBase / hpaRadialWScale`: ρ（radial）参数
- `minCoherence / maxGapNorm / maxAssociatorMean`: HPA gate 阈值（用于“是否进入验证/升级”）

### Workflow 透传方式

- `AxiomReasoningService.BuildReasoningOptions` 将 `language / continue_on_failure` 放入 `ReasoningOptions.Context`
- 当 `hpaEnabled=true` 时，会额外把 `hpa_*` 与 gate 阈值写入 `ReasoningOptions.Context`
- `CognitiveStrategy` 会把 Context 变量注入 workflow 初始变量（`initialVariables`）
- `hypothesis_promotion_loop.yaml` 声明了 `language` 输入，并在 prompt 中引用

> NOTE: DAG 增量更新依赖 workflow 产生 `update_state`（LLM 回写 state）的 Completed 事件；  
> HPL/HPA 类 workflow 通常用 `transform/hpa` 直接修改 state，默认不会触发 DAG 增量更新（可通过后续增强在结束时补一次 snapshot）。

## Graph DB（DAG）推理

### 数据模型（按 session 分区）

- **Node**：`Axiom | Theorem | Hypothesis | Assumption | Unknown`
- **Edge**：`depends_on`（`fromId -> toId`）

### 写入时机

- `AxiomReasoningEventBridge` 在 `update_state` step `Completed` 时解析 state 里的 `axioms/theorems/depends_on`
- 将结果 upsert 进 `IGraphStore`
  - 默认：`AxiomDagService`（内存态，最快）
  - 可选：`SupabaseGraphStore`（落盘到 Supabase/Postgres，便于后续迁移 Neo4j）

### DAG 推理能力（最小可用）

- 依赖闭包（dependency closure）
- 缺失依赖（Hypothesis/Unknown 视为“需要额外假设”）
- 环检测（cycle）
- 近似可证明性：`no cycle && no missing deps`

## API 一览

### Sessions

- `GET /api/sessions`：列出 sessions（包含 workflow/language）
- `POST /api/sessions`：创建 session（可传 workflow/language/budgets）
- `POST /api/sessions/{id}/run`：启动
- `POST /api/sessions/{id}/stop`：停止
- `GET /api/sessions/{id}/result`：结果

### Workflows

- `GET /api/workflows`：列出可用 workflow 名称（用于 UI 下拉框）
  - `Aevatar.AxiomReasoning.csproj` 会将 `src/Aevatar.Agents.Cognitive/workflows/**.yaml` 全量拷贝到输出目录 `./workflows/`

### DAG (Graph DB)

- `GET /api/sessions/{id}/dag`：返回节点/边（含 kind 标注）
- `GET /api/sessions/{id}/dag/{nodeId}`：返回节点推理解释（deps/missing/topo/cycle/provable）
- `GET /api/graphstore/diagnostics`：查看当前 GraphStore 后端（InMemory/Supabase）与表/缓存状态

### LLM Transcript (local output)

- `GET /api/sessions/{id}/llm/review`：下载 `review.md`（人类可读）
- `GET /api/sessions/{id}/llm/transcript`：下载 `transcript.jsonl`（机器可读 / 可喂给 AI）

## MongoDB 持久化（可选，推荐用于“真正无状态前端”）

当你希望“**刷新/断线重连/甚至服务重启后**仍能恢复 Workers 卡片历史”，需要把“对话历史”从内存升级为可恢复的存储层：

- **StateStore（短期窗口）**：持久化 `AevatarAIAgentState.History`（含 step 元数据）
- **LLM Transcript（长期日志）**：`output/{sessionId}/llm/transcript.jsonl`（机器可读，可用于复盘/回放）

### 启用方式

在 `appsettings*.json` 或环境变量中提供 Mongo 连接即可自动启用：

- `MongoDB:ConnectionString`（或 `MONGODB_CONNECTION_STRING` / `AEVATAR_MONGODB_CONNECTION_STRING`）
- `MongoDB:Database`（或 `MONGODB_DATABASE`，默认 `aevatar`）

> NOTE: Transcript 仍会落盘作为 debug artifact，但 **AG-UI bootstrap 不依赖它**。

## Supabase 持久化（JSON）

### 配置

- 配置入口：`appsettings.secrets.json` → `Supabase` 段
- 关键字段：
  - `Enabled`: 是否启用（默认 false）
  - `Url`: 项目 URL
  - `Key`: anon key
  - `ResultsTable`: 表名（兼容旧字段名 `ReviewsTable`）
  - `DagEnabled`: 是否启用 DAG 持久化（默认 false）
  - `DagNodesTable / DagEdgesTable`: DAG 表名（默认 `axiom_reasoning_dag_nodes / axiom_reasoning_dag_edges`；建议保持默认，当前 SDK 表名映射为编译期固定）

### 建表 SQL

在 Supabase Dashboard → SQL Editor 执行（表名默认 `axiom_reasoning_results`）：

```sql
CREATE TABLE axiom_reasoning_results (
  id UUID DEFAULT gen_random_uuid() PRIMARY KEY,
  session_id TEXT NOT NULL UNIQUE,
  axioms_text TEXT,
  goal TEXT,
  status TEXT NOT NULL,
  state_json TEXT,
  theorems_json TEXT,
  content TEXT,
  error TEXT,
  llm_calls INTEGER DEFAULT 0,
  total_tokens BIGINT DEFAULT 0,
  duration_seconds DOUBLE PRECISION DEFAULT 0,
  created_at TIMESTAMP WITH TIME ZONE DEFAULT NOW(),
  completed_at TIMESTAMP WITH TIME ZONE
);

CREATE INDEX idx_axiom_reasoning_results_session_id ON axiom_reasoning_results(session_id);
CREATE INDEX idx_axiom_reasoning_results_created_at ON axiom_reasoning_results(created_at DESC);

ALTER TABLE axiom_reasoning_results ENABLE ROW LEVEL SECURITY;
CREATE POLICY "Allow anonymous access" ON axiom_reasoning_results
  FOR ALL USING (true) WITH CHECK (true);
```

### DAG GraphStore 建表 SQL（可选）

当你开启 `DagEnabled=true` 时，需要额外建两张表：

```sql
-- DAG nodes: (session_id, node_id) 唯一
CREATE TABLE axiom_reasoning_dag_nodes (
  id UUID DEFAULT gen_random_uuid() PRIMARY KEY,
  session_id TEXT NOT NULL,
  node_id TEXT NOT NULL,
  kind TEXT NOT NULL,
  label TEXT,
  proof TEXT,
  updated_at TIMESTAMP WITH TIME ZONE DEFAULT NOW(),
  UNIQUE(session_id, node_id)
);

-- DAG edges: (session_id, from_id, to_id, kind) 唯一
CREATE TABLE axiom_reasoning_dag_edges (
  id UUID DEFAULT gen_random_uuid() PRIMARY KEY,
  session_id TEXT NOT NULL,
  from_id TEXT NOT NULL,
  to_id TEXT NOT NULL,
  kind TEXT NOT NULL DEFAULT 'depends_on',
  updated_at TIMESTAMP WITH TIME ZONE DEFAULT NOW(),
  UNIQUE(session_id, from_id, to_id, kind)
);

-- Indexes
CREATE INDEX idx_axiom_reasoning_dag_nodes_session ON axiom_reasoning_dag_nodes(session_id);
CREATE INDEX idx_axiom_reasoning_dag_nodes_node_id ON axiom_reasoning_dag_nodes(node_id);
CREATE INDEX idx_axiom_reasoning_dag_edges_session ON axiom_reasoning_dag_edges(session_id);
CREATE INDEX idx_axiom_reasoning_dag_edges_to ON axiom_reasoning_dag_edges(session_id, to_id);
CREATE INDEX idx_axiom_reasoning_dag_edges_from ON axiom_reasoning_dag_edges(session_id, from_id);

-- RLS (允许匿名访问；若对外展示需加鉴权，请自行收紧策略)
ALTER TABLE axiom_reasoning_dag_nodes ENABLE ROW LEVEL SECURITY;
CREATE POLICY "Allow anonymous access" ON axiom_reasoning_dag_nodes
  FOR ALL USING (true) WITH CHECK (true);

ALTER TABLE axiom_reasoning_dag_edges ENABLE ROW LEVEL SECURITY;
CREATE POLICY "Allow anonymous access" ON axiom_reasoning_dag_edges
  FOR ALL USING (true) WITH CHECK (true);
```


