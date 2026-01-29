# Configuration

YAML 配置相关实现，用于加载与应用 Agent 的配置，并支持全局注册表。
面向 `AIGAgentBase` 的可配置化场景。

## 主要内容
- `AgentYamlConfigLoader`：加载 YAML 配置与路径解析。
- `AgentYamlConfigApplier`：将配置应用到 Agent 实例。
- `GlobalAgentYamlRegistry`：全局 YAML 注册与索引。
- `YamlConfigurableAIGAgentBase`：可配置的 Agent 基类扩展。

