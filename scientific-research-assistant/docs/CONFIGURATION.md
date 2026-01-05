## 配置说明

### 1) LLMProviders（必需）

后端读取 `src/ScientificResearchAssistant.Api/appsettings.secrets.json`（可选，但推荐）里的 `LLMProviders`：

- **默认 provider**：`LLMProviders:Default`
- **providers 列表**：`LLMProviders:Providers:*`

建议做法：
- 把真实密钥放在 `appsettings.secrets.json`（本地文件、不要提交）
- 仓库内提供 `appsettings.secrets.json.example` 作为模板

### 2) MCP（Claude Scientific Skills）

`src/ScientificResearchAssistant.Api/appsettings.json` 的 `MCP` 控制 skills MCP 连接方式：

- **Http（默认）**：连接 K-Dense 提供的 hosted MCP server
- **Docker**：本地启动 `ghcr.io/k-dense-ai/claude-scientific-skills:latest`（更隐私）

字段：
- `MCP:Type`: `"Http"` | `"Docker"`
- `MCP:HttpUrl`: hosted MCP URL
- `MCP:DockerImage`: docker image
- `MCP:RequestTimeoutMs`: 单次调用超时（毫秒）

### 3) Materials（vibe researching grounding）

`src/ScientificResearchAssistant.Api/appsettings.json` 的 `Materials` 控制本地资料读取：

- `Materials:FactsDir`：默认 `facts`（相对 `scientific-research-assistant/` 根目录）
- `Materials:SourcesDir`：默认 `sources`（相对 `scientific-research-assistant/` 根目录）
- `Materials:MaxContextChars`：注入 LLM 的总字符上限（避免上下文爆炸）

目录约定（两层语义）：

- `sources/`：来源资料（可引用，不要求写进去就为真）
- `facts/`：已验证结论（希望可当作事实依赖）

可选写回（写入文件真相层；默认安全关闭）：

- `Materials:AllowWrite`：默认 `false`
- 当前写入用途：
  - `POST /api/sessions/{id}/facts`：创建 `facts_proposed/` 下的候选事实
  - promote 时写入 `facts/`

### 4) Python 验证（可选，默认关闭）

`Python:Enabled=false`（默认）时，`python_exec` 不会注册，模型也无法调用。

启用方式：

- `src/ScientificResearchAssistant.Api/appsettings.json`：`Python:Enabled=true`
- 可选覆盖 python 可执行文件：
  - `SRA_PYTHON_BIN=python3`（默认就是 `python3`）

相关配置：
- `Python:TimeoutMs`
- `Python:MaxOutputChars`

### 5) 端口与环境变量

- **后端端口**：默认 `5678`（仓库政策：禁止 `5000`）
  - `BACKEND_PORT=5679 ./start.sh` 覆盖
  - 或 `ASPNETCORE_URLS=http://localhost:5678`
- **前端端口**：默认 `5173`
  - `FRONTEND_PORT=5174 ./start.sh` 覆盖
- **前端代理目标**：`SRA_API_PROXY_TARGET`
  - `SRA_API_PROXY_TARGET=http://localhost:5678 npm run dev`


