# InterruptibleChatWebDemo

一个 **前后端一体** 的最小 demo：
- 后端：ASP.NET Core Minimal API + Local runtime
- 前端：纯 `wwwroot/` 静态 HTML/JS（无需 npm）
- 能力：**聊天（SSE 流式输出）** + **新消息打断旧消息（latest-wins interrupt）**

端口策略：
- 默认监听 `http://127.0.0.1:5688`（避免和 SRA 常用的 `:5678/:5679` 冲突）
- 不会使用 `:5000`

## Run

```bash
cd examples/InterruptibleChatWebDemo
dotnet run
```

浏览器打开：`http://127.0.0.1:5688/`

## 接入真实 LLM（OpenAI）

### 配置方式（和仓库其它 demo 一致）

优先级从低到高：
- `appsettings.json`
- `AddAevatarUserSecrets()`（推荐：用户级 `~/.aevatar/secrets.json`）
- `appsettings.secrets.json`（项目级覆盖，不提交）
- 环境变量

### 推荐：写入 user secrets（不污染仓库）

```bash
# 写入 DeepSeek（OpenAI-compatible）API Key
export DEEPSEEK_API_KEY="..."
dotnet run --project src/Aevatar.Agents.SecretsCli -- set "LLMProviders:Providers:deepseek:ApiKey" --from-env DEEPSEEK_API_KEY
```

### 备用：项目级 secrets 文件

- 复制 `appsettings.secrets.json.example` 为 `appsettings.secrets.json`
- 把 `${DEEPSEEK_API_KEY}` 替换成真实 key

## How it proves "interrupt"

- 每次 `/input` 都会发起一次 RPC 调用，并在 `RpcRequest.metadata` 注入 `run_id`/`run_scope_id`
- 框架层的 `RunManager` 会对同一 scope 执行 latest-wins：新 run 启动会 cancel 旧 run
- Agent 的 `ChatSlowAsync` 用 `CancellationToken` 驱动，旧 run 会立刻停止继续输出 token


