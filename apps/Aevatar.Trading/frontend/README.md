# Trade Frontend（演示 UI）

> 目标：给 `apps/Aevatar.Trading/` 下的交易系统提供一个可联调、可演示的 Web 前端（系统控制台 + WEEX 工具）。

## 快速开始（本地开发）

### 1) 启动后端 API（推荐 http profile）

在仓库根目录执行：

```bash
dotnet run --project apps/Aevatar.Trading/Aevatar.Trade.Api/Aevatar.Trade.Api.csproj --launch-profile http
```

默认 API 地址：`http://localhost:7100`  
Swagger：`http://localhost:7100/swagger`

### 2) 启动前端

```bash
cd apps/Aevatar.Trading/frontend
npm install
npm run dev
```

前端地址：`http://localhost:5173`

## 联调策略（为什么不会被 CORS/证书坑住）

- 前端请求统一走同源路径：`/api/*`
- `vite.config.ts` 会把这些请求代理到 `http://localhost:7100`
- 浏览器只看到同源请求 → 不需要 CORS
- 避免 `https` 自签证书导致的 fetch 被浏览器拦截

## 环境变量（可选）

默认不需要配置；如果你希望前端直接请求某个 API 地址（不走代理），可以设置：

- `VITE_API_BASE_URL`：例如 `http://localhost:7100`

## Aspire AppHost 一键启动（后端 + 前端一起管）

> 说明：AppHost 会以 “可执行资源” 的形式运行 `npm run dev`。  
> 你只需要确保 **本机已安装 Node.js/npm**，并且在第一次运行前执行过一次 `npm install`。

```bash
dotnet run --project trade/Aevatar.Trade.AppHost/Aevatar.Trade.AppHost.csproj --launch-profile http
```



