# Trade Frontend（演示 UI）

> 目标：为 `apps/Aevatar.Trading/` 的多智能体交易系统提供一个“可控、可联调、可演示”的 Web UI。  
> 关键词：**同源**、**最少分支**、**接口对齐**、**可视化优先**。

## 目录结构

```
apps/Aevatar.Trading/frontend/
├── README.md            # 如何启动/联调（给第一次上手的人）
├── package.json         # 依赖与脚本（dev/build/preview）
├── vite.config.ts       # 本地同源代理：/api -> http://localhost:7100
├── tsconfig.json
├── index.html
└── src/
    ├── App.tsx          # Tabs：Decision Center / Exchange Tools / AI Wars APIs
    ├── main.tsx         # React 入口
    ├── api.ts           # fetch 封装（统一错误处理）
    ├── types.ts         # 后端 DTO（只定义 UI 用到的字段）
    ├── styles.css       # 轻量 UI（无额外 UI 依赖）
    ├── components/      # Button / Panel / StatusPill
    └── pages/           # TradingPage（决策控制台）/ WeexPage / AiWarsPage
```

## 联调原则（让特殊情况消失）

### 1) 前端只认一个入口：`/api/*`

- UI 里所有请求都使用相对路径（例如：`/api/trading/status`）
- 开发期由 `vite.config.ts` 代理到后端
- 好处：浏览器层面是同源请求 → **不需要 CORS**，也不被 **https 自签证书** 影响

### 2) 需要直连后端时，用环境变量而不是改代码

- `VITE_API_BASE_URL`：例如 `http://localhost:7100`

## 页面与后端接口映射

### Decision Center（决策控制台）

- `POST /api/trading/initialize`
- `POST /api/trading/start`
- `POST /api/trading/stop?reason=...`
- `POST /api/trading/sync-account`
- `GET /api/trading/status`
- `GET /api/agents`
- `GET /api/meta`                      # 安全配置快照（symbol/interval/mode，无 secrets）
- `GET /api/policy`                    # 策略参数快照
- `POST /api/policy`                   # 策略更新（含校验）
- `POST /api/decision/trigger`         # 手工触发决策
- `GET /api/positions`                 # 仓位（交易所无关）
- `GET /api/audit/latest?maxBytes=...` # 最新策略日志（Markdown tail）
- `GET /api/audit/files`               # 审计文件列表
- `GET /api/audit/tail?name=...`       # 读取指定文件尾部（md/jsonl）
- `GET /api/agui/events`               # AG-UI SSE（决策/聊天流式展示）
- `GET /api/agui/messages`             # AG-UI 快照（SSE 断链兜底）
- `POST /api/agui/chat`                # 用户输入 → AI 交互

### Exchange Tools（调试工具箱）

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
- **安全护栏**：在 UI 层增加 “Live 下单” 二次确认（默认提示风险）
- ✅ **AG-UI Streaming**：已接入 `/api/agui/events`，用于前端 AI Chat streaming 卡片

## Aspire 一键启动（推荐）

如果你希望“后端 + 前端”一起被 Aspire Dashboard 管控：

```bash
dotnet run --project apps/Aevatar.Trading/Aevatar.Trade.AppHost/Aevatar.Trade.AppHost.csproj --launch-profile http
```

启动后：

- Trading API：`http://localhost:7100/swagger`
- Frontend：`http://localhost:5173`


