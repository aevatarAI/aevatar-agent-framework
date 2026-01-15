# 技术债清单（AI.Abstractions）

## P2

### LLMProviderConfig.Default 未初始化

- **现状**：`LLMProviderConfig.Default` 为非空属性但未初始化（CS8618）。
- **影响**：配置绑定与默认值语义不明确，易在运行期产生 null。
- **建议**：改为 `required` 或设为可空并在读取点提供默认值/校验。
