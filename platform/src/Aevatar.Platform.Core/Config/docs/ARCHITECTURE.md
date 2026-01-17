## 目录结构
```
Config/
├── AevatarConfigLoader.cs   # 加载 ~/.aevatar/config.json + secrets.json
├── AevatarConfigModels.cs   # 配置模型 (POCO)
├── AevatarConfigSerializer.cs # Map/JsonElement 转强类型
├── ConfigKeyAliases.cs      # 配置 key 别名统一
├── ConfigMapNormalizer.cs   # object/JsonElement 统一归一化
├── ModelDefaults.cs         # provider/model 默认值解析
├── SecretsKeyRules.cs       # secrets.json key 规则统一
└── docs/ARCHITECTURE.md     # 架构与约束记录
```

## 架构决策
- **默认解析集中化**：provider/model 默认值只在 `ModelDefaults` 中解析，避免 CLI/TUI 重复。
- **配置本地化**：Config 只服务 Platform 本地使用，不跨进程/stream，避免 Protobuf 冗余。
- **LLMProviders 兼容**：从 `secrets.json`/`config.json` 合并 provider 列表，保持与 aevatar-config 一致。
- **Key 规则统一**：secrets.json 的 key 规则集中在 `SecretsKeyRules`，禁止散落。
- **Map 归一化**：object/JsonElement 归一化只在 `ConfigMapNormalizer` 实现。
- **强类型转换**：Map/JsonElement → `AevatarConfig` 由 `AevatarConfigSerializer` 负责。
- **别名统一**：配置 key 别名集中在 `ConfigKeyAliases`。

## 开发规范
- 新增配置项时同步更新 `AevatarConfigModels` 与 `AevatarConfigLoader`。
- provider/model 解析必须经过 `ModelDefaults`，禁止自行拆分。

## 变更日志
- 2026-01-15：新增 `ModelDefaults` 统一 provider/model 解析。
- 2026-01-15：切换到 `config.json`/`secrets.json` 并合并 LLMProviders。
- 2026-01-15：新增 `SecretsKeyRules` 统一 key 规则。
- 2026-01-15：新增 `ConfigMapNormalizer` 统一 Map 归一化。
- 2026-01-15：新增 `AevatarConfigSerializer` 去除 YAML 往返。
- 2026-01-15：新增 `ConfigKeyAliases` 统一别名表。
