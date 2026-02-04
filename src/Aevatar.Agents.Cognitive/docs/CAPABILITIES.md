# Cognitive 系统能力与边界

本文件用于完整描述 `Aevatar.Agents.Cognitive` 的功能模块、运行机制、能力边界与配置方式，面向“从零理解到可落地使用”的场景。

---

## 1. 定位与目标

`Aevatar.Agents.Cognitive` 是基于 Aevatar Agent Framework 的认知推理模块，核心目标是：

- 用 **DSL 工作流** 组织 LLM 推理流程（串行、并行、投票、递归）。
- 将 **可验证、可确定** 的处理从 LLM 中剥离（0-token 原语）。
- 对执行过程提供 **可观测事件流**（步骤事件 + ExecutionTrace）。
- 在不同运行时（Local/Orleans/ProtoActor）中复用同一套业务逻辑。

---

## 2. 模块分层与目录映射

### 2.1 Agents（执行层）

- `CoordinatorAgent`（原 `CognitiveCoordinatorGAgent`）  
  负责工作流生命周期、步骤调度、变量表、失败处理、递归控制、步骤事件。

- `RoleAIGAgent` + `CognitiveStepExecutionHandler`  
  执行 `llm_call` / `tool_call` / `tool_validate`（并行 role worker，避免阻塞 Coordinator）。

- `Shared/CognitiveAIGAgentBase`  
  统一 LLM 请求形态与 history 策略（禁用自动压缩，总是只发当前 prompt）。

**边界**：Role Worker 负责 `llm_call`/`tool_call`/`tool_validate`，不会执行 workspace/sandbox 原语；Coordinator 执行所有确定性步骤。

#### 2.1.1 Coordinator 拆分文件与职责

- `CognitiveCoordinatorGAgent.Workflow.cs`（class=`CoordinatorAgent`）：启动/失败/输出构建/主循环
- `CognitiveCoordinatorGAgent.Llm.cs`（class=`CoordinatorAgent`）：单步 LLM 调用（含 streaming 与 guardrails）
- `CognitiveCoordinatorGAgent.Vote.cs`（class=`CoordinatorAgent`）：投票共识（并行提案、语义聚类、red-flag）
- `CognitiveCoordinatorGAgent.Parallel.cs`（class=`CoordinatorAgent`）：`fan_out` / `parallel` 子任务调度与回收
- `CognitiveCoordinatorGAgent.StepEvents.cs`（class=`CoordinatorAgent`）：步骤事件与统计（UI/trace）
- `CognitiveCoordinatorGAgent.Parameters.cs`（class=`CoordinatorAgent`）：参数解析、red-flag 配置与输出解析
- `CognitiveCoordinatorGAgent.Workspace.cs`（class=`CoordinatorAgent`）：workspace_* / sandbox_command 原语执行

#### 2.1.2 运行时状态（Protobuf）

关键状态来自 `cognitive_messages.proto`，包括：

- `CognitiveCoordinatorState`：`execution_id/workflow_name/status/current_step_id/total_llm_calls/total_tokens_used/active_workers`
- `CognitiveAgentState`：`workflow_variables/current_depth/max_depth/total_llm_calls/total_prompt_tokens/total_completion_tokens`
- `CognitiveWorkerState`：`worker_id/status/current_step_id/total_steps_completed`

#### 2.1.3 历史记录与 Prompt 策略

- `CognitiveAIGAgentBase` 禁用 history compaction（避免隐式 LLM 调用）。
- `ChatHistoryMaxMessages = 32`，仅用于 UI hydration。
- 每条 history 自动注入 step 元数据：`step_id/step_type/agent_kind/...`（`BeginStepHistory`）。

### 2.2 Engine（解析与注册）

- `WorkflowParser`  
  YAML → `WorkflowDefinition`（支持 `defaults` 注入与复杂结构）

- `IWorkflowRegistry` + `InMemoryWorkflowRegistry`  
  工作流注册与查找

**边界**：Parser 只负责结构解析，不做跨域验证；运行时限制由执行器与 Guardrails 控制。

#### 2.2.1 YAML 解析行为

- 使用 `YamlDotNet` + `UnderscoredNamingConvention`（`foo_bar` → `FooBar`）
- `IgnoreUnmatchedProperties()`：多余字段会被忽略
- `defaults` 会在 `WorkflowParser` 内注入到 Step 参数中
- Nested mapping 会被归一化为 `Dictionary<string, object?>`，避免 `Dictionary<object, object>` 造成运行时失效

