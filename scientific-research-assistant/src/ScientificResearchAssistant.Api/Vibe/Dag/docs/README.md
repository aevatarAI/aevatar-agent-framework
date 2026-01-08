# Vibe DAG Module（后端：DAG snapshot + explain + consensus）

本目录实现 vibe researching 的 **DAG 知识库（File-SSoT）** 与 **增量写入门控（共识）**。

## 目录结构

```
Dag/
  DagStore.cs                       # artifacts/dag/snapshot.json + staged + consensus artifacts
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

- `artifacts/dag/snapshot.json`: 当前 DAG 快照（Protobuf-JSON）
- `artifacts/dag/staged/`: 未通过共识的候选（保留以便回溯/再跑）
- `artifacts/dag/consensus/`: 共识 artifacts（用于审计与 debug）


