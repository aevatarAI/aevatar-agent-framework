# Competitive Planning in Cognitive

本文档规划一种“竞争 + 不信任 + 责任分块”的多 Agent 计划机制，目标是让计划
由多个角色在正反对抗后达成“平衡状态”，而不是单一 Agent 的主观想象。

---

## 1) 目标与非目标

### 目标
- 引入**刚性责任边界**：每个计划段落由唯一 Owner 负责产出与迭代。
- 引入**对抗机制**：Critic 必须提出反驳与证据，驱动质量提升。
- 引入**多 Agent 平衡**：通过 Arbiter 投票/打分，形成最终决策。
- 保持**事件驱动**与**模块化装配**，与现有 RoleAIGAgent + IEventModule 机制兼容。

### 非目标
- 不引入新运行时或 UI。
- 不改变 Cognitive DSL 语义。
- 不替代现有 Coordinator 的 workflow 执行，仅提供“计划生成/决策”能力。

---

## 2) 核心约束

- **Protobuf-first**：所有跨边界消息与状态必须用 `.proto` 定义。
- **事件驱动**：模块之间通过事件协作，不引入直接调用链。
- **责任隔离**：Owner 能产出 proposal，但 Critic 不能改写 Owner 结论。

---

## 3) 角色模型（不信任 + 责任）

| 角色 | 责任 | 权限 |
|---|---|---|
| Owner | 负责某段落的方案产出与修订 | 可发布 Proposal/Rebuttal |
| Critic | 负责反驳与风险发现 | 只能发布 Critique |
| Arbiter | 负责裁决 | 可发布 Decision/Score |
| Synthesizer | 合并裁决为最终计划 | 可发布 Synthesis |

**刚性规则**：
- 每个 PlanSegment 只有 1 个 Owner。
- Critic 只能挑战，不得直接产出最终内容。
- 只有 Arbiter 能决定“采用/合并/驳回”。

---

## 4) 事件与状态（Protobuf）

建议新增以下 proto（名称可调整）：

### 状态
- `PlanCompetitionConfig`：k、max_rounds、角色配额、评分权重等。
- `PlanSegment`：segment_id、owner_id、title、constraints、status。

### 事件
- `PlanCompetitionStartEvent`
- `PlanProposalRequestEvent` / `PlanProposalEvent`
- `PlanCritiqueRequestEvent` / `PlanCritiqueEvent`
- `PlanRebuttalRequestEvent` / `PlanRebuttalEvent`
- `PlanScoreEvent`
- `PlanDecisionEvent`
- `PlanSynthesisEvent`
- `PlanDisputeEvent`（可选，用于记录未达成共识的争议）

> 所有事件需放在 `.proto`，并在跨 Agent 与 Stream 中传递。

---

## 5) 工作流（建议最小闭环）

1. **Segmenting**：Coordinator 根据输入拆分 PlanSegment，并分配 Owner/Critic。
2. **Proposal**：Owner 输出 `PlanProposalEvent`。
3. **Critique**：Critic 输出 `PlanCritiqueEvent`。
4. **Rebuttal**（可选）：Owner 回应 Critic。
5. **Arbitration**：Arbiter 通过 vote/score 做裁决。
6. **Synthesis**：Synthesizer 输出 `PlanSynthesisEvent`，包含争议点列表。

---

## 6) 模块划分与装配

采用 EventModule 插件化方式，避免侵入 RoleAIGAgent：

- `plan_owner`：处理 ProposalRequest/RebuttalRequest。
- `plan_critic`：处理 CritiqueRequest。
- `plan_arbiter`：处理 Decision/Score，内部可调用 `coordinator_vote`。
- `plan_synth`：合并 Decision，产出 Synthesis。

**EventModule 装配点**：`RoleAgentFactory` + YAML `extensions.event_modules`。

---

## 7) YAML 装配示例

```yaml
extensions:
  event_modules: "plan_owner,plan_critic,plan_arbiter,plan_synth"
  event_routes: |
    - when: event.type == "PlanProposalRequestEvent"
      to: "plan_owner"
    - when: event.type == "PlanCritiqueRequestEvent"
      to: "plan_critic"
    - when: event.type == "PlanArbitrationRequestEvent"
      to: "plan_arbiter"
    - when: event.type == "PlanSynthesisRequestEvent"
      to: "plan_synth"
```

---

## 8) 规划落地步骤（阶段性）

### Phase 0: Proto 设计
- 定义 `PlanSegment` / `PlanCompetitionConfig` / 事件消息。
- 确保字段可扩展（兼容新增字段）。

### Phase 1: 模块骨架
- 实现 `plan_owner / plan_critic / plan_arbiter / plan_synth` 模块。
- 先写最小逻辑：拼接提示词 + 发布事件。

### Phase 2: Arbitration 接入
- Arbiter 内部接入 `VoteEngine` 或 `coordinator_vote`。
- 输出 `PlanDecisionEvent`（winner、reason、confidence）。

### Phase 3: Workflow 编排
- Coordinator 负责分段与派发 Request 事件。
- 支持并行执行多个 segment 的竞争流程。

### Phase 4: Demo 落地
- 在 `examples/ProgressHookChatWebDemo` 接入最小流程。
- 通过 ExecutionTrace 输出进度与争议点。

---

## 9) 测试与验收

- **模块路由**：`event_routes` 能正确投递到目标模块。
- **责任约束**：Critic 不能发布 Decision；Owner 不能裁决。
- **共识稳定性**：vote/score 逻辑在多轮输入下可复现结果。
- **无模块回退**：未配置模块时，RoleAIGAgent 行为不变。

---

## 10) 风险与对策

- **对抗失控**：引入 max_rounds + timeout。
- **仲裁偏差**：多仲裁人 + vote 提升稳健性。
- **成本上升**：支持 segment 级别配置，控制并发与 K 值。

---

## 11) 开放问题

- 仲裁策略默认用 vote 还是 rule-based 打分？
- Critic 的数量与角色配置是否可动态调整？
- 是否需要“争议升级”通道（人工仲裁 / 高权重 agent）？