### 2.3 Execution（确定性原语）

- `TransformExecutor`：0-token 聚合/归一化/统计/状态更新  
- `RetrieveFactsExecutor`：Top-K 事实检索（lexical）  
- `Workspace*Executor`：读文件/搜索/patch  
- `SandboxCommandExecutor`：受限命令验证器

### 2.4 Template / Output

- `TemplateEngine`：Scriban 模板渲染  
- `OutputParserFactory`：输出解析（text/json/json_array/regex 等）

### 2.5 Utilities

- `ProtoValueConverter`：对象 ↔ Protobuf Value  
- `WorkflowExecutionTraceExtensions`：WorkflowStepEvent → ExecutionTrace  
- `AgentYamlResolver`：按 `agent/role` 从 `./aevatar/agents` / `~/.aevatar/agents` 加载 YAML

### 2.6 AG-UI

- `AgUi/CognitiveAgUiEventStream`：将执行事件转换为 AG-UI 标准事件流

### 2.7 内置工作流

位于 `workflows/`（例如 `direct.yaml` / `maker.yaml` / `ralph-loop.yaml` / `hypothesis_promotion_loop.yaml` 等）。

---

## 3. 运行机制（简版流程）

```
YAML Workflow
   │
   ▼
WorkflowParser → WorkflowDefinition
   │
   ▼
Coordinator 执行步骤
   ├─ llm_call → Coordinator / Worker
   ├─ fan_out / parallel → Worker 并发
   ├─ vote → 多轮 LLM + 共识
   └─ transform / retrieve_facts / workspace_* → Coordinator-only
```

- 每步都会发 `WorkflowStepEvent`（用于 UI/trace）。
- 递归由 `workflow_call + max_depth` 控制，避免无限循环。

### 3.1 启动流程与变量注入

- `StartWorkflowRequestEvent` 触发执行
- 输入变量的注入顺序：
  1) Workflow `inputs.default`  
  2) 请求变量覆盖（`StartWorkflowRequestEvent.variables`）
- `max_depth` 可通过变量输入覆盖，最终 clamp 到 `[1, 200]`

### 3.2 执行主循环

- 按 `WorkflowDefinition.Steps` 顺序执行
- 每步执行成功且 `store` 非空时写入变量表
- 变量表 `_workflowVariables` 为 `Dictionary<string, object>`
- Workflow 结束时构建 `_output`：
  - 若 `output` 空：返回整个变量表
  - 否则逐条 `ResolveValue(template, variables)` 生成输出对象

### 3.3 失败处理

- 任一 step 返回 `Success=false` → workflow 失败
- 失败时写 `ExecutionStatus.EsFailed`，并记录 `CustomState.Error`
- `TryExportExecutionTraceAsync()`（若注入 ExecutionTraceStore）仍会输出 trace

---

## 4. DSL 结构与关键字段

### 4.1 Workflow 结构

```yaml
name: xxx
inputs:
  - name: task
    type: string
    required: true
defaults:
  llm_call:
    timeout_seconds: 360
    idle_timeout_seconds: 30
steps:
  - id: step1
    type: llm_call
    prompt: "{{task}}"
    output: text
    store: response
output:
  result: "{{response}}"
```

### 4.2 关键约定

- `defaults` 会注入到所有同类型步骤（可被 step 覆盖）。
- `output` 字段是 **模板表达式**，从变量表读取输出。
- `workflow_call.workflow` 支持模板化（例如 `{{ child_name }}`）。
- `llm_call` 支持 `agent`/`role` 参数，用于加载 agent YAML（system prompt / temperature / max_tokens）。

### 4.3 参数解析与模板计算

解析路径由 `CoordinatorAgent`（Parameters partial）提供：

- `ResolveIntParameter/ResolveFloatParameter/ResolveBoolParameter`
  - 若值是字符串，会先走 `TemplateEngine.Evaluate(...)`
  - 支持 `int/long/double/string` 的宽松转换
- `TemplateEngine.Render` 用于 `prompt/system/path` 等字符串模板
- `PurePathTemplate`（`{{ a.b.c }}`）在部分原语中会直接解析为对象

### 4.4 YAML → Step 参数映射（节选）

