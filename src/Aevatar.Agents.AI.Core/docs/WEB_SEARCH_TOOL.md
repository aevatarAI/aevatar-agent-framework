# Web Search Tool (`web_search`)

## 目标

为 AI Agent 提供一个 **“可插拔 provider 的 web search 工具”**：

- 框架层只做：工具 schema、参数校验、结果输出限幅、安全开关。
- 搜索能力本身交给第三方服务（避免自己维护爬虫/索引/反作弊/排序这一整套地狱工程）。

## 默认行为（框架层）

- 工具名：`web_search`
- 分类：`ToolCategory.Information`
- 安全策略：
  - `RequiresConfirmation = true`
  - 当 `AllowDangerousTools = false` 时，此工具会被 **隐藏/拒绝执行**
- 输出：
  - `success/provider/query/count/httpStatus/error/results[]`
  - `results[].snippet` 有长度上限，避免 token 爆炸

## 支持的 Provider（内置）

- `tavily`（默认）
- `brave`
- `bing` / `azure-bing`
- `serper` / `google-serper`

> 说明：不同 provider 的 `apiKey` 含义不同（分别映射到各自 HTTP Header/Body），但对工具调用侧统一为一个 `apiKey` 配置键。

## 启用方式 A：通过配置自动创建 Provider（推荐）

支持两种配置路径（推荐用 namespaced）：

> 实现位置：配置解析与 provider 物化在 `Tool/Tools/BuiltIn/WebSearch/WebSearchProviderFactory.cs`（best-effort, 返回 null 表示不开启）。

### 1) `Aevatar:Tools:WebSearch`

```json
{
  "Aevatar": {
    "Tools": {
      "WebSearch": {
        "enabled": true,
        "provider": "tavily",
        "apiKey": "YOUR_TAVILY_API_KEY",
        "timeoutMs": 15000,
        "endpoint": "https://api.tavily.com/search",
        "searchDepth": "basic"
      }
    }
  }
}
```

### 2) `WebSearch`

```json
{
  "WebSearch": {
    "enabled": true,
    "provider": "tavily",
    "apiKey": "YOUR_TAVILY_API_KEY",
    "timeoutMs": 15000
  }
}
```

> 若未显式设置 `enabled`，但提供了 `apiKey`，框架会 **自动启用**（除非显式 `enabled=false`）。

## 启用方式 B：宿主 DI 注入自定义 Provider（可替换任何第三方）

1) 在宿主注册 `IAevatarWebSearchProvider`
2) `AIGAgentFactory` 会 best-effort 注入到 Agent 的 `WebSearchProvider` 属性

示例：

```csharp
services.AddHttpClient(); // provides IHttpClientFactory

services.AddSingleton<IAevatarWebSearchProvider>(sp =>
    new TavilyWebSearchProvider(
        new TavilyWebSearchOptions
        {
            ApiKey = "...",
            TimeoutMs = 15000,
            SearchDepth = "basic"
        },
        sp.GetRequiredService<IHttpClientFactory>()));
```

## 环境变量（可选）

当不想写配置文件时可用：

- `AEVATAR_WEBSEARCH_API_KEY`
- `AEVATAR_WEBSEARCH_PROVIDER`（默认 `tavily`）
- `AEVATAR_WEBSEARCH_ENDPOINT`
- `AEVATAR_WEBSEARCH_ENABLED`
- `AEVATAR_WEBSEARCH_TIMEOUT_MS`

## Provider 的 `apiKey` 映射（速查）

- `tavily`: request body `api_key`
- `brave`: header `X-Subscription-Token`
- `bing`: header `Ocp-Apim-Subscription-Key`
- `serper`: header `X-API-KEY`


