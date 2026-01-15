# 技术债清单（AI.Core）

## P2

### Tooling loop host 仍偏大

- **现状**：`IToolingLoopHost` 同时承担执行、发布、hook、LLM 调用与 allowlist 相关职责。
- **影响**：接口膨胀后会逐渐逼近“新聚合点”，降低可替换性与可测性。
- **建议**：拆分为更细 host（例如 `IToolingExecutionHost` / `IToolingPublishHost` / `IToolingHookHost`），再由 `ToolingLoopContext` 组合。
- **触发条件**：新增 tool 相关生命周期/日志/遥测字段，或 loop 行为进一步扩展时优先处理。

