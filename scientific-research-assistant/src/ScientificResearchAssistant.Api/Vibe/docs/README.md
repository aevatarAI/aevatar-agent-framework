# Vibe Module（后端：vibe researching 运行时）

本目录承载 `mode=vibe` 的核心后端能力：**File-SSoT 存储** + **单轮编排** + **DAG 增量写入**（当前不做写入门控验证）。

## 目录结构（核心骨架）

```
Vibe/
  VibeOrchestrator.cs                       # 入口：ExecuteOneRoundAsync（骨架/时序）
  VibeOrchestrator.MeshSeed.cs              # mesh 缺失时自动 seed（从 default_mesh.yaml）（Option B 默认启用）
  VibeOrchestrator.MeshIntegration.cs       # mesh 执行：错误渲染、fail-fast/fallback、mesh 运行产物落盘（best-effort）
  VibeOrchestrator.PlanDag.cs               # plan/brief milestones -> DAG mutation 构建（plan nodes）
  VibeOrchestrator.Steps.cs                 # step 事件模板封装（StepStarted/Finished best-effort）
  VibeOrchestrator.ExecuteOneRound.Parts.cs # ExecuteOneRoundAsync 的拆分实现（pivot/plan/workers），主文件只保留骨架
  VibeModules.cs                            # Vibe 子域模块：VibeCore/VibePivot/VibeMesh/VibeHost（见名知意的依赖分组）
  VibeOrchestrator.Workers.cs               # planner/reasoner/librarian/verifier/dag_builder/paper_editor 的流式调用
  VibeOrchestrator.DagConsensus.cs          # DAG 写入（MVP：不做共识门控；未来可做“写后验证/标注”）
  VibeOrchestrator.Trace.cs                 # trace 追加写入 + round_summary SSE
  VibeOrchestrator.ResearchAssistant.cs     # research_assistant 的 brief/plan/summary 调用与解析
  VibeOrchestrator.Parsing.cs               # JSON 提取/解析 + librarian actions 解析
  VibeOrchestrator.GoalsAndMessages.cs      # prompt 构造（Plan 从 DAG plan nodes 提取；不再使用 goals）
  VibeOrchestrator.DeliveryApply.cs         # paper_editor 输出解析、patch 应用、delivery snapshots 写入

  Brief/BriefStore.cs                       # deliverables/brief.json（Protobuf-JSON）
  Delivery/DeliveryCenterStore.cs           # deliverables/*（结论/证据/任务/快照）
  Compute/ComputeDecisionStore.cs           # artifacts/compute/decisions（MVP）
  (removed) GoalsStore                      # goals are now represented as DAG plan nodes
  Trace/TraceStore.cs                       # artifacts/trace/trace.jsonl + runs/*/summary.md
  Uploads/UploadsStore.cs                   # artifacts/uploads
  Dag/                                      # DAG snapshot + explain + consensus gate（见子目录 docs）
```

## 设计要点（为什么这样拆）

- **单文件 ≤ 800 行**：用 `partial` 拆分 `VibeOrchestrator`，按职责划分，降低认知负担。
- **编排不崩溃**：所有 stage 都是 *best-effort*；失败只会阻断本 stage，不会炸掉 API 进程。
- **SSE 以快照优先**：前端 reconnect 先收 `*_snapshot`，再接 live stream，避免依赖 replay。

## DAG grounded context 过滤（可配置）

Research Assistant 在启动每一轮时，会把 DAG 的一部分节点摘要拼进 system prompt（grounded context）。
默认只选 **`kind=Knowledge` 且 `attestations >= 1`** 的节点。

你可以在配置里调整规则（支持写到 `appsettings.json` / `appsettings.secrets.json` / 用户级加密 secrets）：

```json
{
  "Vibe": {
    "DagGrounding": {
      "MinAttestations": 2,
      "RequiredPubKeys": [
        "04abcd... (hex/base64)",
        "04dead... (hex/base64)"
      ]
    }
  }
}
```

语义：
- `MinAttestations`: 至少多少个签名背书才参与 grounding（例如 `2` 就是 `Attestations.Count > 1`）
- `RequiredPubKeys`: 如果非空，则要求“至少包含其中一个 pubkey”的背书（常用：放 1 个指定 verifier pubkey）

## Mesh Orchestration（Option B：用 Mesh DSL 描述协作拓扑）

Worker phase（`planner/reasoner/librarian/verifier/dag_builder`）除了默认的“硬编码顺序/计划 workers”外，还支持 **Mesh DSL** 驱动（默认启用）。

### 启用方式

在 `ScientificResearchAssistant.Api` 的配置中开启（当前默认 `Enabled=true`）：

```json
{
  "Vibe": {
    "MeshOrchestration": {
      "Enabled": true,
      "OnCompileError": "fallback" // fallback | fail
    }
  }
}
```

