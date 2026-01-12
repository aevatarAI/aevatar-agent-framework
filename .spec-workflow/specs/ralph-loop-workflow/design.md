# Design Document

## Overview

本设计实现一个新的 Cognitive DSL workflow：**`ralph-loop`**，用于执行“可自动验证的有界迭代（bounded objective loop）”。

关键约束来自现有实现：`Aevatar.Agents.Cognitive` 的 Coordinator/Worker 默认 **不启用 Function Calling**（见 `CognitiveAIGAgentBase.RegisterToolsAsync`），因此 ralph-loop 采用 **DSL 原语（deterministic primitives）** 执行工作区读写、代码搜索与命令验证；LLM 只负责“提出下一步改动”，Verifier 由确定性执行结果驱动。

## Applicability to scientific-research-assistant

### Can it be used directly?

可以，但要先把 **“目标可验证”** 定义清楚：ralph-loop 只对“成功标准可自动验证”的目标有意义（例如命令 exit code / JSON schema / 文件存在性与计数阈值）。

`scientific-research-assistant` 当前的 `mode=vibe` 后端是 **单轮编排**（`VibeOrchestrator.ExecuteOneRoundAsync`），并没有内置“多轮直到达标”的 outer loop；因此要实现“**不达到目标不要停下**”，需要引入一个 **外层循环**（见下文）。

### Recommended integration shape for SRA

优先推荐在 SRA 侧实现一个 **Vibe Goal Loop Runner**（outer loop），每轮调用一次 `ExecuteOneRoundAsync`，并在轮末执行一个 **deterministic / quorum verifier**：

- **deterministic verifier（首选）**：直接读取 File-SSoT（例如 `deliverables/*.json`、`decisions/goals.json`、`dag/*.json`）检查阈值/完整性（文件存在、字段非空、数量达标、路径有效等）。
- **quorum verifier（可选）**：复用 `DagConsensusRunner.Quorum` 的风格，让 N 个 verifier 对“是否达标”投票（结构化 JSON），以“>=quorum 且无 hard red-flags”为通过条件。

这样做的好处是：

- **不绕过 SRA 的 Store 约束**：Delivery/Goals/Trace 都已经是 bounded 的 Protobuf-JSON 写入；outer loop 只复用这些入口，不用“补丁改文件”的方式破坏 version/invariants。
- **目标与停机条件更清晰**：把“达标”落到可检查的 artifacts 上，而不是凭主观感觉停。

### “Don’t stop until target” semantics (safety)

“不达到目标不要停下”在工程上必须解释为：

- loop **只有在 `verifier_pass=true` 时才算完成（completed）**
- 但系统 **仍必须有硬护栏**：`max_iterations` / timeout / 外部 cancel（否则遇到不可达目标会无限烧算力与挂死）

因此默认行为应是：**until pass or limit/cancel**，并允许调用方把 `max_iterations` 配置得足够大来接近“尽量不停”。

### SRA integration (concrete design)

本节把“ralph-loop 语义”落到 `scientific-research-assistant` 的现有骨架上（Vibe 单轮编排 + File-SSoT stores）。

#### Integration goal

在 SRA 中提供一个 **`vibe_loop`（或 `vibe_goal_loop`）模式**：系统会反复执行 Vibe 单轮（plan → workers → DAG consensus → delivery + trace），并在每轮结束后运行一个 verifier；只有 verifier 通过才停止。

#### Where to implement

- **新增组件**：`VibeGoalLoopRunner`（建议放在 `scientific-research-assistant/src/ScientificResearchAssistant.Api/Vibe/`）
  - 职责：负责 outer loop（迭代、预算、取消、StepEvent/CustomEvent 投影）
- **新增 verifier**：`IVibeGoalVerifier` + 默认实现 `DeliveryThresholdGoalVerifier`
  - 职责：确定性地读取 File-SSoT（`DeliveryCenterStore` / `DagStore` / `GoalsStore` / `TraceStore`）并判定 pass/fail
- **入口接线**：在 `ResearchRunExecutor.ExecuteAsync` 增加 `mode=vibe_loop` 分支，调用 `VibeGoalLoopRunner.ExecuteUntilGoalAsync`

#### API contract (minimal)

现有 `SessionInputInDto.Mode` 支持 `"chat"` / `"vibe"`。为集成 loop，增加：

- `Mode="vibe_loop"`（或 `"vibe_goal_loop"`）
- 以及一个可选的 loop 配置对象（建议新增到 `SessionInputInDto`）：
  - `Loop.MaxIterations`（int）
  - `Loop.MaxTotalDurationMs`（int）
  - `Loop.Verifier`（string，默认 `"delivery-thresholds"`）
  - `Loop.Criteria`（结构化 JSON，deterministic 可评估）

