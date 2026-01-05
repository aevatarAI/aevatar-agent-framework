# Aevatar.Notebook — Configuration

## 配置文件位置（规范化后）

- **主配置**：`notebook/src/Aevatar.Notebook.Api/appsettings.json`
- **可选 secrets（gitignored）**：`notebook/src/Aevatar.Notebook.Api/appsettings.secrets.json`
  - 示例：`notebook/src/Aevatar.Notebook.Api/appsettings.secrets.json.example`

## LLMProviders（必须配置）

Notebook 使用 `LLMProviders`（多 provider 可选）：

- `LLMProviders:Default`：默认 provider 名称
- `LLMProviders:Providers:<name>`：provider 具体配置

推荐做法：

```bash
cd notebook/src/Aevatar.Notebook.Api
cp appsettings.secrets.json.example appsettings.secrets.json
# 然后填入 ApiKey / Endpoint / Model
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

## Ports（仓库政策）

- **禁止** `:5000`
- 默认：
  - Notebook UI / API：`5678`（`/`, `/health`, `/api/*`）

可用环境变量覆盖（用于 `start.sh`）：

- `API_PORT`


