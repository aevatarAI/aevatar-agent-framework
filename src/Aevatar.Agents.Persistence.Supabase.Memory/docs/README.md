## Aevatar.Agents.Persistence.Supabase.Memory

这个项目提供 **Supabase(Postgres + pgvector) 直连版** AI Memory 持久化实现（与 `Aevatar.Agents.Persistence.Supabase` 的 Agent State/Config 持久化相互独立）：

- **MemoryStore**：`SupabaseMemoryStore`（`MemoryEntry` -> `text/jsonb/timestamptz`）
- **MemoryVectorIndex**：`SupabaseMemoryVectorIndex`（`MemoryVectorRecord` -> `pgvector`）

### 目录结构

```
src/Aevatar.Agents.Persistence.Supabase.Memory/
├── DependencyInjection/                 # DI 扩展（AddAevatarSupabaseMemory）
├── Internal/                            # SQL 安全拼接/校验工具
├── Options/                             # SupabaseMemoryOptions
├── Setup/                               # 自动建表/建索引/权限收紧/RLS
├── Stores/                              # SupabaseMemoryStore / SupabaseMemoryVectorIndex
└── docs/
    ├── README.md
    └── schema.sql                       # 默认建库脚本（示例：vector(1536)）
```

### 快速使用（推荐：自动初始化）

```csharp
using Aevatar.Agents.Core.Extensions;
using Aevatar.Agents.Persistence.Supabase.Memory.DependencyInjection;
using Aevatar.Agents.Persistence.Supabase.Memory.Stores;

// 1) 注册 Supabase(Postgres) 基础设施（可与其它 Supabase 模块共享同一个 NpgsqlDataSource）
services.AddAevatarSupabaseMemory(
    connectionString: configuration.GetConnectionString("SupabasePostgres")!,
    configure: o =>
    {
        o.Schema = "aevatar_memory";        // 建议独立 schema
        o.VectorDimensions = 1536;          // 必填：embedding 维度
        o.DistanceMetric = SupabaseVectorDistanceMetric.Cosine;

        // 推荐：默认收紧权限，避免被 Supabase PostgREST 暴露
        o.LockDownPublicAccess = true;

        // 可选：自动创建 pgvector extension（也可在 Supabase 控制台手动执行 CREATE EXTENSION vector）
        o.AutoCreateVectorExtension = true;
    });

// 2) 接入 Aevatar Agent System（替换默认 FileMemoryStore/FileMemoryVectorIndex）
services.AddAevatarAgentSystem(options =>
{
    options.MemoryStoreType = typeof(SupabaseMemoryStore);
    options.MemoryVectorIndexType = typeof(SupabaseMemoryVectorIndex);
});
```

### 手工部署（SQL 审计友好）

- 默认脚本见：`docs/schema.sql`
- 或者在代码里用 `SupabaseMemorySchemaScript.BuildSql(options)` 输出完整 SQL，再交给 DBA 执行。

### 约束（很重要）

- `Schema/Table` 名称要求：**全小写 + 下划线**（`[a-z][a-z0-9_]*`），用于避免 SQL 注入与引号陷阱。
- `VectorDimensions` 必须配置（>0），并且你生成 embedding 的维度必须与表结构一致。


