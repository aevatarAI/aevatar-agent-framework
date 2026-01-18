# WEEX AI Trading 多智能体交易系统架构

> 基于 Aevatar Agent Framework 的 AI 驱动加密货币交易系统

## 目录

- [系统愿景](#系统愿景)
- [核心架构](#核心架构)
- [Agent 角色设计](#agent-角色设计)
- [事件流设计](#事件流设计)
- [技术实现](#技术实现)
- [开发计划](#开发计划)
- [演示亮点](#演示亮点)

---

## 系统愿景

### 设计哲学

**不是更快的交易机器，而是更智能的决策大脑。**

传统量化策略追求毫秒级执行速度，本系统追求的是：
- 多维度信息融合（技术面 + 情绪面 + 风控面）
- AI 驱动的动态决策
- 可解释的推理过程
- 协作式智能涌现

### 核心差异化

```
传统量化：规则 → 信号 → 执行
    ↓
本系统：感知 → 讨论 → 共识 → 执行
         │      │      │
         │      │      └── 多 Agent 投票决策
         │      └────────── LLM 推理对话
         └───────────────── 多源数据融合
```

---

## 核心架构

### 整体架构图

```
                              ┌─────────────────────────────────────┐
                              │         External Data Sources       │
                              │  ┌─────────┐ ┌─────────┐ ┌────────┐│
                              │  │ WEEX API│ │ News API│ │Twitter ││
                              │  └────┬────┘ └────┬────┘ └───┬────┘│
                              └───────┼───────────┼──────────┼─────┘
                                      │           │          │
                              ┌───────▼───────────▼──────────▼─────┐
                              │         DataCollectorAgent         │
                              │         (数据采集员)                │
                              └───────────────┬────────────────────┘
                                              │
                    ┌─────────────────────────┼─────────────────────────┐
                    │                         │                         │
                    ▼                         ▼                         ▼
        ┌───────────────────┐   ┌───────────────────┐   ┌───────────────────┐
        │ MarketSentiment   │   │ TechnicalAnalyst  │   │   NewsAnalyst     │
        │ Agent             │   │ Agent             │   │   Agent           │
        │ (市场情绪分析师)   │   │ (技术分析师)       │   │ (新闻分析师)       │
        ├───────────────────┤   ├───────────────────┤   ├───────────────────┤
        │ • 恐慌贪婪指数    │   │ • K线形态识别     │   │ • 新闻情感分析    │
        │ • 多空比分析      │   │ • 技术指标计算    │   │ • 事件影响评估    │
        │ • 资金流向监控    │   │ • 趋势强度判断    │   │ • 舆情热度追踪    │
        └─────────┬─────────┘   └─────────┬─────────┘   └─────────┬─────────┘
                  │                       │                       │
                  └───────────────────────┼───────────────────────┘
                                          │
                              ┌───────────▼───────────┐
                              │  TradingCoordinator   │
                              │  Agent                │
                              │  (首席交易决策者)      │
                              ├───────────────────────┤
                              │ • 综合各方分析        │
                              │ • LLM 推理决策        │
                              │ • 生成交易信号        │
                              └───────────┬───────────┘
                                          │
                              ┌───────────▼───────────┐
                              │    RiskManager        │
                              │    Agent              │
                              │    (风控经理)          │
                              ├───────────────────────┤
                              │ • 仓位风险评估        │
                              │ • 止损止盈计算        │
                              │ • 资金管理建议        │
                              │ • 否决权机制          │
                              └───────────┬───────────┘
                                          │
                              ┌───────────▼───────────┐
                              │    ExecutorAgent      │
                              │    (交易执行者)        │
                              ├───────────────────────┤
                              │ • WEEX API 对接       │
                              │ • 订单状态管理        │
                              │ • 执行结果反馈        │
                              └───────────────────────┘
```

### 层次结构

```
apps/Aevatar.Trading/
├── docs/                              # 文档目录
│   └── ARCHITECTURE.md                # 本文档
│   └── FRONTEND.md                    # 前端（演示 UI）说明
│   └── CONFIGURATION.md               # 统一配置说明（交易所/凭证/策略）
│   └── AI_WARS_DOTNET_SKILLS.md        # AI Wars API → DotNet File Skills 索引
│   └── TRADING_WORKFLOW.md             # 面向客户：多智能体交易闭环与“谁在何时如何下单”
│
├── scripts/                           # 工具脚本（开发/运维）
│   └── test_dotnet_skills.py           # DotNet file skills 巡检（走 /api/ai-wars/*）
│
├── frontend/                          # ✅ 演示 UI（Vite + React + TS）
│   ├── README.md                      # 启动/联调说明
│   ├── vite.config.ts                 # 开发期同源代理（默认转发到 http://localhost:7100）
│   └── src/                           # UI：Trading 控制台 / WEEX 工具箱
│
├── Aevatar.Trade/                     # 核心库
│   ├── trade_messages.proto           # Protobuf 消息定义
│   ├── Aevatar.Trade.csproj
│   ├── TradingSystem.cs               # 系统编排入口
│   ├── ServiceCollectionExtensions.cs # DI 扩展
│   ├── Agents/
│   │   ├── Data/
│   │   │   └── DataCollectorAgent.cs
│   │   ├── Analysts/
│   │   │   ├── MarketSentimentAgent.cs
│   │   │   └── TechnicalAnalystAgent.cs
│   │   ├── Coordinator/
│   │   │   └── TradingCoordinatorAgent.cs
│   │   ├── Triggers/
│   │   │   └── DecisionTriggerAgent.cs
│   │   ├── Policy/
│   │   │   ├── PolicyManagerAgent.cs
│   │   │   └── TradingPolicyTools.cs
│   │   ├── RiskControl/
│   │   │   └── RiskManagerAgent.cs
│   │   ├── Streaming/
│   │   │   └── MarketChatAgent.cs
│   │   └── Execution/
│   │       └── ExecutorAgent.cs
│   └── Infrastructure/
│       ├── Exchanges/
│       │   ├── IExchangeClient.cs
│       │   ├── ExchangeCapabilities.cs
│       │   ├── ExchangeCredentialsResolver.cs
│       │   ├── WeexExchangeClient.cs
│       │   └── OkxExchangeClient.cs
│       └── WeexApi/
│           ├── IWeexApiClient.cs
│           ├── WeexApiClientBase.cs
│           ├── WeexSpotApiClient.cs
│           ├── WeexApiConfig.cs
│           ├── WeexApiException.cs
│           ├── WeexContractApiClient.cs            # 合约客户端入口（partial，文件很薄）
│           ├── Contract/                           # ✅ 合约客户端分区（每文件 < 800 行）
│           │   ├── WeexContractApiClient.Market.cs  # 行情
│           │   ├── WeexContractApiClient.Account.cs # 账户/模式
│           │   ├── WeexContractApiClient.Trading.cs # 下单/撤单/查询
│           │   ├── WeexContractApiClient.Rules.cs   # stepSize/对齐规则
│           │   └── WeexContractApiClient.Json.cs    # Json 解析/DTO
│           └── WeexWebSocketClient.cs
│
│   └── Tools/
│       └── DotNetSkills/
│           ├── weex_*.cs               # 旧版 spot 风格工具（保留）
│           └── ai-wars/                # ✅ AI Wars: 每个 endpoint 一个 *.cs
│               ├── market/             # 行情类（公开）
│               ├── account/            # 账户类（鉴权）
│               ├── apps/Aevatar.Trading/              # 交易类（鉴权，部分危险）
│               └── upload/             # 上传 AI log（鉴权）
│
├── Aevatar.Trade.Api/                 # Web API Host
│   ├── Aevatar.Trade.Api.csproj
│   ├── Program.cs
│   ├── AiWarsSkillEndpoints.cs         # ✅ 把 Tools/DotNetSkills/ai-wars/** 映射为 HTTP endpoints（Swagger 可见）
│   ├── appsettings.json
│   ├── AgUi/
│   │   ├── TradeAgUiEndpoints.cs        # /api/agui/events (SSE)
│   │   ├── TradeAgUiAuditBridge.cs      # trade-audit -> AG-UI 事件桥
│   │   ├── TradeAgUiHub.cs              # AG-UI 消息与事件 Hub
│   │   ├── TradeAgUiJson.cs             # AG-UI JSON 序列化配置
│   │   └── TradeAgUiStreamSink.cs       # Agent -> AG-UI 输出适配器
│   ├── Controllers/
│   │   ├── TradingController.cs         # 系统控制 + Agent 状态
│   │   ├── PolicyController.cs          # 策略读写（交易所无关）
│   │   ├── DecisionController.cs        # 触发决策（交易所无关）
│   │   ├── PositionsController.cs       # 仓位查询（交易所无关）
│   │   ├── WeexTestController.cs        # WEEX 联调工具（ticker/balances/orders）
│   │   ├── AuditController.cs           # 读取 trade-audit/*.md/*.jsonl（给前端展示/下载）
│   │   └── MetaController.cs            # 安全配置快照（不给 secrets）
│   └── Extensions/
│       ├── ServiceDefaultsExtensions.cs
│       ├── ObservabilityExtensions.cs
│       └── MEAIExtensions.cs
│
└── Aevatar.Trade.AppHost/             # Aspire 编排
    ├── Aevatar.Trade.AppHost.csproj
    ├── Program.cs
    └── appsettings.json
```

---

## 变更记录

- **2025-12-28**：拆分 `WeexApiClient` → `WeexContractApiClient`（AI Wars 合约）+ `WeexSpotApiClient`（Spot），通过 `Weex:Mode` 在 DI 层选择；默认使用 **Contract**（合约）。
- **2025-12-29**：TradeAudit 追加 `trade-audit/*.md` 人类可读策略日志；新增 `AuditController`/`MetaController` 供前端 Dashboard 展示策略/余额/订单闭环。
- **2025-12-29**：新增 `AiWarsSkillEndpoints`：自动扫描 `Tools/DotNetSkills/ai-wars/**`，并将每个 endpoint 以 `/api/ai-wars/{toolName}` 暴露到 Swagger（便于“看得见、点得动”）。
- **2026-01-17**：新增交易所抽象与 Trigger/Policy 体系（DecisionTriggerAgent / PolicyManagerAgent），并引入策略/触发/仓位 API，前端改为以仓位与决策为中心的布局。
- **2026-01-17**：引入 AG-UI SSE：`/api/agui/events`（trade-audit → TextMessage streaming），前端支持 AGUI 方式展示 AI Chat。
- **2026-01-17**：统一 ExchangeCredentials 解析逻辑到 `ExchangeCredentialsResolver`，消除 Program/DI 逻辑重复。

---

## Agent 角色设计

### 1. DataCollectorAgent (数据采集员)

**职责**：统一数据采集入口，将外部数据转换为内部事件

```csharp
public class DataCollectorAgent : GAgentBase<DataCollectorState>
{
    // 定时采集市场数据
    // 监听 WebSocket 实时数据
    // 标准化数据格式后广播
}
```

**输入**：
- WEEX WebSocket 行情数据
- 新闻 API 数据
- 社交媒体数据

**输出事件**：
- `MarketTickEvent` - 行情更新
- `KlineUpdateEvent` - K线更新
- `NewsDataEvent` - 新闻数据
- `SocialDataEvent` - 社交数据

---

### 2. MarketSentimentAgent (市场情绪分析师)

**职责**：分析市场情绪指标，判断市场整体氛围

```csharp
public class MarketSentimentAgent : AIGAgentBase<SentimentState>
{
    protected override string SystemPrompt => @"
        你是一位资深的加密货币市场情绪分析师。
        你的任务是根据以下数据判断市场情绪：
        1. 多空比数据
        2. 资金费率
        3. 大户持仓变化
        4. 恐慌贪婪指数
        
        输出格式：
        - 情绪评分：-100(极度恐慌) 到 +100(极度贪婪)
        - 情绪趋势：上升/下降/震荡
        - 关键观察：简要说明判断依据
    ";
}
```

**分析维度**：
| 指标 | 权重 | 说明 |
|------|------|------|
| 恐慌贪婪指数 | 30% | 综合市场情绪 |
| 多空比 | 25% | 散户情绪倾向 |
| 资金费率 | 25% | 合约市场情绪 |
| 大户持仓 | 20% | 主力动向 |

**输出事件**：`MarketSentimentAnalysisEvent`

---

### 3. TechnicalAnalystAgent (技术分析师)

**职责**：基于价格数据进行技术分析

```csharp
public class TechnicalAnalystAgent : AIGAgentBase<TechnicalState>
{
    protected override string SystemPrompt => @"
        你是一位专业的加密货币技术分析师。
        基于以下技术指标进行分析：
        
        趋势指标：MA20, MA60, MA120, EMA
        动量指标：RSI, MACD, KDJ
        波动指标：布林带, ATR
        成交量：量价关系, OBV
        
        输出格式：
        - 趋势判断：多头/空头/震荡
        - 趋势强度：1-10分
        - 关键价位：支撑位、阻力位
        - 形态识别：如有特殊形态请标注
        - 建议操作：做多/做空/观望
    ";
}
```

**技术指标计算**：
```
指标体系：
├── 趋势类
│   ├── MA (5, 10, 20, 60, 120)
│   ├── EMA (12, 26)
│   └── MACD (12, 26, 9)
├── 动量类
│   ├── RSI (14)
│   ├── KDJ (9, 3, 3)
│   └── Stochastic
├── 波动类
│   ├── Bollinger Bands (20, 2)
│   └── ATR (14)
└── 成交量类
    ├── Volume MA
    └── OBV
```

**输出事件**：`TechnicalAnalysisEvent`

---

### 4. NewsAnalystAgent (新闻分析师)

**职责**：分析新闻和社交媒体对市场的潜在影响

```csharp
public class NewsAnalystAgent : AIGAgentBase<NewsAnalysisState>
{
    protected override string SystemPrompt => @"
        你是一位加密货币新闻分析专家。
        你的任务是评估新闻事件对市场的潜在影响。
        
        分析维度：
        1. 事件性质：利好/利空/中性
        2. 影响程度：高/中/低
        3. 影响时效：短期/中期/长期
        4. 相关币种：受影响的具体币种
        
        注意：
        - 区分噪音和真正有影响的事件
        - 考虑市场对类似事件的历史反应
        - 评估信息的可靠性
    ";
}
```

**数据来源**：
- 加密货币新闻网站
- Twitter/X 关键账号
- Reddit 讨论热度
- Telegram 群组情绪

**输出事件**：`NewsImpactAnalysisEvent`

---

### 5. TradingCoordinatorAgent (首席交易决策者)

**职责**：综合各分析师意见，做出最终交易决策（入口由 `DecisionTriggerEvent` 驱动）

```csharp
public class TradingCoordinatorAgent : AIGAgentBase<CoordinatorState>
{
    protected override string SystemPrompt => @"
        你是首席交易决策者，负责综合各方分析做出交易决策。
        
        你会收到以下分析报告：
        1. 市场情绪分析（MarketSentimentAgent）
        2. 技术分析（TechnicalAnalystAgent）
        3. 新闻分析（NewsAnalystAgent）
        
        决策框架：
        - 当多数分析一致时，跟随主流判断
        - 当分析存在分歧时，偏向保守
        - 新闻事件优先级高于技术分析
        - 极端情绪时考虑逆向操作
        
        输出格式：
        - 交易方向：BUY/SELL/HOLD
        - 信心度：1-100
        - 建议仓位比例：0-100%
        - 决策理由：综合各方意见的推理过程
    ";
    
    [EventHandler]
    public async Task OnSentimentAnalysis(MarketSentimentAnalysisEvent evt)
    {
        State.LatestSentiment = evt;
        await TryMakeDecision();
    }
    
    [EventHandler]
    public async Task OnTechnicalAnalysis(TechnicalAnalysisEvent evt)
    {
        State.LatestTechnical = evt;
        await TryMakeDecision();
    }
    
    [EventHandler]
    public async Task OnNewsAnalysis(NewsImpactAnalysisEvent evt)
    {
        State.LatestNews = evt;
        await TryMakeDecision();
    }
    
    private async Task TryMakeDecision()
    {
        // 确保收集到足够信息后再决策
        if (!HasSufficientData()) return;
        
        var decision = await InvokeAIAsync(BuildDecisionContext());
        await PublishAsync(new TradingDecisionEvent { ... });
    }
}
```

**决策权重**：
| 分析来源 | 权重 | 说明 |
|----------|------|------|
| 技术分析 | 40% | 核心决策依据 |
| 市场情绪 | 30% | 情绪验证 |
| 新闻分析 | 30% | 事件驱动调整 |

**输出事件**：`TradingDecisionEvent`

---

### 5.1 DecisionTriggerAgent (价格触发器)

**职责**：监听 `MarketTickEvent`，在价格变化达到阈值并满足冷却条件时触发决策

**输入事件**：`MarketTickEvent`、`TradingPolicyUpdatedEvent`

**输出事件**：`DecisionTriggerEvent`

---

### 5.2 PolicyManagerAgent (策略参数管理器)

**职责**：集中管理交易策略参数，校验更新并广播给下游

**输入**：AI 工具调用 / 配置启动

**输出事件**：`TradingPolicyUpdatedEvent`

---

### 6. RiskManagerAgent (风控经理)

**职责**：评估交易风险，设定止损止盈，必要时否决交易

```csharp
public class RiskManagerAgent : AIGAgentBase<RiskState>
{
    protected override string SystemPrompt => @"
        你是风控经理，负责保护资金安全。
        
        风控规则：
        1. 单笔最大亏损不超过总资金的 2%
        2. 单日最大亏损不超过总资金的 5%
        3. 最大持仓不超过总资金的 30%
        4. 连续亏损 3 次后强制休息 1 小时
        
        你需要：
        - 计算合适的仓位大小
        - 设定止损止盈价格
        - 评估当前风险敞口
        - 必要时否决高风险交易
        
        输出格式：
        - 风险评估：低/中/高/极高
        - 是否批准：APPROVE/REJECT
        - 调整后仓位：如需调整
        - 止损价格：建议止损位
        - 止盈价格：建议止盈位
        - 否决理由：如果否决，说明原因
    ";
    
    [EventHandler]
    public async Task OnTradingDecision(TradingDecisionEvent evt)
    {
        var riskAssessment = await InvokeAIAsync(BuildRiskContext(evt));
        
        if (riskAssessment.Approved)
        {
            await PublishAsync(new ApprovedTradeEvent { ... });
        }
        else
        {
            await PublishAsync(new TradeRejectedEvent { ... });
        }
    }
}
```

**风控指标**：
```
风控体系：
├── 仓位管理
│   ├── 单笔仓位上限：10%
│   ├── 总仓位上限：30%
│   └── 动态仓位调整
├── 止损管理
│   ├── 固定止损：2%
│   ├── 移动止损：根据盈利调整
│   └── 时间止损：超时平仓
├── 风险评估
│   ├── 波动率评估
│   ├── 相关性风险
│   └── 流动性风险
└── 熔断机制
    ├── 日亏损熔断：5%
    ├── 连续亏损熔断：3次
    └── 异常波动熔断
```

**输出事件**：`ApprovedTradeEvent` / `TradeRejectedEvent`

---

### 7. ExecutorAgent (交易执行者)

**职责**：执行实际交易，管理订单生命周期

```csharp
public class ExecutorAgent : GAgentBase<ExecutorState>
{
    private readonly IWeexApiClient _weexClient;
    
    [EventHandler]
    public async Task OnApprovedTrade(ApprovedTradeEvent evt)
    {
        try
        {
            // 执行交易
            var order = await _weexClient.PlaceOrderAsync(new OrderRequest
            {
                Symbol = evt.Symbol,
                Side = evt.Side,
                Quantity = evt.Quantity,
                Price = evt.Price,
                StopLoss = evt.StopLoss,
                TakeProfit = evt.TakeProfit
            });
            
            await PublishAsync(new OrderExecutedEvent
            {
                OrderId = order.Id,
                Status = order.Status,
                FilledPrice = order.FilledPrice
            });
        }
        catch (Exception ex)
        {
            await PublishAsync(new OrderFailedEvent
            {
                Reason = ex.Message
            });
        }
    }
}
```

**订单状态管理**：
```
订单生命周期：
PENDING → SUBMITTED → PARTIAL_FILLED → FILLED
                   ↘ CANCELLED
                   ↘ REJECTED
                   ↘ EXPIRED
```

**输出事件**：`OrderExecutedEvent` / `OrderFailedEvent`

---

## 事件流设计

### AG-UI SSE（前端流式）

- **数据源**：
  - `MarketChatAgent`（ChatStreamAsync → 实时 token 流）
  - `trade-audit/*.md`（TradeAuditAgent 产出，补充审计摘要）
- **事件桥**：
  - `TradeAgUiStreamSink`：Agent → AG-UI（TextMessageStart/Content/End）
  - `TradeAgUiAuditBridge`：Audit → AG-UI（TextMessageStart/Content/End）
- **交互入口**：`POST /api/agui/chat`（用户输入 → MarketChatAgent）
- **前端订阅**：`GET /api/agui/events`（snapshot-first + SSE）

### 核心事件定义 (Protobuf)

```protobuf
syntax = "proto3";

package trade.messages;

import "google/protobuf/timestamp.proto";

// ============ 数据事件 ============

message MarketTickEvent {
    string symbol = 1;
    double price = 2;
    double volume_24h = 3;
    double change_24h = 4;
    google.protobuf.Timestamp timestamp = 5;
}

message KlineUpdateEvent {
    string symbol = 1;
    string interval = 2;  // 1m, 5m, 15m, 1h, 4h, 1d
    double open = 3;
    double high = 4;
    double low = 5;
    double close = 6;
    double volume = 7;
    google.protobuf.Timestamp timestamp = 8;
}

// ============ 分析事件 ============

message MarketSentimentAnalysisEvent {
    string symbol = 1;
    int32 sentiment_score = 2;      // -100 到 +100
    string sentiment_trend = 3;      // UP, DOWN, SIDEWAYS
    double fear_greed_index = 4;
    double long_short_ratio = 5;
    double funding_rate = 6;
    string analysis_summary = 7;
    google.protobuf.Timestamp timestamp = 8;
}

message TechnicalAnalysisEvent {
    string symbol = 1;
    string trend_direction = 2;      // BULLISH, BEARISH, SIDEWAYS
    int32 trend_strength = 3;        // 1-10
    double support_level = 4;
    double resistance_level = 5;
    double rsi = 6;
    double macd_histogram = 7;
    string signal = 8;               // BUY, SELL, HOLD
    string analysis_summary = 9;
    google.protobuf.Timestamp timestamp = 10;
}

message NewsImpactAnalysisEvent {
    string headline = 1;
    string impact_type = 2;          // POSITIVE, NEGATIVE, NEUTRAL
    string impact_level = 3;         // HIGH, MEDIUM, LOW
    string impact_duration = 4;      // SHORT, MEDIUM, LONG
    repeated string affected_symbols = 5;
    string analysis_summary = 6;
    google.protobuf.Timestamp timestamp = 7;
}

// ============ 决策事件 ============

message TradingDecisionEvent {
    string symbol = 1;
    string direction = 2;            // BUY, SELL, HOLD
    int32 confidence = 3;            // 1-100
    double suggested_position_pct = 4;
    string reasoning = 5;
    google.protobuf.Timestamp timestamp = 6;
}

message ApprovedTradeEvent {
    string symbol = 1;
    string side = 2;                 // BUY, SELL
    double quantity = 3;
    double price = 4;
    double stop_loss = 5;
    double take_profit = 6;
    string risk_assessment = 7;
    google.protobuf.Timestamp timestamp = 8;
}

message TradeRejectedEvent {
    string symbol = 1;
    string original_direction = 2;
    string rejection_reason = 3;
    string risk_level = 4;
    google.protobuf.Timestamp timestamp = 5;
}

// ============ 执行事件 ============

message OrderExecutedEvent {
    string order_id = 1;
    string symbol = 2;
    string side = 3;
    double quantity = 4;
    double filled_price = 5;
    string status = 6;
    google.protobuf.Timestamp timestamp = 7;
}

message OrderFailedEvent {
    string symbol = 1;
    string side = 2;
    string reason = 3;
    google.protobuf.Timestamp timestamp = 4;
}

// ============ Agent 状态 ============

message DataCollectorState {
    map<string, double> latest_prices = 1;
    google.protobuf.Timestamp last_update = 2;
}

message SentimentState {
    int32 current_sentiment = 1;
    repeated int32 sentiment_history = 2;
    google.protobuf.Timestamp last_analysis = 3;
}

message TechnicalState {
    string current_trend = 1;
    int32 trend_strength = 2;
    double last_rsi = 3;
    double last_macd = 4;
    google.protobuf.Timestamp last_analysis = 5;
}

message NewsAnalysisState {
    repeated string recent_headlines = 1;
    string overall_sentiment = 2;
    google.protobuf.Timestamp last_analysis = 3;
}

message CoordinatorState {
    MarketSentimentAnalysisEvent latest_sentiment = 1;
    TechnicalAnalysisEvent latest_technical = 2;
    NewsImpactAnalysisEvent latest_news = 3;
    string last_decision = 4;
    google.protobuf.Timestamp last_decision_time = 5;
}

message RiskState {
    double current_position = 1;
    double total_equity = 2;
    double daily_pnl = 3;
    int32 consecutive_losses = 4;
    bool is_trading_allowed = 5;
}

message ExecutorState {
    repeated string active_orders = 1;
    map<string, double> positions = 2;
    double realized_pnl = 3;
}
```

### 事件流向图

```
┌──────────────────────────────────────────────────────────────────────┐
│                         EVENT FLOW                                   │
├──────────────────────────────────────────────────────────────────────┤
│                                                                      │
│  [External] ──MarketTickEvent──► [DataCollector]                    │
│                                        │                             │
│              ┌─────────────────────────┼─────────────────────────┐  │
│              │                         │                         │  │
│              ▼                         ▼                         ▼  │
│    MarketTickEvent           KlineUpdateEvent           NewsDataEvent│
│              │                         │                         │  │
│              ▼                         ▼                         ▼  │
│    [Sentiment]               [Technical]               [News]       │
│              │                         │                         │  │
│              ▼                         ▼                         ▼  │
│    SentimentAnalysis         TechnicalAnalysis       NewsAnalysis   │
│              │                         │                         │  │
│              └─────────────────────────┼─────────────────────────┘  │
│                                        │                             │
│                                        ▼                             │
│                              [Coordinator]                           │
│                                        │                             │
│                                        ▼                             │
│                            TradingDecisionEvent                      │
│                                        │                             │
│                                        ▼                             │
│                              [RiskManager]                           │
│                                        │                             │
│                          ┌─────────────┴─────────────┐              │
│                          ▼                           ▼              │
│                 ApprovedTradeEvent          TradeRejectedEvent      │
│                          │                                          │
│                          ▼                                          │
│                     [Executor]                                      │
│                          │                                          │
│                          ▼                                          │
│                 OrderExecutedEvent                                  │
│                                                                      │
└──────────────────────────────────────────────────────────────────────┘
```

---

## 技术实现

### WEEX API 封装

```csharp
public interface IWeexApiClient
{
    // 市场数据
    Task<Ticker> GetTickerAsync(string symbol);
    Task<IEnumerable<Kline>> GetKlinesAsync(string symbol, string interval, int limit);
    Task SubscribeTickerAsync(string symbol, Action<Ticker> onTicker);
    
    // 交易接口
    Task<Order> PlaceOrderAsync(OrderRequest request);
    Task<Order> CancelOrderAsync(string orderId);
    Task<IEnumerable<Order>> GetOpenOrdersAsync();
    
    // 账户接口
    Task<AccountInfo> GetAccountInfoAsync();
    Task<IEnumerable<Position>> GetPositionsAsync();
}
```

### 配置设计

```json
{
  "Trading": {
    "Symbol": "BTCUSDT",
    "Interval": "15m",
    "MaxPositionPercent": 30,
    "MaxLossPerTrade": 2,
    "MaxDailyLoss": 5
  },
  "Analysis": {
    "SentimentWeight": 0.3,
    "TechnicalWeight": 0.4,
    "NewsWeight": 0.3,
    "MinConfidenceToTrade": 70
  },
  "LLM": {
    "Provider": "OpenAI",
    "Model": "gpt-4",
    "Temperature": 0.3
  },
  "Weex": {
    "ApiKey": "${WEEX_API_KEY}",
    "ApiSecret": "${WEEX_API_SECRET}",
    "BaseUrl": "https://api.weex.com"
  }
}
```

### 可观测性集成

```csharp
// 框架已内置 OpenTelemetry 支持
// 可以追踪完整的决策链路

/*
Trace 示例：
├── DataCollector.OnMarketTick (2ms)
│   ├── MarketSentiment.Analyze (150ms)
│   │   └── LLM.Invoke (145ms)
│   ├── Technical.Analyze (50ms)
│   │   └── Indicators.Calculate (48ms)
│   └── News.Analyze (200ms)
│       └── LLM.Invoke (195ms)
├── Coordinator.MakeDecision (180ms)
│   └── LLM.Invoke (175ms)
├── RiskManager.Assess (100ms)
│   └── LLM.Invoke (95ms)
└── Executor.PlaceOrder (50ms)
    └── Weex.API.Call (45ms)
*/
```

---

## 开发计划

### Phase 1: 基础骨架 (Week 1)

```
任务列表：
├── [x] 创建项目结构
├── [ ] 定义 Protobuf 消息
├── [ ] 实现 DataCollectorAgent
├── [ ] 实现 ExecutorAgent
├── [ ] WEEX API 封装
└── [ ] 基础事件流测试
```

### Phase 2: 分析 Agent (Week 2)

```
任务列表：
├── [ ] 实现 TechnicalAnalystAgent
│   ├── [ ] 技术指标计算库
│   └── [ ] LLM 分析 Prompt
├── [ ] 实现 MarketSentimentAgent
│   └── [ ] 情绪指标数据源
├── [ ] 实现 NewsAnalystAgent
│   └── [ ] 新闻 API 集成
└── [ ] 分析 Agent 联调测试
```

### Phase 3: 决策与风控 (Week 3)

```
任务列表：
├── [ ] 实现 TradingCoordinatorAgent
│   ├── [ ] 多源信息融合逻辑
│   └── [ ] 决策 Prompt 调优
├── [ ] 实现 RiskManagerAgent
│   ├── [ ] 风控规则引擎
│   └── [ ] 否决机制
└── [ ] 完整流程联调
```

### Phase 4: 优化与演示 (Week 4)

```
任务列表：
├── [ ] Agent 协作可视化
├── [ ] 决策日志 Dashboard
├── [ ] Prompt 效果调优
├── [ ] 回测验证
├── [ ] 演示视频录制
└── [ ] 文档完善
```

---

## 演示亮点

### 1. Agent 协作对话展示

```
┌─────────────────────────────────────────────────────────────┐
│ 🤖 AI Trading Agents Discussion                            │
├─────────────────────────────────────────────────────────────┤
│                                                             │
│ 📊 [Technical] 15:32:05                                    │
│ BTC 4H 级别 MACD 金叉，RSI 从超卖区回升至 45，              │
│ 布林带收窄后向上突破中轨。建议：做多，趋势强度 7/10         │
│                                                             │
│ 😱 [Sentiment] 15:32:08                                    │
│ 恐慌贪婪指数 35（偏恐慌），多空比 0.85（空头占优），        │
│ 但资金费率转负说明空头已较拥挤。建议：谨慎做多              │
│                                                             │
│ 📰 [News] 15:32:10                                         │
│ 近期无重大利空新闻，ETF 资金持续净流入。                    │
│ 影响评级：中性偏多                                          │
│                                                             │
│ 🎯 [Coordinator] 15:32:15                                  │
│ 综合分析：技术面看多，情绪面偏恐慌但有反转迹象，            │
│ 基本面中性。决策：BUY，信心度 72%，建议仓位 15%             │
│                                                             │
│ 🛡️ [RiskManager] 15:32:18                                  │
│ 风险评估：中等。当前无持仓，风险敞口正常。                  │
│ 批准交易，止损 -2%，止盈 +4%                                │
│                                                             │
│ ⚡ [Executor] 15:32:20                                      │
│ 订单已提交：BUY 0.5 BTC @ $67,250                          │
│ 订单成交：成交价 $67,248，已设置止损止盈                   │
│                                                             │
└─────────────────────────────────────────────────────────────┘
```

### 2. 决策追踪可视化

```
                    Decision Trace
                    
Input Data          Analysis            Decision
─────────────────────────────────────────────────
BTC: $67,250   ──►  Technical: +7   ──►
RSI: 45             Sentiment: +3       BUY 15%
MACD: Golden        News: +2            Confidence: 72%
Volume: ↑           ─────────────       Stop: -2%
                    Combined: +12       Take: +4%
```

### 3. 风控仪表盘

```
┌─────────────────────────────────────────┐
│         RISK DASHBOARD                  │
├─────────────────────────────────────────┤
│ Position      [████████░░] 28%          │
│ Daily P&L     [██████░░░░] +2.3%        │
│ Risk Level    [███░░░░░░░] LOW          │
│                                         │
│ Today's Trades: 5                       │
│ Win Rate: 60% (3W/2L)                   │
│ Max Drawdown: -1.2%                     │
│                                         │
│ Status: ✅ TRADING ENABLED              │
└─────────────────────────────────────────┘
```

---

## 核心竞争力

| 维度 | 传统方案 | 本方案 |
|------|----------|--------|
| 决策透明度 | 黑盒策略 | 完整推理链路 |
| 信息融合 | 单一信号源 | 多维度分析 |
| 适应性 | 固定规则 | LLM 动态推理 |
| 风控能力 | 简单止损 | 智能风控 Agent |
| 演示效果 | 收益曲线 | Agent 对话过程 |
| 技术创新 | 传统量化 | AI 多智能体协作 |

---

## 总结

本系统通过多智能体协作架构，将交易决策过程拆解为：

**感知 → 分析 → 决策 → 风控 → 执行**

每个环节由专门的 AI Agent 负责，通过事件驱动实现协作。这不仅符合 hackathon 对 AI 创新的要求，更展示了「协作式智能」在金融领域的应用潜力。

**核心理念**：交易不是算法的竞速，而是智能的协作。

---

*文档版本：v1.0*  
*最后更新：2024-12*
