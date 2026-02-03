# Vibe Graph Module（后端：KnowledgeGraph + DAG 兼容 API + explain）

本目录实现 vibe researching 的 **知识图谱（KnowledgeGraph, Session-scoped）** 与 **DAG 快照/解释能力**。

中文说明（关键点）：
- **SSoT**：`Aevatar.Agents.Knowledge.Graph`（图后端可 InMemory/Neo4j）
- **镜像/审阅**：仍然把可读快照同步写入 `artifacts/dag/snapshot.json`（Protobuf-JSON，便于 diff/debug/恢复）
- **兼容前端**：保留 `/dag` API 与 `DagExplain` 语义（依赖 -> 被依赖：`from -> to`）
- **增强能力**：新增 `/graph` API 提供知识链（chain）与论文（paper）生成
- **共享 DAG（跨 session）**：多个 session 可绑定同一个 `dagId`，共享同一份 KnowledgeGraph/DAG（见 `/dag/binding` API）

## 目录结构

```
Dag/
  DagStore.cs                       # KnowledgeGraph SSoT + artifacts/dag/snapshot.json mirror + staged + consensus artifacts
  DagExplain.cs                     # explain(node): topo order / dependencies / cycle check（UI 调试用）
  PlanDagMutationBuilder.cs         # 统一构建 plan milestone 的 DAG mutation（plan_edit / plan_apply 复用）

  DagConsensusRunner.cs             # 共识/验证：maker（Cognitive DSL）或 verifier-quorum
  DagConsensusRunner.Quorum.cs      # verifier-quorum（N verifiers 投票 + red-flag）
```

## 验证/共识（当前策略：作为写入门控）

当前实现为 **“先共识，再写入”**：
- `DagConsensusRunner` 产出 accepted mutation 后才执行 `ApplyMutationAsync`
- 支持 `verifier-quorum` 与 `maker`（Cognitive DSL `maker.yaml`）两种模式
- 共识 artifacts 持久化到 `artifacts/dag/consensus/*`，用于审计/回放

## 文件落点（File-SSoT）

- 默认（per-session）：
  - `workspace/sessions/{sessionId}/artifacts/dag/*`
- 共享（shared dagId）：
  - `workspace/dags/{dagId}/artifacts/dag/*`

其中：
- `artifacts/dag/snapshot.json`: 图快照镜像（Protobuf-JSON，审阅/恢复用）
- `artifacts/dag/staged/`: 预留（未来可用于人工 review 队列）
- `artifacts/dag/consensus/`: 共识 artifacts（verifier-quorum / maker 输出）

## API 速览

- **兼容 DAG（前端继续用）**
  - `GET /api/sessions/{sessionId}/dag`
  - `GET /api/sessions/{sessionId}/dag/{nodeId}/explain`
  - `GET /api/sessions/{sessionId}/dag/staged`
- **共享 DAG 绑定**
  - `GET /api/sessions/{sessionId}/dag/binding`
  - `PUT /api/sessions/{sessionId}/dag/binding`（body: `{ "dagId": "xxx" }`，空字符串表示解绑）
- **KnowledgeGraph（新能力）**
  - `GET /api/sessions/{sessionId}/graph`（完整图快照）
  - `GET /api/sessions/{sessionId}/graph/{nodeId}/chain`（知识链 + Markdown）
  - `GET /api/sessions/{sessionId}/graph/paper`（全量 Markdown 论文）


