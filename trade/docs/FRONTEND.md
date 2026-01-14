# Trade Frontend（演示 UI）

> 目标：为 `trade/` 的多智能体交易系统提供一个“可控、可联调、可演示”的 Web UI。  
> 关键词：**同源**、**最少分支**、**接口对齐**、**可视化优先**。

## 目录结构

```
trade/frontend/
├── README.md            # 如何启动/联调（给第一次上手的人）
├── package.json         # 依赖与脚本（dev/build/preview）
├── vite.config.ts       # 本地同源代理：/api -> http://localhost:7100
├── tsconfig.json
├── index.html
└── src/
    ├── App.tsx          # Tabs：Trading / WEEX Tools / AI Wars APIs
    ├── main.tsx         # React 入口
    ├── api.ts           # fetch 封装（统一错误处理）
    ├── types.ts         # 后端 DTO（只定义 UI 用到的字段）
    ├── styles.css       # 轻量 UI（无额外 UI 依赖）
    ├── components/      # Button / Panel / StatusPill
    └── pages/           # TradingPage（Auto Trading Dashboard）/ WeexPage / AiWarsPage
```

## 联调原则（让特殊情况消失）

### 1) 前端只认一个入口：`/api/*`

- UI 里所有请求都使用相对路径（例如：`/api/trading/status`）
- 开发期由 `vite.config.ts` 代理到后端
- 好处：浏览器层面是同源请求 → **不需要 CORS**，也不被 **https 自签证书** 影响

### 2) 需要直连后端时，用环境变量而不是改代码

- `VITE_API_BASE_URL`：例如 `http://localhost:7100`

## 页面与后端接口映射

### Trading（系统控制台）

- `POST /api/trading/initialize`
- `POST /api/trading/start`
- `POST /api/trading/stop?reason=...`
- `POST /api/trading/sync-account`
- `GET /api/trading/status`
- `GET /api/agents`
- `GET /api/meta`                      # 安全配置快照（symbol/interval/mode，无 secrets）
- `GET /api/audit/latest?maxBytes=...` # 最新策略日志（Markdown tail）
- `GET /api/audit/files`               # 审计文件列表
- `GET /api/audit/tail?name=...`       # 读取指定文件尾部（md/jsonl）
- `GET /api/audit/stream`              # SSE：实时推送 trade-audit/*.jsonl 新增行（用于 Streaming Cards）

### WEEX Tools（调试工具箱）

- `GET /api/weex-test/ticker?symbol=...`
- `GET /api/weex-test/balances`
- `GET /api/weex-test/open-orders?symbol=...`
- `POST /api/weex-test/place-order`
- `POST /api/weex-test/cancel-order?symbol=...&orderId=...&clientOrderId=...`

### AI Wars APIs（DotNetSkills 可视化）

- `GET /api/ai-wars`                 # 工具索引（每个 tool 对应一个 WEEX AI Wars endpoint）
- `POST /api/ai-wars/{toolName}`     # 执行某个 tool（危险/需要确认：加 `?confirm=true`）

## 改进建议（下一步）

- ✅ **可观测性时间线**：已在 Dashboard 中以 `trade-audit/*.md` 的方式呈现（按 cycle 汇总）
- ✅ **AI 输出流（Streaming Cards）**：Dashboard 新增基于 SSE 的 per-symbol 卡片流（展开可看 raw JSON）
- **安全护栏**：在 UI 层增加 “Live 下单” 二次确认（默认提示风险）
- **状态推送**：未来可把更多状态（仓位/订单/LLM 进度）也改成 SSE 推送，减少轮询

## Aspire 一键启动（推荐）

如果你希望“后端 + 前端”一起被 Aspire Dashboard 管控：

```bash
dotnet run --project trade/Aevatar.Trade.AppHost/Aevatar.Trade.AppHost.csproj --launch-profile http
```

启动后：

- Trading API：`http://localhost:7100/swagger`
- Frontend：`http://localhost:5173`


