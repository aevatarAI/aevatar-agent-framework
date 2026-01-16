## Vibe Researching（目标：通用科研助手平台）

> 参考 “vibe researching” 的思路：**先把研究推进成可执行的循环**（提出假设 → 检索/引用 → 推理 → 计算验证 → 产出结论/下一步），再逐步把每一步做成可插拔模块。
>
> 原文链接（微信文章）：`https://mp.weixin.qq.com/s/ikOTyk5xKzCi0wAeL-W5eA`

### 1) 三层抽象：资料 / 推理 / 计算

- **Knowledge（资料层）**：负责读取、索引、检索“已知输入”
  - **DAG knowledge nodes**：事实即 DAG 节点（统一真相源）
  - **可沉淀**：当某个结论被 multi-agent 共识 + 可执行验证通过，可写回为新的 DAG 节点
  - 未来演进：对 DAG 节点做 chunker + vector index，把“资料检索”从 lexical 升级到 embedding

- **Reasoning（推理层）**：负责把输入转成结构化推论
  - 目标：可追溯（引用 id）、可视化（graph/state）、可复现（流程步骤）
  - 未来演进：借鉴 `Aevatar.AxiomReasoning` 的“事件投影”思路，把 DAG/Graph 作为 `STATE_SNAPSHOT/DELTA` 推给 UI

- **Compute（计算层）**：负责做可执行验证
  - 数学/物理：Python（符号计算、数值模拟）
  - 生物：Python（序列处理、统计检验）
  - 人文社科：可能更多依赖检索与引用一致性，Compute 可弱化
  - 安全策略：默认关闭（危险工具），显式开启才允许执行

### 2) 多学科平台：Domain Profile（学科画像）

不同学科差异不在“聊天 UI”，而在：

- **默认工具集**：例如数学需要 `python_exec`，历史学更需要引用与来源校验
- **默认 workflow**：例如生物学需要“数据 → 统计 → 可视化”，而数学需要“定义 → 引理 → 证明/反例”
- **输出结构**：例如文学批评更重“观点-证据-文本引用”，物理更重“假设-模型-方程-量纲-仿真”

建议的最小抽象（未来可落地为接口/插件）：

- `IDomainProfile`
  - `Name`
  - `DefaultWorkflow`（plan/reason/verify/summarize）
  - `AllowedTools`（allowlist，危险工具默认禁用）
  - `OutputSchema`（可选：强制结构化输出）

### 3) 当前 MVP 已落地的东西

- **`mode=vibe`**：后端跑 `vibe.materials → vibe.plan → vibe.reason`
- **Materials MVP**：从 DAG knowledge nodes 构建 bounded context 注入
- **Multi-agent MVP**：
  - `VibePlannerAgent`：产出研究计划
  - `VibeReasonerAgent`：基于 materials（DAG facts）推理与引用
- **Workspace State**：通过 `STATE_SNAPSHOT` 推送到前端（UI 有 Workspace 面板）

### 4) 下一步（建议）

- **Notebook-style 索引**：把 DAG 节点写进 MemoryStore + VectorIndex（复用 notebook 的边界/预算思路）
- **Graph 可视化**：像 `Aevatar.AxiomReasoning` 一样输出 DAG，并用 `STATE_DELTA` 做增量更新
- **Compute 沙盒强化**：引入资源限制（CPU/内存/文件系统隔离），或把 python_exec 移到独立 sidecar
- **可复现实验记录**：把每次 run 的 “输入/资料快照/代码/输出” 保存为 artifact（后续可导出 notebook/report）


