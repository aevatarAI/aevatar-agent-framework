# Vibe Module（后端：vibe researching 运行时）

本目录承载 `mode=vibe` 的核心后端能力：**File-SSoT 存储** + **单轮编排** + **DAG 增量写入**（支持共识门控：verifier-quorum / maker）。

## 目录结构（核心骨架）

**框架层（核心逻辑已迁移）**

```
src/Aevatar.Agents.Cognitive/Researching/
  Round/ResearchingRoundServices*.cs        # 共享服务/解析逻辑（以 workflow step module 为入口）
  Workflow/ResearchingWorkflowRunner.cs     # workflow 运行器（vibe_round）
  Workflow/ResearchingMeshExecutionRunnerAdapter.cs
  Modules/ResearchingModules.cs             # ResearchingCore/Pivot/Mesh/Host
  Dag/*                                     # DAG snapshot + explain + consensus gate
  Brief/*                                   # deliverables/brief.json
  Delivery/*                                # deliverables/*（结论/证据/任务/快照）
  Compute/*                                 # artifacts/compute/decisions（MVP）
  Trace/*                                   # artifacts/trace/trace.jsonl + runs/*/summary.md
  Uploads/*                                 # artifacts/uploads + 文件抽取
  Mesh/*                                    # Mesh DSL 编译/规划/执行/存储
```

**应用层（Vibe 仅保留 AG-UI 适配与 API 配置）**

```
Vibe/
  Workflow/VibeRunEventSinks.cs             # AG-UI 事件适配（工具进度流）
  Pivot/PivotApi.cs                         # pivot HTTP API
  Mesh/default_mesh.yaml                    # 默认 mesh 模板
  docs/README.md                            # 本文档
```

## 设计要点（为什么这样拆）

- **单文件 ≤ 800 行**：用 `partial` 拆分 `ResearchingRoundServices`，按职责划分，降低认知负担。
- **编排不崩溃**：所有 stage 都是 *best-effort*；失败只会阻断本 stage，不会炸掉 API 进程。
- **SSE 以快照优先**：前端 reconnect 先收 `*_snapshot`，再接 live stream，避免依赖 replay。
- **编排入口**：`workflows/vibe_round.yaml` + `ResearchingWorkflowRunner` + step modules（框架层 `WorkflowRunExecutor`）。

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

Worker phase（`planner/reasoner/librarian/verifier/dag_builder`）由 **Mesh DSL** 驱动（默认启用）。

### 启用方式

在 `VibeResearching.Api` 的配置中开启（当前默认 `Enabled=true`）：

```json
{
  "Vibe": {
    "MeshOrchestration": {
      "Enabled": true
    }
  }
}
```

语义：
- `Enabled`: 为 true 时，`ResearchingRoundServices` 会尝试加载 session 的 `mesh.yaml`（优先）或 `mesh.json` 并执行 mesh worker phase；缺失时会自动 seed
- mesh 无效（编译/计划失败）或缺失时：worker phase 直接跳过（本轮仍会走 DAG apply/summary/trace）

### 存储位置（File-SSoT）

MeshDefinition 的 raw（YAML/JSON）存放在：

- `workspace/sessions/{sessionId}/decisions/mesh.yaml`（推荐）
- `workspace/sessions/{sessionId}/decisions/mesh.json`（兼容）

同时会 best-effort 写审计/回放产物到：

- `workspace/sessions/{sessionId}/artifacts/mesh/*.{yaml|json}`

默认模板文件（仓库内）：

- `apps/Aevatar.VibeResearching/src/Aevatar.VibeResearching.Api/Vibe/Mesh/default_mesh.yaml`

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

#### Built-in Roles 也可用 YAML 覆盖（并保持默认实现）

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


