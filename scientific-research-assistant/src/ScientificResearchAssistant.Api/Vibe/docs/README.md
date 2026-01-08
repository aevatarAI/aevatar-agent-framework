# Vibe Module（后端：vibe researching 运行时）

本目录承载 `mode=vibe` 的核心后端能力：**File-SSoT 存储** + **单轮编排** + **共识门控**。

## 目录结构（核心骨架）

```
Vibe/
  VibeOrchestrator.cs                       # 入口：ExecuteOneRoundAsync（骨架/时序）
  VibeOrchestrator.Workers.cs               # planner/reasoner/librarian/verifier/dag_builder/paper_editor 的流式调用
  VibeOrchestrator.DagConsensus.cs          # DAG 共识门控（调用 DagConsensusRunner）
  VibeOrchestrator.Trace.cs                 # trace 追加写入 + round_summary SSE
  VibeOrchestrator.ResearchAssistant.cs     # research_assistant 的 brief/plan/summary 调用与解析
  VibeOrchestrator.Parsing.cs               # JSON 提取/解析 + librarian actions 解析
  VibeOrchestrator.GoalsAndMessages.cs      # goals 持久化 + mailbox 广播 + prompt 构造
  VibeOrchestrator.DeliveryApply.cs         # paper_editor 输出解析、patch 应用、delivery snapshots 写入

  Brief/BriefStore.cs                       # deliverables/brief.json（Protobuf-JSON）
  Delivery/DeliveryCenterStore.cs           # deliverables/*（结论/证据/任务/快照）
  Compute/ComputeDecisionStore.cs           # artifacts/compute/decisions（MVP）
  Goals/GoalsStore.cs                       # decisions/goals.json（Protobuf-JSON）
  Trace/TraceStore.cs                       # artifacts/trace/trace.jsonl + runs/*/summary.md
  Uploads/UploadsStore.cs                   # artifacts/uploads
  Dag/                                      # DAG snapshot + explain + consensus gate（见子目录 docs）
```

## 设计要点（为什么这样拆）

- **单文件 ≤ 800 行**：用 `partial` 拆分 `VibeOrchestrator`，按职责划分，降低认知负担。
- **编排不崩溃**：所有 stage 都是 *best-effort*；失败只会阻断本 stage，不会炸掉 API 进程。
- **SSE 以快照优先**：前端 reconnect 先收 `*_snapshot`，再接 live stream，避免依赖 replay。


