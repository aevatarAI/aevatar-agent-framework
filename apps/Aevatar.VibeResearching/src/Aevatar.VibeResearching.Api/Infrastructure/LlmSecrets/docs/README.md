## LlmSecrets (SRA built-in Secrets API)

目的：让科研平台（Web/Obsidian/其他 host）可以通过同一套 API 配置：
- LLM provider **types**（可配置）
- LLM provider **instances**（已配置，多模型）

### 文件职责

```
LlmSecrets/
├── LlmSecretsApi.cs            # 路由映射（MapLlmSecretsApi）
├── LlmSecretsApi.Contracts.cs  # DTO/内部模型（不跨服务边界）
├── LlmSecretsApi.ProviderProfiles.cs  # provider 类型表 + instanceName 推断
├── LlmSecretsApi.ProviderResolver.cs  # instance -> resolved
├── LlmSecretsApi.ProviderCatalog.cs   # providers + instances 列表
└── LlmSecretsApi.Probe.cs             # best-effort test/models
```

### 多实例命名约定

- 推荐：`<providerType>-<model>`，例如：
  - `deepseek-deepseek-chat`
  - `deepseek-deepseek-reasoner`


