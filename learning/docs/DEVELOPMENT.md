# Aevatar.Learning — Development Guide

## 本地开发：三种启动方式

> 端口策略：仓库内 **禁止使用 `:5000`**。默认后端 `:5678`，前端 `:5173`（可通过环境变量覆盖）。

### 方式 A：一键启动（推荐）

```bash
./learning/start.sh
```

- 默认：后端 + **Tauri 桌面端**
- Web 模式（仅 Vite）：`./learning/start.sh --web`

常用环境变量：
- `LEARNING_API_PORT`（默认 5678）
- `LEARNING_FRONTEND_PORT`（默认 5173）
- `LEARNING_NOTEBOOK_ROOT`（默认 `~/AevatarLearning/`）

### 方式 B：Aspire AppHost（推荐联调）

```bash
dotnet run --project learning/Aevatar.Learning.AppHost
```

说明：
- AppHost 通常编排 `dev:web`（Vite）更稳定
- `tauri dev` 建议用方式 A（本机 GUI 环境）

### 方式 C：分别启动（精细调试）

后端：

```bash
cd learning/src/Aevatar.Learning.Api
ASPNETCORE_URLS=http://localhost:5678 dotnet run --no-launch-profile
```

前端（Web）：

```bash
cd learning/frontend
export VITE_LEARNING_API_URL="http://localhost:5678"
npm run dev:web
```

前端（Tauri）：

```bash
cd learning/frontend
export VITE_LEARNING_API_URL="http://localhost:5678"
npm run dev
```

## 调试入口

- 健康检查：`GET http://localhost:5678/health`
- 诊断信息：`GET http://localhost:5678/api/info`
- AG‑UI SSE：`GET http://localhost:5678/api/sessions/{id}/agui/events`

## 常见问题排查

### 1) 端口被占用（启动报错）

快速查看监听进程：

```bash
lsof -nP -iTCP:5678 -sTCP:LISTEN
lsof -nP -iTCP:5173 -sTCP:LISTEN
```

杀掉监听（谨慎）：

```bash
kill -TERM <pid>
```

> `learning/start.sh` 默认会尝试自动清理端口（macOS/Linux，依赖 `lsof`）。

### 2) SSE 连不上 / 连接后无事件

确认顺序：
1. 先 `POST /api/sessions` 创建 session
2. 再 `GET /api/sessions/{id}/agui/events` 连接 SSE
3. 再 `POST /api/sessions/{id}/input` 触发 run

确认 SSE 响应头（应该包含）：
- `Content-Type: text/event-stream`
- `Cache-Control: no-store`
- `X-Accel-Buffering: no`

### 3) 断线重连后消息丢失

Learning 采用 **snapshot-first**：
- 重连时先发 `MESSAGES_SNAPSHOT`（必要）
- 可选 `STATE_SNAPSHOT`
- live stream 不做 replay（`replay:false`）

因此：
- 前端必须消费 `MESSAGES_SNAPSHOT` 做状态恢复
- 不应依赖“重放所有历史 token 事件”

### 4) Tauri 里请求被 CSP 拦截（console 报 connect-src）

Tauri 的 WebView 会受 CSP 限制，开发期需要允许连接本地端口：
- `http://localhost:*`
- `http://127.0.0.1:*`

参考：`novel/frontend/src-tauri/tauri.conf.json` 的 `app.security.csp` 配置方式。

### 5) LLM 调用超时 / 返回 TaskCanceledException

通常是 provider 的请求超时或网络中断：

- 调大：`LLMProviders:Providers:<name>:TimeoutMilliseconds`
- 检查：`Endpoint` 是否可达、API Key 是否正确、代理/网络是否稳定
- 用 `/api/info` 确认当前默认 provider 的非敏感诊断信息

## 开发习惯（非常重要）

- **跨边界类型必须 Protobuf**：Agent State / Stream Event / Config 等一律 `.proto` 生成
- **AG‑UI 事件 JSON 必须 camelCase**
- **不要绑定或示例化 `:5000`**（仓库 policy）