- `llm_call`：`prompt/system/output/agent/role/max_length/timeout_seconds/idle_timeout_seconds/strict_parse`
- `vote`：`k/max_rounds/similarity/red_flag/max_red_flags/generator`
- `fan_out`：`for_each/step/reduce/max_concurrency/include_failures/timeout_seconds`
- `parallel`：`steps`
- `workflow_call`：`workflow/params/max_depth`
- `conditional`：`condition/if_true/if_false`
- `assign`：`from`
- `transform`：`ops`
- `retrieve_facts`：`query/source/text_field/id_field/top_k/mode`
- `tool_call`：`tool/args/output/strict_parse`
- `tool_validate`：`tool/validation_args`
- `tool_evolve`：`generator/candidates/policy/max_candidates/tool_storage_dir/use_vote/validation_args`
- `workspace_read_file`：`path/max_chars`
- `workspace_code_search`：`pattern/glob/file_type/max_results/context_lines/max_total_chars`
- `workspace_apply_patch`：`patch/patches`
- `sandbox_command`：`command/args/working_dir/timeout_ms/max_output_chars`

---

## 5. Step 类型与语义边界

| Step 类型 | 执行者 | 主要用途 | 关键边界 |
|---|---|---|---|
| `llm_call` | Coordinator/Worker | 单次 LLM 调用 | 可配置 `output/max_length/timeout/idle_timeout/strict_parse` |
| `conditional` | Coordinator | 条件分支 | condition 使用模板表达式 |
| `fan_out` / `parallel` | Coordinator+Workers | 并行执行 | Worker 执行 `llm_call` / `tool_call` / `tool_validate` |
| `vote` | Coordinator | 共识投票 | 语义聚类需 embedding generator |
| `workflow_call` | Coordinator | 递归/子流程 | 受 `max_depth` 限制 |
| `checkpoint` | Coordinator | 变量快照 | 仅用于 observability |
| `assign` | Coordinator | 变量投影 | 支持 dotted path |
| `transform` | Coordinator | 0-token 聚合 | 见 `docs/PRIMITIVES.md` |
| `retrieve_facts` | Coordinator | 0-token 检索 | 默认 lexical |
| `tool_call` | Coordinator/Worker | 工具调用 | 受 ToolEvolutionOptions.EnableToolCalls 控制 |
| `tool_validate` | Coordinator/Worker | 工具验证 | 使用 `validation_args` |
| `tool_evolve` | Coordinator | 工具演化 | 受 ToolEvolutionOptions.EnableToolEvolutionSteps 控制 |
| `workspace_read_file` | Coordinator | 安全读文件 | 受 `WorkspacePathGuard` 限制 |
| `workspace_code_search` | Coordinator | 搜索 | 限制 max_results / context_lines |
| `workspace_apply_patch` | Coordinator | 受限 patch | 只允许 create/replace/span |
| `sandbox_command` | Coordinator | 验证命令 | 受 allowlist + timeout 限制 |

### 5.1 `llm_call`

**输入参数（常用）**

- `prompt`：必填，用户提示（可模板渲染）
- `system`：可选，系统提示（可模板渲染）
- `output`：输出类型，默认 `text`
- `agent` / `role`：可选，指定 agent YAML（见 5.2）
- guardrails：`max_length` / `timeout_seconds` / `idle_timeout_seconds` / `strict_parse`

**执行行为**

- 使用 `ChatRequest.Create(prompt)`，设置 `StageHint=stepId`
- `system_prompt` 通过 `request.Context["system_prompt"]` 覆盖
- Streaming 支持检测：`SupportsStreamingAsync()`
  - 支持时走 `ChatStreamAsync`
  - 不支持时走 `ChatAsync`
- Stream 节流：每 16 个 chunk 或间隔 ≥ 250ms 发一次事件
- 安全限制：
  - `max_length` 默认 102400，clamp `[1024, 1048576]`
  - `timeout_seconds` 默认 360，clamp `[5, 3600]`
  - `idle_timeout_seconds` 默认 30，clamp `[1, timeout]`
  - stream chunk > 20000 直接失败（`llm-stream-too-long>20000`）
- `strict_parse=true` 且输出解析失败 → `redflag-parse-null`

### 5.2 `agent` / `role` override

- `agent` / `role` 会触发 `AgentYamlResolver`
- 解析顺序：`<workspaceRoot>/aevatar/agents` → `~/.aevatar/agents`
- YAML 可注入：
  - `system_prompt`（优先）
  - `temperature` / `max_tokens`
- 如果找不到 YAML，保持原 `system`/默认 system prompt

**Agent YAML 常用字段（节选）**

