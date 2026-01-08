## 配置说明

### 1) LLMProviders（必需）

后端通过 `IConfiguration` 读取 `LLMProviders`（支持全局 secrets + 项目级覆盖）：

- **默认 provider**：`LLMProviders:Default`
- **providers 列表**：`LLMProviders:Providers:*`

建议做法：
- **推荐（一次配置，全仓复用）**：把真实密钥写入用户级 secrets（加密）：
  - 默认：`~/.aevatar/secrets.json`
  - 覆盖：`AEVATAR_SECRETS_PATH` / `AEVATAR_SECRETS_DIR`
  - key 示例：`LLMProviders:Providers:deepseek:ApiKey`
  - 写入方式：
    - UI：前端左侧栏 **Set API Key**
    - Secrets App：`apps/Aevatar.Secrets.Api`（Web）
    - CLI：`src/Aevatar.Agents.SecretsCli`（见 `src/Aevatar.Agents.Core/docs/UserSecrets.md`）
- **可选（项目级覆盖）**：`src/ScientificResearchAssistant.Api/appsettings.secrets.json`（本地文件、不要提交）
  - 仓库内提供 `appsettings.secrets.json.example` 作为模板

### 2) MCP（Model Context Protocol / 外部工具）

科研助手使用框架内置的 MCP 集成（`AIGAgentBase` 在初始化 tools 时 **best-effort 自动连接**），配置风格参考 Cursor：`mcpServers`。

位置：
- 推荐：`src/ScientificResearchAssistant.Api/appsettings.json` 的 `MCP:mcpServers`
- 也支持：直接提供 Cursor 原生 `mcp.json`（root 级 `mcpServers`）
  - 本项目已在 `Program.cs` 中 `AddJsonFile("mcp.json", optional: true)`，放在 `src/ScientificResearchAssistant.Api/mcp.json` 即可（不提交，参考 `mcp.json.example`）

与 oh-my-opencode 的关系（你选的集成路径 A）：
- `oh-my-opencode` 默认送 `context7` / `grep_app` 等 curated MCP（可通过其配置禁用）[[oh-my-opencode README.zh-cn](https://raw.githubusercontent.com/code-yeongyu/oh-my-opencode/dev/README.zh-cn.md)]
- Aevatar 这边不需要“集成插件本体”，只要把 **同一份 Cursor/OpenCode 风格的 `mcpServers` 配置**（例如你本机的 `~/.cursor/mcp.json`）拷贝/同步到 `src/ScientificResearchAssistant.Api/mcp.json` 即可自动连接并注册工具。
- 仓库内的 `src/ScientificResearchAssistant.Api/mcp.json.example` 已预留了 `context7`（HTTP/SSE）与 `grep_app`（stdio/npx）的字段模板；把 `enabled` 打开并填好 `url/authToken/command/args` 就能跑。

关键字段：
- `MCP:autoConnect`: 是否自动连接（默认 `true`）
- `MCP:namespaceTools`: 是否对 MCP 工具名做命名空间前缀（默认 `true`，避免多 server 重名；格式 `mcp__{serverKey}__{toolName}`）
- `MCP:retryMinIntervalSeconds`: 会话触发的 MCP 重连最小间隔（默认 30 秒；设为 0 表示不节流，更激进）
- `MCP:mcpServers:<serverKey>`: 每个 MCP server 的配置

合并与优先级（更激进、尽可能用已配置能力）：
- 同时支持三种来源并做 merge：
  - legacy：`MCP:Type/HttpUrl/DockerImage/RequestTimeoutMs`（最低优先级）
  - `MCP:mcpServers`（中）
  - `mcp.json` 的 root `mcpServers`（最高；同名 server 以这里为准）

示例（K-Dense Claude Scientific Skills / Hosted + Docker 二选一）见 `appsettings.json` 里的默认配置。

### 2.5) Git Skill Packs（本地 repo 自动同步，可选，支持多个 repo）

如果你希望 **LLM 能直接读取/执行 skill 目录内的 `scripts/`**（闭环跑起来），推荐把 skills repo 下载到本地并自动更新：

- 上游示例 repo（K-Dense）：[K-Dense-AI/claude-scientific-skills](https://github.com/K-Dense-AI/claude-scientific-skills/tree/main)
- 启动时后端 best-effort `git clone/pull`（失败不阻塞服务启动）
- 同步成功后，会把每个 pack 的 `SkillsSubDir` 目录加入 `AEVATAR_AGENT_SKILLS_DIRS`，供 `find_helpful_skills/skills_load` 发现
- 下载目录默认在本项目根目录的 `.skillpacks/`（已加入 `.gitignore`：谁用谁下载，不进仓库）

可选：语义检索（embeddings）
- 如果配置了 `LLMProviders:Embeddings`（全局默认）或 `LLMProviders:Providers:<name>:Embeddings`（provider 覆盖），sync 时会 best-effort 构建 embeddings 索引到 `.skillpacks/.index/`
- `find_helpful_skills` 会优先使用该索引做语义排序（索引缺失时会 query 时惰性构建）

#### 配置方式 A（推荐）：`skillpacks.json`（可多 repo）

位置：`src/ScientificResearchAssistant.Api/skillpacks.json`（不提交，参考 `skillpacks.json.example`）

核心字段（每个 pack 至少需要）：
- `RepoUrl`：git repo URL
- `SkillsSubDir`：repo 内 skills 根目录（例如 K-Dense 是 `scientific-skills`）

可选（更激进的“失败自动重试”节流）：
- `SkillPacks:RetryMinIntervalSeconds`：会话触发的后台重试最小间隔（默认 60 秒；太小会频繁 git pull）

#### 手动同步命令（只执行 sync，然后退出）

```bash
cd scientific-research-assistant/src/ScientificResearchAssistant.Api
dotnet run -- --sync-skills
```

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


