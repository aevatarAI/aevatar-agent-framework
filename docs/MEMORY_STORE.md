## Memory Store（记忆资源化存储）

目标：把 “记忆” 从 Agent 私有 state/字符串拼接，升级为 **可持久化、可共享、可检索、可治理** 的资源（SSOT）。

> 核心铁律：任何跨边界数据必须使用 Protobuf。Memory 的数据契约在 `memory.proto` 中定义。

---

### 1. 数据契约（SSOT）

- **Proto 定义**：`src/Aevatar.Agents.Abstractions/memory.proto`
- 核心类型：
  - `MemoryScopeType`：记忆的共享边界（private/session/run/execution/graph/tenant）
  - `MemoryScope`：`type + scope_id`
  - `MemoryEntry`：append-only 记忆条目
  - `MemoryResourceSummary`：资源列表（用于 UI/管理）

---

### 2. Store 抽象（DI 边界）

- **接口**：`src/Aevatar.Agents.Abstractions/Memory/IMemoryStore.cs`
- 语义：
  - `AppendAsync`：追加写（append-only）
  - `ListResourcesAsync`：列出 memory 资源（按最近更新时间排序）
  - `ListEntriesAsync`：列出某资源最近 N 条
  - `SearchAsync`：best-effort 搜索（实现可为 substring/FTS/vector）

注意：`IMemoryStore` 是 **进程内 DI 边界**，不是跨 runtime 消息；但它读写的 `MemoryEntry` 是 Protobuf（跨边界安全）。

---

### 3. 默认实现（Core）

当前 Core 提供三种实现：

- **File**（默认 best-effort）：
  - `src/Aevatar.Agents.Core/Memory/FileMemoryStore.cs`
  - 输出目录：
    - 环境变量：`AEVATAR_MEMORY_DIR`
    - 未设置时：默认 `<repoRoot>/memory`（与 trace 一样 best-effort 自动探测 repo root）
  - 目录结构（Memory Bundle v1）：
    - `<memoryId>/entries.pb`（delimited protobuf, append-only）
    - `<memoryId>/manifest.json`（资源列表加速）

- **InMemory**：
  - `src/Aevatar.Agents.Core/Memory/InMemoryMemoryStore.cs`
  - 用于测试/开发（无外部依赖）

- **Null（no-op）**：
  - `src/Aevatar.Agents.Core/Memory/NullMemoryStore.cs`
  - 仅用于 file store 初始化失败等异常兜底

---

### 4. DI 注册

在 `AddAevatarAgentSystem(...)` 中默认注册：

- `IMemoryStore`：File（best-effort），失败降级 Null

代码位置：`src/Aevatar.Agents.Core/Extensions/ServiceCollectionExtensions.cs`

如果你需要替换实现（例如接入 Postgres/pgvector 或 ES），可以通过 `GAgentOptions.MemoryStoreType` 覆盖。

---

### 4.1 AIGAgentBase 写入开关（默认关闭）

框架不会默认把对话写入 MemoryStore（避免“隐藏 IO”）。需要在 Agent 中显式开启：

- `EnableMemoryStoreAppend = true`
- `EnableSessionMemoryStoreAppend = true`（需要 `ChatRequest.Context["session_id"]`，写入 `scope=session`，用于跨 Agent 的会话聚合）
- `MemoryStoreScopeType = PrivateAgent / Session / Run / ...`
- （可选）`MemoryStoreScopeIdOverride` / `MemoryIdOverride`

写入点：

- `ChatAsync`：写入 user message + assistant final content
- `ChatStreamAsync`：写入 user message + streaming 完成后的 assistant final content

并且写入是 best-effort：MemoryStore 写失败不会影响 chat 主流程。

---

### 4.2 向量写入开关（默认关闭）

如果你希望“语义检索可跨进程持久化”，需要同时开启向量索引写入：

- `EnableMemoryVectorIndexAppend = true`
- 并且 embeddings 必须在 provider 配置里启用，embedding generator 成功初始化

向量索引根目录（file-based）：
- 环境变量：`AEVATAR_MEMORY_VECTOR_DIR`
- 未设置：默认与 `AEVATAR_MEMORY_DIR` 共用根目录

详见：`docs/MEMORY_VECTOR_INDEX.md`

---

### 5. 下一步（与向量/图谱对齐）

- **Vector Index**：把 `MemoryEntry` 写入时同步写入向量索引（持久化），`search_memory` 优先走向量 top‑k。
- **Trace→Graph**：把 `ExecutionTrace` 投影成 MemoryEntry + Graph（scope=execution），用于可解释回忆与审计。


