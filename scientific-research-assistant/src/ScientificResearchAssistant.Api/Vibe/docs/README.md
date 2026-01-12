# Vibe Module（后端：vibe researching 运行时）

本目录承载 `mode=vibe` 的核心后端能力：**File-SSoT 存储** + **单轮编排** + **DAG 增量写入**（当前不做写入门控验证）。

## 目录结构（核心骨架）

```
Vibe/
  VibeOrchestrator.cs                       # 入口：ExecuteOneRoundAsync（骨架/时序）
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


