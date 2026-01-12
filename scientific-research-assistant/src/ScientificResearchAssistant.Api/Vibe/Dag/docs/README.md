# Vibe Graph Module（后端：KnowledgeGraph + DAG 兼容 API + explain + consensus）

本目录实现 vibe researching 的 **知识图谱（KnowledgeGraph, Session-scoped）** 与 **增量写入门控（共识）**。

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

  DagConsensusRunner.cs             # 共识入口 + maker-v2 路径（可选）
  DagConsensusRunner.Quorum.cs      # 默认：verifier-quorum（N verifiers 投票 + red-flag）
```

## 共识策略（默认与可选）

- **默认：`verifier-quorum`**
  - 多个 `VibeVerifierAgent` 实例投票
  - 通过条件：`approve >= quorum` 且无硬 red-flag
  - 产物：`artifacts/dag/consensus/*.json`
- **可选：`maker-v2`**
  - 调用 `CognitiveStrategy.ExecuteAsync` 执行 `maker-v2` 工作流
  - 用于更重的“审查/规范化”（MVP 仍保留）

## 文件落点（File-SSoT）

- 默认（per-session）：
  - `workspace/sessions/{sessionId}/artifacts/dag/*`
- 共享（shared dagId）：
  - `workspace/dags/{dagId}/artifacts/dag/*`

其中：
- `artifacts/dag/snapshot.json`: 图快照镜像（Protobuf-JSON，审阅/恢复用）
- `artifacts/dag/staged/`: 未通过共识的候选（保留以便回溯/再跑）
- `artifacts/dag/consensus/`: 共识 artifacts（用于审计与 debug）

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


