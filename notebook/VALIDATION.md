# Aevatar.Notebook — VALIDATION (E2E Checklist)

本文件用于验证 `notebook/Aevatar.Notebook` 在不同持久化后端组合下可用（Sources / Q&A / Report / Trace / Graph）。

> 原则：**先跑 file**（零依赖），再切 MongoDB/Supabase/Neo4j。

---

## 0. 启动

在仓库根目录执行（推荐）：

```bash
./notebook/start.sh
```

默认地址见 `notebook/src/Aevatar.Notebook.Api/appsettings.json`：`http://localhost:5678`

打开浏览器访问：`http://localhost:5678`

---

## 1. File 后端（默认）

### 1.1 验证 /api/info

- 打开页面顶部 badge 应显示 `ready`
- 在中间栏 `chatMeta` 里确认：
  - `persistence.providers.memoryStore == file`
  - `persistence.providers.memoryVectorIndex == file`
  - `persistence.providers.memoryGraph == file`

### 1.2 Sources（文本 + 上传）

- **文本添加**：
  - 左栏输入 title（可空）+ 粘贴文本
  - 点击 **Add**
  - 预期：聊天窗口出现 `Source added: <id>`

- **上传添加**：
  - 选择一个 `.txt` 文件（<= 1MB）
  - 点击 **Upload**
  - 预期：出现 `Source uploaded: <id>`

- 点击 **Refresh**：
  - 预期：sources 列表出现条目，显示 entryCount / latestAt

### 1.3 Source 选择与引用跳转

- 勾选 1~2 个 source
- 中间栏提问：`总结一下这些资料里关于 X 的关键点`
  - 预期：回答下方出现 citations chip（形如 `sourceId:chunkId`）
- 点击 citation chip：
  - 预期：左栏 `sourceDetail` 显示对应 chunk 命中（`tags.chunk_id == chunkId`）

### 1.4 Q&A：context 注入 + trace 记录 + graph 投影

- 再问一个问题：
  - 预期：接口返回包含 `executionId`
  - 预期：`citations` 数量 > 0（当有 sources 时）

- 验证 trace 产物（默认落到 `${AEVATAR_TRACE_DIR}` 或仓库根 `trace/`，由 Core 默认逻辑决定）：
  - 在 repo 里搜索最新目录名包含 `notebook-chat-`
  - 预期：存在 `artifacts/` 子目录
  - 预期：其中存在 `memory_graph.pb`（或类似 graph artifact；取决于 `FileMemoryGraphStore` 的实现命名）

> 说明：`IExecutionTraceStore` 默认是 file，并被 `ProjectingExecutionTraceStore` 装饰，从而自动投影 `ExecutionTrace -> MemoryGraph`。

### 1.5 Report：多步 pipeline + 版本化落库

- 右栏输入 report topic
- 点击 **Generate**
  - 预期：返回 `reportId` + `version`
  - 预期：报告内容渲染在右栏

- 点击 **Refresh** 报告列表：
  - 预期：出现 reportId，点击后可看到最新版本内容

---

## 2. MongoDB（MemoryStore + VectorIndex）

### 2.1 配置

在 `notebook/src/Aevatar.Notebook.Api/appsettings.secrets.json` 填：

- `ConnectionStrings:MongoDB`
- `MongoDB:Database`（默认 `aevatar`）

并在 `notebook/src/Aevatar.Notebook.Api/appsettings.json` 或 secrets 覆盖：

```json
{
  "Aevatar": {
    "Persistence": {
      "MemoryStore": "mongodb",
      "MemoryVectorIndex": "mongodb",
      "MemoryGraph": "file"
    }
  }
}
```

### 2.2 验证点

- `/api/info` 中 providers 显示 `mongodb`
- Sources/Chat/Report 行为同 1.x
- 如果 embeddings 未开启（默认 `appsettings.secrets.json` 里 `Embeddings.Enabled=false`）：
  - `retrieve_chunks` / 语义检索会自动退化为 lexical（正常）

---

## 3. Supabase（MemoryStore + VectorIndex）

### 3.1 配置

在 `notebook/src/Aevatar.Notebook.Api/appsettings.secrets.json` 填：

- `ConnectionStrings:SupabasePostgres`

并切换：

```json
{
  "Aevatar": {
    "Persistence": {
      "MemoryStore": "supabase",
      "MemoryVectorIndex": "supabase",
      "MemoryGraph": "file"
    }
  }
}
```

### 3.2 验证点

- `/api/info` providers 显示 `supabase`
- 首次运行可能会自动建 schema/table/index（由 `SupabaseMemoryOptions` 控制）
- Sources/Chat/Report 行为同 1.x

---

## 4. Neo4j（MemoryGraphStore）

### 4.1 配置

确保 Neo4j 可访问（bolt），并在 `notebook/src/Aevatar.Notebook.Api/appsettings.secrets.json` 填：

- `Aevatar:Persistence:Neo4j:Password`

然后切换：

```json
{
  "Aevatar": {
    "Persistence": {
      "MemoryGraph": "neo4j"
    }
  }
}
```

> `MemoryStore/VectorIndex` 可以仍然用 `file`/`mongodb`/`supabase` 任意组合；Neo4j 只负责 Layer 4.2 图谱落库。

### 4.2 验证点

- 发起一次 chat（产生 `executionId`）
- 预期：`ProjectingExecutionTraceStore` 保存 trace 后，`ExecutionTraceMemoryProjector` 会调用 `IMemoryGraphStore.SaveAsync` → Neo4j
- 打开 Neo4j Browser/Console（手动）查询：
  - 以 `graphId == executionId` 过滤应能看到节点/关系（具体 label/rel 见 `Aevatar.Agents.Persistence.Neo4j.MemoryGraph` 的实现）

---

## 5. Notebook Tools（模型可调用）

当模型触发工具调用（或未来 UI 增加“tool call”开关）时，以下工具应可用：

- `list_sources`
- `get_source`
- `retrieve_chunks`
- `generate_report`
- `get_report`
- `get_execution_graph`（需要 `IMemoryGraphStore` 可用；file/neo4j 都可以）

---

## 常见问题排查

- **MongoDB/Supabase 被选择但启动直接抛错**：检查 `ConnectionStrings:*` 是否为空（NotebookPersistence 会主动 fail-fast）
- **vector 不工作**：检查 `notebook/src/Aevatar.Notebook.Api/appsettings.secrets.json` 中 `LLMProviders:Providers:default:Embeddings:Enabled`
- **graph 没投影**：
  - 确认 `/api/chat` 返回的 `executionId`
  - 确认 `IExecutionTraceStore` 不是 `NullExecutionTraceStore`
  - file 模式下确认 trace 目录下是否有 `artifacts/`


