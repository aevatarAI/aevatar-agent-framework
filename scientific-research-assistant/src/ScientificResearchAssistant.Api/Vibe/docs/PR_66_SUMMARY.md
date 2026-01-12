# PR 66 - Vibe researching platform（给 Reviewer 的摘要）

> 目标：把本 PR 的“做了什么 / 为什么 / 怎么验证 / 风险点”用最短路径讲清楚，方便 review。

## 一句话概述

本 PR 落地 **Vibe researching platform** 的端到端闭环：后端以 **File-SSoT**（文件为单一事实源）沉淀可审阅产物，提供 **单轮编排 + DAG 增量写入 + snapshot-first SSE**；前端 Workbench 增加 **Chat / Files / DAG** 多视图与 `aevatar.vibe.*` 快照事件投影。

## 主要改动（按模块）

### 1) 后端：Vibe Runtime（核心）

目录：`scientific-research-assistant/src/ScientificResearchAssistant.Api/Vibe/`

- **`VibeOrchestrator` 单轮编排**
  - 典型时序：**Brief（首次可选）→ research_assistant Plan(JSON) → workers → dag_builder 产出 DAG mutation(JSON) → 写入 DAG/Trace/Deliverables**
  - 设计取向：整体 **best-effort**，尽量避免单 stage 失败导致 API 进程崩溃。

- **Plan 先落 DAG（区分 plan vs knowledge）**
  - 每轮 plan 会写入 DAG，作为 `kind=PLAN` 节点，便于审阅/回放/可视化。
  - 与知识节点（`kind=KNOWLEDGE`）语义隔离，避免“临时计划”污染 grounded context。

- **缺失 sources 自动补占位（避免断引用）**
  - 当 DAG candidate 引用了不存在的 `sources/*.md|txt`，会自动创建 placeholder，保证 workspace 不出现断链引用。

### 2) 后端：DAG Store / Grounding（可配置）

目录：`scientific-research-assistant/src/ScientificResearchAssistant.Api/Vibe/Dag/`

- **`DagStore`：图后端为权威，文件快照为镜像**
  - 以 KnowledgeGraph 为权威图后端（session/dag-scoped）。
  - 同步镜像快照到 `artifacts/dag/snapshot.json`（Protobuf-JSON，review 友好、diff 稳定）。

- **DAG grounded context 过滤（可配置）**
  - 默认只将 **`kind=Knowledge` 且满足背书（attestations）门槛**的节点摘要拼入 Research Assistant system prompt。
  - 配置项（示例）：

```json
{
  "Vibe": {
    "DagGrounding": {
      "MinAttestations": 1,
      "RequiredPubKeys": [
        "04abcd... (hex/base64)"
      ]
    }
  }
}
```

### 3) 协议：Protobuf（跨边界契约）

文件：`scientific-research-assistant/src/ScientificResearchAssistant.Contracts/sra_collab.proto`

- **DAG 节点增强**
  - `SraDagNode` 增加：
    - `kind`：`KNOWLEDGE` vs `PLAN`（grounding/过滤与语义隔离）
    - `owner`：作者公钥（可选）
    - `attestations`：背书签名集合（用于 grounding 门槛/未来共识门控）

> 注：遵循仓库铁律——所有跨边界类型必须用 Protobuf 定义。

### 4) Knowledge Graph（支撑 DAG/知识库）

目录：`src/Aevatar.Agents.Knowledge.Graph/`

- `KnowledgeNode` 模型扩展：增加 `Kind` / `Owner` / `Attestations` 等字段，支持“计划节点 vs 知识节点”与背书元数据。
- `KnowledgeGraphClient` 增强：支持更适配迭代式 agent 流程的 upsert（含 kind/owner 等），并保持依赖边与 `DependsOn` 的一致性（best-effort）。
- `GraphClientBackedStore` 增强：在图存储中持久化 `nodeKind`、`owner`、`attestationsJson`，并对依赖边做更可去重/可取回的存储（edge properties 存 from/to 业务 id）。

### 5) 前端：Workbench（UI）

目录：`scientific-research-assistant/ui/`

- **多视图与可分享路由**
  - `SraWorkbenchApp` 支持 query 路由：`?view=files|dag&session=...`（兼容 `view=graph`）。
  - Files 视图依赖后端 files API（local-only）。

- **`useWorkbenchController` 统一控制器**
  - 汇总 session 列表、SSE、tools/MCP、skills sync、以及 Vibe 快照（brief/dag/trace/delivery/compute）与聊天 UX（过滤/新消息提示等）。

## 如何验证（建议给 Reviewer 的最短路径）

### 后端
- 启动 `scientific-research-assistant` API（本仓库已有启动方式）。
- 创建 session → 发送一条输入（mode=vibe）：
  - 观察 chat 中是否出现 **Brief（首次）**、**Plan(JSON)**、worker 输出，以及 **DAG apply** 段落。

### 前端
- 打开 Workbench：
  - `Chat`：正常对话/编排输出
  - `DAG`：`?view=dag&session=<id>`，确认 DAG 快照/变更可见
  - `Files`：`?view=files&session=<id>`，确认能浏览/读取/写入 session 工作区文件（仅本地）

### Grounding 策略
- 在 `appsettings.json` 或 `appsettings.secrets.json` 配置 `Vibe:DagGrounding`：
  - 调大 `MinAttestations` 或配置 `RequiredPubKeys`，观察 grounded context 是否收敛（仅纳入满足门槛节点）。

## 风险点 / Reviewer 关注点

- **DAG 写入门控仍是 MVP**：当前存在直接 apply 的路径；共识/验证门控虽然有架构预留，但需要确认产品期望与安全边界。
- **placeholder sources 自动生成**：可能引入噪音文件；需要确认与 Materials 管理策略一致。
- **Files API 安全边界**：目前以 loopback 作为 local-only 保护策略；需确认部署形态是否允许。
- **构建产物是否应提交**：如包含 `frontend/dist` 等打包输出，请确认是否属于本 PR 范畴。

## 备注（读代码入口）

- Vibe 后端结构总览：`scientific-research-assistant/src/ScientificResearchAssistant.Api/Vibe/docs/README.md`