- `id` / `name` / `version`
- `provider` / `model`
- `temperature` / `max_tokens` / `top_p`
- `frequency_penalty` / `presence_penalty`
- `stop_sequences`
- `persona.role/expertise/style/traits`
- `system_prompt`
- `capabilities.max_tool_calls_per_turn` / `max_history_length` / `supports_streaming` / `operation_timeout_seconds`

### 5.3 `conditional`

- `condition` 使用 `TemplateEngine.Evaluate(...)`
- 解析失败会记录 `_last_conditional_error`，并默认走 `if_false` 分支
- 分支步骤按顺序执行，失败会中断分支

### 5.4 `vote`

- 参数：`k`(默认 3), `max_rounds`(默认 10), `similarity`
- `red_flag` 配置支持：
  - `true/false`
  - `english/chinese/code`
  - `map`（`min_length/max_length/detect_refusal/detect_degeneration/validate_length`）
- `max_red_flags` 默认 `max_rounds * 2`，超过后提前终止
- 投票引擎：`Aevatar.Agents.Maker.VoteEngine`
  - 有 embedding generator → 语义聚类
  - 无 embedding generator → exact matching
- 批次并行大小：`min(k + 1, workersCount or 3)`，至少 1
- 生成步 id：`{stepId}.gen[n]`（便于 UI/trace）

### 5.5 `fan_out`

- 参数：
  - `for_each`：变量名（列表）
  - `step`：子步骤（通常为 `llm_call`）
  - `reduce`：`collect|flatten|first|last|concat`（默认 `collect`）
  - `include_failures`：失败项是否保留到结果列表
  - `timeout_seconds`：默认 600，clamp `[5, 3600]`
  - `max_concurrency`：已解析但当前实现未强制生效（预留字段）
- 运行策略：
  - 有 Worker 且子 step 非 `workflow_call` → 分发给 Worker
  - 无 Worker 或 `workflow_call` → Coordinator 顺序执行
  - 顺序执行路径避免 `Task.Run`（兼容 Orleans 单线程执行模型）
- Worker 路由：
  - `__target_worker` 注入到变量表
  - `ExecuteStepRequestEvent` 下发（`EventDirection.Down`）
- 返回结果解析：
  - `output=text` → 原样保留
  - 非 `text` → `OutputParserFactory` 解析
  - `include_failures=true` 时错误项也会加入结果列表

### 5.6 `parallel`

- 参数：`steps`（多个子步骤）
- 无 Worker → 顺序执行
- 有 Worker → 并行下发
- 子步骤结果写入变量表（按 `store`）
- 非 `text` 输出会按 `output` 解析，`strict_parse=false` 时保留原字符串

### 5.7 `workflow_call`

- `workflow` 可模板化（如 `{{ child_name }}`）
- `params` 会覆盖子 workflow 的变量表
- `params.max_depth` 可单次覆盖递归上限
- 会保存/恢复父变量表，输出来自子 workflow `_output`

### 5.8 `assign`

- 参数：`from`（支持 dotted path）、`store`
- 解析失败或值为 `null` → step 失败

### 5.9 `checkpoint`

- 参数：`variables`（字符串列表，支持 dotted path）
- 生成 JSON 快照写入 `PrimitiveResult.Value`
- 主要用于 observability，不改变执行路径

### 5.10 `transform`

- 参数：`ops`（list）
- 0-token 变换，详见 `docs/PRIMITIVES.md`
- 支持 `eval/set/set_path/inc_path/append/.../make_workers` 等

### 5.11 `retrieve_facts`

- 参数：
  - `query`（模板）
  - `source`（dotted path）
  - `text_field`（默认 `statement`）
  - `id_field`（默认 `id`）
  - `top_k`（默认 20，clamp `[1, 200]`）
  - `mode`（仅支持 `lexical`）
- 输出：`[{ id, statement, score }]`

### 5.12 `workspace_read_file`

- 参数：`path`、`max_chars`
- `max_chars` 默认 16000，clamp `[1, 200000]`
- 大文件不计算 `total_chars`（> 8MB）
- 输出：
  - `ok/path/content/truncated/total_chars/kept_chars/error`

### 5.13 `workspace_code_search`

- 参数：`pattern`、`glob`、`file_type`、`max_results`、`context_lines`、`max_total_chars`
- 默认值与上限：
  - `max_results` 默认 50，cap 500
  - `context_lines` 默认 2，cap 8
  - `max_total_chars` 默认 60000，cap 500000
