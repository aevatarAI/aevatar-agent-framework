## 配置说明

### 1) LLMProviders（必需）

后端读取 `src/ScientificResearchAssistant.Api/appsettings.secrets.json`（可选，但推荐）里的 `LLMProviders`：

- **默认 provider**：`LLMProviders:Default`
- **providers 列表**：`LLMProviders:Providers:*`

建议做法：
- 把真实密钥放在 `appsettings.secrets.json`（本地文件、不要提交）
- 仓库内提供 `appsettings.secrets.example.json` 作为模板

### 2) MCP（Claude Scientific Skills）

`src/ScientificResearchAssistant.Api/appsettings.json` 的 `MCP` 控制 skills MCP 连接方式：

- **Http（默认）**：连接 K-Dense 提供的 hosted MCP server
- **Docker**：本地启动 `ghcr.io/k-dense-ai/claude-scientific-skills:latest`（更隐私）

字段：
- `MCP:Type`: `"Http"` | `"Docker"`
- `MCP:HttpUrl`: hosted MCP URL
- `MCP:DockerImage`: docker image
- `MCP:RequestTimeoutMs`: 单次调用超时（毫秒）

### 3) 端口与环境变量

- **后端端口**：默认 `5678`（仓库政策：禁止 `5000`）
  - `BACKEND_PORT=5679 ./start.sh` 覆盖
  - 或 `ASPNETCORE_URLS=http://localhost:5678`
- **前端端口**：默认 `5173`
  - `FRONTEND_PORT=5174 ./start.sh` 覆盖
- **前端代理目标**：`SRA_API_PROXY_TARGET`
  - `SRA_API_PROXY_TARGET=http://localhost:5678 npm run dev`


