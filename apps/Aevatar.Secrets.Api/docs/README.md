## Architecture (Aevatar.Secrets.Api)

目的：提供一个**本地** Web UI，把 secrets 写入 **用户级加密存储**（默认 `~/.aevatar/secrets.json`），供仓库内任意系统复用。

### 目录结构

```
apps/Aevatar.Secrets.Api/
├── Program.cs                 # Minimal API + embedded HTML UI
├── Aevatar.Secrets.Api.csproj # Web project (net10.0) + 引用 Aevatar.Agents.Core
├── README.md                  # 使用说明
└── docs/README.md             # 本文：架构镜像
```

### 边界与安全

- **写入接口仅允许 localhost**（非 loopback 直接 Forbid）
- **不回显 secrets value**（UI/日志都不打印）
- **加密落盘**由 `Aevatar.Agents.Core.Secrets.FileAevatarUserSecretsStore` 负责

### 主要 API（localhost-only）

- `GET /api/llm/providers`：provider 列表 + Connected 状态（不返回 key）
- `GET /api/llm/provider/{providerName}`：单个 provider 详情（含 resolved endpoint）
- `GET /api/llm/test/{providerName}`：测试连通性（best-effort list models）
- `GET /api/llm/models/{providerName}?limit=200`：拉取模型列表（best-effort）
- `POST /api/llm/api-key` / `DELETE /api/llm/api-key/{providerName}`：写入/移除 API key
- `POST /api/secrets/set` / `POST /api/secrets/remove`：高级自定义 key/value


