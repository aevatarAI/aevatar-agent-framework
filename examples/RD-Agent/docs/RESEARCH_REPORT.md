# RD-Agent 调研报告（面向 Aevatar 借鉴）

目标：调研 [microsoft/RD-Agent](https://github.com/microsoft/RD-Agent) 的定位与工程化亮点，并给出 Aevatar Agent Framework 可借鉴、可落地的改进建议。

---

## 1. RD-Agent 是什么（现象层）

- **定位**：把“数据/模型驱动的研发流程”当作自动化对象，让 AI 自动完成从想法到实现再到验证的闭环（仓库与技术报告均强调 “AI drive data-driven AI”）  
  - Repo: [microsoft/RD-Agent](https://github.com/microsoft/RD-Agent)  
  - Tech report: [aka.ms/RD-Agent-Tech-Report](https://aka.ms/RD-Agent-Tech-Report)  
  - Paper: [arXiv:2505.14738](https://arxiv.org/abs/2505.14738)

- **工程形态信号**：
  - `.streamlit/`：面向用户的交互 UI（更偏“产品化应用”而非纯 SDK）
  - `constraints/`：约束体系是“一等公民”（预算/规则/风险控制）
  - `docs/` + `test/`：强调可复现与可回归

---

## 2. RD-Agent 的关键工程化亮点（本质层）

### 2.1 场景驱动（Scenario-first）

RD-Agent 把“场景”当作核心入口：不同场景有不同配置与运行方式，用户关注的是“跑一个研发闭环”，而不是拼装底层组件。

### 2.2 Trace 优先（Trace-first）

它强调产出可下载/可复盘的执行 trace，用于调试、复现实验过程、对比迭代效果（这点是它从“Demo”迈向“研发流水线”的关键）。

### 2.3 约束一等公民（Constraints-as-Product）

通过约束体系把不确定性关进笼子：预算（tokens/时间/调用次数）、规则（安全/合规/流程）等，被统一管理，而不是散落在 prompt 与代码。

---

## 3. Aevatar 可借鉴点（对照）

### 3.1 Aevatar 已有强项（不需要照搬 RD-Agent 的部分）

- **Actor + 事件流 + 多运行时**：Local/Orleans/ProtoActor 的分布式骨架，是 RD-Agent（脚本/单机形态）天然缺的。
- **Protobuf 铁律**：跨边界类型强制 Protobuf，为跨 runtime、持久化与回放打地基。
- **可观测性**：Aevatar 已有 OpenTelemetry spans/metrics（agent/llm/workflow 等）。

### 3.2 Aevatar 真正需要学的（优先）

#### A) 统一“可导出执行轨迹”协议

RD-Agent 的“trace-first”提示我们：trace 不是日志，是产物；而 Aevatar 之前存在多套 trace（MAKER/UoT/WorkflowStepEvent/OTel），碎片化会迅速拖垮 Debug 与评测。

本次已落地：
- **统一协议**：`ExecutionTrace`（Protobuf）
  - Proto：`src/Aevatar.Agents.Abstractions/execution_trace.proto`
  - 说明：`src/Aevatar.Agents.Abstractions/docs/EXECUTION_TRACE.md`
- **适配器**：
  - MAKER：`MakerResult.ToExecutionTrace()`
  - UoT：`UoTResult/TUoTResult.ToExecutionTrace()`
  - Cognitive Workflow：`IEnumerable<WorkflowStepEvent>.ToExecutionTrace()`

#### B) 下一步（建议，但未在本次提交实现）

在 `ExecutionTrace` 统一后，再做 RD-Agent 风格的：
- Scenario Runner（场景入口）
- Eval（评测器与回归对比）
- Trace Bundle（统一导出：proto/bin + json + artifacts + OTel trace link）

---

## 4. 品味自检（避免坏味道）

- 不要把长文本/全文塞进 trace 的 `output`（会让 trace 变成“体积炸弹”）
- 统一口径：如果一个模块要“导出/存档/对比”执行过程，必须最终归一到 `ExecutionTrace`


