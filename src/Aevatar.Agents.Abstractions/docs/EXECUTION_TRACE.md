# Execution Trace (Unified) - 统一执行轨迹协议

> 目标：让 **MAKER / UoT / Cognitive Workflow** 的“可导出执行轨迹”用 **同一套 Protobuf 契约** 表达，避免系统里同时存在多套 JSON/POCO/事件回放格式导致的碎片化。

---

## 1. 核心铁律

- **跨边界的 trace 必须是 Protobuf**：可持久化、可跨 runtime、可跨语言。
- **一棵树 + 一条时间线**：
  - `ExecutionTrace.root` 是树（执行结构）
  - `ExecutionTrace.events` 是可选时间线（UI/回放/调试）
- **能用通用字段表达就别再造私有 JSON**：优先用 `metrics/labels/decisions/alerts`，避免把大对象塞进 `output`。

---

## 2. Proto 定义位置

- 文件：`src/Aevatar.Agents.Abstractions/execution_trace.proto`
- C# 命名空间：`Aevatar.Agents.Abstractions.Tracing`
- 核心类型：
  - `ExecutionTrace`
  - `ExecutionTraceNode`
  - `ExecutionTraceDecisionSession` / `ExecutionTraceCandidate`
  - `ExecutionTraceAlert`

---

## 3. 统一模型（最小心智负担）

### 3.1 Root

- `execution_id`：稳定 ID（文件名/索引键）
- `kind`：MAKER / UoT / WORKFLOW / CUSTOM
- `status`：RUNNING / SUCCEEDED / FAILED / CANCELLED / TIMEOUT
- `cost`：token/llm_calls/duration 的统一口径
- `link`：可选关联 OpenTelemetry（`otel_trace_id/span_id`）

### 3.2 Node（树）

每个节点都能表达：

- **它是什么**：`name/type/description`
- **是否成功**：`status/error`
- **花费**：`cost`
- **分析字段**：`metrics/labels`
- **决策过程**：`decisions`（投票/排序/选择统一抽象）
- **异常信号**：`alerts`（red-flag 等）

### 3.3 Timeline（可选）

`events` 用于 UI 和回放，不要求所有系统都产出。

---

## 4. JSON 导出（给人看的）

Protobuf 是主格式；JSON 是导出格式：

- 工具：`Aevatar.Agents.Abstractions.Tracing.ExecutionTraceJsonExtensions`
  - `trace.ToJsonString()`
  - `ExecutionTraceJsonExtensions.ParseExecutionTraceJson(json)`

---

## 4.1 统一导出目录（AEVATAR_TRACE_DIR）

框架层提供统一环境变量：

- **`AEVATAR_TRACE_DIR`**：Trace Bundle 输出根目录

默认情况下（未设置该 env var），框架会自动选择一个默认目录（best-effort）：

- **`<repoRoot>/trace`**（从当前工作目录向上查找 `.git` / `Directory.Packages.props` / `*.sln*`）

如果设置了 `AEVATAR_TRACE_DIR`，则优先使用该目录。

### Trace Bundle v1 目录规范（FileExecutionTraceStore）

```
${AEVATAR_TRACE_DIR}/
  <executionId>/
    trace.pb          # Protobuf binary (canonical)
    trace.json        # JSON export (human/UI)
    manifest.json     # Bundle metadata
    artifacts/        # Optional extra files (UI/debug)
```

---

## 5. 适配器（把旧世界接进统一世界）

> 适配器的职责：**从各模块已有的结果/事件回放结构**，构造 `ExecutionTrace`。

- MAKER：`Aevatar.Agents.Maker` 内提供 `MakerResult.ToExecutionTrace()`
- UoT：`Aevatar.Agents.CreativeReasoning` 内提供 `UoTResult/TUoTResult.ToExecutionTrace()`
- Cognitive Workflow：`Aevatar.Agents.Cognitive` 内提供 `IEnumerable<WorkflowStepEvent>.ToExecutionTrace()`

（具体实现以对应项目的 `*ExecutionTrace*` 扩展类为准）

---

## 6. 品味自检（避免坏味道）

- 不要把论文全文/长日志/大对象塞进 `output`（会把 state/trace 变成“体积炸弹”）
- `labels` 只放索引用的轻字段；可分析字段放 `metrics`
- 一个系统如果要“导出 trace”，最终必须落到 `ExecutionTrace`（避免新体系继续分裂）


