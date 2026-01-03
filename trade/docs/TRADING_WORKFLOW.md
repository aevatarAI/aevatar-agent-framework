# 多智能体自动交易 Workflow（面向客户）

> 目标：用 **可解释、可审计、可控** 的方式，把“盯盘 + 手工下单”升级为“多智能体协作的自动交易闭环”，并且能清楚回答：**谁在什么时候、基于什么证据、通过什么机制下单**。

---

## 1) 为什么客户应该用它，而不是自己盯盘/换个 Bot？

- **把“决策”拆成可验证的分工**：情绪/技术/（可扩展：新闻）分别产出结构化结论，最终由协调员做综合决策，风控拥有否决权。
- **把“下单”从 LLM 的不确定性里剥离出来**：LLM 负责推理与建议；**真正触达交易所 API 的只有执行器（Executor）**，保证行为可控、可复盘、可兜底。
- **每一笔交易都有“证据链”**：系统会落盘 JSONL（机器可读）+ Markdown（人类可读）策略日志，展示“AI 为什么这么做”。
- **默认安全演进**：DryRun 先跑闭环（决策→风控→模拟成交→审计），再切 Live（真实挂单）。

---

## 2) 角色分工（谁负责什么）

| Agent | 本质职责 | 输入事件 | 输出事件 | LLM | 工具（Tool）使用 |
|---|---|---|---|---:|---|
| `DataCollectorAgent` | 市场数据“感知层”（REST polling） | 定时轮询 WEEX 行情 | `MarketTickEvent` / `KlineUpdateEvent` | 否 | 不需要 |
| `MarketSentimentAgent` | 情绪解读（LLM + 指标） | `MarketTickEvent`（聚合后触发） | `MarketSentimentAnalysisEvent` | 是 | 默认不需要（可扩展：读行情/读账户的只读技能） |
| `TechnicalAnalystAgent` | 技术解读（指标 + LLM） | `KlineUpdateEvent`（聚合后触发） | `TechnicalAnalysisEvent` | 是 | 默认不需要（可扩展：读行情的只读技能） |
| `TradingCoordinatorAgent` | 综合决策（把多方结论合成一个交易意图） | `MarketSentimentAnalysisEvent` + `TechnicalAnalysisEvent` + `MarketTickEvent` | `TradingDecisionEvent` + `DecisionCycle*` | 是 | 可选：可切 `CognitiveMeshDecisionEngine`（外部推理服务） |
| `RiskManagerAgent` | 风控闸门（规则 + LLM 风险评估） | `TradingDecisionEvent` | `ApprovedTradeEvent` / `TradeRejectedEvent` | 是 | **不直接触达交易所**（默认禁用危险工具）；只输出“可执行指令” |
| `ExecutorAgent` | **唯一交易执行面**（把指令变成交易所挂单） | `ApprovedTradeEvent` + `CircuitBreakerTriggeredEvent` | `OrderExecutedEvent` / `OrderFailedEvent` / `OrderSimulatedEvent` | 否 | 不走 LLM 工具；直接调用 `IWeexApiClient.PlaceOrderAsync` |
| `TradeAuditAgent` | 证据链落盘（JSONL + Markdown） | 全量关键事件（通过层级订阅） | 文件落盘 +（可选）`AiWarsLogUploadRequestedEvent` | 否 | 不需要 |

> **关键点**：LLM 负责“想”；Executor 负责“做”。这样才能把风险从“模型随机性”隔离出来。

---

## 3) 真实挂单（Live）是怎么发生的？

### 3.1 触发时机

- 当 `TradingCoordinatorAgent` 同时拿到：
  - 最新情绪分析（`MarketSentimentAnalysisEvent`）
  - 最新技术分析（`TechnicalAnalysisEvent`）
  - 最新价格快照（`MarketTickEvent`）
- 且满足最小决策间隔（默认 30s）和最小置信度阈值（配置项 `Trading:MinConfidenceToTrade`）

### 3.2 事件链（从“看盘”到“挂单”）

1. `DataCollectorAgent` 轮询行情 → 发布 `MarketTickEvent` / `KlineUpdateEvent`
2. 分析师 Agents 产出分析 → 发布 `MarketSentimentAnalysisEvent` / `TechnicalAnalysisEvent`
3. `TradingCoordinatorAgent` 组合分析，调用 LLM（或 Cognitive Mesh）→ 发布 `TradingDecisionEvent`
4. `RiskManagerAgent` 做硬规则校验 + LLM 风控评估：
   - 通过：发布 `ApprovedTradeEvent`
   - 拒绝：发布 `TradeRejectedEvent`
5. **`ExecutorAgent` 接收 `ApprovedTradeEvent`，调用 WEEX 合约 API 下单**
   - Live：`IWeexApiClient.PlaceOrderAsync` → `POST /capi/v2/order/placeOrder`
   - DryRun：不触网，只发布 `OrderSimulatedEvent`
6. `TradeAuditAgent` 记录整条链路并输出可读策略日志

---

## 4) “用什么方式/工具下单”——一句话说清

- **下单不是 LLM Tool Calling**。  
  下单由 `ExecutorAgent` 直接调用 `IWeexApiClient.PlaceOrderAsync(...)` 完成（并做 stepSize 适配）。

为什么这么设计：
- LLM 的优势在推理，不在副作用执行；执行必须可控、可回滚、可审计。
- 即便你允许 LLM 直接调用危险工具，下单也应该是“最后 1 公里”的确定性组件做。

---

## 5) Demo/上线建议（客户视角）

- **先 DryRun 跑通闭环**：看 Dashboard 的策略时间线、风控结论、模拟下单与审计日志。
- **再切 Live 做小额挂单**：系统会以 LIMIT 单方式演示“挂单→订单列表→撤单/风控熔断”闭环。
- **审计与可解释性**：把 `trade-audit/*.md` 直接给业务方/合规看——他们关心的是“为什么下单”，不是你的代码。

---

## 6) AI Wars：是否会自动上传 AI log？怎么开启？

### 默认行为

- 默认 **不会自动上传**（避免你在 DryRun/本地调试时把日志打到主办方网关）。

### 开启方式（推荐）

在 `trade/Aevatar.Trade/appsettings.json` 和 `trade/Aevatar.Trade.Api/appsettings.json` 中设置：

- `TradeAudit:RequestAiwarsUpload = true`

并确保你的 WEEX 鉴权环境变量已配置（系统会把 `Weex:*` bridge 到 `WEEX_*` 给 dotnet-file skill 使用）：

- `WEEX_API_KEY`
- `WEEX_API_SECRET`
- `WEEX_PASSPHRASE`
-（可选）`WEEX_BASE_URL=https://api-contract.weex.com`

### 上传触发时机（自动）

- **无交易**：`DecisionCycleCompleted.executed=false` → 上传一条 “Decision (No Trade)” 的 AI log
- **有交易**：等到终态事件再上传（只上传一次）：
  - `TradeRejectedEvent` → stage=`Risk Control`
  - `OrderExecutedEvent` → stage=`Order Execution`（能解析到 orderId 时会填）
  - `OrderSimulatedEvent` → stage=`Order Simulation`
  - `OrderFailedEvent` → stage=`Order Failure`

### 真正调用的 API（满足主办方格式）

- 上传由 `AiWarsLogUploaderAgent` 执行 dotnet-file skill：`Tools/DotNetSkills/ai-wars/upload/weex_ai_order_upload_ai_log.cs`
- 对应接口：`POST /capi/v2/order/uploadAiLog`


