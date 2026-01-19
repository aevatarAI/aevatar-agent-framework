# Cognitive Mesh × WEEX AI Wars Trade System — Plan (v0)

> 目标：把 `apps/Aevatar.Trading/` 里的多 Agent 交易系统升级为 **Cognitive Mesh 驱动的协作决策系统**，满足 WEEX AI Wars 的“自动交易 + 上传 AI Log”比赛形态。  
> WEEX AI Wars API 入口：`https://www.weex.com/api-doc/ai/intro`

---

## 1. 现状（已存在的基线）

`apps/Aevatar.Trading/` 目录已经具备一条完整的事件链路：

- **数据面**：`DataCollectorAgent`（REST + WebSocket）
- **分析面**：`MarketSentimentAgent` / `TechnicalAnalystAgent`（LLM + 指标）
- **决策面**：`TradingCoordinatorAgent`（融合 + 结构化 JSON 输出）
- **风控面**：`RiskManagerAgent`（硬规则 + LLM 辅助）
- **执行面**：`ExecutorAgent`（WEEX 下单/撤单/查单）
- **契约**：`trade_messages.proto` 已覆盖核心事件与 State（Protobuf ✅）

这条链路的优势是“能跑”；不足是“决策脑仍是单点（Coordinator）”，且**与 Cognitive Mesh 的并行投票/递归 workflow 没打通**。

---

## 2. 目标架构（把“决策脑”换成 Cognitive Mesh）

我们把系统拆成四层，强制职责边界清晰（减少 if/else、减少耦合）：

```
┌─────────────────────────────────────────────────────────────┐
│ Data Plane   数据面：行情/账户/仓位/订单                       │
│   DataCollectorAgent + WeexContractApiClient/WeexSpotApiClient + WeexWebSocketClient │
├─────────────────────────────────────────────────────────────┤
│ Cognition     认知面：并行思考→共识→产出结构化决策              │
│   Cognitive Mesh (Maker / UoT / Cognitive DSL)                │
├─────────────────────────────────────────────────────────────┤
│ Control        控制面：硬风控、熔断、模式切换、参数热更新         │
│   RiskManagerAgent + Config + KillSwitch                      │
├─────────────────────────────────────────────────────────────┤
│ Observability  观测面：trace/log/memory + AI Wars log upload   │
│   TradeAudit/Trace + Weex AI Log Uploader                      │
└─────────────────────────────────────────────────────────────┘
```

核心变化只有一个：**TradingCoordinatorAgent 不再直接“自己想”，而是触发一次 Cognitive Mesh 的 workflow run，产出决策 JSON → 映射为 `TradingDecisionEvent`。**

---

## 3. Agent 协作设计（Trade Agents × Mesh Agents）

### 3.1 外围 Agent（保留/微调）

- **`DataCollectorAgent`（保留）**
  - 只做：采集、标准化、广播（绝不夹杂“策略判断”）
- **`RiskManagerAgent`（保留 + 强化硬规则）**
  - 只做：硬规则闸门 + 风控参数化 + 熔断（LLM 只能建议，不能绕过硬规则）
- **`ExecutorAgent`（保留 + 增加 DryRun）**
  - 支持两种模式：
    - `DryRun`: 只生成“拟下单记录”事件，不触达交易所
    - `Live`: 真正调用 WEEX 下单接口

### 3.2 Mesh 内部 Agent（由 Cognitive Mesh 创建/管理）

以 `maker`（或自定义 trading workflow）为骨架，实现“并行提案 → 投票共识 → 产出决策”：

- **Worker（多名）**：每个 Worker 扮演不同角色（技术/情绪/宏观/反身性/对手盘）
- **Vote/Red-Flag**：把“胡说/不合规/输出不合法 JSON”当作红旗自动重试
- **Coordinator（Mesh 的协调者）**：合并结果为最终结构化输出

> 实战建议：**先用 MakerStrategy（voting）跑通可控闭环**，再考虑 UoT/Cognitive DSL 的更复杂工作流。

---

## 4. 事件与 Protobuf 契约（必须遵守跨边界 Protobuf）

现有 `trade_messages.proto` 已覆盖核心交易事件。为了对接 AI Wars 与 Cognitive Mesh，我们需要新增（计划）：

