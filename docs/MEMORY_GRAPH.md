## Memory Graph（ExecutionTrace → 轻量图谱）

目标：利用 `ExecutionTrace` 作为“结构化事实来源”，在不引入外部依赖的前提下，生成 **可导航、可解释** 的轻量图谱（GraphRAG 工程版骨架），并把关键片段落到 `IMemoryStore` 里供 `search_memory` 检索。

---

### 1. 数据契约（Protobuf）

- **Proto 定义**：`src/Aevatar.Agents.Abstractions/memory_graph.proto`
- 核心类型：
  - `MemoryGraph`：`graph_id + scope + nodes + edges`
  - `MemoryGraphNode`：`node_id + type + name + content`
  - `MemoryGraphEdge`：`from/to + type (+ label)`

设计取舍：
- `type` 采用 string（可扩展，避免过早固化枚举）。
- 图谱是 **可推导的 artifact**：来源是 trace/event，允许重建与演进。

---

### 2. Store 抽象（DI 边界）

- **接口**：`src/Aevatar.Agents.Abstractions/Memory/IMemoryGraphStore.cs`
- 默认实现（Core / file，零外部依赖）：
  - `src/Aevatar.Agents.Core/MemoryGraph/FileMemoryGraphStore.cs`
  - 写入位置（Trace Bundle v1 扩展）：
    - `${AEVATAR_TRACE_DIR}/<executionId>/artifacts/memory_graph.pb`
    - `${AEVATAR_TRACE_DIR}/<executionId>/artifacts/memory_graph.json`

- Neo4j 实现（可替换）：
  - `src/Aevatar.Agents.Persistence.Neo4j.MemoryGraph/Stores/Neo4jMemoryGraphStore.cs`
  - 适用：希望把 MemoryGraph 当成“真实可查询图谱”而非 file artifact 的场景（Axiom reasoning / GraphRAG）。

#### 2.1 Neo4j 存储模型（约定）

- Graph Meta Node
  - label：`AevatarMemoryGraph`
  - key：`graphId`
- Graph Node
  - label：`AevatarMemoryGraphNode`
  - key：`graphId + nodeId`
- Graph Edge
  - relationship type：`AEVATAR_MEMORY_GRAPH_EDGE`
  - key：`graphId + edgeId`

> 说明：`labels`（map）以 JSON 字符串 `labelsJson` 存储，避免 Neo4j property 类型限制引入复杂性。

---

### 3. Trace 投影器（ExecutionTraceMemoryProjector）

- **实现**：`src/Aevatar.Agents.Core/MemoryGraph/ExecutionTraceMemoryProjector.cs`
- 产物：
  1) `MemoryGraph`（结构：execution → trace_node → decision/candidate/alert）
  2) `MemoryEntry`（scope=execution，memoryId=`execution::<executionId>`）用于检索

写入策略：
- best-effort：任何失败不会阻塞主流程
- 文本有硬限制（避免把巨大 output dump 进索引）

---

### 4. 自动挂载（IExecutionTraceStore 装饰器）

框架默认的 `IExecutionTraceStore`（file）会被装饰为：

- `ProjectingExecutionTraceStore`：`src/Aevatar.Agents.Core/Tracing/ProjectingExecutionTraceStore.cs`

行为：
- `SaveAsync(trace)` 成功后，best-effort 触发投影：
  - 写 graph artifact
  - 写 execution-scoped MemoryEntry

---

### 5. 如何检索（search_memory）

`search_memory` 新增可选参数 `memoryId`，因此你可以这样检索某次执行的记忆：

- `memoryType = "working"`
- `memoryId = "execution::<executionId>"`

即使 embeddings 不可用，也会退化到 `IMemoryStore` 的 substring 搜索（best-effort）。

---

### 6. 如何启用 Neo4j GraphStore（替换默认 FileMemoryGraphStore）

```csharp
using Aevatar.Agents.Core.Extensions;
using Aevatar.Agents.Persistence.Neo4j.MemoryGraph.DependencyInjection;
using Aevatar.Agents.Persistence.Neo4j.MemoryGraph.Stores;

// 1) 注册 Neo4j MemoryGraphStore（会复用/注册 Neo4j.Driver 基础设施）
services.AddAevatarMemoryGraphNeo4j("bolt://localhost:7687", "neo4j", "password");

// 2) 替换默认 IMemoryGraphStore
services.AddAevatarAgentSystem(options =>
{
    options.MemoryGraphStoreType = typeof(Neo4jMemoryGraphStore);
});
```


