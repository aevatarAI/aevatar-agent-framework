## AI Memory 设计与使用指南（State.History / Compaction / DB Memory）

目的：把 Aevatar 里的 “AI 记忆”讲清楚——**State.History 该存什么**、**什么时候 compact**、**什么时候读数据库里的 memory**，以及两种主流 Agent 形态（对话式 vs Cognitive/Workflow）应该怎么选。

---

### 0. 一句话 TL;DR

- **State.History**：短期窗口（UI/上下文回放），必须有界；否则 token 和 state 都会爆炸。
- **State.Context["history_summary"]**：滚动摘要（压缩记忆），用于把“被裁掉的历史”以更低 token 成本塞回 system prompt。
- **IAevatarAIMemory（DB）**：长期存储（append-only，按 agentId/可选 sessionId 隔离），**默认不自动回灌 prompt**，主要走 `search_memory`/AG-UI snapshot 按需读取。

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

#### Layer 3：Long-term DB Memory（`IAevatarAIMemory`）
- **载体**：外部依赖（MongoDB/Supabase 等），通过 DI 注入到 `AIGAgentBase.AIMemory`。
- **用途**：
  - **可检索**：`search_memory` 通过 `IAevatarAIMemory.SearchAsync` 做 best-effort 检索
  - **可追溯**：保存结构化的 per-step interaction（Cognitive）
  - **可恢复 UI**：AG-UI bootstrap 在 State.History 不足时，fallback 读 memory 补齐
- **关键点**：这层“存得久”，但不应该每次都读、每次都塞回 prompt（成本/噪音会反噬）。

---

### 2. State.History 里到底应该存什么？

- **应该存（短期窗口 + UI 可恢复）**
  - **user/assistant 的最终文本**
  - **tool 调用的结果摘要**（必要时）
  - **step 级元数据**（Cognitive 系列会写 `step_id/step_type/agent_kind/...` 到 `AevatarChatMessage.metadata`）

- **不应该存（会导致 state 膨胀或跨边界风险）**
  - 大段原始资料（论文/网页全文/长日志）
  - 大对象/非 Protobuf 类型（违反跨边界 Protobuf 铁律）
  - “长期知识库”内容（应进 `IAevatarAIMemory` 或业务数据库）

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
- **Layer 3（可选归档到 DB）**：
  - 若 `ArchiveCompactedHistoryToAIMemory == true` 且 `AIMemory != null`
  - 会把被裁掉的原文按 chunk 写入 `AIMemory.AddMessageAsync(...)`（用于后续检索/审计）

#### 3.4 代价与选型
- **对话式 Agent**：开 Layer1+2 是合理的（用预算换可持续对话）
- **Workflow/Cognitive**：通常不允许“隐藏 LLM 调用”  
  Cognitive 系列做法：覆写 summary 生成逻辑直接返回 `null`，避免额外 LLM 成本，同时仍保留短期窗口用于 UI。

---

### 4. 什么时候读数据库里的 memory（IAevatarAIMemory）

#### 原则：不要把 DB 当成“每轮 prompt 的默认输入”
每次都读、每次都塞回 prompt，会带来三类坏味道：
- **延迟**：DB IO + 搜索
- **噪音**：历史越多，越容易把无关内容塞进上下文
- **token 反噬**：你想省 token，结果把检索结果全塞回去了

#### 4.1 LLM 推理路径：按需读取（推荐）
两条推荐路线（二选一或组合）：

- **路线 A：让模型自己决定“何时回忆”**（工具化）
  - 启用内置工具 `search_memory`
  - 模型需要回忆时调用 tool
  - tool 的优先级是：
    - 先搜 `State.Context["history_summary"]` 和 `State.History`（无 IO）
    - 再 best-effort 搜 `IAevatarAIMemory.SearchAsync`（有 IO）

- **路线 B：由 Agent 决策预取（规则化）**
  - 仅在出现明确意图时读 DB（例如：用户问“上次你说的 X”/“我的偏好是什么”/“继续上次未完成任务”）
  - 只取 top‑k（小 k），并把结果以“facts list/引用片段”形式注入 prompt（不要整段 dump）