> 注：SRA 已经强依赖“bounded + Protobuf-JSON 文件写入”，因此 Criteria 也应保持简单、可检查、可版本化。

#### Deterministic success criteria schema (example)

（示例，仅用于说明；最终以 JSON schema 固定并单测）

- `deliverables.conclusions.min_count`
- `deliverables.evidence.min_count`
- `deliverables.tasks.min_count`
- `delivery_snapshot.changed_summary.min_chars`
- `dag.nodes.min_count` / `dag.edges.min_count`

verifier 只做 **确定性检查**，禁止让 LLM 自评“达到目标”。

#### Execution flow

```mermaid
graph TD
  U[User POST /api/sessions/{id}/input<br/>mode=vibe_loop] --> R[ResearchRunExecutor]
  R --> L[VibeGoalLoopRunner]

  L -->|for each iteration| M[MaterialsService.LoadAsync + workspace hydrate]
  M --> O[VibeOrchestrator.ExecuteOneRoundAsync]
  O --> S[File-SSoT Stores<br/>Goals/Brief/DAG/Delivery/Trace]
  S --> V[IVibeGoalVerifier.VerifyAsync]
  V -->|pass| END[Stop: completed]
  V -->|fail| L
```

#### Stop conditions (must be explicit)

- **completed**: `verifier_pass=true`
- **limit**: `iteration >= max_iterations` 或 `elapsed >= max_total_duration`
- **cancelled**: `CancellationToken` 取消（HTTP abort / server shutdown）
- **failed**: 仅在“不可恢复/不可继续”的硬错误（例如 verifier 配置非法、workspace root 不可用）

#### Observability

沿用现有 AG-UI 投影风格：

- 每轮开始/结束发 `StepStartedEvent/StepFinishedEvent`（StepName 例如 `vibe.loop.round_{i}`）
- verifier 输出通过 `CustomEvent` 发布（例如 `aevatar.vibe.loop_verifier`，包含 `pass`、`reason`、关键计数与截断标记）

#### Safety & fairness

- outer loop 必须持有 `session.RunLock`（避免并发 run 导致 File-SSoT 竞争与 UI 混乱）
- 但这意味着 loop 会占用 session 的交互窗口：如需“后台长跑 + 前台仍可交互”，需在后续引入后台 job/队列（本 spec 暂不强制实现）

## Steering Document Alignment

### Technical Standards (tech.md)

- **.NET 10 / Protobuf-first**：ralph-loop 的跨边界状态与输出必须可被 `google.protobuf.Value` 承载；如引入新契约，必须新增 `.proto`（不手写可序列化类）。
- **Actor + Event-driven**：workflow 执行沿用 `CognitiveCoordinatorGAgent` 的 StepEvent（Protobuf）事件流，可观测、可回放。
- **安全策略**：命令执行必须 non-interactive、超时、输出限长；工作区路径必须 sandbox（禁止路径逃逸）。
- **端口策略**：本功能不引入常驻服务；任何示例/默认配置 **禁止使用 `:5000`**。

### Project Structure (structure.md)

实现遵循既有组织：

- Workflow 定义放在 `src/Aevatar.Agents.Cognitive/workflows/ralph-loop.yaml`
- 新增 DSL 原语执行器放在 `src/Aevatar.Agents.Cognitive/Execution/*Executor.cs`
- 对外/跨边界消息（如需要）放在 `src/Aevatar.Agents.Cognitive/*.proto`（独立文件，避免膨胀 `cognitive_messages.proto`）
- 单元测试放在 `test/Aevatar.Agents.Cognitive.Tests/`

## Code Reuse Analysis

### Existing Components to Leverage

- **`src/Aevatar.Agents.Cognitive/Engine/WorkflowParser.cs`**：YAML→`WorkflowDefinition` 的解析与 defaults 注入。
- **`src/Aevatar.Agents.Cognitive/Agents/CognitiveCoordinatorGAgent.cs`**：Step 调度、StepEvent 事件、递归 `workflow_call` 执行模型。
- **`src/Aevatar.Agents.Cognitive/Execution/TransformExecutor.cs`**：token-free 的状态更新（`set_path/inc_path/append_path/...`），用于迭代计数、状态机推进。
- **`src/Aevatar.Agents.Cognitive/workflows/axiom_theorem_loop.yaml`**：递归 loop 的参考范式（`state.done/status` + `stop_or_continue`）。
- **`scientific-research-assistant/.../DagConsensusRunner.Quorum.cs`**：确定性 precheck + 投票/判定的“先便宜、再昂贵”的治理风格（ralph-loop 也会先做 cheap verifier，再决定是否继续）。

