# 技术债清单（AI.LLMTornado）

## P2

### LLMTornadoProvider 存在可能的 null 赋值

- **现状**：`LLMTornadoProvider` 内部存在可能的 null 赋值告警（CS8601）。
- **影响**：类型语义与运行时行为不一致，潜在 NRE。
- **建议**：为可空来源加兜底（默认值/guard），或调整类型注解与构造逻辑。
