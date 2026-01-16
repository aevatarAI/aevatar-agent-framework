## Aevatar.Agents.Persistence.MongoDB.Memory

这个项目提供 **MongoDB 版** AI Memory 持久化实现：

- **MemoryStore**：`MongoDbMemoryStore`（`MemoryEntry` -> `memory_entries`）
- **MemoryVectorIndex**：`MongoDbMemoryVectorIndex`（`MemoryVectorRecord` -> `memory_vectors`）

> 说明：当前 `MongoDbMemoryVectorIndex` 默认实现是 **MongoDB 过滤 + 进程内 brute-force cosine**，适合 Demo/小数据量。
> 生产环境建议接入 MongoDB Atlas Vector Search（或替换成专用向量库）。

### 目录结构

```
src/Aevatar.Agents.Persistence.MongoDB.Memory/
├── DependencyInjection/                 # AddAevatarMongoDBMemory
├── Documents/                           # MongoDB 文档模型
├── Internal/                            # CosineSimilarity
├── Options/                             # MongoDbMemoryOptions
├── Stores/                              # MongoDbMemoryStore / MongoDbMemoryVectorIndex
└── docs/
    └── README.md
```

### 快速使用（推荐）

```csharp
using Aevatar.Agents.Core.Extensions;
using Aevatar.Agents.Persistence.MongoDB;
using Aevatar.Agents.Persistence.MongoDB.Memory.DependencyInjection;
using Aevatar.Agents.Persistence.MongoDB.Memory.Stores;

// 1) 注册 MongoDB 连接（base infra）
services.AddAevatarMongoDB(
    connectionString: configuration.GetConnectionString("MongoDB")!,
    databaseName: "aevatar");

// 2) 注册 Memory 选项（可选）
services.AddAevatarMongoDBMemory(o =>
{
    o.MemoryEntriesCollection = "memory_entries";
    o.MemoryVectorsCollection = "memory_vectors";
});

// 3) 接入 Aevatar Agent System（替换默认 FileMemoryStore/FileMemoryVectorIndex）
services.AddAevatarAgentSystem(options =>
{
    options.MemoryStoreType = typeof(MongoDbMemoryStore);
    options.MemoryVectorIndexType = typeof(MongoDbMemoryVectorIndex);
});
```

### 约束（很重要）

- `MemoryVectorIndex` 的向量检索是 brute-force：请控制数据规模，或替换为 Atlas Vector Search
- `IMemoryStore.SearchAsync` 默认用 regex contains（大小写不敏感）；生产建议使用 Atlas Search / text index