- **决策周期事件**
  - `DecisionCycleStartedEvent`（包含 symbol、触发原因、关键行情快照）
  - `DecisionCycleCompletedEvent`（包含 decision_id、耗时、是否执行）
- **审计/日志上传事件（AI Wars）**
  - `AiWarsLogUploadRequestedEvent`（trace/log bundle 元信息）
  - `AiWarsLogUploadSucceededEvent` / `AiWarsLogUploadFailedEvent`

原则：

- 任何会通过 Stream 广播/跨 runtime 的数据：**一律 Protobuf message**
- Protobuf 字段避免 `decimal`：价格/数量用 `double` + 明确精度策略（例如 price_tick/qty_step 由配置给出）

---

## 5. Cognitive Mesh Workflow 设计（最小可跑版本）

### 5.1 第 0 版：直接复用 `maker`

输入：

- `task`: “根据市场上下文做 BUY/SELL/HOLD 决策，输出严格 JSON”
- `context`: 当前 tick、kline 指标、仓位、风险限制、比赛规则

输出：

- `solution`: 一个严格 JSON（包含 direction/confidence/position_pct/reasoning）

落地方式：

- 在 TradeSystem 的决策点调用 `MakerStrategy.ExecuteAsync(...)` 或 `CognitiveStrategy.ExecuteAsync(...)`
- 把 `solution` 映射为 `TradingDecisionEvent`

### 5.2 第 1 版：新增专用 workflow（trade-maker-v1.yaml）

后续我们会在 `apps/Aevatar.Trading/` 内增加一个专用 workflow（复用 DSL 原语，但用交易 prompt）：

- `fan_out`: 并行生成 3~5 个候选动作（每个动作都要给出止损/止盈/仓位建议）
- `vote`: 通过一致性 + 红旗机制挑选最稳健方案
- `llm_call`: 最终把结果“压缩”为 **严格 JSON**（可直接解析）

> 这一步的价值：把“交易风控输出格式”固定成 DSL 级约束，减少在 C# 里写特殊分支。

---

## 6. WEEX AI Wars 适配（API + 上传 AI log）

比赛需要的不只是交易，还要**上传 AI log**：

- **API 面**：
  - `Market/Account/Trade`：已拆分为 `WeexContractApiClient`（AI Wars 合约）与 `WeexSpotApiClient`（Spot），通过 `Weex:Mode` 选择
  - `Upload AI log`：计划新增 `WeexAiWarsLogClient`（独立于交易 client，避免污染交易调用栈）
- **日志面**：
  - 用框架的 `ExecutionTrace` / 决策事件链生成可上传的 log payload
  - **一切可重放**：同样的输入→同样的决策输出（为赛后复盘、debug 服务）

---

## 7. 实施路线图（从能跑到能赢）

### Phase A：先把闭环“可控”

- [ ] `ExecutorAgent` 增加 `DryRun` 模式（默认 DryRun）
- [ ] `RiskManagerAgent` 强化硬规则 & KillSwitch（配置热更新）
- [ ] 新增 `TradeAuditAgent`：订阅关键事件，落盘/输出 trace bundle

### Phase B：接入 Cognitive Mesh

- [ ] 新增 `TradeMeshDecisionService`（或 `MeshDecisionAgent`）：把“决策”改成 workflow run
- [ ] 复用 `maker` 跑通（最小改动）
- [ ] 定义 `trade-maker-v1.yaml`（固定输出 JSON schema）

### Phase C：AI Wars 对齐与上传日志

- [ ] 核对 AI Wars API 与现有 spot API 的差异（路径/签名/字段）
- [ ] 新增 `Upload AI log` client + 事件驱动上传
- [ ] 演示脚本：从行情→决策→风控→执行→上传 log

---

## 8. 品味自检（防止系统烂掉）

- 能消失的分支 > 能写对的分支：把“输出格式/重试/红旗”放到 workflow DSL 层解决
- 单一真相源：**决策只从 Mesh 输出产生**，外围 Agent 不再偷偷“做判断”
- 安全第一：默认 DryRun，任何 Live 需要显式配置 + 风控闸门


