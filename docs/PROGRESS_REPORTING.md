# Progress Reporting (ExecutionTraceEvent → AG-UI)

本规范统一 **Agent 会话 / LLM / Tool** 的进度汇报，使用 `ExecutionTraceEvent` 作为唯一进度事件，再投影到 AG‑UI。

## 1) 核心原则

- **单一进度源**：只用 `ExecutionTraceEvent` 表达 session/llm/tool 进度。
- **Hook 发 trace**：所有关键边界事件通过 Hook 发出，避免漏报。
- **AG‑UI 投影**：UI 仅消费 `ExecutionTraceEvent` 的投影结果。
- **best‑effort**：任何进度 emit 失败不能影响主流程。

## 2) 标准字段（ExecutionTraceEvent.fields）

字段键名定义在：
- `src/Aevatar.Agents.Abstractions/Tracing/ExecutionTraceEventFields.cs`（核心字段）
- `src/Aevatar.Agents.Maker/Tracing/ExecutionTraceEventMakerFields.cs`（Maker/共识扩展）

必须/常用字段：

- `status`（pending/running/completed/failed/cancelled）
- `progress`（0..1）
- `session_id`
- `execution_id`
- `agent_id`
- `phase`
- `message_id`
- `tool_name`
- `tool_call_id`
- `llm_model`
- `duration_ms`
- `error`

## 3) 标准 phase

定义在：
`ExecutionTraceEventPhase`

- `session.start`
- `session.stop`
- `llm.request`
- `llm.response`
- `tool.start`
- `tool.progress`
- `tool.end`
- `error`

## 4) Hook 发 trace（内置）

AI.Core 内置 Hook 会在以下阶段自动发 `ExecutionTraceEvent`：

- `OnSessionStart / OnStop`
- `BeforeLLMRequest / AfterLLMResponse`
- `BeforeToolExecute / AfterToolExecute`
- `OnError`

工具的中间进度由 `ToolExecutionContext.ReportProgressAsync` 汇报后转换为 `tool.progress` trace。

## 5) AG‑UI 投影（Trace‑only）

使用 `AgUiTraceProjector`：

```csharp
using Aevatar.Agents.AGUI;
using Aevatar.Agents.Abstractions.Tracing;

IReadOnlyList<AgUiEvent> events = AgUiTraceProjector.Map(traceEvent);
```

默认映射：

- `session.start/stop` → `RUN_*`
- `tool.*` → `TOOL_CALL_*`
- `llm.*` → `CUSTOM`（如存在 `assistant_response` 则输出 TEXT_MESSAGE）
- 其它 phase → 回落到 `AgUiExecutionTraceMapper`（Step + Custom）

## 6) 应用迁移建议

- 原先直接 emit `ToolCallStart/End/Result` 的应用：
  - 迁移为构造 `ExecutionTraceEvent`，再用 `AgUiTraceProjector` 投影。
- 原先用 `AgUiExecutionTraceMapper.Map` 的执行流：
  - 替换为 `AgUiTraceProjector.Map`（保持 Step/Custom 兼容）。

