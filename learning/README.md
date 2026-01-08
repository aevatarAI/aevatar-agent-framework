# Aevatar.Learning（AI Native 学习系统）

> AI Native：没有 AI（LLM Provider）就不成立。核心能力（问答/整理报告/专题百科/测验/学习卡片/skills 生成）都依赖可配置的 LLMProviders。

## 快速开始（推荐）

> 端口策略：**禁止使用 `:5000`**；默认后端 `:5678`，前端（Vite dev）`:5173`。

```bash
./learning/start.sh
```

- 默认启动 **Tauri 桌面端**（`--tauri`）+ 后端 API
- 如只启动 Web（Vite）：

```bash
./learning/start.sh --web
```

启动后：
- Backend API: `http://localhost:5678`
- Frontend: `http://localhost:5173`

## Aspire AppHost（推荐用于联调与可观测）

```bash
dotnet run --project learning/Aevatar.Learning.AppHost
```

## 分别启动（调试用）

### 1) 启动后端

```bash
cd learning/src/Aevatar.Learning.Api
ASPNETCORE_URLS=http://localhost:5678 dotnet run --no-launch-profile
```

健康检查：`http://localhost:5678/health`  
诊断信息：`http://localhost:5678/api/info`

### 2) 启动前端（Web）

```bash
cd learning/frontend
export VITE_LEARNING_API_URL="http://localhost:5678"
npm run dev:web
```

### 3) 启动前端（Tauri 桌面端）

```bash
cd learning/frontend
export VITE_LEARNING_API_URL="http://localhost:5678"
npm run dev
```

## 配置入口

- **LLMProviders**：
  - 推荐：把 key 写入用户级 secrets（加密，一次配置多系统复用），默认 `~/.aevatar/secrets.json`（用 `src/Aevatar.Agents.SecretsCli` 写入）
    - 可用 `AEVATAR_SECRETS_PATH/AEVATAR_SECRETS_DIR` 覆盖
  - 可选：项目级 secrets（gitignored，便于覆盖/模板）
    - `learning/src/Aevatar.Learning.Api/appsettings.secrets.json.example`
    - → `learning/src/Aevatar.Learning.Api/appsettings.secrets.json`
- 详细说明见：`learning/docs/CONFIGURATION.md`

## 最小 API（MVP skeleton）

后端提供最小会话模型（`threadId=sessionId`）与 AG‑UI SSE：

- `GET  /health`
- `GET  /api/info`
- `POST /api/sessions`
- `POST /api/sessions/{id}/input`
- `GET  /api/sessions/{id}/agui/events`（SSE，**snapshot-first**）

## 文档

- 架构：`learning/docs/ARCHITECTURE.md`
- 配置：`learning/docs/CONFIGURATION.md`
- 开发：`learning/docs/DEVELOPMENT.md`


