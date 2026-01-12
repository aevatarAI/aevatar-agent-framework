## Infrastructure (ScientificResearchAssistant.Api)

职责：放置 **跨模块基础设施**（sidecar/本地能力/同步任务/Secrets API 等），保持可组合与可替换。

### 目录结构

```
Infrastructure/
├── LlmSecrets/                     # 内置 Secrets API（用于配置 LLM providers/instances）
│   ├── LlmSecretsApi.cs            # endpoints（/api/llm/* /api/secrets/*）
│   ├── LlmSecretsApi.Contracts.cs  # DTO + 内部模型 + mask helper
│   ├── LlmSecretsApi.ProviderProfiles.cs
│   ├── LlmSecretsApi.ProviderResolver.cs
│   ├── LlmSecretsApi.ProviderCatalog.cs
│   └── LlmSecretsApi.Probe.cs      # best-effort test/models
├── SkillPacksOptions.cs
├── SkillPacksSyncProgress.cs
├── SkillPacksSyncService.cs
├── SkillPacksSyncHostedService.cs
├── BroadcastEventHub.cs
└── docs/README.md                  # 本文：架构镜像
```

### 设计原则

- **Local-only 写入**：Secrets/LLM 配置写入严格限制 loopback，避免远程误配置。
- **职责拆分**：Probe/Resolver/Catalog/Profile 分离，避免“一个文件吞掉全部复杂度”。


