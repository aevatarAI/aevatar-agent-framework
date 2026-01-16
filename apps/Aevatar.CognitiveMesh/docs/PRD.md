# 产品需求文档: Cognitive Mesh (Aevatar Nexus)

> **状态**: 草案 (DRAFT)
> **目标**: Aevatar Agent Framework v2.0
> **负责人**: Linus (Virtual)

## 1. 执行摘要
**Cognitive Mesh (认知网格)** 是一个声明式、有状态的认知架构构建器。它允许用户使用高级自然语言指令定义复杂的、自我纠正的、进化的 AI 系统。它将 "提示工程 (Prompt Engineering)" 转化为 "**认知架构工程 (Cognitive Architecture Engineering)**"。

## 2. 问题定义
*   **现状**: AI "工作流" 是脆弱的 DAG（有向无环图）。如果一步失败，链条就会断裂。上下文会丢失。复杂性受限于上下文窗口。
*   **愿景**: 一个由持久化 Actor 组成的健壮 **网格 (Mesh)**，其中 "思维" 是 Agent 交互的涌现属性。系统通过状态持久化和层级分解支持无限步推理。

## 3. 用户故事

### 3.1 "架构师" 用户
> "作为系统设计者，我想说 '建立一个 3 阶段的批评循环'，然后系统自动生成所需的 Actor 和连接。"

### 3.2 "深度思考者" 用户
> "作为研究人员，我想让系统探索 10,000 种可能的化学组合（探索式 UoT），并只报告通过特定模拟过滤器的组合。"

### 3.3 "操作员" 用户
> "作为操作员，我想在第 500,000 步暂停一个百万步的任务，检查状态，调整参数，并在不丢失数据的情况下恢复。"

## 4. 功能需求

### 4.1 认知 DSL (领域特定语言)
系统必须将自然语言解析为结构化定义 (JSON/YAML)，包含：
*   **节点 (Nodes)**: Agent 类型 (如 `发散思考者`, `收敛总结者`, `代码执行者`)。
*   **边 (Edges)**: 通信协议 (如 `辩论`, `投票`, `流式传输`)。
*   **约束 (Constraints)**: 退出条件 (如 "置信度 > 0.9")。

### 4.2 "宇宙" (状态存储)
*   实现共享的 "思维空间 (Thought Space)"，Agent 可以在其中发布和订阅想法。
*   **要求**: 必须使用 Aevatar 的事件溯源 (Event Sourcing) 以确保零数据丢失。
*   **特性**: "时间旅行" 调试 (重放思维过程)。

### 4.3 思考策略 (可插拔内核)
开箱即用支持：
*   **CoT (思维链)**: 线性执行。
*   **ToT (思维树)**: 分支与剪枝。
*   **GoT (思维图)**: 分支重组。
*   **UoT (思维宇宙)**: 
    *   *组合式*: RAG + 合成。
    *   *探索式*: 基于 Agent 的并行蒙特卡洛树搜索。
    *   *变革式*: 元提示词 (Meta-prompt) 重写。

### 4.4 零错误保证 (百万步承诺)
*   **原子步骤**: 每个推理步骤都是一个事务。
*   **主管模式 (Supervisor Pattern)**: "经理 Agent" 验证 "工人 Agent" 输出的层级结构。
*   **自愈 (Self-Healing)**: 如果 Agent 崩溃或产生幻觉（未通过验证），它会被重置并以调整后的温度或提示重试。

## 5. 非功能需求
*   **延迟**: 不关键 (系统 2 思维本身就是慢的)。
*   **吞吐量**: 高并发 (数千个并行思维分支)。
*   **成本**: 必须包含 "Token 预算" 监控器，防止失控的推理循环。

## 6. 界面设计
*   **输入**: 聊天界面 ("架构师")。
*   **可视化**: 活跃 Actor (节点) 和消息传递 (边) 的实时图视图。
*   **控制**: 暂停/播放/倒带控制。

## 7. 用户交互旅程
1.  **构思 (Prompt-to-Mesh)**：用户用自然语言描述任务。Cognitive Compiler 将其转译成 DSL，列出节点、策略与预算。
2.  **拓扑审阅**：系统给出可视化蓝图，允许用户在图上调整节点、设置 UoT 模式或附加百万步约束。
3.  **运行控制**：启动后由 Actor Runtime（Orleans）托管，各节点状态实时回传到操作面板，可随时暂停 / 回滚 / 分叉。
4.  **回放与学习**：每次运行生成审计日志，可供下一次 DSL 生成时引用，形成“提示 → 拓扑 → 运行 → 复用”的闭环。

## 8. 服务能力矩阵
- **Mesh Compiler API**：把自然语言编译为受限 DSL；暴露版本化 Schema、语义校验结果与建议。
- **Strategy Template Catalog**：内置 UoT（组合/探索/变革）模板，可一键套用论文中的工作流，也允许自定义。
- **Runtime Control Plane**：提供 Orleans Actor 的生命周期管理、健康检查、扩缩容与 Million-Step Checkpoint。
- **Observability & Replay**：事件溯源 + 语义日志，支持以思维节点为单位回放、Diff、导出。
- **Integration Hooks**：暴露 Webhook / gRPC / SDK，让外部系统可以提交 DSL、接收进度或注入自定义 Agent。

## 9. Actor Runtime 与 Aspire Host
- **CognitiveMesh.App**：运行在 Orleans/Actor Runtime 之上的服务骨架，负责加载 DSL、调度 Aevatar Agents，并在后台服务中执行自愈/重试。
- **CognitiveMesh.AppHost (Aspire)**：通过 `DistributedApplicationBuilder` 将 CognitiveMesh.App、Orleans Silo、向量存储、可观测性栈统一编排，可根据租户动态扩缩容。
- **部署模式**：开发者可在本地运行 AppHost 获取完整体验，生产环境可映射到 Kubernetes / AKS，仅需切换 Aspire 配置。
- **研究论文映射**：
  - *Universe of Thoughts*: 通过 Strategy Template Catalog 映射到具体 Agent 拓扑，并由 AppHost 注入所需检索/自适应模块。
  - *Solving a Million-Step LLM Task*: 由 App 提供事务化执行与检查点，AppHost 负责横向扩展、故障转移与监控。

## 10. 路线图
1.  **阶段 0 (Foundry)**：统一文档与 DSL 编译器，输出模板示例库，确保 Prompt-to-Mesh 的 determinism。
2.  **阶段 1 (Synapse)**：让 CognitiveMesh.App + AppHost 可运行，最小化集成 Aevatar Agents + Orleans（仅组合式 UoT）。
3.  **阶段 2 (Cortex)**：扩展策略目录（探索式/变革式）、引入策略市场与 DSL 回溯自修能力。
4.  **阶段 3 (Hive)**：完成操作界面、全局监控、时间旅行调试，并开放 API / SDK 给第三方产品。

