## Aevatar.Agents.SecretsCli

最小 secrets 管理 CLI，用于写入/读取框架的 **用户级加密 secrets**（默认 `~/.aevatar/secrets.json`），避免每个 demo/app 都复制 `appsettings.secrets.json`。

### 常用命令

```bash
# 写入（推荐：从 env 读，避免泄露到 shell history）
export DEEPSEEK_API_KEY="..."
dotnet run --project src/Aevatar.Agents.SecretsCli -- set "LLMProviders:Providers:deepseek:ApiKey" --from-env DEEPSEEK_API_KEY

# 列出 keys（不会打印 value）
dotnet run --project src/Aevatar.Agents.SecretsCli -- list
```

### 路径覆盖

- `AEVATAR_SECRETS_PATH=/path/to/secrets.json`
- `AEVATAR_SECRETS_DIR=/path/to/dir`

也可用 CLI 参数：

```bash
dotnet run --project src/Aevatar.Agents.SecretsCli -- --path /tmp/aevatar.secrets.json list
```

更多说明见：`src/Aevatar.Agents.Core/docs/UserSecrets.md`