### Integration Points

- **`cognitive-mesh/Aevatar.CognitiveMesh.Strategies/CognitiveStrategy.cs`**：自动加载 `src/Aevatar.Agents.Cognitive/workflows/` 下的 YAML；新增 `ralph-loop.yaml` 后可直接通过 `ReasoningOptions.CognitiveWorkflow="ralph-loop"` 使用。
- **Workflow Step System**：在 `CognitiveCoordinatorGAgent.ExecuteStepAsync` 增加新的 step type 分支（见下文“Components and Interfaces”）。

## Architecture

ralph-loop 的执行被拆为“固定管线 + 有界递归”：

1. **初始化 state**（deterministic）
2. **提取信号**：从 `goal + last_verifier` 生成搜索/读取计划（LLM，输出严格 JSON）
3. **收集上下文**：代码搜索 + 文件读取（deterministic primitives）
4. **生成补丁**：基于上下文输出 apply_patch 风格的补丁集合（LLM，严格 JSON）
5. **应用补丁**：逐个 patch 应用（deterministic primitive）
6. **验证**：运行 verifier 命令（deterministic primitive），并以 exit code 决定 pass/fail
7. **状态推进**：`iteration++`，决定 stop 或 recurse

```mermaid
graph TD
  A[Caller / Service] --> B[CognitiveStrategy.ExecuteAsync<br/>CognitiveWorkflow=ralph-loop]
  B --> C[CognitiveCoordinatorGAgent]
  C --> D[Workflow: ralph-loop.yaml]

  D --> E[llm_call: extract_signals]
  D --> F[primitive: workspace_code_search]
  D --> G[primitive: workspace_read_file]
  D --> H[llm_call: propose_patches]
  D --> I[primitive: workspace_apply_patch]
  D --> J[primitive: sandbox_command (verifier)]
  D --> K[transform/conditional: stop_or_continue]
  K -->|recurse| D

  C --> L[StepEvent/WorkflowProgressEvent (Protobuf)]
```

## Components and Interfaces

### Component 1 — `ralph-loop.yaml` (workflow definition)

- **Purpose:** 声明式编排 ralph-loop 的步骤、输入、预算与停止条件（有界递归）。
- **Interfaces:** 通过 `ReasoningOptions.CognitiveWorkflow="ralph-loop"` 执行；输入参数由 YAML `inputs` 定义。
- **Dependencies:** `CognitiveCoordinatorGAgent` step runtime；新增的 deterministic primitives。
- **Reuses:** `axiom_theorem_loop.yaml` 的 loop/递归范式；`maker-v2.yaml` 的 defaults/guardrails（timeout/strict_parse/max_length）。

### Component 2 — Deterministic Primitives (executors)

> 设计目标：把“必须可验证/可审计/可限界”的操作（读文件、搜代码、改文件、跑验证）从 LLM prompt 中剥离为确定性执行器。

#### 2.1 `workspace_read_file` executor

- **Purpose:** 在 workspace root 内读取文件片段（bounded），返回内容 + 截断标记 + 元信息。
- **Interfaces:** workflow step `type: workspace_read_file`，参数：
  - `path`（string, required）
  - `max_chars`（int, optional, 默认由 workflow defaults 控制）
- **Dependencies:** 本地文件系统；workspace root 配置（见“Non-Functional / Security”）。

#### 2.2 `workspace_code_search` executor

- **Purpose:** 在 workspace root 内执行代码搜索（ripgrep 或托管实现），结果限条数与上下文。
- **Interfaces:** step `type: workspace_code_search`，参数：
  - `pattern`（string, required）
  - `glob`/`file_type`（optional）
  - `max_results`/`context_lines`（optional）
- **Dependencies:** `rg`（如采用外部进程）或托管搜索实现；必须有输出限长。

#### 2.3 `workspace_apply_patch` executor

- **Purpose:** 以“apply_patch 风格”的 patch 对文件做创建/更新，要求幂等/可回滚提示。
- **Interfaces:** step `type: workspace_apply_patch`，参数：
  - `patch`（string, required）或 `patches[]`（array, optional）
- **Dependencies:** 文件系统；冲突检测（至少 best-effort）。

#### 2.4 `sandbox_command` executor (verifier)

