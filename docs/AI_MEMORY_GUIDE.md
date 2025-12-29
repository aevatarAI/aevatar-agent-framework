## AI Memory 设计与使用指南（State.History / Compaction / CQRS Read Model）

目的：把 Aevatar 里的 “AI 记忆”讲清楚——**State.History 该存什么**、**什么时候 compact**、**什么时候读数据库里的 memory**，以及两种主流 Agent 形态（对话式 vs Cognitive/Workflow）应该怎么选。

---

### 0. 一句话 TL;DR

- **State.History**：短期窗口（UI/上下文回放），必须有界；否则 token 和 state 都会爆炸。
- **State.Context["history_summary"]**：滚动摘要（压缩记忆），用于把“被裁掉的历史”以更低 token 成本塞回 system prompt。
- **CQRS Read Model**：OnStateChanged 投影到 read-model，通过 `IStateQueryService` 查询，`search_memory` 会优先尝试。

---

### 1. 框架里的记忆分层（按“成本/作用域”排序）

#### Layer 0：Stateless Prompt（无记忆）
- **特点**：每次 LLM 调用只带当前 user message（+ system prompt），最省 token/最可控。
- **适用**：Workflow/DSL、严格预算推理、确定性可复现流程（Cognitive 系列默认就是这个）。

#### Layer 1：Short-term Window（`State.History`）
- **载体**：`AevatarAIAgentState.history`（Protobuf，跨 runtime 可序列化）
- **用途**：
  - 对话式 Agent：回放最近 N 条消息，让 LLM “看起来有状态”
  - UI：重连快照、渲染最近消息/步骤结果（AG-UI）
- **关键约束**：必须有界（window），否则 token 与 state payload 线性增长。

#### Layer 2：Rolling Summary（`State.Context["history_summary"]`）
- **载体**：`AevatarAIAgentState.context`（`map<string,string>`）
- **用途**：把被裁掉的历史做“压缩摘要”，以固定格式注入 system prompt（降低 token 成本）。
- **注意**：默认不会自动开；开启后可能产生**额外 LLM 调用**（用于生成 summary）。

#### Layer 3：CQRS Read Model（投影状态快照）
- **载体**：`IStateQueryService` + read-model（Elasticsearch / 内存等）
- **用途**：跨进程/跨 runtime 查询投影后的状态字段（`search_memory` 会以 `cqrs_state` 返回命中）
- **关键点**：只投影“你需要被检索”的字段，避免把大段原文塞进 state

#### Layer 4：Memory Store（资源化 append-only 记忆）
- **载体**：`IMemoryStore` + `MemoryEntry`（Protobuf，跨 runtime 可序列化）
- **用途**：把“记忆”从 Agent 私有 state 升级为可共享资源（private/session/run/...），便于治理与后续向量/图谱索引
- **默认行为**：框架默认注册 file store（best-effort；`${AEVATAR_MEMORY_DIR}`，否则 `<repoRoot>/memory`），但 **AIGAgentBase 默认不会自动写入**：需要显式开启 `EnableMemoryStoreAppend = true`（`memory_id` 默认由 scope 派生：`{scope_type}::{scope_id}`，例如 `privateagent::<agentId>` / `session::<sessionId>` / `execution::<executionId>`；也可用 `MemoryStoreScopeType` / `MemoryStoreScopeIdOverride` / `MemoryIdOverride` 覆写）
- 详见：`docs/MEMORY_STORE.md`

#### Layer 4.1：Vector Index（持久化语义检索索引）
- **载体**：`IMemoryVectorIndex` + `MemoryVectorRecord`（Protobuf）
- **用途**：把语义检索从“一次性 rerank”升级为可持久化 top‑k 召回（跨进程/跨 run）
- **默认行为**：框架默认注册 file index（best-effort；`${AEVATAR_MEMORY_VECTOR_DIR}` 或与 `${AEVATAR_MEMORY_DIR}` 共址），但写入与使用都 **默认关闭**：需要显式开启 `EnableMemoryVectorIndexAppend = true`（且 embedding generator 可用）
- 详见：`docs/MEMORY_VECTOR_INDEX.md`

#### Layer 4.2：Memory Graph（ExecutionTrace → 图谱）
- **载体**：`IMemoryGraphStore` + `MemoryGraph`（Protobuf）
- **用途**：把执行过程（trace）转成可导航的实体/边，支撑“为什么这么做”的可解释回忆（GraphRAG 工程骨架）
- **默认行为**：默认 `IExecutionTraceStore`（File 实现）会被 `ProjectingExecutionTraceStore` 装饰：`SaveAsync(trace)` 后 best-effort 投影产出 `MemoryGraph`（trace bundle artifact）+ execution‑scoped `MemoryEntry`（`memory_id = execution::<executionId>`）
- 详见：`docs/MEMORY_GRAPH.md`

---

### 2. State.History 里到底应该存什么？

