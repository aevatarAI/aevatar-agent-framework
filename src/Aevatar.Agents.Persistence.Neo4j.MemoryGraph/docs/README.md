# Aevatar.Agents.Persistence.Neo4j.MemoryGraph

为 Aevatar 的 **Layer 4.2（Memory Graph）** 提供 Neo4j 落库实现：`IMemoryGraphStore`。

## 目录结构

```
src/Aevatar.Agents.Persistence.Neo4j.MemoryGraph/
├── DependencyInjection/                 # DI 扩展（AddAevatarMemoryGraphNeo4j）
├── Stores/                              # Neo4jMemoryGraphStore（IMemoryGraphStore）
└── docs/README.md
```

## 存储模型（Neo4j）

- **Graph Meta Node**
  - label：`AevatarMemoryGraph`
  - key：`graphId`
  - props：`scopeType/scopeId/createdAtUnixMs/labelsJson/nodeCount/edgeCount/...`

- **Graph Node**
  - label：`AevatarMemoryGraphNode`
  - key：`graphId + nodeId`
  - props：`type/name/content/labelsJson`

- **Graph Edge**
  - relationship type：`AEVATAR_MEMORY_GRAPH_EDGE`
  - key：`graphId + edgeId`
  - props：`type/label/labelsJson`

> 说明：节点/边的 `labels`（map）以 JSON 字符串 `labelsJson` 存储，避免 Neo4j property 类型限制带来的复杂性。

## 使用方式

```csharp
using Aevatar.Agents.Core.Extensions;
using Aevatar.Agents.Persistence.Neo4j.MemoryGraph.DependencyInjection;
using Aevatar.Agents.Persistence.Neo4j.MemoryGraph.Stores;

// 1) 注册 Neo4j MemoryGraphStore（会复用/注册 Neo4j.Driver 基础设施）
services.AddAevatarMemoryGraphNeo4j(
    uri: "bolt://localhost:7687",
    username: "neo4j",
    password: "password");

// 2) 接入 Agent System：替换默认 FileMemoryGraphStore
services.AddAevatarAgentSystem(options =>
{
    options.MemoryGraphStoreType = typeof(Neo4jMemoryGraphStore);
});
```


