## 开发指南

### 一键启动（推荐）

在 `scientific-research-assistant/` 下：

```bash
./start.sh
```

默认：
- Backend: `http://localhost:5678`
- Frontend: `http://localhost:5173`

### Aspire AppHost（可选）

如果你希望由 Aspire Dashboard 统一编排前后端：

```bash
cd ScientificResearchAssistant.AppHost
dotnet run
```

默认端口：
- Backend: `5678`
- Frontend: `5173`

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

#### 4) vibe 模式读不到 facts / sources

- 检查 `facts/` 与 `sources/` 下是否存在 `.md` / `.txt`
- 检查 `src/ScientificResearchAssistant.Api/appsettings.json` 的 `Materials:FactsDir` / `Materials:SourcesDir`
- UI 里打开 `Workspace`（STATE_SNAPSHOT）确认 materials 是否被加载

#### 5) python_exec 不可用

- 默认 `Python:Enabled=false`（安全策略）
- 开启后重启后端：`Python:Enabled=true`
- 确保本机 `python3` 可用（或用 `SRA_PYTHON_BIN` 指定）


