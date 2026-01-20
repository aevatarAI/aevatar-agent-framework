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

- `CognitiveCoordinatorGAgent`  
  负责工作流生命周期、步骤调度、变量表、失败处理、递归控制、步骤事件。

- `CognitiveWorkerGAgent`  
  执行 `llm_call`（并行 worker，避免阻塞 Coordinator）。

- `Shared/CognitiveAIGAgentBase`  
  统一 LLM 请求形态与 history 策略（禁用自动压缩，总是只发当前 prompt）。

**边界**：Worker **只负责 `llm_call`**，不会执行 workspace/sandbox 原语；Coordinator 执行所有确定性步骤。

### 2.2 Engine（解析与注册）

- `WorkflowParser`  
  YAML → `WorkflowDefinition`（支持 `defaults` 注入与复杂结构）

- `IWorkflowRegistry` + `InMemoryWorkflowRegistry`  
  工作流注册与查找

**边界**：Parser 只负责结构解析，不做跨域验证；运行时限制由执行器与 Guardrails 控制。

### 2.3 Execution（确定性原语）

- `TransformExecutor`：0-token 聚合/归一化/统计/状态更新  
- `RetrieveFactsExecutor`：Top-K 事实检索（lexical）  
- `HpaExecutor`：HPA 计算（scan/embed/gap/associator/holonomy）  
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

位于 `workflows/`（例如 `direct.yaml` / `maker.yaml` / `ralph-loop.yaml` / `hypothesis_promotion_loop*.yaml` 等）。

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
   └─ transform / retrieve_facts / hpa / workspace_* → Coordinator-only
```

- 每步都会发 `WorkflowStepEvent`（用于 UI/trace）。
- 递归由 `workflow_call + max_depth` 控制，避免无限循环。

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

---

## 5. Step 类型与语义边界

| Step 类型 | 执行者 | 主要用途 | 关键边界 |
|---|---|---|---|
| `llm_call` | Coordinator/Worker | 单次 LLM 调用 | 可配置 `output/max_length/timeout/idle_timeout/strict_parse` |
| `conditional` | Coordinator | 条件分支 | condition 使用模板表达式 |
| `fan_out` / `parallel` | Coordinator+Workers | 并行 LLM | Worker 仅执行 `llm_call` |
| `vote` | Coordinator | 共识投票 | 语义聚类需 embedding generator |
| `workflow_call` | Coordinator | 递归/子流程 | 受 `max_depth` 限制 |
| `checkpoint` | Coordinator | 变量快照 | 仅用于 observability |
| `assign` | Coordinator | 变量投影 | 支持 dotted path |
| `transform` | Coordinator | 0-token 聚合 | 见 `docs/PRIMITIVES.md` |
| `retrieve_facts` | Coordinator | 0-token 检索 | 默认 lexical |
| `hpa` | Coordinator | 0-token 数学推理 | 依赖结构化字段 |
| `workspace_read_file` | Coordinator | 安全读文件 | 受 `WorkspacePathGuard` 限制 |
| `workspace_code_search` | Coordinator | 搜索 | 限制 max_results / context_lines |
| `workspace_apply_patch` | Coordinator | 受限 patch | 只允许 create/replace/span |
| `sandbox_command` | Coordinator | 验证命令 | 受 allowlist + timeout 限制 |

---

## 6. LLM 调用策略与 Guardrails

- **超时保护**：`timeout_seconds` + `idle_timeout_seconds`（默认 360s / 30s）。
- **长度限制**：`max_length` 防止输出爆炸。
- **解析严格性**：`strict_parse` 失败直接视为 red-flag。
- **禁用工具调用**（默认）：`CognitiveAIGAgentBase.RegisterToolsAsync` 返回空。
- **agent/role override**：从 YAML 注入 system prompt / temperature / max_tokens。

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

### 9.2 ExecutionTrace

`WorkflowExecutionTraceExtensions` 可将 `WorkflowStepEvent` 统一成 `ExecutionTrace`：

- 统一 trace schema（与 MAKER/UoT 兼容）
- 包含树结构（workflow → steps）与 metrics

### 9.3 AG-UI

`CognitiveAgUiEventStream` 可以把执行事件转换成 AG-UI 标准事件流，用于前端展示与回放。

---

## 10. Skills（可选）

`skills/workflow-agent-writer` 提供两个 file skill：

- `workflow_write`：写入 `~/.aevatar/workflows`
- `agent_write`：写入 `~/.aevatar/agents`

注意：Cognitive 默认 **不启用工具调用**，因此这些 skill 需要在自定义 Agent 中显式启用后才会生效。  
技能根目录由 `AEVATAR_AGENT_SKILLS_DIRS` 与 `AEVATAR_AGENT_SKILLS_MAX_DEPTH` 控制（见 `AgentSkillsRuntime`）。

---

## 11. 扩展点

1. **新增 step 类型**：在 `CognitiveCoordinatorGAgent.ExecuteStepAsync` 中添加 case
2. **新增输出解析器**：实现 `IOutputParser` 并注册到 `OutputParserFactory`
3. **自定义 Red-Flag 策略**：实现 `IRedFlagStrategy`
4. **自定义聚合器**：扩展 `ApplyReducer` / `TransformExecutor` op
5. **新增 workspace 原语**：在 `WorkspacePathGuard` 安全边界内扩展

---

## 12. 能力边界 / 非目标

- **默认不启用工具调用**（Cognitive 侧只使用 LLM + 确定性原语）。
- **无内置持久化**（如需持久化需外部注入存储/事件系统）。
- **无外部数据源连接器**（检索/存储接入需扩展）。
- **无自动热更新**（workflows 仅在注册时加载）。
- **Workflow 校验** 仅限结构解析，复杂语义验证需额外组件。

---

## 13. 测试与验证入口

- `test/Aevatar.Agents.Cognitive.Tests/`：核心原语与 DSL 解析测试
- `docs/Tests.md`：测试覆盖面评审

