## Aevatar.Agents.Persistence.MongoDB

这个项目是 **MongoDB 持久化的基础设施层**，只负责：

- **MongoClient / IMongoDatabase** 注册（连接管理由 MongoDB.Driver 内部完成）
- **BSON 序列化配置**（`GuidRepresentation.Standard`，避免 GuidRepresentation 相关异常）

实际的持久化实现按领域拆分在两个项目里：

- `Aevatar.Agents.Persistence.MongoDB.GAgent`：Agent 的 **State / Config / EventRouter**
- `Aevatar.Agents.Persistence.MongoDB.Memory`：AI Memory 的 **IMemoryStore / IMemoryVectorIndex**

### 目录结构

```
src/Aevatar.Agents.Persistence.MongoDB/
├── MongoDBServiceCollectionExtensions.cs   # AddAevatarMongoDB + ConfigureBsonSerializers
└── docs/
    └── README.md
```

### 快速使用（注册 MongoDB 连接）

```csharp
using Aevatar.Agents.Persistence.MongoDB;

services.AddAevatarMongoDB(
    connectionString: configuration.GetConnectionString("MongoDB")!,
    databaseName: "aevatar");
```

下一步请根据需要接入：
- GAgent：看 `src/Aevatar.Agents.Persistence.MongoDB.GAgent/docs/README.md`
- AI Memory：看 `src/Aevatar.Agents.Persistence.MongoDB.Memory/docs/README.md`


