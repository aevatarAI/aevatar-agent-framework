# Researching Module (Framework)

## 结构总览

```
Researching/
  Brief/            # brief.json SSoT（研究简报）
  Compute/          # compute decisions（执行/降级/跳过）
  Dag/              # DAG snapshot + explain + consensus gate
  Delivery/         # delivery center snapshots
  Materials/        # DAG facts → bounded context
  Mesh/             # Mesh DSL 编译/规划/执行/存储
  Modules/          # ResearchingCore/Pivot/Mesh/Host 依赖分组
  Paper/            # paper scaffolding + patch apply
  Round/            # 轮次编排与里程碑执行
    ResearchingRoundServices.*.cs            # workflow step + 编排逻辑
    ResearchingMilestoneLoopRunner.*.cs      # 里程碑循环（intent/extension/helpers 分拆）
    ResearchingMilestoneLoopModels.cs        # MilestoneLoopResult / InterruptionContext
  Runtime/          # IResearchingRuntime（agent 获取抽象）
  Sessions/         # session / providers / workspace state
  Trace/            # trace.jsonl + summary.md
  Uploads/          # uploads SSoT + 文本抽取 + 知识点生成
  Workflow/         # workflow runner + run context + mesh adapter
  Workspace/        # workspace 路径与文件服务
```

## 架构决策（为什么这样拆）

- **框架化复用**：除 Vibe proto 与 Vibe 专属 AG-UI 适配外，其余 researching 能力全部沉淀到框架层，供其他应用复用。
- **File-SSoT**：关键状态以文件为真源，内存与 UI 仅为投影，便于重放与调试。
- **Runtime 抽象**：通过 `IResearchingRuntime` 隔离 agent 获取与运行时细节，避免应用耦合。
- **Mesh/DAG 解耦**：Mesh 负责多 agent 拓扑，DAG 负责知识落盘与共识；互相只通过契约交互。

## 开发规范

- **Protobuf 作为跨边界契约**：状态/事件/持久化均使用 proto 定义。
- **API 与 UI 逻辑不入框架**：AG-UI 事件适配留在应用层；框架只提供运行能力。
- **单文件 ≤ 800 行**：复杂逻辑用 `partial` 拆分；模块职责清晰可定位。

## 变更日志

- 2026-01-30：Vibe researching 核心逻辑迁移到 `Researching/` 框架模块；Vibe 应用层精简为 AG-UI 适配与配置入口。

