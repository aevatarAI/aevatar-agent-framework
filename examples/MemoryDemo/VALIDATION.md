## MemoryDemo 验证清单（File / MongoDB / Supabase / Neo4j）

本文件用于验证当前 MemoryDemo 的 **AI Memory 全链路**在不同后端下是否可用（无需改代码，仅改配置）。

> 建议：先跑通 `file`（零依赖），再切换到 MongoDB / Supabase / Neo4j。

---

### 0) 前置条件

- **.NET 10 SDK**
- 运行目录：

```bash
cd examples/MemoryDemo
```

- **LLM 可选**：
  - 没配 LLM 也能验证大部分能力：用 UI 的 **Seed** / **Seed Trace** 即可
  - 要验证向量检索，需要在 `appsettings.secrets.json` 里启用 embeddings（并配置 API key）

---

### 1) 配置开关（核心）

编辑 `examples/MemoryDemo/appsettings.json`：

- **`Aevatar:Persistence:MemoryStore`**: `file | mongodb | supabase`
- **`Aevatar:Persistence:MemoryVectorIndex`**: `file | mongodb | supabase`
- **`Aevatar:Persistence:MemoryGraph`**: `file | neo4j`

当你选择数据库后端时，通常在 `examples/MemoryDemo/appsettings.secrets.json` 里提供连接信息：

- `ConnectionStrings:MongoDB`
- `ConnectionStrings:SupabasePostgres`
- `Aevatar:Persistence:Neo4j:Password`

启动后打开 `http://localhost:5098`，在 **Demo Settings** 的 JSON 面板中确认：

- `persistence.providers.*` 与 `persistence.types.*` 是否符合预期

---

### 2) File（默认，零依赖基线）

#### 配置

保持默认即可：

- `Aevatar:Persistence:* = file`

#### 验证点（UI）

1. 点击 **Seed**（无需 LLM）  
2. 在 **search_memory** 输入 `aevatar-cqrs` → 看到 `cqrs_state` 命中
3. 在 **Memory Store**：
   - 点 **Resources** 能看到 `privateagent::<agentId>` 资源
   - 填 `privateagent::<agentId>` 点 **Entries** 能看到新增条目
   - 点 **Stats** 能看到 `entries.pb / manifest.json` 文件信息

#### 验证点（ExecutionTrace → MemoryGraph）

1. 点击 **Seed Trace**
2. 复制输出的 `executionId`
3. 点 **Memory Graph / Load**（executionId）  
   - 返回 JSON（来自 `FileMemoryGraphStore`，落在 trace bundle artifacts）
4. 在 **search_memory** 中设置 `memoryId = execution::<executionId>`，搜索 `aevatar-trace-graph`  
   - 能命中 execution-scoped MemoryEntry（由投影器写入 MemoryStore）

---

### 3) MongoDB（MemoryStore + VectorIndex）

#### 配置

1) `appsettings.json`：

- `Aevatar:Persistence:MemoryStore = mongodb`
- `Aevatar:Persistence:MemoryVectorIndex = mongodb`

2) `appsettings.secrets.json`：

- `ConnectionStrings:MongoDB = "mongodb://localhost:27017"`（示例）
- `MongoDB:Database = "aevatar"`（可选，默认 aevatar）

#### 验证点

1. 启动后在 `persistence.providers` 中看到 `mongodb`
2. 点击 **Seed** / 正常聊天几轮（确保写入 long-term memory 开关为 ON）
3. **Memory Store / Resources + Entries** 能正常返回数据
4. 向量检索验证（可选）：
   - embeddings 配好后，用 **Vector Search** 查询（MongoDB 实现为“MongoDB 过滤 + 进程内 cosine brute-force”）

> 说明：`/api/memory/stats` / `/api/vector/stats` 只对 file 后端有效，MongoDB 后端会返回错误信息（预期行为）。

---

### 4) Supabase(Postgres + pgvector)（MemoryStore + VectorIndex）

#### 配置

1) `appsettings.json`：

- `Aevatar:Persistence:MemoryStore = supabase`
- `Aevatar:Persistence:MemoryVectorIndex = supabase`
- `Aevatar:Persistence:SupabaseMemory:VectorDimensions = <你的 embedding 维度>`（必须匹配）

2) `appsettings.secrets.json`：

- `ConnectionStrings:SupabasePostgres = "<your postgres connection string>"`

#### 验证点

1. 启动后在 `persistence.providers` 中看到 `supabase`
2. 点击 **Seed** / 聊天几轮
3. **Memory Store** 的 Resources/Entries 正常工作
4. embeddings 配好后，用 **Vector Search** 验证 pgvector top-k

> 如果你不想自动建表/建索引：将 `Aevatar:Persistence:SupabaseMemory:AutoCreate*` 和 `LockDownPublicAccess` 全关（但需要你自己先执行 schema）。

---

### 5) Neo4j（MemoryGraph）

Neo4j 目前用于 **Layer 4.2 MemoryGraph**（ExecutionTrace → Graph 投影落库），与 MemoryStore/VectorIndex 可独立组合。

#### 配置

1) `appsettings.json`：

- `Aevatar:Persistence:MemoryGraph = neo4j`
- `Aevatar:Persistence:Neo4j:Uri = "bolt://localhost:7687"`
- `Aevatar:Persistence:Neo4j:Username = "neo4j"`

2) `appsettings.secrets.json`：

- `Aevatar:Persistence:Neo4j:Password = "<password>"`

#### 验证点

1. 点击 **Seed Trace**
2. 使用 `executionId` 点 **Memory Graph / Load**
3. 如果返回 JSON，说明 `IMemoryGraphStore` 已切换到 `Neo4jMemoryGraphStore` 并可读写

---

### 6) 常见问题排查

- **启动时报 “MongoDB selected but ConnectionStrings:MongoDB is empty”**  
  - 说明你把 `MemoryStore/VectorIndex` 选成 mongodb 但没配置连接串

- **Supabase 报 VectorDimensions 必填**  
  - 需要在 `Aevatar:Persistence:SupabaseMemory:VectorDimensions` 配置维度（并与 embeddings 一致）

- **Neo4j 报 Uri/Username/Password 未配置**  
  - 需要补齐 `Aevatar:Persistence:Neo4j:*`


