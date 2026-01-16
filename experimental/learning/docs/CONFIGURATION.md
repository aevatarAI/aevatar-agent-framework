# Aevatar.Learning — Configuration

> 端口策略：仓库内 **禁止使用 `:5000`**。默认后端 `:5678`，前端（Vite dev）`:5173`，且都必须可配置。

## LLMProviders（必须配置，否则 AI Native 功能不成立）

Learning 系统通过 `LLMProviders` 选择默认 provider，并允许请求级覆盖。

### 1) 配置文件（推荐）

两种方式（二选一，优先推荐全局 secrets）：

- **推荐（一次配置，全仓复用）**：写入用户级 secrets（加密）
  - 默认：`~/.aevatar/secrets.json`（用 `src/Aevatar.Agents.SecretsCli` 写入）
  - 覆盖：`AEVATAR_SECRETS_PATH` / `AEVATAR_SECRETS_DIR`
- **可选（项目级覆盖）**：复制示例为真实配置（不要提交真实密钥）
  - `experimental/learning/src/Aevatar.Learning.Api/appsettings.secrets.json.example`
  - → `experimental/learning/src/Aevatar.Learning.Api/appsettings.secrets.json`

示例结构（与仓库 Notebook 系统一致）：

```json
{
  "LLMProviders": {
    "Default": "deepseek",
    "Providers": {
      "deepseek": {
        "Name": "deepseek",
        "ProviderType": "OpenAI",
        "ApiKey": "${DEEPSEEK_API_KEY}",
        "Endpoint": "https://YOUR_OPENAI_COMPAT_ENDPOINT",
        "Model": "deepseek-chat",
        "Temperature": 0.2,
        "MaxTokens": 1200,
        "TimeoutMilliseconds": 900000
      }
    }
  }
}
```

说明：
- `ProviderType`：常用为 `OpenAI`（OpenAI-compatible，包括 DeepSeek / DashScope 等）；如需 `AzureOpenAI` 按对应字段填写。
- `TimeoutMilliseconds`：建议从 2~10 分钟起步，避免长文档处理时频繁超时。

### 2) 如何验证配置生效

启动后访问：

- `GET http://localhost:5678/api/info`

应返回默认 provider 的 **非敏感** 信息（例如 providerType/model/endpoint/timeout）。

### 3) 请求级覆盖（providerName）

系统允许在创建 session 或提交 input 时覆盖 provider（用于不同 Notebook 主题使用不同模型）：

- `POST /api/sessions`：`{ "providerName": "..." }`（可选）
- `POST /api/sessions/{id}/input`：`{ "message": "...", "providerName": "..." }`（可选）

> 具体字段名以 API 合同为准；原则是：**不硬编码 provider，默认走 `LLMProviders:Default`，允许请求级覆盖**。

## 运行时选择（Local / Orleans）

Learning 系统遵循 Aevatar “业务逻辑与运行时解耦”的原则。

规划配置项（MVP 默认 Local）：

```json
{
  "AgentRuntime": {
    "RuntimeType": "Local"
  }
}
```

可选值：
- `Local`：本地单机开发/测试（默认）
- `Orleans`：分布式模式（后续任务实现时启用）

## 端口与环境变量

### Backend（默认 5678）

后端实际监听由 `ASPNETCORE_URLS` 决定；`experimental/learning/boot.sh` 会负责拼装它。

常用：
- `LEARNING_API_PORT`：后端端口（默认 `5678`）
- `ASPNETCORE_URLS`：例如 `http://localhost:5678`

### Frontend（默认 5173）

常用：
- `LEARNING_FRONTEND_PORT`：前端端口（默认 `5173`，用于 boot.sh kill 端口与提示）
- `VITE_LEARNING_API_URL`：前端连接后端的 baseUrl（例如 `http://localhost:5678`）

## Notebook 根目录（目录即真相源）

每个 Notebook 对应一个真实目录；默认根目录建议：

- `~/AevatarLearning/`

可通过环境变量覆盖：

- `LEARNING_NOTEBOOK_ROOT=/your/path`

## Secrets 管理（必须）

- `experimental/learning/src/Aevatar.Learning.Api/appsettings.secrets.json` 必须保持在 `.gitignore` 中（不入库）
- 推荐把真正的 key 放在环境变量（例如 `${DEEPSEEK_API_KEY}`），配置文件只引用它
 - 或者把 key 放在用户级 secrets（加密）里（推荐：跨项目复用）


