# Helpers

依赖注入辅助类集合，用于注入各类运行时依赖（LLM、工具、Hooks、Web 搜索等）。

## 主要内容
- `AIAgentLLMProviderFactoryInjector`：LLM Provider 注入。
- `AIAgentToolManagerInjector`：工具管理器注入。
- `AIAgentWebSearchProviderInjector`：Web 搜索 Provider 注入。
- `AIAgentEmbeddingFactoryInjector`：Embedding 工厂注入。
- `AIAgentHookInjector` / `AIAgentHostConfigurationInjector` 等：Hooks 与宿主配置注入。

