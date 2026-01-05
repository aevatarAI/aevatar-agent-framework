## Aevatar.Agents.Persistence.Supabase

这个项目是 **Supabase(Postgres) 持久化的基础设施层**，只负责：

- **连接池**：注册并复用 `NpgsqlDataSource`
- **共享工具**：SQL identifier 校验/拼接（防注入）

实际的持久化实现按领域拆分在两个项目里：

- `Aevatar.Agents.Persistence.Supabase.GAgent`：Agent 的 **State / Config / EventRouter**
- `Aevatar.Agents.Persistence.Supabase.Memory`：AI Memory 的 **IMemoryStore / IMemoryVectorIndex(pgvector)**

### 目录结构

```
src/Aevatar.Agents.Persistence.Supabase/
├── DependencyInjection/                 # DI 扩展（AddAevatarSupabase：注册 NpgsqlDataSource）
├── Internal/                            # SQL 安全拼接/校验工具（identifier validation）
└── docs/
    ├── README.md
```

### 快速使用（注册连接池）

```csharp
using Aevatar.Agents.Persistence.Supabase.DependencyInjection;

services.AddAevatarSupabase(
    connectionString: configuration.GetConnectionString("SupabasePostgres")!);
```

### 下一步

- 要接入 **GAgent State/Config**：看 `src/Aevatar.Agents.Persistence.Supabase.GAgent/docs/README.md`
- 要接入 **AI Memory(pgvector)**：看 `src/Aevatar.Agents.Persistence.Supabase.Memory/docs/README.md`


