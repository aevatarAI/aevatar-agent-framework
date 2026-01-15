## Aevatar Agent Framework 博客写作计划（面向 .NET × AI 爱好者）

> 目标：用“由浅入深、由上到下”的方式，把 Aevatar 讲清楚、讲透彻、讲到读者愿意 **跑起来**、愿意 **在项目里用**、愿意 **贡献代码/文档**。

---

## 0. 写作定位（先对齐“写给谁、解决什么”）

### 0.1 目标读者画像

- **.NET 工程师**：熟悉 DI/Host/Logging，但对 Actor/事件流/分布式 Agent 体系缺少落地路径。
- **AI 工程师/爱好者**：熟悉 LLM/Tool/Prompt，但需要“可扩展、可观测、可治理”的多 Agent 工程化底座。
- **架构/平台工程**：关心跨运行时、可靠性、可观测性、演进策略、性能/成本。

### 0.2 一句话卖点（每篇文章开头都要复用）

- **Write Once, Run Anywhere**：同一份 Agent 代码，切换 Local / ProtoActor / Orleans 运行时不改业务逻辑。
- **Events are the truth**：通信只通过事件；层级传播 Up/Down/Both 把“协作”变成默认能力。
- **Protocol Buffers First（铁律）**：跨边界类型必须 Protobuf，避免分布式序列化地狱。
- **AI 是一等公民**：原生集成 `Microsoft.Extensions.AI`，并具备 Tool Calling / MCP 生态接入路径。

### 0.3 内容可信度策略（避免“营销味”）

- **每篇都给可运行路径**：对应到仓库 `examples/` 或 `apps/`，读者能复现。
- **每篇都给“不适合用”的边界**：例如简单 CRUD、强同步调用为主的系统。
- **每篇都给“为什么这样设计”**：讲清约束（Protobuf、事件流）背后的工程学原因。

---

## 1. 系列结构（宏观→中观→微观→工程化）

### 1.1 文章分层（由上到下）

- **层 A：愿景与模型（Why）**：Actor + 事件驱动为何适合大规模智能体协作？
- **层 B：框架心智模型（What）**：GAgent vs GAgentActor、Stream、EventDirection、边界与约束。
- **层 C：上手与扩展（How）**：写一个 Agent、定义 Proto、运行 Demo、切换运行时、加入 AI 与 Tool。
- **层 D：生产化（Operate）**：可观测性、持久化/事件溯源、测试策略、性能与成本、部署与治理。

### 1.2 三条写作主线（面向不同读者）

- **主线 1：5 分钟跑起来（开发者体验）**
- **主线 2：把“分布式协作”讲明白（架构与约束）**
- **主线 3：把 AI 变成工程系统（工具、记忆、编排、治理）**

---

## 2. 基础写作规范（统一口径，减少返工）

### 2.1 每篇文章固定骨架（强制）

- **开场 30 秒**：一句话卖点 + 本文能获得什么
- **问题/痛点**：不用框架会怎样，现有方案的坑在哪里
- **核心概念（最多 3 个）**：一次只讲 3 个概念，保证可吸收
- **可运行示例**：给命令/路径/截图点（避免“只讲不跑”）
- **工程化落点**：日志/指标/测试/演进（至少选 1 个）
- **延伸阅读**：指向仓库 `docs/` 或下一篇文章

### 2.2 代码与演示约束（与仓库规则一致）

- **跨边界类型必须 Protobuf**：State / Event / EventSourcing Event / Configuration。
- **端口策略**：文档/示例/默认配置不要使用 5000 端口；如需要本地默认端口，优先 `:5678`。
- **Agent 基本规范**：
  - 无参构造函数
  - `OnActivateAsync` 初始化 State 属性（不 `State = new()`）
  - 事件处理器 `async Task`，不阻塞

---

## 3. 文章路线图（由浅入深）

> 说明：每篇都给“目标/关键点/对应仓库素材/读者能跑的东西”。标题可按发布渠道做微调。

### 3.1 Layer A：愿景与模型（Why）

#### A1. 《为什么多 Agent 最终会走向 Actor + 事件驱动？》
- **目标**：把“多智能体协作 = 分布式系统”的工程现实讲清楚
- **关键点**：状态与时间、并发与隔离、事件作为事实、可观测性是底线
- **素材**：`docs/CONSTITUTION.md`、`docs/ARCHITECTURE_REFERENCE.md`
- **产出**：两张图（Agent/Actor 二元结构、事件传播示意）

#### A2. 《Write Once, Run Anywhere：一份 Agent 代码如何跑三种运行时？》
- **目标**：让 .NET 读者产生“这很实用”的第一印象
- **关键点**：Local/ProtoActor/Orleans 的取舍，切换仅一行配置
- **素材**：根目录 `README.md` / `README_zh.md` 的 runtime 对比表
- **可跑**：跑 `examples/SimpleDemo/`

### 3.2 Layer B：框架心智模型（What）

#### B1. 《GAgent vs GAgentActor：把业务与基础设施彻底分开》
- **目标**：建立框架最重要的“二元分层”心智模型
- **关键点**：业务只写 GAgent；Actor 负责生命周期/路由/分布式
- **素材**：`docs/AEVATAR_FRAMEWORK_GUIDE.md`（Core Concepts）

#### B2. 《第一铁律：为什么跨边界必须 Protobuf？》
- **目标**：把“约束”说成“护城河”，让读者不再抵触
- **关键点**：序列化一致性、跨运行时/跨语言、演进策略（只加字段不改号）
- **素材**：`AGENTS.md`、根目录 `README_zh.md`
- **可跑**：挑一个示例里的 `.proto`，演示 build 自动生成

