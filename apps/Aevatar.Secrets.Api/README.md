## Aevatar.Secrets.Api

一个**本地**小工具：用 Web UI 把密钥写入 Aevatar 的 **用户级加密 secrets**（默认 `~/.aevatar/secrets.json`），避免每个 demo/app 都复制 `appsettings.secrets.json`。

### 运行

```bash
dotnet run --project apps/Aevatar.Secrets.Api/Aevatar.Secrets.Api.csproj
```

默认访问：

- 浏览器推荐：`http://localhost:6677`
- 兼容/脚本：`http://localhost:6667`（Chrome 会报 `ERR_UNSAFE_PORT`）

健康检查：

- `GET /health`

### 能做什么（面向“我写了 key，但不知道能不能用”）

- **展示配置状态**：是否已配置 API key、当前 resolved endpoint
- **Test connection**：best-effort 调用 provider 的 “list models” 来验证 key+endpoint 是否可用
- **Fetch models**：拉取可用模型列表（不回显 key）

常用接口（均为 localhost-only）：

- `GET /api/llm/providers`
- `GET /api/llm/instances`
- `GET /api/llm/provider/{providerName}`
- `GET /api/llm/test/{providerName}`
- `GET /api/llm/models/{providerName}?limit=200`
- `POST /api/llm/instance`

### 覆盖 secrets 路径

- `AEVATAR_SECRETS_PATH=/path/to/secrets.json`
- `AEVATAR_SECRETS_DIR=/path/to/dir`

> 说明：该应用只允许 localhost 调用写入 API（非本地请求会被拒绝）。