- **应该存（短期窗口 + UI 可恢复）**
  - **user/assistant 的最终文本**
  - **tool 调用的结果摘要**（必要时）
  - **step 级元数据**（Cognitive 系列会写 `step_id/step_type/agent_kind/...` 到 `AevatarChatMessage.metadata`）

- **不应该存（会导致 state 膨胀或跨边界风险）**
  - 大段原始资料（论文/网页全文/长日志）
  - 大对象/非 Protobuf 类型（违反跨边界 Protobuf 铁律）
  - “长期知识库/外部资料”内容（应进业务数据库/检索系统，而不是塞进 State）

---

### 3. 什么时候 compact（以及 compact 做了什么）

框架内置逻辑在 `AIGAgentBase`：

#### 3.1 触发条件（真实规则）
- 只有在以下开关都开启时才会执行：
  - `EnableChatHistoryInState == true`
  - `EnableChatHistoryCompaction == true`
- 且满足：
  - `State.History.Count > ChatHistoryMaxMessages`

#### 3.2 执行时机（真实调用点）
- `ChatAsync`：
  - **构建 request 前**先 compact 一次（避免 token blow-up）
  - **写入新消息后**再 compact 一次（保证下一次调用仍有界）
- `ChatStreamAsync`：
  - streaming 开始前 compact
  - streaming 完成并落盘 assistant 后再 compact

#### 3.3 compact 的具体动作（真实行为）
- **Layer 1（滑窗）**：只保留最后 `ChatHistoryMaxMessages` 条，其余移除
- **Layer 2（摘要）**：
  - 把移除的消息合并进 `State.Context["history_summary"]`
  - 默认实现会调用一次 LLM 做“结构化摘要”（失败则降级为启发式 notes）
  - summary 会被截断到 `ChatHistorySummaryMaxChars`（默认 4000 chars）

#### 3.4 代价与选型
- **对话式 Agent**：开 Layer1+2 是合理的（用预算换可持续对话）
- **Workflow/Cognitive**：通常不允许“隐藏 LLM 调用”  
  Cognitive 系列做法：覆写 summary 生成逻辑直接返回 `null`，避免额外 LLM 成本，同时仍保留短期窗口用于 UI。

---

### 4. 什么时候按需检索（search_memory / CQRS Read Model）

#### 原则：不要把外部存储/检索当成“每轮 prompt 的默认输入”
每次都读、每次都塞回 prompt，会带来三类坏味道：
- **延迟**：IO + 搜索
- **噪音**：历史越多，越容易把无关内容塞进上下文
- **token 反噬**：你想省 token，结果把检索结果全塞回去了

#### 4.1 LLM 推理路径：按需读取（推荐）
两条推荐路线（二选一或组合）：

- **路线 A：让模型自己决定“何时回忆”**（工具化）
  - 启用内置工具 `search_memory`（实现：`src/Aevatar.Agents.AI.Core/WithTool/Tools/BuiltIn/AevatarMemorySearchTool.cs`）
  - 参数：
    - `memoryType`：`all` / `working` / `conversation`
    - `memoryId`：限定检索的资源（格式 `{scope_type}::{scope_id}`；默认 `privateagent::<agentId>`；执行回放：`execution::<executionId>`）
  - 安全策略（工具执行）：
    - 工具执行会受 `ToolExecutionContext` 的 `AllowInternalTools` / `AllowDangerousTools` 约束；标准策略默认 `AllowInternalTools=true`、`AllowDangerousTools=false`（危险/需确认工具不会执行）
  - 模型需要回忆时调用 tool
  - tool 的优先级是（best-effort）：
    - **长期记忆（可持久化）**：
      - `IMemoryVectorIndex` + `ToolContext.GenerateEmbeddingsAsync` 可用时：先走向量 top‑k（`memory_vector`）
      - 否则：走 `IMemoryStore.SearchAsync` lexical/substring（`memory_store`）
    - 再查 CQRS read-model（优先 `IStateQueryService.QueryAsync`，失败降级 `GetByIdAsync` + state scan；返回 `cqrs_state`）
    - 再搜 `State.Context["history_summary"]`（`conversation_summary`）和 `State.History`（`conversation`）（无 IO）
    - 若以上都没有任何 lexical 命中：再 best-effort 做一次 state 语义检索/rerank（需要 embedding callback；有成本，上层已做“无命中才触发”的保护）

- **路线 B：由 Agent 决策预取（规则化）**
  - 仅在出现明确意图时读外部检索（例如：用户问“上次你说的 X”/“我的偏好是什么”/“继续上次未完成任务”）
  - 只取 top‑k（小 k），并把结果以“facts list/引用片段”形式注入 prompt（不要整段 dump）

#### 4.2 UI 重连/快照路径：依赖 StateStore（推荐）
- AG-UI snapshot 从 `State.History` 收集每个 step 的最终 assistant 输出
- `State.History/Context` 属于 Agent State，应由 StateStore（Orleans / Mongo / Supabase StateStore）负责恢复

