# Weaviate《Context Engineering》学习报告

本报告基于 `docs/Weaviate-Context-Engineering-ebook.pdf` 的主要内容，提炼可落地的技术要点，并给出与 `Aevatar.Agents.Cognitive` 结合的设计思路。

---

## 1. 核心定义与问题背景

**Context Engineering** 是“设计一个体系，把正确的信息在正确的时间送入 LLM 的上下文窗口”。其目标不是改变模型本身，而是通过 **检索、工具、记忆与流程编排**，让模型具备真实世界可用的“连接能力”与“延续性”。

### 1.1 Context Window 的硬约束

- LLM 的上下文窗口是“工作内存”，空间有限。
- 超出窗口容量的内容会被挤出，导致重要信息丢失。
- 越大窗口并不意味着越好，窗口膨胀会带来质量下降与噪声积累（信息污染、错误放大）。

---

## 2. Context Engineering 的系统构成

文档强调：系统能力来自 **组件协作** 而非单点模型能力。核心组件包括：

- **Query Augmentation**：把用户输入转为检索可用的高质量查询。
- **Retrieval + Chunking**：从外部知识中选取合适片段并喂入上下文。
- **Memory**：短期/长期/工作记忆分层管理，实现“连续性”。
- **Tools**：把行动能力外包给外部系统（搜索、数据库、文件、API）。
- **Agents**：负责决策、路由、重试与多策略切换。
- **Prompting Techniques**：使模型更稳定地产出可用结果。

---

## 3. 上下文质量管理策略

随着上下文规模增大，错误会以“复用 + 叠加”的形式放大。文档提出以下治理手段：

1. **Quality Validation**：检查检索到的内容是否一致、可信。
2. **Context Summarization**：定期压缩历史，保留关键知识。
3. **Context Pruning**：移除冗余或过期信息。
4. **Context Offloading**：把细节存放到外部系统，仅在需要时取回。
5. **Adaptive Retrieval**：检索失败时重写查询、切换库、调整 chunking。
6. **Dynamic Tool Selection**：只加载与当前任务相关的工具，减少噪声。
7. **Multi-Source Synthesis**：综合多来源信息、消解冲突。

---

## 4. Agents 的角色与分工

文档把 Agent 视为“上下文编排的大脑”，其核心价值在于 **动态决策**：

- 选择最合适的检索策略与数据源
- 判断是否需要重写查询、扩展查询或拆解子问题
- 决定是否压缩历史、切换工具或合并多来源证据

常见角色示意（概念层）：

- Query Rewriter / Query Agent
- Retriever / Data Collector
- Tool Router
- Answer Synthesizer
- Context Compressor / Pruner

---

## 5. Query Augmentation 关键技术

### 5.1 Query Rewriting（重写）

- 目的：把不清晰问题转为结构化、检索友好的表达。
- 价值点：
  - **重结构**：模糊 → 明确
  - **去噪**：删掉无关上下文
  - **补充关键词**：提高检索匹配率
- 典型模式：**rewrite → retrieve → read**

### 5.2 Query Expansion（扩展）

从单一输入生成多个相关查询，扩大覆盖范围。

风险：
- **Query Drift**：偏离原意
- **Over-Expansion**：精度下降
- **Compute Overhead**：延迟增加

### 5.3 Query Decomposition（拆解）

把复杂问题拆成子问题，分别检索/推理。

### 5.4 Query Agents（自治查询）

Query Agent 能根据 **数据 schema** 动态构建查询，并执行：

- 多集合路由（multi-collection routing）
- 动态拼接过滤条件
- 结果评估与再检索
- 可选的最终回答生成

---

## 6. Retrieval 与 Chunking

### 6.1 Chunking 是检索成败的关键

文档将 chunking 视为“检索系统最关键的决策”。

核心矛盾：

- **Retrieval Precision**：块越小，嵌入越精确
- **Contextual Richness**：块越大，语义越完整

最佳方案在于平衡两者：**小而完整的语义单元**。

### 6.2 Chunking 策略矩阵（按复杂度）

常见策略与适用场景：

- **Fixed-Size**：速度快但可能切断语义，可用 overlap 缓解
- **Recursive**：按分隔符层级切分，适合无结构文本
- **Document-Based**：基于结构边界（标题、段落、函数）
- **Hierarchical**：多层级块（摘要 → 章节 → 段落）
- **Late Chunking**：先对整文 embedding，再切块

### 6.3 Pre-Chunking vs Post-Chunking

- **Pre-Chunking**：先切块再 embedding
- **Post-Chunking**：先 embedding 再切块（Late Chunking）

---

## 7. Memory 架构（分层思路）

### 7.1 Short-Term Memory

放在 context window 中的即时信息，主要用于当前推理。

### 7.2 Long-Term Memory

存放在外部系统（常见为向量数据库），包含：

- Episodic Memory（事件/对话）
- Semantic Memory（事实/知识）
- Procedural Memory（流程/方法）

### 7.3 Working Memory

任务中间态的暂存空间，任务完成后可清理。

### 7.4 Hybrid Memory

现实系统通常采用短期 + 长期 + 工作记忆的组合，配合：

- Summarization（压缩）
- Pruning（淘汰）
- Offloading（外置）

---

## 8. Prompting Techniques（提示工程）

文档将 prompt engineering 视为“表达方式优化”，但强调 **离不开高质量上下文**。

常见技术：

- **Chain of Thought (CoT)**：分步推理
- **Few-shot**：给定样例提升稳定性
- **Tree of Thoughts (ToT)**：多路径并行推理
- **ReAct**：推理 + 行动 + 再推理

---

## 9. Tool 使用规范

文档强调：工具描述决定模型是否能正确使用工具。

高质量工具描述的关键要素：

- **Action Verb**：动词开头（例如 `get_weather`）
- **Inputs**：明确参数名称与格式
- **Outputs**：说明返回结构
- **Limitations**：边界条件（区域/时间/权限）

同时建议明确：

- **何时使用工具**
- **如何正确调用**

---

## 10. 对 Aevatar Cognitive 的落地启示

结合现有能力（LLM + 原语 + workflow），可直接实现：

- **Query Augmentation Workflow**：重写/扩展/拆解 → 输出结构化查询集合
- **Chunking Strategy Workflow**：根据文档类型与检索目标给出 chunking 方案
- **Memory Policy Workflow**：定义短期/工作/长期记忆与清理策略
- **Context Orchestration Workflow**：顺序调度上面组件形成完整上下文方案

这些流程可以用 `llm_call + transform + workflow_call` 组合完成，并通过 `agent/role` 指定专用角色。

