## 开发指南

### 一键启动（推荐）

在 `scientific-research-assistant/` 下：

```bash
./start.sh
```

默认：
- Backend: `http://localhost:5678`
- Frontend: `http://localhost:5173`

### 分别启动

#### 后端

```bash
cd src/ScientificResearchAssistant.Api
ASPNETCORE_URLS=http://localhost:5678 dotnet run
```

验证：
- `GET http://localhost:5678/health`
- `GET http://localhost:5678/api/info`

#### 前端

```bash
cd frontend
SRA_API_PROXY_TARGET=http://localhost:5678 npm run dev -- --port 5173
```

### 常见问题

#### 1) 前端连接不上 /api/xxx

- 确认后端 `health` OK
- 确认前端代理环境变量：`SRA_API_PROXY_TARGET=http://localhost:5678`

#### 2) MCP tools 注册失败

- `MCP:Type=Http`：检查网络与 `MCP:HttpUrl`
- `MCP:Type=Docker`：检查 Docker Desktop 是否运行，以及 image 拉取是否成功
- 适当增大 `MCP:RequestTimeoutMs`

#### 3) UI 看不到 tool 调用

AG-UI tool 事件是 `CUSTOM`：
- `aevatar.scientific.tool_start`
- `aevatar.scientific.tool_end`

如果你在 UI 里完全看不到：
- 先检查 `tools_snapshot` 是否返回（侧栏 MCP Tools 数量）
- 再确认 run 触发成功（`RUN_STARTED` / `STEP_STARTED(chat)`）


