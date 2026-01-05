## Aevatar.Agents.Persistence.MongoDB.GAgent

这个项目提供 **MongoDB 版** GAgent 持久化实现（用于保存 Agent 的 **State / Config / EventRouter**）：

- **StateStore**：`MongoDBStateStore<TState>`（Protobuf -> `byte[]`，按类型分集合：`agent_states_{TState}`）
- **ConfigStore**：`MongoDbConfigStore<TConfig>`（BSON，按类型分集合：`agent_configs_{TConfig}`）
- **EventRouterStore**：`MongoDBEventRouterStore`（`agent_event_router_hierarchies`）

> 注意：连接与 BSON 序列化配置由基础设施项目 `Aevatar.Agents.Persistence.MongoDB` 负责。

### 目录结构

```
src/Aevatar.Agents.Persistence.MongoDB.GAgent/
├── DependencyInjection/                 # AddMongoDBStateStore/AddMongoDBConfigStore/AddMongoDBEventRouterStore
├── docs/
│   └── README.md
├── AgentStateDocument.cs               # internal: Protobuf bytes
├── AgentConfigDocument.cs              # internal generic: AgentConfigDocument<TConfig>
├── EventRouterHierarchyDocument.cs
├── MongoDBIndexManager.cs              # per-collection index init (process lifetime cache)
├── MongoDBStateStore.cs
├── MongoDbConfigStore.cs
└── MongoDBEventRouterStore.cs
```

### 快速使用（Aevatar Agent System）

```csharp
using Aevatar.Agents.Core.Extensions;
using Aevatar.Agents.Persistence.MongoDB;
using Aevatar.Agents.Persistence.MongoDB.GAgent;

// 1) 注册 MongoDB 连接（base infra）
services.AddAevatarMongoDB(
    connectionString: configuration.GetConnectionString("MongoDB")!,
    databaseName: "aevatar");

// 2) 接入 Aevatar Agent System（替换默认 File* store）
services.AddAevatarAgentSystem(options =>
{
    options.StateStoreType = typeof(MongoDBStateStore<>);
    options.ConfigStoreType = typeof(MongoDbConfigStore<>);
    options.EventRouterStoreType = typeof(MongoDBEventRouterStore);
});
```

### DI 扩展（可选）

如果你需要在 App 侧显式注册某个具体类型的 Store（用于手工 resolve），可用：

```csharp
using Aevatar.Agents.Persistence.MongoDB.GAgent.DependencyInjection;

services.AddMongoDBStateStore<MyState>();
services.AddMongoDBConfigStore<MyConfig>();
services.AddMongoDBEventRouterStore();
```

### 约束（很重要）

- `TState` 必须是 **Protobuf IMessage**（符合框架核心铁律）
- MongoDB 索引初始化是 best-effort 且进程内缓存（同一集合只初始化一次）



