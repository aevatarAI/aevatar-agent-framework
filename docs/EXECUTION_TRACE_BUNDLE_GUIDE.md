# ExecutionTrace Bundle Guide（统一执行轨迹导出）

本指南说明：

- 如何通过 **`AEVATAR_TRACE_DIR`** 启用统一 `ExecutionTrace` 导出
- Trace Bundle v1 的目录结构
- MakerSystem / CreativeSystem / Cognitive Workflow 如何查看与消费 trace

> 背景：Aevatar 之前存在多套“执行轨迹/回放/日志”形态；现在收敛到 **一个跨边界 Protobuf 契约**：`ExecutionTrace`。

---

## 1. 一句话 TL;DR

- **默认落盘到 `<repoRoot>/trace`**（best-effort）
- 你也可以设置 `AEVATAR_TRACE_DIR` 覆盖默认目录；框架会将每次执行的 `ExecutionTrace` 导出为 bundle：
  - `trace.pb`（canonical）
  - `trace.json`（给人/给 UI）
  - `manifest.json`

---

## 2. 目录选择方式（环境变量优先）

```bash
export AEVATAR_TRACE_DIR="/abs/path/to/aevatar_traces"
```

### 默认行为（未设置 env var）

框架会自动选择默认目录（best-effort）：

- 从当前工作目录向上查找 `.git` / `Directory.Packages.props` / `*.sln*`
- 找到的目录视为 `<repoRoot>`，默认输出到：`<repoRoot>/trace`

---

## 3. Trace Bundle v1 目录规范

```
${AEVATAR_TRACE_DIR}/
  <executionId>/
    trace.pb
    trace.json
    manifest.json
    artifacts/
      memory_graph.pb
      memory_graph.json
```

说明：

- `<executionId>` 来自 `ExecutionTrace.execution_id`
- `artifacts/` 为可选：用于 UI/调试/检索的额外文件
  - 当前框架会 best-effort 写入 `memory_graph.pb/json`（ExecutionTrace → MemoryGraph 投影）

---

## 4. 关键代码位置

- **统一 Protobuf 协议**：`src/Aevatar.Agents.Abstractions/execution_trace.proto`
- **协议说明**：`src/Aevatar.Agents.Abstractions/docs/EXECUTION_TRACE.md`
- **Store 抽象**：`src/Aevatar.Agents.Abstractions/Tracing/IExecutionTraceStore.cs`
- **默认文件落盘实现**：`src/Aevatar.Agents.Core/Tracing/FileExecutionTraceStore.cs`
- **Trace→Graph 投影**：`src/Aevatar.Agents.Core/MemoryGraph/ExecutionTraceMemoryProjector.cs`
- **Graph Store 抽象**：`src/Aevatar.Agents.Abstractions/Memory/IMemoryGraphStore.cs`
- **默认 Graph 文件实现**：`src/Aevatar.Agents.Core/MemoryGraph/FileMemoryGraphStore.cs`
- **Trace Store 装饰器（保存后自动投影）**：`src/Aevatar.Agents.Core/Tracing/ProjectingExecutionTraceStore.cs`
- **fallback no-op 实现**：`src/Aevatar.Agents.Core/Tracing/NullExecutionTraceStore.cs`（仅在 file store 初始化失败等异常情况下使用）
- **DI 注册（优先 env var）**：`src/Aevatar.Agents.Core/Extensions/ServiceCollectionExtensions.cs`

---

## 5. 三个系统的接入方式与查看路径

### 5.1 MakerSystem（examples/MakerSystem）

- **执行完成时**：
  - `MakerResult.ToExecutionTrace()` → `IExecutionTraceStore.SaveAsync(...)`
  - 并额外写入本次 run 的 `output/.../artifacts/trace.json`（方便 UI 直接看）
- **UI 查看**：
  - 左侧 File Explorer → `ARTIFACTS/trace.json`（已做结构化渲染）
- **Bundle 落盘**（如果设置了 `AEVATAR_TRACE_DIR`）：
  - `${AEVATAR_TRACE_DIR}/<makerExecutionId>/trace.json`

### 5.2 CreativeSystem（examples/CreativeSystem）

- **执行完成时**：
  - `UoTResult.ToExecutionTrace()` 或 `TUoTResult.ToExecutionTrace()` → `IExecutionTraceStore.SaveAsync(...)`
- **UI 查看**：
  - 前端会调用后端端点：`GET /api/runs/{runId}/trace` 并渲染统一 trace
- **Bundle 落盘**（如果设置了 `AEVATAR_TRACE_DIR`）：
  - `${AEVATAR_TRACE_DIR}/<uotExecutionId>/trace.json`

### 5.3 Cognitive Workflow（src/Aevatar.Agents.Cognitive）

- **执行完成/失败时**：
  - `CognitiveCoordinatorGAgent` 会 best-effort 构造 `ExecutionTrace`（优先基于 `WorkflowStepEvent` 回放数据）
  - 并调用注入的 `ExecutionTraceStore.SaveAsync(...)`
- **注意**：只有在 Host 使用 `AddAevatarAgentSystem(...)` 时，`IExecutionTraceStore` 才会按“环境变量优先 + 默认 `<repoRoot>/trace`”的规则配置。

---

## 6. 常见问题（FAQ）

### Q1：为什么默认会落盘？

为了让调试/回归/对比具备“默认可用”的基础能力（特别是本 repo 的 examples 场景）。如果你需要将 trace 输出到其他路径，请用 `AEVATAR_TRACE_DIR` 覆盖默认目录。

### Q2：为什么要同时写 trace.pb 和 trace.json？

- `trace.pb`：机器友好、稳定、最小体积（canonical）
- `trace.json`：人类/前端友好（便于快速调试与展示）

---

## 7. 下一步建议

- 将更多运行态产物（例如 consensus analysis / UI artifacts）统一放入 bundle 的 `artifacts/`
- 把 OTel traceId/spanId 写入 `ExecutionTrace.link`，实现“bundle ↔ OTel”双向跳转


