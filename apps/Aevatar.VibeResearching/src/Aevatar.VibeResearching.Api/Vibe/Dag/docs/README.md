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

  DagConsensusRunner.cs             # 预留：共识/验证（未来可作为“标注/审核”而非写入门控）
  DagConsensusRunner.Quorum.cs      # 预留：verifier-quorum（N verifiers 投票 + red-flag）
```

## 验证/共识（当前策略：不阻断写入）

当前（MVP）实现为 **“先写入，再验证（可选）”**：
- `dag_builder` 产出的 mutation 会直接 `ApplyMutationAsync` 写入 KnowledgeGraph，并同步快照到 `artifacts/dag/snapshot.json`
- 不再把 verifier 结果作为写入门控（不再 staged / 不再 block）
- 未来可以把 `DagConsensusRunner` 用作：
  - 为节点打标签（verified/unverified）
  - 输出审计 artifact
  - 但不影响写入链路的可用性（避免研究流程被卡死）

## 文件落点（File-SSoT）

- 默认（per-session）：
  - `workspace/sessions/{sessionId}/artifacts/dag/*`
- 共享（shared dagId）：
  - `workspace/dags/{dagId}/artifacts/dag/*`

其中：
- `artifacts/dag/snapshot.json`: 图快照镜像（Protobuf-JSON，审阅/恢复用）
- `artifacts/dag/staged/`: 预留（未来可用于人工 review 队列；当前不再作为门控）
- `artifacts/dag/consensus/`: 预留（未来可用于验证 artifacts；当前不再作为门控）

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


