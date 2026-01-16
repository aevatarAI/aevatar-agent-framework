## Aevatar.Agents.AI（聚合注册）

一句话：**给应用提供一个无反射的一行 DI 入口，默认同时启用 MEAI + LLMTornado。**

### 为什么存在

- `Aevatar.Agents.AI.Core` 是核心能力层，不应反向依赖具体 provider（避免依赖环/体积膨胀）。
- 应用又希望“一行代码启用多家模型提供商”。
- 于是把“聚合注册”单独放到 `Aevatar.Agents.AI`：它显式引用 `MEAI` 和 `LLMTornado`，所以无需反射。

### 你该怎么用

在应用中引用该项目/包后：

- `services.AddAevatarLLMProviders();`

### 依赖关系

```
Aevatar.Agents.AI
 ├── Aevatar.Agents.AI.Core
 ├── Aevatar.Agents.AI.MEAI
 └── Aevatar.Agents.AI.LLMTornado
```

### 关键机制（框架层）

- 多个 `ILLMProviderFactory` 会被框架注入器自动组合成 `CompositeLLMProviderFactory`，从而让同一应用同时支持多种 provider。