#### 4.2 UI 重连/快照路径：读 DB 作为 fallback（推荐）
AG-UI bootstrap 的策略是：
- **优先用 `State.History`** 收集每个 step 的最终 assistant 输出
- 当 `State.History` 缺失 assistant body 时，**fallback**：
  - `memoryFactory.Create(actorId).GetHistoryAsync(limit: 800)`
  - 从 JSON 中解析 `stepId` + `assistantResponse` 补齐

这类读 DB 是“为 UI 可恢复性服务”，不参与下一轮 LLM prompt 构建。

#### 4.3 Agent 冷启动/重启恢复：通常不需要读 DB memory
- `State.History/Context` 属于 Agent State，应该由 StateStore（Orleans / Mongo / Supabase StateStore）负责恢复
- `IAevatarAIMemory` 是长期层，**不要在 OnActivate 把全量历史加载进 State**  
  正确做法是：只在 `search_memory` 或 UI snapshot 时按需读取

---

### 5. 两种推荐形态（落地模板）

#### 5.1 对话式 Agent（需要“持续对话”）
- **开关建议**：
  - `EnableChatHistoryInState = true`
  - `EnableChatHistoryCompaction = true`
  - `ChatHistoryMaxMessages = 20~60`（看 token 密度）
  - `ChatHistorySummaryMaxChars = 2000~6000`
  - `ArchiveCompactedHistoryToAIMemory = true`（想要可追溯/可检索就开）
- **读 DB 的时机**：
  - 默认不读
  - 让模型用 `search_memory` 自己读（推荐）
  - 或在明确“回忆意图”时由 Agent 预取 top‑k

#### 5.2 Cognitive / Workflow Agent（严格预算 + 可复现）
- **策略**：
  - LLM request **保持无状态**（不 replay `State.History`）
  - `State.History` 仅用于 UI hydration（写 step 元数据）
  - 不允许 hidden LLM summary：summary 生成返回 `null`
  - 把每一步 interaction（system/user/assistant + stepId 等）以 JSON 写入 `IAevatarAIMemory`（长期审计/回放/检索）
- **读 DB 的时机**：
  - UI 重连：AG-UI snapshot fallback
  - 人类复盘/分析：拉 `GetHistoryAsync` 生成 transcript/review
  - 需要跨 step 检索：`search_memory` tool

---

### 6. Long-term Memory（DB）里建议存什么格式？

`IAevatarAIMemory` 的接口是 `AddMessageAsync(role, content)`，所以**content 需要你自己定义可解析格式**。

- **推荐：JSON（便于 AG-UI、检索、后处理）**
  - 至少包含：
    - `stepId`（用于 UI lane/step 对齐）
    - `assistantResponse`（用于补齐快照）
    - `kind`（版本化 schema，例如 `aevatar.cognitive.llm_interaction.v1`）

---

### 7. 配置速记（让 DB memory “真的接上”）

- **MongoDB**：注册 `AddMongoDBAIMemory(...)`，框架会通过 `IAevatarAIMemoryFactory` 为每个 agentId 注入 `AIMemory`
- **Supabase(Postgres)**：`services.AddSupabaseAIMemory()`（详见 `src/Aevatar.Agents.Persistence.Supabase/docs/README.md`）

注意：AIMemory 是长期层；State.History 是否能“重启后仍在”取决于你是否配置了 StateStore（Mongo/Supabase/Orleans provider）。

---

### 8. 最后：品味自检（避免常见坏味道）

- **不要把 memory 设计成“全量回放”**：那不是记忆，是 token 炸弹。
- **把特殊情况消掉**：让“短期窗口 + 结构化摘要 + 按需检索”成为常规路径，而不是写一堆 if/else 拼 prompt。
- **跨边界类型一律 Protobuf**：State、Event、Config 任何跨 runtime/stream 的数据必须是 `.proto` 生成。


