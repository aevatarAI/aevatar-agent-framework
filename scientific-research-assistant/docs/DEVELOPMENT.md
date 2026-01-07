## 开发指南

### 一键启动（推荐）

在 `scientific-research-assistant/` 下：

```bash
./start.sh
```

默认：
- Backend: `http://localhost:5678`
- Frontend: `http://localhost:5173`

### 同步 Skill Packs（可选）

只要你配置了 `skillpacks.json` 且某个 pack `Enabled=true`，后端启动时会 best-effort 自动同步（失败不阻塞启动；后续每次会话/请求也会 best-effort 触发一次重试）。

你也可以手动执行一次同步（只 sync，然后退出）：

```bash
cd src/ScientificResearchAssistant.Api
dotnet run -- --sync-skills
```

### 前端一键更新（无需重启）

左侧栏新增按钮 **Update Skills**：
- 会调用后端 `POST /api/skills/sync`
- 后端执行 git clone/pull（best-effort），并把本地 skills root 追加到 `AEVATAR_AGENT_SKILLS_DIRS`
- 更新完成后，LLM 下一次调用 `skills_list/skills_load` 就能看到新技能（无需重启）

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

- 检查 `src/ScientificResearchAssistant.Api/appsettings.json` 的 `MCP:mcpServers` 是否配置正确
- `url` 场景：检查网络连通性 + hosted MCP 是否可用
- `command: docker` 场景：检查 Docker Desktop 是否运行，以及 image 是否可拉取
- 适当增大对应 server 的 `timeoutMs`

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

#### 5) Paper Collaboration / facts_proposed 不工作

- 默认写入是关闭的：确认 `src/ScientificResearchAssistant.Api/appsettings.json` 的 `Materials:AllowWrite=true`
- 通过 `GET /api/sessions/{id}/workspace` 看文件快照（facts/facts_proposed/sources 计数与最近提案）
- 检查 `workspace/sessions/{id}/` 下是否生成：
  - `paper/`、`facts_proposed/`、`decisions/`、`mailbox/`、`runs/`、`artifacts/`

#### 6) 运行 PaperCollab 集成测试

```bash
dotnet test scientific-research-assistant/test/ScientificResearchAssistant.Api.Tests/ScientificResearchAssistant.Api.Tests.csproj
```

#### 7) python_exec 不可用

- 默认 `Python:Enabled=false`（安全策略）
- 开启后重启后端：`Python:Enabled=true`
- 确保本机 `python3` 可用（或用 `SRA_PYTHON_BIN` 指定）