#### B3. 《事件传播 Up/Down/Both：父子层级如何塑造协作网络？》
- **目标**：让读者理解“协作不是 if/else，而是传播拓扑”
- **关键点**：Parent Stream、Sibling 广播、向上/向下/双向语义
- **素材**：`AGENTS.md`（事件传播方向）、相关实现（后续可加源码阅读篇）

### 3.3 Layer C：上手与扩展（How）

#### C1. 《5 分钟写一个 Agent：从 Proto 到第一个事件处理器》
- **目标**：最短路径形成正反馈
- **关键点**：`.proto` → `GAgentBase<TState>` → `[EventHandler]` → `PublishAsync`
- **素材**：`README_zh.md` Quick Start、`examples/SimpleDemo/`

#### C2. 《从“能跑”到“能维护”：状态初始化、幂等与错误路径》
- **目标**：把 Demo 升级成工程代码
- **关键点**：`OnActivateAsync`、幂等处理、失败也可观测
- **素材**：`AGENTS.md`（开发规范/常见错误）

#### C3. 《运行时选择指南：Local / ProtoActor / Orleans 该怎么选？》
- **目标**：给架构决策一个清晰、可复用的答案
- **关键点**：吞吐/延迟/内存/启动时间；开发/性能/分布式三类场景
- **素材**：`README_zh.md` runtime 对比表

### 3.4 Layer C+：AI（把 AI 从“脚本”变成“系统”）

#### AI1. 《把 LLM 变成 Agent：Microsoft.Extensions.AI 在 Aevatar 里的正确打开方式》
- **目标**：让 AI 爱好者找到熟悉的入口
- **关键点**：IChatClient、对话历史、流式输出、成本治理的钩子
- **素材**：`README_zh.md` AI 章节、`src/Aevatar.Agents.AI.*`
- **可跑**：选择一个 AI demo（以仓库实际 demo 为准，发布前验证命令）

#### AI2. 《Tool Calling 与 MCP：如何把工具生态接到你的 Agent 网络》
- **目标**：展示“工具生态”不是拼脚本，而是可治理的能力面
- **关键点**：schema 驱动、调用链路可观测、失败重试与隔离
- **素材**：`examples/MCPToolDemo/`、`examples/AIAgentWithToolDemo/`（如存在）

#### AI3. 《多 Agent 编排的工程化：从 MAKER 到 Cognitive Mesh（从 demo 到系统）》
- **目标**：把“多智能体协作”落到可运行的大系统
- **关键点**：分解/协调/执行、事件流驱动的编排、可扩展的执行内核
- **素材**：`apps/Aevatar.MakerSystem/examples/MakerSystem/`、`apps/Aevatar.CognitiveMesh/README.md`

### 3.5 Layer D：生产化（Operate）

#### O1. 《可观测性不是锦上添花：OpenTelemetry + Aspire 怎么接》
- **目标**：把“调试多 Agent”从玄学变成工程
- **关键点**：trace/metric/log correlation、关键指标（吞吐、延迟、失败率、订阅数）
- **素材**：`docs/OBSERVABILITY.md`、各 `*.AppHost` 示例

#### O2. 《事件溯源（EventSourcing）：什么时候用、怎么用、不该怎么用》
- **目标**：给复杂业务一个可审计/可回放的路径
- **关键点**：事件作为事实、重放、版本演进策略、存储选择
- **素材**：`examples/EventSourcingDemo/`、`README_zh.md` EventSourcing 章节

#### O3. 《测试策略：如何测试事件处理、传播拓扑与跨运行时一致性》
- **目标**：让框架在团队协作中“可持续”
- **关键点**：handler 发现、Parent-Child 关系、Up/Down/Both、Happy/Error Path
- **素材**：`test/` 下对应测试项目（发布前挑 1-2 个代表性测试讲解）

#### O4. 《性能与成本：高并发事件系统的常见坑与 Aevatar 的解法》
- **目标**：把性能话题从“跑分”变成“工程决策”
- **关键点**：状态大小、序列化开销、订阅管理、背压与限流、批处理
- **素材**：runtime 对比、相关最佳实践文档（发布前补引用）

---

## 4. 发布节奏（建议）

### 4.1 最小可行节奏（MVP）

- **2 周 / 6 篇**：A1/A2 + B1/B2 + C1 + AI1  
- 目标：让读者“能跑 + 能理解约束 + 知道下一步怎么扩展”

### 4.2 完整系列（建议）

- **6–8 周 / 12–16 篇**：覆盖 B3/AI2/AI3/O1/O2/O3/O4，并穿插 1–2 篇源码导读

---

## 5. 素材清单（写作时直接引用/链接）

- **入门必读**：`README_zh.md`、`docs/AEVATAR_FRAMEWORK_GUIDE.md`
- **设计哲学**：`docs/CONSTITUTION.md`
- **深度架构**：`docs/ARCHITECTURE_REFERENCE.md`
- **工程规则**：`AGENTS.md`
- **可观测性**：`docs/OBSERVABILITY.md`
- **示例入口**：`examples/SimpleDemo/`、`examples/EventSourcingDemo/`、`apps/Aevatar.MakerSystem/examples/MakerSystem/`、`examples/MCPToolDemo/`（以实际为准）

---

## 6. 变更日志

- 2026-01-06：初始化博客写作计划（建立分层结构与文章路线图）。


