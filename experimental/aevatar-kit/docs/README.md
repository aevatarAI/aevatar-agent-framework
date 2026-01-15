## AevatarKit MVP

这个目录包含 **AevatarKit 的产品形态 MVP**（前后端齐全，但功能不全是预期的）。

### 目录结构（当前）

```text
experimental/aevatar-kit/
  docs/
    PLAN.md            # 产品与架构计划（SSOT）
    README.md          # 你正在看的这份
  src/
    AevatarKit.Core/    # Protobuf 契约 + 基础序列化工具
    AevatarKit.Runtime/ # MVP Runtime（in-memory：Agents/Graphs/MCP/Memory/Runs）
    AevatarKit.Api/     # ASP.NET Core API + SSE + 静态前端托管
  frontend/
    index.html         # 单页 UI（无构建步骤）
    app.js             # 调用 API + SSE Timeline
    styles.css         # Workspace UI 皮肤（对标 mcp-agent-graph 形态）
```

### MVP 已实现的展示能力

- **Workspace UI（对标 mcp-agent-graph）**：
  - 左侧导航：Agent / Workflow / Model / Tools / MCP / Prompt / File / Memory
  - 顶部工具栏：搜索/刷新/创建（按页面裁剪）
- **Agent Manager（可用）**：
  - 分类折叠 + 搜索 + Create Agent（in-memory）
  - API：`GET /api/agents`、`GET /api/agents/categories`、`POST /api/agents`
- **Workflow Editor（可用）**：
  - Graph Library：保存/加载（in-memory）
  - Run：启动 run + SSE Timeline + Run History（回放）
  - API：`GET /api/graphs`、`GET /api/graphs/{id}`、`POST /api/graphs`、`PUT /api/graphs/{id}`
  - API：`POST /api/runs`、`GET /api/runs`、`GET /api/runs/{id}`、`GET /api/runs/{id}/events`
- **MCP Manager（MVP）**：
  - server registry 管理（不做真实连接）
  - API：`GET /api/mcp/servers`、`POST /api/mcp/servers`、`PUT /api/mcp/servers/{id}`
- **Memory Manager（可用）**：
  - 资源列表 + 子串搜索（MVP）+ runId 快速跳转
  - run scope + session scope（用于演示“共享 memory”）
  - API：`GET /api/memory/resources`、`GET /api/memory/{memoryId}/entries`、`POST /api/memory/search`

### 运行方式

```bash
cd experimental/aevatar-kit/src/AevatarKit.Api
dotnet run
```

启动后访问：`http://localhost:5777`。

### 约束与已知限制（MVP 故意不做）

- **不接真实 Aevatar runtime**：Run Engine 目前是模拟器（用于“形态展示”）
- **不做多租户/权限**：Memory/Graph/Run 目前都在内存里
- **不做 GraphDefinition(IR) 执行**：MVP 接受字符串，V1 才会以 Protobuf IR 为 SSOT 并编译执行


