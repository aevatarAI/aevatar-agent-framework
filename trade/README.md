# 🚀 WEEX AI Trading System

> 基于 **Aevatar Agent Framework** 的多智能体自动交易系统（WEEX AI Wars 合约赛道友好）。

为 [WEEX AI Wars Hackathon](https://dorahacks.io/hackathon/weex-ai-trading/detail) 设计：不仅“会下单”，更强调 **可解释策略**、**可观测闭环**、**可复盘日志**、**一键对接 AI Wars OpenAPI**。

## 你会获得什么（面向 Demo / 评审 / 复盘）

- **可视化控制台（Vite + React + TS）**：策略时间线、仓位/成交图表、余额/订单、系统健康
- **多智能体闭环**：Data → 分析（AI）→ 决策（AI）→ 风控（AI）→ 执行 → 审计
- **高频决策模式**：系统可做到“接近 1 秒一次尝试决策”（实际受 LLM 响应耗时影响），并防止并发堆积
- **AI Wars OpenAPI 友好**：所有 AI Wars API 以 **dotnet-file skill** 落盘，并自动暴露为 Swagger 可调用 HTTP endpoints
- **审计落盘**：JSONL（机器回放）+ Markdown（人类可读），包含 **启动自检动作** 与 **AI Wars 上传回执**

## 系统能力与特色

- **可解释性优先**：每个 decision cycle 都会总结“AI 怎么想 → 风控怎么裁决 → 执行结果如何”
- **可观测性优先**：系统“自发动作”（例如启动自检自动补仓）也会进入 `trade-audit`
- **安全模式**：默认 DryRun（跑通闭环但不真实下单），切 Live 才会真实下单
- **合约/现货切换**：`Weex:Mode = Contract|Spot`（AI Wars 默认 Contract + `cmt_btcusdt`）
- **AI Wars 上传自动化**：TradeAudit 可触发 `UploadAiLog`，并写入本地回执（成功/失败都可追踪）

## Aevatar Agent Framework（极简介绍）

本系统基于 Aevatar 的 **Actor + Event-Driven** 模型：

- **每个 Agent 是一个 Actor**：天然隔离状态、并发安全
- **事件按层级路由传播**：Up/Down/Both，适合“数据源 → 多分析 → 决策/风控/执行”的拓扑
- **跨边界类型统一 Protobuf**：事件/状态/配置可序列化、可回放、可跨运行时
- **运行时可切换**：开发默认 Local；需要分布式可切 Orleans

## 架构亮点

```
┌───────────────────────────────────────────────────────────────────────┐
│ DataCollector                                                          │
│ - AI Wars 环境默认走 REST polling（1s）                                 │
│ - 负责产生 MarketTick / KlineUpdate                                    │
└───────────────┬───────────────────────────────┬───────────────────────┘
                │                               │
                ▼                               ▼
     ┌─────────────────────┐         ┌─────────────────────┐
     │ MarketSentimentAgent │         │ TechnicalAnalystAgent│
     │ (AI / 高频 ~1s)      │         │ (AI / 高频 ~1s)      │
     │ Publish Up           │         │ Publish Up           │
     └───────────┬─────────┘         └───────────┬─────────┘
                 │                               │
                 └───────────────┬───────────────┘
                                 ▼
                      ┌─────────────────────────┐
                      │ TradingCoordinatorAgent │
                      │ (AI 决策者 / ~1s)        │
                      └───────────┬─────────────┘
                                  ▼
                      ┌─────────────────────────┐
                      │ RiskManagerAgent (AI)   │
                      └───────────┬─────────────┘
                                  ▼
                      ┌─────────────────────────┐
                      │ ExecutorAgent           │
                      └─────────────────────────┘

旁路可观测：
  - TradeAuditAgent：挂在 Coordinator/Risk/Executor 上，落盘 JSONL + MD
  - AiWarsLogUploaderAgent：接收 Upload 请求，执行 dotnet-file skill 并落盘回执
```

## 目录结构

```
trade/
├── README.md                      # 本文件
├── docs/
│   └── ARCHITECTURE.md            # 详细架构文档
│   └── FRONTEND.md                # 前端（演示 UI）说明
│   └── TRADING_WORKFLOW.md        # 面向客户：多智能体交易闭环与“谁在何时如何下单”
│
├── frontend/                      # ✅ 演示 UI（Vite + React + TS）
│   ├── README.md                  # 前端启动/联调说明
│   ├── vite.config.ts             # 本地同源代理（默认转发到 http://localhost:7100）
│   └── src/                       # UI 源码（Trading 控制台 / WEEX 工具）
│
├── Aevatar.Trade/                 # 核心库
│   ├── trade_messages.proto       # Protobuf 消息定义
│   ├── Aevatar.Trade.csproj
│   ├── TradingSystem.cs           # 系统编排
│   ├── Agents/                    # Agent 实现
│   │   ├── Data/DataCollectorAgent.cs
│   │   ├── Analysts/
│   │   │   ├── MarketSentimentAgent.cs
│   │   │   └── TechnicalAnalystAgent.cs
│   │   ├── Coordinator/TradingCoordinatorAgent.cs
│   │   ├── RiskControl/RiskManagerAgent.cs
│   │   └── Execution/ExecutorAgent.cs
│   ├── Tools/
│   │   └── DotNetSkills/ai-wars/  # ✅ AI Wars 全量 API（dotnet-file skills）
│   └── Infrastructure/WeexApi/    # WEEX API 封装（Contract/Spot）
│       ├── WeexApiClientBase.cs
│       ├── WeexSpotApiClient.cs
│       └── Contract/             # Contract client（拆分 partial）
│           ├── WeexContractApiClient.Market.cs
│           ├── WeexContractApiClient.Account.cs
│           ├── WeexContractApiClient.Trading.cs
│           ├── WeexContractApiClient.Rules.cs
│           └── WeexContractApiClient.Json.cs
│
├── Aevatar.Trade.Api/             # Web API Host
│   ├── Aevatar.Trade.Api.csproj
│   ├── Program.cs                 # 应用入口
│   ├── AiWarsSkillEndpoints.cs    # ✅ dotnet-file skills -> HTTP endpoints（Swagger 可见）
│   ├── Controllers/               # API 控制器
│   │   ├── TradingController.cs
│   │   ├── WeexTestController.cs
│   │   ├── MetaController.cs
│   │   └── AuditController.cs
│   ├── Extensions/                # 扩展方法
│   └── appsettings.json
│   └── trade-audit/               # ✅ 默认日志输出目录（jsonl/md/ai-wars 回执）
│
└── Aevatar.Trade.AppHost/         # Aspire 编排
    ├── Aevatar.Trade.AppHost.csproj
    ├── Program.cs
    └── appsettings.json
```

## 快速开始

### 依赖

- **.NET SDK**：建议 .NET 10（本仓库以 net10.0 构建）
- **Node.js**：建议 18+（前端 Vite）

### 推荐配置方式：User Secrets（全局加密） + `appsettings.secrets.json`（项目级覆盖）

- **推荐（一次配置，全仓复用）**：用户级 secrets（加密）
  - 默认：`~/.aevatar/secrets.json`（用 `src/Aevatar.Agents.SecretsCli` 写入）
  - 覆盖：`AEVATAR_SECRETS_PATH/AEVATAR_SECRETS_DIR`
- **可选（项目级覆盖）**：在 `trade/Aevatar.Trade.Api/` 下创建 `appsettings.secrets.json`（该文件 gitignored），参考示例：

- `trade/Aevatar.Trade.Api/appsettings.secrets.json.example`

> 说明：API Host 会把 `Weex:*` 自动导出为 `WEEX_*` 环境变量，供 dotnet-file skills 子进程使用（无需你再手工 export）。

### 方式一：直接运行 API

```bash
# 1. 进入 API 目录
cd trade/Aevatar.Trade.Api

# 2. （推荐）准备 secrets
# cp appsettings.secrets.json.example appsettings.secrets.json
# 然后填入 Weex / LLMProviders

# 3. 运行（推荐 http profile：避免本机证书未信任导致浏览器/代理异常）
dotnet run --launch-profile http

# 4. 访问 Swagger: http://localhost:7100/swagger
```

### 方式二：使用 Aspire AppHost

```bash
# 1. 进入仓库根目录 (推荐在根目录用 --project，避免目录切换/路径误用)
cd aevatar-agent-framework

# 2. 运行（推荐 http profile：避免本机证书未信任导致 Dashboard gRPC 报 UntrustedRoot）
dotnet run --project trade/Aevatar.Trade.AppHost/Aevatar.Trade.AppHost.csproj --launch-profile http

# 4. 访问 Aspire Dashboard: http://localhost:15888
# 5. 访问 Trading API: http://localhost:7100/swagger
# 6. 访问 Frontend UI: http://localhost:5173
```

### 方式三：启动演示前端（推荐）

后端用 http profile（避免本机证书/CORS 干扰），前端用同源代理：

```bash
# 1) 启动后端 API（仓库根目录）
dotnet run --project trade/Aevatar.Trade.Api/Aevatar.Trade.Api.csproj --launch-profile http

# 2) 启动前端（新终端）
cd trade/frontend
npm install
npm run dev
```

前端：`http://localhost:5173`  
Swagger：`http://localhost:7100/swagger`

> 提示：AI Wars 的 dotnet-file skills 已自动暴露为 Swagger 可调用的 HTTP endpoints：
> - 索引：`GET /api/ai-wars`
> - 执行：`POST /api/ai-wars/{toolName}?confirm=false|true`（confirm 默认 false）

### 常见坑：HTTPS 开发证书未信任

如果你想用 `https` profile（或希望浏览器/客户端不再提示不安全证书），在 macOS 上执行：

```bash
dotnet dev-certs https --trust
```

### 常见现象：DataCollector 显示 REST polling

AI Wars 合约环境 **常见没有可用 WebSocket**，系统会自动降级为 **REST polling**（默认 1 秒一次）。这是预期行为。

### AI Wars（合约赛道）校准要点

根据 WEEX AI Wars 参赛指南，合约 OpenAPI 会进行 **IP 白名单**校验；提交 BUIDL 时需要填写你的出口 IP 才能“成功调用 OpenAPI”。参见：
- [AI Wars: Participant Guide](https://www.weex.com/api-doc/ai/introduction/ParticipantGuide)

工程侧建议（当前仓库默认已按 **Contract 模式**配置）：

- `Weex:Mode = "Contract"`
- `Weex:BaseUrl = "https://api-contract.weex.com"`

> 说明：AI Wars 环境下，部分网关对“无签名的 market 请求”也会直接返回 403（HTML 网关页）。本仓库的 Contract client 已对 market/ticker、market/candles 默认带签名头，确保联调稳定。

### 让 AI 自动交易（DryRun → Live）

默认为了安全是 **DryRun**（全链路跑通，但不真实下单）。

要让 AI 真实下单：

- 在 `trade/Aevatar.Trade.Api/appsettings.json`（或你的环境变量/配置源）把：
  - `Trading:ExecutionMode` 改成 `"Live"`
- 确保 `trade/Aevatar.Trade.Api/appsettings.secrets.json` 里 `Weex:ApiKey/ApiSecret/Passphrase` 有 **交易权限**（并完成比赛要求的 IP 白名单）

### API 操作

```bash
# 初始化系统
curl -X POST http://localhost:7100/api/trading/initialize

# 启动交易
curl -X POST http://localhost:7100/api/trading/start

# 获取状态
curl http://localhost:7100/api/trading/status

# 停止交易
curl -X POST http://localhost:7100/api/trading/stop
```

### 其他常用 API（可观测 / 调试 / AI Wars 工具）

- **Meta（安全配置快照）**：`GET /api/meta`
- **Audit（读日志）**
  - `GET /api/audit/latest`：读取最新策略 Markdown（tail）
  - `GET /api/audit/files`：列出 `trade-audit/` 文件
  - `GET /api/audit/tail?name=trade_audit_xxx.md`：读取指定文件 tail
  - `POST /api/audit/normalize?name=trade_audit_xxx.jsonl`：把历史双重编码的 JSONL 转成可读格式
- **WEEX Tools（高级调试）**：`/api/weex-test/*`（ticker/balances/open-orders/place/cancel）
- **AI Wars Tools（全量 OpenAPI）**
  - `GET /api/ai-wars`：工具索引
  - `POST /api/ai-wars/{toolName}?confirm=false|true`：执行工具（Swagger/前端可直接点）

### 策略日志落盘（给人看的）

开启 `TradeAudit:Enabled=true` 后（默认已开），后端会在 `TradeAudit:OutputDir`（默认 `trade-audit/`）生成两类文件：

- **机器可回放**：`trade_audit_<runId>.jsonl`（每行一个事件 envelope，适合上传/程序分析）
- **人类可读**：`trade_audit_<runId>.md`（按 decision cycle 汇总：AI 分析 → 决策 → 风控 → 执行结果）

此外还会落盘两类“旁路可观测”信息（避免黑箱）：

- **Startup Guard**：当 `Trading:MinBaseAssetUsdOnStart > 0` 且 Live 模式时，系统可能自动下单补足底仓（`BOOTSTRAP_...`），会写入 `.md` 与 `.jsonl`。
- **AI Wars Upload 回执**：当 `TradeAudit:RequestAiwarsUpload=true` 时：
  - 生成的上传 payload：`trade-audit/ai-wars/aiwars_upload_*.json`
  - 上传回执（成功/失败都写）：`trade-audit/ai-wars/receipts/aiwars_receipt_<requestId>.json`

> 日志输出目录是相对路径：通常你在 `trade/Aevatar.Trade.Api/` 目录运行 API，那么日志会出现在 `trade/Aevatar.Trade.Api/trade-audit/`。

## 前端页面指南（Demo UI）

前端入口：`http://localhost:5173`

- **Auto Trading**
  - 顶部卡片：ExecutionMode / Symbol / LastPrice / USDT Equity
  - **仓位概览**：Long/Short 暴露条 + 未实现盈亏可视化
  - **成交概览**：成交价折线 + 买卖量柱状条
  - **AI 策略时间线**：从 `trade-audit/*.md` 解析（可读、可演示）
- **WEEX Tools（高级）**：偏底层的 REST 调试入口（ticker/balances/open orders/place/cancel）
- **AI Wars APIs**
  - 自动读取 tool manifest，生成表单、JSON body、curl
  - 一键执行 `POST /api/ai-wars/{toolName}?confirm=false|true`

## 配置参考（appsettings）

> 推荐只改 `trade/Aevatar.Trade.Api/appsettings.json` + `appsettings.secrets.json`（运行时以 API Host 配置为准）。

### 运行时：Local / Orleans

- `AgentRuntime:RuntimeType`：`Local`（开发/单机）或 `Orleans`（分布式）
- `AgentRuntime:Orleans:*`：Orleans 端口/ClusterId/ServiceId

### 交易参数（Trading）

- `Trading:Symbol`：AI Wars 合约赛道推荐 `cmt_btcusdt`
- `Trading:Interval`：`1m/5m/15m/1h...`（影响 kline 采样）
- `Trading:ExecutionMode`：`DryRun` 或 `Live`
- `Trading:MinBaseAssetUsdOnStart`
  - `0`：关闭启动自检补仓
  - `>0`：Live 模式下可能自动下单补足底仓（会记录为 Startup Guard）
- `Trading:MinConfidenceToTrade`：低于该置信度的决策会降为 HOLD（仍会写入决策 cycle）

### 审计与 AI Wars 上传（TradeAudit）

- `TradeAudit:Enabled`：是否落盘审计日志（建议一直开）
- `TradeAudit:OutputDir`：默认 `trade-audit`
- `TradeAudit:IncludeMarketData`：是否把 tick/kline 也写入 JSONL（会很大）
- `TradeAudit:RequestAiwarsUpload`：是否自动触发 AI Wars `UploadAiLog`（并写回执）

### WEEX（Weex）

- `Weex:Mode`：`Contract`（默认）或 `Spot`
- `Weex:BaseUrl`：AI Wars 合约推荐 `https://api-contract.weex.com`
- `Weex:ApiKey/ApiSecret/Passphrase`：建议放在 `appsettings.secrets.json`（不要提交到 Git）

### LLM（LLMProviders / LLM）

`trade/Aevatar.Trade.Api` 支持两种配置：

- **推荐：`LLMProviders`**（适合多服务/多 provider 统一配置）
- **兼容：`LLM`**（trade API 里保留的简化配置）

示例请参考：

- `trade/Aevatar.Trade.Api/appsettings.secrets.json.example`

## AI Wars：dotnet-file skills + Swagger/前端一键执行

### dotnet-file skills（为什么这样做）

AI Wars 文档覆盖面大、接口多，把每个 API 做成单文件工具有三个好处：

- **可审计**：每次请求/响应可落盘、可回放
- **可复用**：既能被 Agent Tool 调用，也能被 HTTP/前端调试
- **隔离运行**：`dotnet run --file` 独立进程执行，降低主进程复杂度

### HTTP 映射（Swagger 可见）

- 索引：`GET /api/ai-wars`
- 执行：`POST /api/ai-wars/{toolName}?confirm=false|true`

> confirm 默认是 false；危险操作（下单/撤单）请显式 `confirm=true`。

## 核心 Agent

| Agent | 职责 | 技术 |
|-------|------|------|
| DataCollectorAgent | 行情采集（tick/kline） | REST polling（默认 1s），WS 可选 |
| MarketSentimentAgent | 市场情绪分析（AI） | LLM（高频、单飞） |
| TechnicalAnalystAgent | 技术分析（AI） | 指标计算 + LLM（高频、单飞） |
| TradingCoordinatorAgent | 汇总决策（AI） | LLM + 高频决策（约 1s） |
| RiskManagerAgent | 风控裁决（AI） | 风险规则 + Tool Calling |
| ExecutorAgent | 交易执行 | WEEX 合约/现货 API |
| TradeAuditAgent | 审计/策略日志 | JSONL + Markdown + AI Wars upload 触发 |
| AiWarsLogUploaderAgent | AI Wars log 上传 | dotnet-file skill + 回执落盘 |

## 决策频率与性能（高频模式）

本仓库目前默认启用“高频模式”，目标是 **尽可能快地产出决策与可观测日志**：

- **DataCollector**：REST polling 默认 1 秒一次（WS 不可用时）
- **MarketSentimentAgent / TechnicalAnalystAgent**：最多 1 秒一次（并发互斥，避免堆积）
- **TradingCoordinatorAgent**：每个 tick 都尝试决策，且最短间隔 1 秒（并发互斥）

> 重要：即使设置为 1 秒一次，**真实频率仍取决于 LLM 响应耗时**。系统会保证“不会并发堆积把自己拖死”，因此采用 single-flight（同一时刻只跑一个决策）。

想调整频率（更快/更慢）：

- `trade/Aevatar.Trade/Agents/Data/DataCollectorAgent.cs`：REST polling 间隔
- `trade/Aevatar.Trade/Agents/Analysts/MarketSentimentAgent.cs`：`MinAnalysisIntervalSeconds`
- `trade/Aevatar.Trade/Agents/Analysts/TechnicalAnalystAgent.cs`：`MinAnalysisIntervalSeconds`
- `trade/Aevatar.Trade/Agents/Coordinator/TradingCoordinatorAgent.cs`：`DecisionMinIntervalSeconds`

## 常见问题排查（尤其是“有仓位但没看到决策”）

### 1) 明明没手动下单，为啥出现仓位/USDT Equity 浮动？

优先检查两件事：

- **Startup Guard 是否开启**：`Trading:MinBaseAssetUsdOnStart > 0` 且 Live 模式时，系统会自动下一个 `BOOTSTRAP_...` 的补仓单  
  - 现在会记录到 `trade-audit/*.md` 的 `Startup Guard` 区块
- **是否有外部仓位/外部成交**：比如你在交易所 App/其它脚本开过仓位，UI 也会通过 AI Wars API 拉到并展示

### 2) 半小时没有任何决策？

按顺序检查：

- 你是否执行了 `POST /api/trading/initialize` 与 `POST /api/trading/start`
- `GET /api/trading/status`：
  - `DataCollector` 的 ticks 是否持续增长
  - `TradingCoordinator` 的 Decisions 是否增长
- `trade-audit/`：
  - 是否生成了 `trade_audit_<runId>.md`
  - 是否持续追加 `Cycle` 段落（即便 HOLD 也会记录）
- LLM 配置是否可用（`LLMProviders` / `LLM` 的 key 是否正确）

### 3) AI Wars log 到底有没有上传？如何确认回执？

启用 `TradeAudit:RequestAiwarsUpload=true` 后：

- 生成 payload：`trade-audit/ai-wars/aiwars_upload_*.json`
- 上传回执：`trade-audit/ai-wars/receipts/aiwars_receipt_<requestId>.json`
- `trade-audit/*.md` 会追加 `AI Wars Upload` 小节（REQUESTED/SUCCESS/FAILED）

## 文档

- [📖 详细架构文档](./docs/ARCHITECTURE.md)
- [🧭 交易闭环（客户版）](./docs/TRADING_WORKFLOW.md)
- [🧠 Cognitive Mesh 集成计划](./docs/COGNITIVE_MESH_PLAN.md)
- [🖥️ 前端（演示 UI）](./docs/FRONTEND.md)
- [🧰 AI Wars API → DotNet Skills](./docs/AI_WARS_DOTNET_SKILLS.md)

## 开发计划

- [x] Week 1: 基础骨架 + WEEX API ✅
  - [x] Protobuf 消息定义
  - [x] WEEX REST API 封装
  - [x] WEEX WebSocket 封装
  - [x] DataCollectorAgent
  - [x] ExecutorAgent
- [x] Week 2: 分析 Agent 实现 ✅
  - [x] MarketSentimentAgent (AI)
  - [x] TechnicalAnalystAgent (AI)
- [x] Week 3: 决策 + 风控逻辑 ✅
  - [x] TradingCoordinatorAgent (AI)
  - [x] RiskManagerAgent (AI)
- [x] Week 4: 优化 + 演示准备 ✅
  - [x] 创建 Host 应用 (Aevatar.Trade.Api)
  - [x] 创建 Aspire AppHost
  - [ ] 集成测试
  - [x] 演示 UI（trade/frontend）
  - [ ] 演示视频

## 技术栈

- **框架**: Aevatar Agent Framework
- **运行时**: .NET 10（Local / Orleans 可切换）
- **AI**: Microsoft.Extensions.AI（MEAI Provider），支持 `LLMProviders` 统一配置
- **通信**: Protobuf + Event-Driven（层级路由 Up/Down/Both）
- **可观测性**: OpenTelemetry + `trade-audit`（JSONL + Markdown + 上传回执）
- **前端**: Vite + React + TypeScript

---

*WEEX AI Wars: Alpha Awakens*  
*Prize Pool: $880,000 🏆*
