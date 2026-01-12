## Persistence 项目组织与命名（Backend-first）

目的：让 “持久化后端（Neo4j/MongoDB/Supabase/…）” 与 “能力模块（Graph/Memory/MemoryGraph/GAgent/…）” 的边界清晰可扩展，避免命名轴混用导致的认知噪音与依赖纠缠。

---

### 1. 命名规则（唯一轴：后端优先）

- **规则**：`Aevatar.Agents.Persistence.<Backend>.<Capability>`
  - **Backend**：稳定边界（Driver/Session/连接选项），例如：`Neo4j` / `MongoDB` / `Supabase` / `InMemory`
  - **Capability**：可插拔能力（实现某个 DI 边界接口），例如：`Graph` / `Memory` / `MemoryGraph` / `GAgent`

例外（只有一个）：**跨后端的能力核心**可以不带 Backend：

- `Aevatar.Agents.Persistence.Graph`：Graph 的抽象/IR/统一 API（不依赖任何具体 DB）

---

### 2. 当前目录结构（src/）

```
src/
├── Aevatar.Agents.Persistence.Graph/                 # Graph Core：IR + 语义模型 + IGraphClient
├── Aevatar.Agents.Persistence.InMemory.Graph/        # InMemory Graph Provider（开发/测试）
│
├── Aevatar.Agents.Persistence.Neo4j/                 # Neo4j Backend：Options/Driver/Session/INeo4jClient
├── Aevatar.Agents.Persistence.Neo4j.Graph/            # Neo4j Graph Provider：Compiler/Executor/Mapper + AddAevatarGraphNeo4j
├── Aevatar.Agents.Persistence.Neo4j.MemoryGraph/      # Neo4j MemoryGraph：IMemoryGraphStore 落库
│
├── Aevatar.Agents.Persistence.MongoDB/                # MongoDB Backend（已有）
├── Aevatar.Agents.Persistence.MongoDB.GAgent/          # MongoDB：Agent State/Config/EventRouter 等
├── Aevatar.Agents.Persistence.MongoDB.Memory/          # MongoDB：AI Memory（IMemoryStore + IMemoryVectorIndex）
│
├── Aevatar.Agents.Persistence.Supabase/                # Supabase Backend（已有）
├── Aevatar.Agents.Persistence.Supabase.GAgent/         # Supabase：Agent State/Config/EventRouter 等
└── Aevatar.Agents.Persistence.Supabase.Memory/         # Supabase：AI Memory（IMemoryStore + IMemoryVectorIndex）
```

---

### 3. 架构合理性（为什么这样分层是“对的”）

#### 3.1 消灭命名歧义：后端与能力不再混用

- 以前混用两套轴（`Graph.*` vs `Neo4j.*`）会让人无法从名字判断：
  - 这是 “Graph 的某个后端实现”，还是 “Neo4j 的某个能力模块”？
- 现在只有一套规则：**先看后端，再看能力**，认知成本固定。

#### 3.2 依赖方向稳定：Backend → Capability（单向）

- **Backend 项目**（例如 `Aevatar.Agents.Persistence.Neo4j`）只负责：
  - 连接选项、driver/session 生命周期、通用读写 client
- **Capability 项目**（例如 `Aevatar.Agents.Persistence.Neo4j.Graph` / `.MemoryGraph`）只负责：
  - 实现某个接口（`IGraphClient` / `IMemoryGraphStore` …）并提供 DI 扩展

结果：
- 能力模块之间可共享同一套 backend 基础设施（避免重复造轮子）
- 不会出现 “Graph provider 反向依赖 Graph core 的 Neo4j 细节” 这种结构污染

#### 3.3 复用最大化：一个 Backend，多种能力

典型例子（Neo4j）：
- `Neo4j`（基础设施）同时服务：
  - `Neo4j.Graph`（Graph CRUD/Query）
  - `Neo4j.MemoryGraph`（ExecutionTrace → MemoryGraph 的图谱落库）

未来你想加：
- `Neo4j.Memory`（AI Memory store/vector）
- `Neo4j.GAgent`（Agent state/config/eventrouter）

都能自然落在同一套规则里，不需要再发明新命名。

#### 3.4 DI 边界清晰：可替换、可测试、可分布式演进

- **跨运行时/跨进程的数据契约**：用 Protobuf（`MemoryGraph` / `MemoryEntry` / `MemoryVectorRecord`）
- **持久化实现的替换边界**：用 C# 接口（`IMemoryStore` / `IMemoryVectorIndex` / `IMemoryGraphStore` / `IGraphClient`）

这样做的工程收益：
- 单测可用 mock 替身（不需要真数据库）
- 生产可按需替换存储实现（file → neo4j/supabase/mongodb）

---

### 4. 扩展指南（未来怎么加不会乱）

#### 4.1 新增一个 Backend（例如 Redis）

- 新建：`Aevatar.Agents.Persistence.Redis`（连接/客户端/DI）
- 按需加能力：
  - `Aevatar.Agents.Persistence.Redis.Memory`
  - `Aevatar.Agents.Persistence.Redis.Graph`

#### 4.2 新增一个 Capability（例如 MemoryGraph 的 Postgres 版本）

- 新建：`Aevatar.Agents.Persistence.Supabase.MemoryGraph`（或 `Postgres.MemoryGraph`）
- 实现 `IMemoryGraphStore`，提供 DI 扩展


