## Memory Vector Index（持久化语义检索）

目标：把“语义检索”从一次性的 rerank（进程内、当次调用）升级为 **可持久化、可跨进程** 的 top‑k 召回能力，并保持 **零外部依赖**（先把链路打通，抽象可替换）。

> 核心铁律：向量记录是跨边界资产，必须有 Protobuf 契约。

---

### 1. 数据契约（Protobuf）

- **Proto 定义**：`src/Aevatar.Agents.Abstractions/memory_vector.proto`
- 核心类型：
  - `MemoryVectorRecord`：`entry_id + memory_id + scope + embedding + content(snippet)`

说明：
- `content` 只是展示用 snippet，**canonical 仍是 MemoryStore 里的 `MemoryEntry`**。
- `scope` 复制一份在 record 里，避免检索时必须 join MemoryStore。

---

### 2. Index 抽象（DI 边界）

- **接口**：`src/Aevatar.Agents.Abstractions/Memory/IMemoryVectorIndex.cs`
- 语义：
  - `UpsertAsync`：写入（默认实现为 append-only）
  - `SearchAsync`：输入 query embedding，输出 `MemoryVectorMatch`（record + cosine similarity）

---

### 3. 默认实现（Core / 无外部依赖）

- **FileMemoryVectorIndex**：`src/Aevatar.Agents.Core/Memory/FileMemoryVectorIndex.cs`
- 机制：
  - 存储：delimited protobuf 追加写 `vectors.pb`
  - 检索：brute-force cosine，相似度 top‑k（内存占用可控）
- 根目录：
  - 环境变量：`AEVATAR_MEMORY_VECTOR_DIR`
  - 未设置：默认与 MemoryStore 共用 `AEVATAR_MEMORY_DIR` 的根（便于携带/迁移）

---

### 4. AIGAgentBase 写入（默认关闭）

写入点在 `ChatAsync/ChatStreamAsync`（user + assistant final content）：

- 开启条件：
  - `EnableMemoryStoreAppend = true`（先有 MemoryEntry）
  - `EnableMemoryVectorIndexAppend = true`（才会写向量）
  - provider 配置里 embeddings 启用且 embedding generator 成功初始化

写入是 best-effort：失败不会影响 chat 主流程。

---

### 5. search_memory 的使用方式（向量优先）

当满足以下条件时：
- 工具拿得到 embeddings（`ToolContext.GenerateEmbeddingsAsync`）
- `IMemoryVectorIndex` 可用
- `memoryType` 是 `working` 或 `all`

则 `search_memory` 会优先走向量召回，返回 `memory_vector` 类型结果，并带上：
- `Metadata.source = "memory.vector_index"`
- `Metadata.ranking = "vector"`

如果 embeddings 不可用，则会退化为 `IMemoryStore` 的 substring 搜索（`memory_store`，`ranking=lexical`）。

#### 5.1 限定检索范围（memoryId）

你可以通过 `memoryId` 把检索限定在某个 memory resource 内：

- 不传：默认 `privateagent::<agentId>`
- execution 回放：`execution::<executionId>`
- session 共享：`session::<sessionId>`