- 优先使用 `rg`，失败则回退到 managed scan
- 默认跳过目录：`.git/bin/obj/node_modules/.spec-workflow`

### 5.14 `workspace_apply_patch`

- 参数：`patch` 或 `patches`
- 支持 op：
  - `create_or_replace`
  - `replace_span`（`start_line`/`end_line_exclusive`，1-based）
- 限制：
  - `MaxPatches=10`
  - `MaxTextCharsPerPatch=120000`
- 输出：`ok/files_changed/patches_applied/errors/error`

### 5.15 `sandbox_command`

- 参数：`command`、`args`、`working_dir`、`timeout_ms`、`max_output_chars`
- 默认值与上限：
  - `timeout_ms` 默认 120000，clamp `[100, 600000]`
  - `max_output_chars` 默认 60000，clamp `[1000, 500000]`
- Allowlist：`AEVATAR_COGNITIVE_ALLOWED_COMMANDS`
- 输出：
  - `ok/exit_code/timed_out/duration_ms/stdout/stderr/truncated/error`

---

## 6. LLM 调用策略与 Guardrails

- **超时保护**：`timeout_seconds` + `idle_timeout_seconds`（默认 360s / 30s）。
- **长度限制**：`max_length` 防止输出爆炸。
- **解析严格性**：`strict_parse` 失败直接视为 red-flag。
- **禁用工具调用**（默认）：`CognitiveAIGAgentBase.RegisterToolsAsync` 返回空。
- **agent/role override**：从 YAML 注入 system prompt / temperature / max_tokens。

### 6.1 输出解析类型

`OutputParserFactory` 支持以下类型（区分大小写不敏感）：

- `text`
- `json`
- `json_array`
- `first_line`
- `code_block`
- `regex`
- `fallback`

### 6.2 strict_parse 语义

- `strict_parse=true` 且解析失败 → `PrimitiveResult.Fail("redflag-parse-null")`
- `strict_parse=false` 时解析失败会回退为原始文本

### 6.3 Red-Flag 策略

`red_flag` 字段可取：

- `true/false`
- `english/chinese/code`
- 结构化配置：
  - `min_length`
  - `max_length`
  - `detect_refusal`
  - `detect_degeneration`
  - `validate_length`

### 6.4 Prompt 组装策略

- `CognitiveAIGAgentBase.BuildLLMRequest` 仅发送 **当前 step 的 user message**  
  不重放 `State.History`，避免 token 爆炸与语义漂移。
- `StageHint` 会写入 LLM request context（`stage_hint`），便于日志/观测。


---

## 7. 工作流加载与默认扫描

默认工作流目录：  

- `~/.aevatar/workflows`（基于环境变量解析）
- `DependencyInjection.AddCognitiveAgents()` 默认启用扫描
- 内置 workflow 先注册，用户目录中同名 workflow **覆盖内置版本**（`InMemoryWorkflowRegistry` 为 last-wins）

### 7.1 目录解析顺序（默认值）

1. `AEVATAR_CONFIG_DIR`
2. `AEVATAR_SECRETS_DIR`
3. `AEVATAR_SECRETS_PATH`（取上级目录）
4. `AEVATAR_SECRETS`（取上级目录）
5. `AEVATAR_CONFIG` / `AEVATAR_CONFIG_PATH`（取上级目录）
6. `~/.aevatar`

最终 workflow 目录为：`<configDir>/workflows`

---

## 8. Workspace 与安全边界

### 8.1 Workspace Root

- 通过 `AEVATAR_COGNITIVE_WORKSPACE_ROOT` 指定工作区根目录。
- 未设置时 fallback 为 repo root（通过 `Directory.Packages.props` 探测）。
- 不接受 workflow 输入覆盖（安全边界由 host 控制）。

### 8.2 可访问根目录

`WorkspacePathGuard` 会将 **workspace root** 与 **Aevatar config dir** 同时加入允许根目录，因此默认可读写：

- `<workspaceRoot>/**`
- `~/.aevatar/**`

任何路径必须落在允许根目录内，否则返回 `path_out_of_workspace`。

### 8.3 sandbox_command 白名单

- 环境变量：`AEVATAR_COGNITIVE_ALLOWED_COMMANDS`
- 若设置白名单，仅允许名单内命令执行
- 超时与输出大小由 step 参数控制（`timeout_ms` / `max_output_chars`）

---

## 9. 可观测与事件

