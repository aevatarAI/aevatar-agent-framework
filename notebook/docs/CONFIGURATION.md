# Aevatar.Notebook — Configuration

## 配置文件位置（规范化后）

- **主配置**：`notebook/src/Aevatar.Notebook.Api/appsettings.json`
- **用户级 secrets（加密，推荐）**：默认 `~/.aevatar/secrets.json`（用 `src/Aevatar.Agents.SecretsCli` 写入；可用 `AEVATAR_SECRETS_PATH/AEVATAR_SECRETS_DIR` 覆盖）
- **可选项目级 secrets（gitignored）**：`notebook/src/Aevatar.Notebook.Api/appsettings.secrets.json`
  - 示例：`notebook/src/Aevatar.Notebook.Api/appsettings.secrets.json.example`

## LLMProviders（必须配置）

Notebook 使用 `LLMProviders`（多 provider 可选）：

- `LLMProviders:Default`：默认 provider 名称
- `LLMProviders:Providers:<name>`：provider 具体配置

推荐做法：

```bash
cd notebook/src/Aevatar.Notebook.Api
# 方式 A（推荐）：把 key 写入用户级 secrets（一次配置，多系统复用）
# 方式 B（项目级覆盖）：复制 example（不要提交真实密钥）
cp appsettings.secrets.json.example appsettings.secrets.json
```

## Persistence（可选，配置驱动）

配置段：`Aevatar:Persistence`

- `Aevatar:Persistence:MemoryStore`: `file | mongodb | supabase`
- `Aevatar:Persistence:MemoryVectorIndex`: `file | mongodb | supabase`
- `Aevatar:Persistence:MemoryGraph`: `file | neo4j`

当选择 DB provider 时，需要在 `appsettings.secrets.json`（或环境变量）提供连接信息：

- `ConnectionStrings:MongoDB`
- `ConnectionStrings:SupabasePostgres`
- `Aevatar:Persistence:Neo4j:Uri/Username/Password`

## Long-running runs（断线续跑）

- `Aevatar:Notebook:ContinueReportOnDisconnect`：是否允许 **Report 生成在浏览器断线/刷新后继续运行**。
  - `true`：断线后停止推流，但后台继续生成；最终会写入 `report::<reportId>`，刷新 Reports 列表即可看到。
  - `false`：断线会取消本次生成（节省 token）。

- `Aevatar:Notebook:ContinueChatOnDisconnect`：是否允许 **Chat 生成在浏览器断线/刷新后继续运行**。
  - `true`：断线后停止推流，但后台继续生成；最终 assistant 回复会写入 `State.History`，刷新页面后聊天区会自动恢复历史（含最新回复）。
  - `false`：断线会取消本次生成（节省 token）。

## Ports（仓库政策）

- **禁止** `:5000`
- 推荐：
  - Notebook UI / API：`5678`（`/`, `/health`, `/api/*`）
  - 如 `5678` 被占用，可使用任意未占用端口

可用环境变量覆盖（用于 `start.sh`）：

- `API_PORT`


