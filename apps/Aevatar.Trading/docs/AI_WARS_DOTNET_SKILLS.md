# AI Wars API → DotNet File Skills（全量映射）

> 目标：把 WEEX AI Wars 文档里的 **每个 API endpoint** 做成一个独立的 `dotnet run --file` 脚本，供 `DotNetFileSkillTool` 直接调用。  
> 文档入口：[AI Wars APIs 概览](https://www.weex.com/api-doc/ai/intro)

## 设计原则（为什么要“一 API 一文件”）

- **最小权限**：危险 API（下单/撤单/调杠杆）用 manifest 标注 `isDangerous=true`，默认不会被 agent 暴露/执行。
- **最小语义**：一个文件只做一件事（一个 endpoint），避免“万能客户端”变成泥团。
- **可审计**：每个脚本自带 method/path/参数清单，便于代码审阅与赛后复盘。

## 目录结构（按类别拆分，单目录 ≤ 8 文件）

```
apps/Aevatar.Trading/Aevatar.Trade/Tools/DotNetSkills/ai-wars/
├── market/               # 行情（公开接口）
│   ├── core/             # time/contracts/depth/ticker(s)/trades
│   └── rates/            # candles/index/open_interest/funding_rate
├── account/              # 账户（鉴权接口）
│   ├── assets/
│   ├── config/           # leverage/margin/mode
│   └── positions/
├── apps/Aevatar.Trading/                # 交易（鉴权接口，部分危险）
│   ├── orders/
│   ├── pending-orders/
│   └── risk-actions/     # close/cancel-all/tp-sl
└── upload/               # 上传 AI log（鉴权接口）
```

## 认证与签名（统一规则）

- **BaseUrl**：默认 `WEEX_BASE_URL=https://api-contract.weex.com`
- **Env**：
  - `WEEX_API_KEY`
  - `WEEX_API_SECRET`
  - `WEEX_PASSPHRASE`
  - 可选 `WEEX_LOCALE`（默认 `en-US`）
- **签名**：按参赛指南示例的拼接规则（`timestamp + method + request_path + query_string + body`）。参见：[AI Wars: Participant Guide](https://www.weex.com/api-doc/ai/introduction/ParticipantGuide)

## 运行方式（DotNetFileSkillTool）

这些脚本会在运行时由 `RiskManagerAgent` 扫描并注册（见 `TradeDotNetSkillPaths.WeexAiWarsAll`）。  
你也可以单独用 `dotnet run --file <path>` 手动验证。

另外，为了“可见可调”，后端也会把这些 skills 暴露为 HTTP endpoints（Swagger/前端可直接点）：

- `GET /api/ai-wars`：列出所有 toolName / 参数 / route
- `POST /api/ai-wars/{toolName}`：执行某个 tool（危险/需要确认：加 `?confirm=true`）

## 变更记录

- 2025-12-28：新增 `ai-wars/` 全量 endpoint skills（Market/Account/Trade/Upload AI log），并在 agent 启动时自动注册。