---

### 5. 两种推荐形态（落地模板）

#### 5.1 对话式 Agent（需要“持续对话”）
- **开关建议**：
  - `EnableChatHistoryInState = true`
  - `EnableChatHistoryCompaction = true`
  - `ChatHistoryMaxMessages = 20~60`（看 token 密度）
  - `ChatHistorySummaryMaxChars = 2000~6000`
- **读外部检索的时机**：
  - 默认不读
  - 让模型用 `search_memory` 自己读（推荐）
  - 或在明确“回忆意图”时由 Agent 预取 top‑k

#### 5.2 Cognitive / Workflow Agent（严格预算 + 可复现）
- **策略**：
  - LLM request **保持无状态**（不 replay `State.History`）
  - `State.History` 仅用于 UI hydration（写 step 元数据）
  - 不允许 hidden LLM summary：summary 生成返回 `null`
- **检索的时机**：
  - 需要跨 step 检索：让模型调用 `search_memory`（优先 `cqrs_state`，其次 `history_summary`/`State.History`）

---

### 8. 最后：品味自检（避免常见坏味道）

- **不要把 memory 设计成“全量回放”**：那不是记忆，是 token 炸弹。
- **把特殊情况消掉**：让“短期窗口 + 结构化摘要 + 按需检索”成为常规路径，而不是写一堆 if/else 拼 prompt。
- **跨边界类型一律 Protobuf**：State、Event、Config 任何跨 runtime/stream 的数据必须是 `.proto` 生成。

---

### 9. 对照 Cognee：Aevatar 记忆还能怎么进化（图 + 向量 + Pipeline）

> Cognee 的主张：把“记忆”做成一个独立、可扩展的基础设施层，用 **图谱 + 向量检索** 承载长期记忆，并通过 ECL（Extract/Cognify/Load）流水线把原始数据转成可检索的记忆。参考：[`topoteretes/cognee`](https://github.com/topoteretes/cognee)

对照现状，Aevatar 已经具备两条很强的“记忆主线”：

- **对话/状态记忆**：`State.History`（滑窗）+ `history_summary`（滚动摘要）+ CQRS read-model（投影查询）+ 工具化 `search_memory`
- **执行/回放记忆**：统一的 `ExecutionTrace`（跨边界 Protobuf，bundle 导出，适合审计/回放/对比）

目前的落地状态（按性价比排序）：

- **P0（已实现）：把检索从“子串 contains”升级为“可用的全文检索”**
  - `search_memory` 已优先走 CQRS 的 `QueryAsync`（best-effort；实现方可用 Lucene/QueryString/FTS），并在 CQRS 不可用时降级到 `GetByIdAsync` + state scan。
  - 仍建议：投影层（CQRS Projector）增加面向检索的扁平字段（例如 `historyText/contextText/historySummary`），减少“复杂 JSON 字符串”对检索质量的伤害。

- **P1（已实现）：语义检索（向量）落地到工具链**
  - 当 `IMemoryVectorIndex` + embedding callback 可用时，`search_memory` 会优先走向量 top‑k（默认 File + brute-force cosine，无外部依赖）。
  - 写入默认关闭：`AIGAgentBase.EnableMemoryVectorIndexAppend = true` 才会把对话写入向量索引（best-effort）。
  - embeddings 不可用时：退化为 `IMemoryStore` lexical/substring + CQRS/State scan（best-effort）。
  - 关键原则：向量索引是“外部层”，不要塞进 Agent State（避免 state 膨胀与跨 runtime payload 爆炸）。

- **P2（已实现）：用“图”承载关系（轻量图谱即可）**
  - `ExecutionTrace` 天然是一棵树（并可带 labels/metrics/decisions/alerts），非常适合做“可解释的记忆图谱”输入。
  - 现在框架会在 `SaveAsync(trace)` 后 best-effort 投影出：
    - `MemoryGraph`（trace artifact，保存在 trace bundle 的 artifacts 下）
    - `MemoryEntry`（scope=execution，可用 `search_memory(memoryId="execution::<executionId>")` 检索）
  - 下一步可以把图谱与向量召回组合：向量找“相关片段”，图谱解释“关系路径/选择原因”。

- **P3（规划中）：把“记忆处理”显式化为 Pipeline（ECL 对齐）**
  - Extract：从 `ExecutionTrace` / `WorkflowStepEvent` / Agent 对话、工具输出抽取候选记忆条目（append-only）。
  - Cognify：做结构化总结/去重/归一化（可 LLM、也可 token-free 规则）。
  - Load：写入 Memory Store + FTS/Vector/Graph 索引（异步投影，失败可重试，保证主流程不被拖慢）。

> 如果你希望进一步“产品化”（跨 Agent/跨 Run 共享 + 治理），可以直接复用 AevatarKit 的方向：把 Memory 资源化（memory_id + scope + append-only entry），再把 FTS/vector/graph 作为可插拔索引层。


