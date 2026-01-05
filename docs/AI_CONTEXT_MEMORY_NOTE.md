## AI 上下文/记忆（Context & Memory）备忘录

目的：记录 Aevatar Agent Framework 当前“记住上下文”的能力边界，以及在**不浪费 token**的前提下应如何演进。

---

### 现状结论（先说人话）

- **LLM 本质无状态**：模型不会“自动记住上一次请求”。所谓“记忆”，只能靠框架把历史/摘要/检索结果再次放进 prompt 发送给模型。
- **框架支持“对话历史（State.History）”**：可选把历史消息存入 agent state，并在下一次请求中 replay 到 `AevatarLLMRequest.Messages`。
- **框架已提供可选的“压缩/摘要/滑窗”**（默认关闭）：开启后会把 `State.History` 维持在短窗口，并把被裁掉的历史合并成滚动摘要写入 `State.Context["history_summary"]`，再注入 system prompt（必要时会产生一次额外 LLM summary 调用）。
- **长期记忆（DB）是“按需读取”的外部层**：通过 `IAevatarAIMemory`（MongoDB/Supabase 等）注入，不会默认自动回灌 prompt，主要走 `search_memory`/AG-UI snapshot 等场景按需读取。

换句话说：**能记，但不省 token；要省 token，必须做压缩或检索裁剪。**

---

### 当前能力（代码层面）

- **请求结构支持历史**：`AevatarLLMRequest.Messages` 可携带 `AevatarChatMessage` 列表（历史对话）。
- **Agent State 里也有 history 字段（Protobuf）**：`AevatarAIAgentState.history`，因此跨 runtime 边界的状态序列化是安全的（符合框架 Protobuf 铁律）。
- **AIGAgentBase 可选 replay + compaction**：
  - `EnableChatHistoryInState=true`：`ChatAsync/ChatStreamAsync` 追加 user/assistant 到 `State.History`，并在下一次构建请求时 replay history。
  - `EnableChatHistoryCompaction=true`：当 `State.History.Count > ChatHistoryMaxMessages` 时做滑窗裁剪，并维护 `State.Context["history_summary"]`（可选写入 `IAevatarAIMemory`）。
- **内置工具 `search_memory`**：优先搜 `State.History + history_summary`，再 best-effort 搜 `IAevatarAIMemory.SearchAsync`（若已接入）。

⚠️ 注意：Cognitive 系列（DSL/Workflow）通常采用“无状态 prompt”，`State.History` 主要用于 UI hydration；长期交互会以结构化 JSON 写入 `IAevatarAIMemory`，供 AG-UI snapshot / 复盘使用。

建议把这份备忘录当作入口，细节以：
- `docs/AI_MEMORY_GUIDE.md`

---

### 问题：为什么这不能直接省 token？

- 如果每次都把 history 追加进 `Messages`，token 仍然随着历史增长而增长；
- 如果 history 里包含大量原始上下文（axioms/theorems/长证明），增长会非常快；
- 因此必须引入“**有损压缩**”与“**相关性裁剪**”：
  - **摘要（Summarization）**：把旧内容压缩成短 memory
  - **检索（Retrieval / RAG）**：只带与当前问题相关的 top‑k facts

---

### 下一步建议（A：让 AI Agent 记忆更省 token）

目标：让“记忆”从“历史回放”升级为“**短摘要 + 小窗口历史 + 按需检索**”。

- **对话式 Agent（需要连续对话）**：
  - 开 `EnableChatHistoryInState=true`（把 user/assistant 写入 `State.History` 并 replay）
  - 开 `EnableChatHistoryCompaction=true`（滑窗 + 滚动摘要到 `State.Context["history_summary"]`）
  - 用 `ChatHistoryMaxMessages / ChatHistorySummaryMaxChars` 控制规模
  - 需要可追溯/可检索时开 `ArchiveCompactedHistoryToAIMemory=true`
- **长期回忆（DB memory）**：
  - 不要每轮都读；优先让模型调用 `search_memory`（或由 Agent 在“明确回忆意图”时预取 top‑k）
- **Cognitive/Workflow（严格预算/可复现）**：
  - 保持无状态 prompt（不 replay `State.History`）
  - `State.History` 仅用于 UI hydration（step 元数据）
  - 交互过程以结构化 JSON 写入 `IAevatarAIMemory` 供快照/复盘

落地入口：
- 详细规则见：`docs/AI_MEMORY_GUIDE.md`
- 代码入口见：`src/Aevatar.Agents.AI.Core/AIGAgentBase.cs`（compaction + summary 注入）

---

### 适用边界（避免误用）

对“从公理推导大量定理”这类任务：
- 纯 history 回放通常不是最省 token 的方案；
- 更理想的是 **检索相关 facts + 引用 id**（把定理库当知识库，而不是聊天记录）。


