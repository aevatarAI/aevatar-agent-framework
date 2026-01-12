## Aevatar User Secrets（用户级加密密钥）

目标：把 **API Key / 连接串 / 凭据** 从每个系统的 `appsettings.secrets.json` 里解放出来，改为 **用户级统一存放**，并在应用启动时自动 merge 到 `IConfiguration`。

### 默认位置

- **macOS/Linux**：`~/.aevatar/secrets.json`
- **Windows**：`%USERPROFILE%\\.aevatar\\secrets.json`

> 文件是**加密**的（不建议手工编辑）；旁边可能存在 `masterkey.bin`（后备主密钥文件）。

### 环境变量覆盖

- **`AEVATAR_SECRETS_PATH`**：指定 secrets 文件完整路径（最高优先级）
- **`AEVATAR_SECRETS_DIR`**：指定 secrets 目录（若未设置 `AEVATAR_SECRETS_PATH`）

### 读取与覆盖优先级（推荐）

配置源从低到高（越靠后优先级越高）：

1) `appsettings.json`
2) `AddAevatarUserSecrets()`（全局 user secrets，跨项目复用）
3) `appsettings.secrets.json`（可选：项目级覆盖，不提交）
4) 环境变量（部署/CI 场景覆盖）

### 存储格式

secrets 内部是 **IConfiguration 风格的 key/value**（例如 `Foo:Bar`），典型例子：

- `LLMProviders:Providers:deepseek:ApiKey`
- `LLMProviders:Providers:openai-gpt4:ApiKey`
- `ConnectionStrings:MongoDB`

### 如何在代码中启用

在 `Program.cs` 里（建议在 `appsettings.secrets.json` 之前）：

```csharp
builder.Configuration.AddAevatarUserSecrets();
builder.Configuration.AddJsonFile("appsettings.secrets.json", optional: true, reloadOnChange: true);
```

### 如何写入（运行时）

框架提供了可写 store（供 UI/API/CLI 使用）：

- 注册：`services.AddAevatarUserSecretsStore()`
- 接口：`IAevatarUserSecretsStore`（`Set/TryGet/Remove/GetAll`）

### 如何写入（CLI，推荐）

仓库内提供了一个最小 CLI：`src/Aevatar.Agents.SecretsCli/`。

> 推荐使用 `--from-env`，避免把密钥写进 shell history。

```bash
# 1) 写入 LLM API Key（示例：DeepSeek / OpenAI-compatible）
export DEEPSEEK_API_KEY="..."
dotnet run --project src/Aevatar.Agents.SecretsCli -- set "LLMProviders:Providers:deepseek:ApiKey" --from-env DEEPSEEK_API_KEY

# 2) 列出已保存的 keys（不会打印 value）
dotnet run --project src/Aevatar.Agents.SecretsCli -- list
```

### 如何写入（Web App）

仓库内提供了一个最小本地 Web App：`apps/Aevatar.Secrets.Api/`（默认端口 `6667/6677`）。

```bash
dotnet run --project apps/Aevatar.Secrets.Api/Aevatar.Secrets.Api.csproj
```

打开 `http://localhost:6677`（浏览器推荐；`6667` 在 Chrome 里会触发 `ERR_UNSAFE_PORT`），填写 providerName + apiKey 即可写入（仅允许 localhost 调用写入接口）。

该 Web App 还支持：

- **显示配置状态**（是否已配置 key、resolved endpoint）
- **Test connection**（best-effort：调用 list models 验证 key+endpoint）
- **Fetch models**（best-effort）

### 安全说明（务实）

- payload 使用 **AES‑GCM** 加密落盘
- 主密钥：
  - macOS：best‑effort 尝试写入 Keychain（超时/不可用则回退）
  - 其他平台：默认使用 `masterkey.bin`（同目录，权限 best‑effort 收紧为 0600/0700）