- **Purpose:** 以 non-interactive 模式运行验证命令（如 `dotnet test`），强制 timeout + stdout/stderr 限长。
- **Interfaces:** step `type: sandbox_command`，参数：
  - `command`（string, required）
  - `args[]`（array<string>, optional）
  - `timeout_ms`（int, optional）
  - `working_dir`（string, optional，必须在 workspace root 内）
- **Dependencies:** `ProcessStartInfo`；命令白名单/前缀策略（配置驱动）。

> 注：这些 step type 将通过在 `CognitiveCoordinatorGAgent.ExecuteStepAsync` 增加新的 case 分支接入（与现有 `transform/retrieve_facts/hpa` 一致）。

## Data Models

### Model 1 — `RalphLoopState` (workflow variable, stored in Protobuf Value)

> state 以 workflow 变量 `state` 保存；通过 `ProtoValueConverter` 写入 `CognitiveAgentState.workflow_variables`（跨边界为 Protobuf）。

建议结构（字段有界，禁止存放大块文件原文）：

```
state:
  goal: string
  success_criteria: string
  iteration: int
  max_iterations: int
  status: "running" | "completed" | "failed" | "limit"
  last_verifier:
    ok: bool
    exit_code: int
    timed_out: bool
    duration_ms: int
    stdout_excerpt: string   # bounded
    stderr_excerpt: string   # bounded
    truncated: bool
  last_patch_summary: string # bounded
  diagnostics: [string]      # bounded list (e.g. max 20)
  done: bool
```

### Model 2 — Primitive outputs (all bounded, Protobuf-safe)

- `workspace_read_file` output:
  - `ok: bool`, `path: string`, `content: string`, `truncated: bool`, `total_chars: int`, `kept_chars: int`, `error?: string`
- `workspace_code_search` output:
  - `ok: bool`, `pattern: string`, `matches: [{path,line,excerpt}]`, `truncated: bool`, `error?: string`
- `workspace_apply_patch` output:
  - `ok: bool`, `files_changed: int`, `error?: string`
- `sandbox_command` output:
  - `ok: bool`, `exit_code: int`, `timed_out: bool`, `stdout: string`, `stderr: string`, `truncated: {stdout: bool, stderr: bool}`, `duration_ms: int`

## Error Handling

### Error Scenarios

1. **Workspace path escape / invalid path**
   - **Handling:** executor 直接拒绝（`ok=false` + `error=path_out_of_workspace`），并在 state.diagnostics 记录；根据策略终止或继续（默认终止为 failed）。
   - **User Impact:** 最终结果明确提示“workspace root 配置/路径非法”。

2. **Verifier command denied / not allowed**
   - **Handling:** `sandbox_command` 返回 `ok=false`，error 指明 deny reason；workflow 进入 failed（避免无限循环）。
   - **User Impact:** 明确提示需要在宿主配置允许该命令或切换为只读模式。

3. **Timeout / output explosion**
   - **Handling:** 强制超时 kill；stdout/stderr 截断并标记；计为一次迭代失败并推进 iteration（直到 max_iterations）。
   - **User Impact:** 结果中可见 timed_out 与 truncated 标记，便于调参。

4. **LLM JSON parse failure (extract_signals / propose_patches)**
   - **Handling:** 视为 red-flag：记录诊断并终止（failed）或进入下一轮（可配置）。
   - **User Impact:** 明确提示“strict JSON 输出失败”，建议降低输出复杂度或调整提示词。

## Testing Strategy

### Unit Testing

- `WorkflowParserTests`: 新增用例验证 `ralph-loop.yaml` 可被解析、inputs/steps 结构正确。
- Executor 单测（新增测试文件）：
  - `WorkspaceReadFileExecutorTests`：路径逃逸、截断、编码/大文件处理。
  - `WorkspaceCodeSearchExecutorTests`：结果上限、上下文行、空结果。
  - `WorkspaceApplyPatchExecutorTests`：创建/更新、冲突/找不到上下文的失败路径。
  - `SandboxCommandExecutorTests`：timeout、stdout/stderr 截断、exit code 判定。

### Integration Testing

- 在 `test/Aevatar.Agents.Cognitive.Tests` 增加端到端：用 stub LLM provider（固定输出 signals/patches）+ 真实 executor（在临时目录 workspace）运行 ralph-loop，验证：
  - pass 时提前停止
  - fail 时迭代并在 max_iterations 触发 limit
  - 所有输出保持 bounded

### End-to-End Testing

- 在一个示例/受控工程目录运行 `CognitiveStrategy.ExecuteAsync(... CognitiveWorkflow="ralph-loop")`，以 `dotnet test` 为 verifier，验证真实回路（CI 可选）。


