## Aevatar.Agents.Persistence.Supabase.GAgent

这个项目提供 **Supabase(Postgres) 直连版** GAgent 持久化实现（用于保存 Agent 的 **State / Config / EventRouter**）：

- **StateStore**：`SupabaseStateStore<TState>`（Protobuf -> `bytea`）
- **ConfigStore**：`SupabaseConfigStore<TConfig>`（`jsonb`）
- **EventRouterStore**：`SupabaseEventRouterStore`（parent/children）

> 注意：连接池（`NpgsqlDataSource`）由基础设施项目 `Aevatar.Agents.Persistence.Supabase` 负责注册与复用。

### 目录结构

```
src/Aevatar.Agents.Persistence.Supabase.GAgent/
├── DependencyInjection/                 # DI 扩展（AddAevatarSupabaseGAgent + 注册 store）
├── Internal/                            # 配置序列化（Protobuf JSON 优先）
├── Options/                             # SupabasePersistenceOptions（GAgent 侧）
├── Setup/                               # 自动建表/建索引/权限收紧/RLS
├── Stores/                              # StateStore/ConfigStore/EventRouterStore
└── docs/
    ├── README.md
    └── schema.sql                       # 默认建库脚本（与默认 Options 对齐）
```

### 快速使用（推荐：自动初始化）

```csharp
using Aevatar.Agents.Core.Extensions;
using Aevatar.Agents.Persistence.Supabase.DependencyInjection; // base infra: NpgsqlDataSource
using Aevatar.Agents.Persistence.Supabase.GAgent.DependencyInjection;
using Aevatar.Agents.Persistence.Supabase.GAgent.Stores;

// 1) 注册 Supabase(Postgres) 基础设施（连接池）
services.AddAevatarSupabase(
    connectionString: configuration.GetConnectionString("SupabasePostgres")!);

// 2) 注册 GAgent 持久化选项（schema/table/安全策略等）
services.AddAevatarSupabaseGAgent(o =>
{
    o.Schema = "aevatar";              // 建议独立 schema
    o.LockDownPublicAccess = true;     // 默认收紧权限（推荐）
    o.EnableRowLevelSecurity = false;  // 默认关闭，避免误伤直连服务端
});

// 3) 接入 Aevatar Agent System（替换默认内存 store）
services.AddAevatarAgentSystem(options =>
{
    options.StateStoreType = typeof(SupabaseStateStore<>);
    options.ConfigStoreType = typeof(SupabaseConfigStore<>);
    options.EventRouterStoreType = typeof(SupabaseEventRouterStore);
});
```

### 手工部署（SQL 审计友好）

- 默认脚本见：`docs/schema.sql`
- 或者在代码里用 `SupabaseSchemaScript.BuildSql(options)` 输出完整 SQL，再交给 DBA 执行。

### 权限与暴露建议（Supabase 场景）

- 默认 `Schema = aevatar`：**不放在 public**，降低被 PostgREST 暴露的概率。
- 默认 `LockDownPublicAccess = true`：**撤销 PUBLIC/anon/authenticated 权限**，防止 anon key 直接读写。
- 若你“就是要”通过 PostgREST 暴露：
  - 开启 `EnableRowLevelSecurity = true`
  - 并自行补充更细粒度 Policy（本库仅可选生成 service_role 全通 policy）

### 约束（很重要）

- `Schema/Table` 名称要求：**全小写 + 下划线**（`[a-z][a-z0-9_]*`），用于避免 SQL 注入与引号陷阱。
- `TState` 必须是 **Protobuf IMessage**（符合框架核心铁律）。


