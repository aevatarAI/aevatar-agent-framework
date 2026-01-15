## Aevatar.Secrets.Client

一个轻量的 .NET Client，用来调用 Secrets API（`/api/llm/*`、`/api/secrets/*`），避免每个应用复制 HTTP/JSON 细节。

### 典型用法

- 配置 `HttpClient.BaseAddress` 指向你的 sidecar / `Aevatar.Config`（例如 `http://localhost:6677`）。
- 调用：
  - `ListProviderTypesAsync()`
  - `ListInstancesAsync()`
  - `UpsertInstanceAsync(...)`
  - `FetchModelsAsync(...)`