### 9.1 WorkflowStepEvent（运行时事件）

Coordinator 会为每个步骤发送 `WorkflowStepEvent`，包含：

- step 生命周期（Pending/Running/Completed/Failed）
- tokens/llm_calls、duration、prompt/response（用于 UI/Trace）

事件定义在 `cognitive_messages.proto` 中，为跨 runtime 边界提供 Protobuf 序列化保证。

**关键字段（节选）**

- `step_id/step_type/status/progress/message/timestamp`
- `parent_step_id/depth`
- `vote_*`（round/max_rounds/k/current_votes）
- `parallel_*`（total/completed/failed）
- `duration_ms/llm_calls/tokens_used`
- `system_prompt/user_prompt/assistant_response`
- `winner_*`（proposal_id/hash/votes/cluster/semantic/consensus）

**结构事件**

- `WorkflowStructure` / `WorkflowStepNode` / `WorkflowEdge`  
  用于前端图渲染（节点/边/条件）

### 9.2 Worker ↔ Coordinator 事件

**ExecuteStepRequestEvent**（Coordinator → Worker）

- `request_id/step_id/step_type/parameters/variables`

**StepCompletedEventProto**（Worker → Coordinator）

- `success/result/error/tokens_used/llm_calls/duration_ms`
- **流式语义**：`success=false && error=""` 表示 streaming 中间态，不计入完成数

### 9.3 ExecutionTrace

`WorkflowExecutionTraceExtensions` 可将 `WorkflowStepEvent` 统一成 `ExecutionTrace`：

- 统一 trace schema（与 MAKER/UoT 兼容）
- 包含树结构（workflow → steps）与 metrics

### 9.4 AG-UI

`CognitiveAgUiEventStream` 可以把执行事件转换成 AG-UI 标准事件流，用于前端展示与回放。

### 9.5 ExecutionTraceStore（可选）

`CoordinatorAgent` 支持注入 `IExecutionTraceStore`，用于落盘 trace：

- 若没有 step events，会生成最小化 trace
- 会写入 `cognitive.*` labels 便于索引

### 9.6 Run/Step 状态消息（Protobuf）

来自 `cognitive_messages.proto`：

- `WorkflowRunState`：run_id/workflow_name/status/current_step/metrics/error
- `StepResult`：step_id/type/status/duration/input/output/metrics/voting_trace
- `StepMetrics`：llm_calls/prompt_tokens/completion_tokens/total_tokens
- `VotingTrace`：rounds/votes/winner/consensus_at_round

---

## 10. Skills（可选）

`skills/workflow-agent-writer` 提供两个 file skill：

- `workflow_write`：写入 `~/.aevatar/workflows`
- `agent_write`：写入 `~/.aevatar/agents`

注意：Cognitive 默认 **不启用工具调用**，因此这些 skill 需要在自定义 Agent 中显式启用后才会生效。  
技能根目录由 `AEVATAR_AGENT_SKILLS_DIRS` 与 `AEVATAR_AGENT_SKILLS_MAX_DEPTH` 控制（见 `AgentSkillsRuntime`）。  
默认技能目录为 `~/.aevatar/skills`（由 `AgentYamlConfigLoader.GetSkillsDirectory()` 提供）。

---

## 11. 扩展点

1. **新增 step 类型**：在 `CoordinatorAgent.ExecuteStepAsync` 中添加 case
2. **新增输出解析器**：实现 `IOutputParser` 并注册到 `OutputParserFactory`
3. **自定义 Red-Flag 策略**：实现 `IRedFlagStrategy`
4. **自定义聚合器**：扩展 `ApplyReducer` / `TransformExecutor` op
5. **新增 workspace 原语**：在 `WorkspacePathGuard` 安全边界内扩展

---

## 12. 能力边界 / 非目标

- **默认不启用工具调用**（Cognitive 侧只使用 LLM + 确定性原语）。
- **Agent YAML 的 tools/skills 不会自动生效**（除非显式开启工具调用）。
- **无内置持久化**（如需持久化需外部注入存储/事件系统）。
- **无外部数据源连接器**（检索/存储接入需扩展）。
- **无自动热更新**（workflows 仅在注册时加载）。
- **Workflow 校验** 仅限结构解析，复杂语义验证需额外组件。

---

## 13. 测试与验证入口

- `test/Aevatar.Agents.Cognitive.Tests/`：核心原语与 DSL 解析测试
- `docs/Tests.md`：测试覆盖面评审