语义：
- `Enabled`: 为 true 时，`VibeOrchestrator` 会尝试加载 session 的 `mesh.yaml`（优先）或 `mesh.json` 并执行 mesh worker phase；缺失时会自动 seed
- `OnCompileError`:
  - `fallback`（默认）：mesh 无效时回退到默认 worker pipeline
  - `fail`：mesh 无效时 **不回退**，直接跳过 worker phase（本轮仍会走 DAG apply/summary/trace，但通常不会有 DAG candidate）

### 存储位置（File-SSoT）

MeshDefinition 的 raw（YAML/JSON）存放在：

- `workspace/sessions/{sessionId}/decisions/mesh.yaml`（推荐）
- `workspace/sessions/{sessionId}/decisions/mesh.json`（兼容）

同时会 best-effort 写审计/回放产物到：

- `workspace/sessions/{sessionId}/artifacts/mesh/*.{yaml|json}`

默认模板文件（仓库内）：

- `scientific-research-assistant/src/ScientificResearchAssistant.Api/Vibe/Mesh/default_mesh.yaml`

### Session Mesh API（本地回环限制）

用于开发/调试注入 mesh（不会执行，只保存并校验）：

- `GET /api/sessions/{sessionId}/mesh`
- `PUT /api/sessions/{sessionId}/mesh`（body: `{ "raw": "...", "format": "yaml|json" }`；保存前会 compile/validate）

### Sample：等价默认 pipeline 的 mesh.yaml（推荐 YAML）

```yaml
dsl_version: "0.1"
goal:
  name: "vibe default pipeline"
strategy: "cot"
budget:
  max_steps: 30
  token_limit: 20000
nodes:
  - id: planner
    type: planner
  - id: reasoner
    type: reasoner
  - id: librarian
    type: librarian
  - id: verifier
    type: verifier
  - id: dag_builder
    type: dag_builder
edges:
  - from: planner
    to: reasoner
    channel: upstream_output
  - from: reasoner
    to: librarian
    channel: upstream_output
  - from: reasoner
    to: verifier
    channel: upstream_output
  - from: librarian
    to: dag_builder
    channel: upstream_output
  - from: verifier
    to: dag_builder
    channel: upstream_output
  - from: planner
    to: reasoner
    channel: dag_snapshot
  - from: planner
    to: librarian
    channel: dag_snapshot
  - from: planner
    to: verifier
    channel: dag_snapshot
  - from: planner
    to: dag_builder
    channel: dag_snapshot
  - from: planner
    to: reasoner
    channel: planner_output
  - from: planner
    to: librarian
    channel: planner_output
  - from: planner
    to: verifier
    channel: planner_output
  - from: planner
    to: dag_builder
    channel: planner_output
constraints: []
```

说明：
- `type` 与 `channel` 有 allowlist（见 `SraMeshMappings`），不在 allowlist 内会被拒绝
- `dag_snapshot` 会把 DAG 的 bounded 摘要作为 Inputs（材料 `materials_context` 仍由 system prompt 注入）
 - MeshExecutionPlanner 禁止 cycles，因此不要使用回边构造环

### Dynamic Roles（长期形态：mesh 节点引用 role）

除了内置的 `planner/reasoner/librarian/verifier/dag_builder` 外，Mesh 也支持 **动态 role**：

- **role 定义位置（全局，可复用）**：`~/.aevatar/agents/{role}.yaml`
- **mesh 里引用方式**：`nodes[*].type: {role}`
- **运行时行为**：
  - 编译/计划阶段会扫描 `~/.aevatar/agents/*.yaml`，将文件名作为可用 role
  - 执行阶段会用通用 `VibeRoleAgent` 跑该节点，并应用 YAML 的模型参数与 `system_prompt`
  - provider 解析优先级：request override > `~/.aevatar/agents/{role}.yaml` > `agent_providers.json` > session.ProviderName

#### Built-in Roles 也可用 YAML 覆盖（并保持 fallback）

内置的 `planner/reasoner/librarian/verifier/dag_builder/paper_editor` 也会尝试读取同名 YAML：

- `~/.aevatar/agents/planner.yaml`
- `~/.aevatar/agents/reasoner.yaml`
- ...

语义：
- **没有 YAML**：完全使用当前硬编码行为（提示词、默认 temperature/max_tokens、工具注册逻辑不变）
- **有 YAML**：
  - `provider/model/temperature/max_tokens/system_prompt` 等会按 YAML 覆盖（未填字段继续走原默认值）
  - `tools:` 会作为 baseline tool allowlist（每次 LLM request 都会注入；空则不限制）
  - `skills:` 非空时会自动启用 skills roots（默认 `~/.aevatar/skills`）并自动把 skills 相关工具加入 allowlist

最小示例：

1) 创建 `~/.aevatar/agents/citation_checker.yaml`

```yaml
id: "citation_checker"
name: "Citation Checker"
provider: "openai"   # 可选：也可以不写，走 session/provider mapping
model: "gpt-4.1-mini"
temperature: 0.1
system_prompt: |
  You are a strict citation checker.
  - Flag missing citations
  - Output a checklist
```

2) 在 session 的 `mesh.yaml` 里引用：

```yaml
nodes:
  - id: checker
    type: citation_checker
```


