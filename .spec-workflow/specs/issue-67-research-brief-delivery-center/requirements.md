# Requirements Document

## Introduction

本 spec 在现有 **Vibe Researching Platform**（`scientific-research-assistant` 子系统）基础上，补齐客户在 [issue #67](https://github.com/aevatarAI/aevatar-agent-framework/issues/67) 中提出的关键体验：**研究简报（1页）→ 多轮并行调研 →（可选）程序运算确认/进度/快照 → 交付物中心更新 → 下一轮选项**。

核心新增点：
- **研究简报（Brief）**：用户输入方向后立刻回传 1 页“可执行研究问题”+默认假设/风险/里程碑，并提供“继续/纠偏”最小交互。
- **交付物中心（Delivery Center）**：论文（主叙事）+ 三类清单（结论卡/证据对照/下一步任务）作为多 agent 协作骨架，持续迭代且始终可读。
- **Compute 任务体验**：当 verifier 判定必须进行程序运算/实验时，以“执行计划卡”对用户分层确认，并在运行期间持续输出状态/日志摘要/中间快照。
- **DAG ↔ 论文互转**：引入 `paper_editor`（单写入者）将 DAG 变化合并进论文/清单，并从论文/清单抽取结构化内容反哺 DAG。

## Alignment with Product Vision

该能力对齐 steering 的产品原则与目标：
- **Events are Truth**：所有阶段输出（Brief/Compute/Delivery 更新）都通过事件流（AG-UI SSE）对前端可观测，避免隐藏状态。
- **Boundary Types Must Be Protobuf**：所有跨边界的消息（Brief/Compute/Delivery 的结构化事件/快照）以 Protobuf 定义并可演进。
- **Runtime Agnostic by Design**：能力实现落在 `scientific-research-assistant` 子系统的 File-SSoT + Orchestrator，不引入运行时绑定。

## Requirements

### Requirement 1 — 研究简报（1页）与纠偏入口

**User Story:** 作为用户，我希望在输入研究方向后立刻看到一页研究简报，以便确认系统理解了方向并能在跑偏前快速纠偏。

#### Acceptance Criteria

1. WHEN 用户在 `mode=vibe` 下提交“方向/边界/成功标准” THEN 系统 SHALL 生成一份 **Research Brief**，包含：研究问题改写、范围界定、关键术语解释、默认假设清单、风险清单、不确定点清单、里程碑路线图（按轮次预告产物）。
2. WHEN Research Brief 生成完成 THEN 系统 SHALL 通过 AG-UI SSE 推送可渲染的 brief 结构化事件，并在 UI 中以卡片呈现（无需刷新页面）。
3. IF 用户选择“纠偏”并修改范围/假设/边界 THEN 系统 SHALL 将该修改写入 File-SSoT（可追溯），并在下一轮计划中强制体现修改后的约束。

---

### Requirement 2 — 多轮 Round N 的并行推进与时间流逝感

**User Story:** 作为用户，我希望系统在每一轮中并行推进计划、证据收集与推理，并持续输出进度，让我有掌控感但不被打扰。

#### Acceptance Criteria

1. WHEN Round N 开始 THEN 系统 SHALL 产生一个 Round 级别的执行计划（含并行 worker 任务）。
2. WHEN worker（planner/librarian/reasoner/verifier/dag_builder/paper_editor）输出增量 THEN 系统 SHALL 以 streaming 方式追加到 UI，且不会导致整页刷新或全量重渲染。
3. WHEN Round N 执行期间 THEN 系统 SHALL 提供可折叠的“当前在做什么/下一产物”面板，并允许用户随时中断并改方向。

---

### Requirement 3 — Compute 任务的分层确认（执行计划卡）

**User Story:** 作为用户，我希望系统只在必要时打扰我确认程序运算，并用一张执行计划卡让我一键做决定。

#### Acceptance Criteria

1. WHEN verifier 判定需要 Compute（程序运算/实验） THEN 系统 SHALL 生成“执行计划卡”，包含：要算什么、为什么必须算、预期产物、成本级别、风险点、失败替代方案、推荐选项。
2. IF Compute 属于“低成本低风险”且策略允许自动执行 THEN 系统 SHALL 自动执行并将该决策记录为 File-SSoT。
3. IF Compute 成本/风险显著或可能改变研究方向 THEN 系统 SHALL 要求用户确认（执行/降级/暂不执行），并将选择写入 File-SSoT。

---

### Requirement 4 — Compute 运行期间的持续输出（状态/日志摘要/中间快照）

**User Story:** 作为用户，我希望长任务不黑箱：运行期间系统持续输出状态与异常预警，并提供中间结果快照（若有）。

#### Acceptance Criteria

1. WHEN Compute 运行中 THEN 系统 SHALL 在 UI 中持续更新状态时间线（阶段解释、进度/心跳、异常预警）。
2. WHEN Compute 运行中 THEN 系统 SHALL 允许系统并行推进其它工作（继续找证据、准备两套结论分支等）并在 UI 中可见。
3. IF Compute 产生中间结果快照 THEN 系统 SHALL 将快照落盘并在 UI 中提供可点击查看的引用。

---

### Requirement 5 — 交付物中心（论文 + 结论卡/证据对照/任务清单）

**User Story:** 作为用户，我希望系统每轮结束都更新“交付物中心”，我只看论文就能把握全局，想深挖时再点开结论卡与证据对照。

#### Acceptance Criteria

1. WHEN Round N 结束 THEN 系统 SHALL 更新交付物中心文件集合：论文草稿、结论卡清单、证据对照清单、下一步任务清单，并保证可读且 bounded。
2. WHEN 交付物中心更新 THEN 系统 SHALL 通过 SSE 推送“交付物更新事件”（包含变更摘要与文件路径引用），UI 以卡片/面板展示。
3. IF 论文越来越长 THEN 系统 SHALL 优先在清单中对齐协作，论文仅做高质量合并更新（避免每轮把论文写成日志）。

---

### Requirement 6 — DAG ↔ 论文互转的 `paper_editor`（单写入者）

**User Story:** 作为系统（多 agent 协作），我希望有一个单写入者 agent 负责 DAG 与论文/清单的互相转换，以避免多人并发写同一份稿件导致冲突与失控。

#### Acceptance Criteria

1. WHEN DAG 共识通过并写入 snapshot THEN 系统 SHALL 触发 `paper_editor` 将本轮 DAG 变化合并进论文与交付物清单（通过可审计的 patch 方式）。
2. WHEN 论文/清单产生结构化结论（结论卡/证据对照） THEN 系统 SHALL 允许 `paper_editor`/`dag_builder` 抽取并生成 DAG mutation 候选（仍走 maker-v2 共识 gate）。
3. IF 非 `paper_editor` agent 需要修改论文 THEN 系统 SHALL 只能通过 mailbox 提交 patch proposal，禁止直接写文件（单写入者约束）。

---

### Requirement 7 — HITL 中断语义与 goals 对齐

**User Story:** 作为用户，我希望我的消息像 CPU interrupt 一样能随时改变研究方向，并让所有 agent 重新校准。

#### Acceptance Criteria

1. WHEN 用户更新 goals THEN 系统 SHALL 将更新以 Protobuf mailbox 广播给所有 worker agents，并在下一轮计划中体现。
2. IF goals 为空 THEN `research_assistant` SHALL 在计划阶段自动初始化 goals；IF 发现新的 goals THEN 系统 SHALL 在 summary 阶段向用户确认后再修改（除非是空 goals 的 bootstrap）。

## Non-Functional Requirements

### Code Architecture and Modularity
- **Single Responsibility Principle**：`paper_editor`、Compute runner、Delivery store 等分离为清晰模块；避免在 `VibeOrchestrator` 内无限膨胀。
- **Modular Design**：Brief/Compute/Delivery 各自可独立演进；文件落盘为 SSoT，SSE/内存为投影。
- **Clear Interfaces**：跨边界数据必须 Protobuf；文件引用用相对路径并做路径安全校验。

### Performance
- 所有 UI 输出必须 streaming 增量更新；追加 token 不得触发全量重渲染。
- 大文本优先落盘，事件仅传路径与摘要；读取扫描必须 bounded。

### Security
- 文件写入必须防路径穿越、限制后缀/大小、原子写；危险工具（如执行代码）必须分层确认与策略控制。

### Reliability
- best-effort：单个 agent/工具失败不应导致 session 崩溃；应可继续下一轮或给出降级路径。
- 所有关键决策（brief/compute/confirm/delivery）必须可追溯（文件或共识 artifacts）。

### Usability
- 用户最小动作：输入方向后只需“继续/纠偏”；Compute 只在必要时弹卡片，且是“一张卡三种选项一句推荐理由”。
- 交付物中心始终可见且可快速跳转，不被聊天内容淹没。


