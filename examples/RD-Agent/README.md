# RD-Agent 调研与 Aevatar 借鉴（Example）

本目录用于沉淀对 [microsoft/RD-Agent](https://github.com/microsoft/RD-Agent) 的调研结论，以及它对 Aevatar 的可落地借鉴点。

- 调研报告：见 `docs/RESEARCH_REPORT.md`
- 本次落地（B 路）：Aevatar 统一执行轨迹协议 `ExecutionTrace`（Protobuf）
  - Proto：`src/Aevatar.Agents.Abstractions/execution_trace.proto`
  - 说明文档：`src/Aevatar.Agents.Abstractions/docs/EXECUTION_TRACE.md`

## Trace Bundle 输出（统一环境变量）

默认情况下，框架会把 `ExecutionTrace` 输出到 `<repoRoot>/trace`（best-effort）。

如果你希望把输出目录改到其他位置（覆盖默认），请设置：

```bash
export AEVATAR_TRACE_DIR="/abs/path/to/aevatar_traces"
```


