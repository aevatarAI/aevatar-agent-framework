## Architecture (Aevatar.Config)

目的：提供一个**本地** Web UI，把 secrets 写入 **用户级加密存储**（默认 `~/.aevatar/secrets.json`），供仓库内任意系统复用。

### 目录结构

```
apps/Aevatar.Config/
├── Aevatar.Config.csproj       # Web project (net10.0) + 共享源码链接
├── boot.sh                     # 启动入口
├── README.md                  # 使用说明
├── docs/README.md             # 本文：架构镜像
└── docs/TECH_DEBT.md           # 技术债记录

（共享源码链接自 tools/Aevatar.Tools.Config/）
├── Program.cs                 # Minimal API + 静态资源托管
├── LlmContracts.cs            # LLM/Secrets/WebSearch 的 DTO + 内部模型
├── LlmProviderProfiles.cs     # Provider types 与 instanceName 推断
├── LlmProviderResolver.cs     # instance -> resolved endpoint/model/apiKey(不回显)
├── LlmProbe.cs                # best-effort Test / Fetch models
├── ProviderCatalog.cs         # Providers + Instances 列表构建
└── wwwroot/                   # UI 静态资源（与 tool 共享）
    ├── index.html             # UI 入口页（纯静态）
    ├── aevatar-secrets-ui.js  # UI 逻辑（vanilla JS）
    └── aevatar-secrets-ui.css # UI 样式
```

> 说明：`apps/Aevatar.Config` 与 `tools/Aevatar.Tools.Config` 共享同一套源码与 UI 资产，避免重复维护。

### 边界与安全

- **写入接口仅允许 localhost**（非 loopback 直接 Forbid）
- **不回显 secrets value**（UI/日志都不打印）
- **加密落盘**由 `Aevatar.Agents.Core.Secrets.FileAevatarUserSecretsStore` 负责

### 主要 API（localhost-only）

- `GET /api/llm/providers`：基础 provider 类型列表（永远可配置）
- `GET /api/llm/instances`：已配置实例列表（multi-model / multi-instance）
- `GET /api/llm/provider/{providerName}`：单个 provider 详情（含 resolved endpoint）
- `GET /api/llm/test/{providerName}`：测试连通性（best-effort list models）
- `GET /api/llm/models/{providerName}?limit=200`：拉取模型列表（best-effort）
- `POST /api/llm/instance`：写入实例（ProviderType/Model/Endpoint + ApiKey 或 copyApiKeyFrom）
- `POST /api/llm/api-key` / `DELETE /api/llm/api-key/{providerName}`：写入/移除 API key
- `GET /api/websearch` / `POST /api/websearch` / `DELETE /api/websearch`：Web Search provider 配置
- `GET /api/websearch/api-key`：Web Search API key 状态（可 reveal）
- `GET /api/websearch/status`：Web Search 可用性（available/provider/enabled/configured）
- `POST /api/secrets/set` / `POST /api/secrets/remove`：高级自定义 key/value

> WebSearch 配置键：`Aevatar:Tools:WebSearch:*`（若为空则回退读取 `WebSearch:*`）。


