### novel/src - .NET Sidecar 实现目录

这里放 Novel 产品的 **C# 后端实现**（.NET 10），作为 Tauri/React 的 sidecar。

#### 项目划分（v1）

- `Aevatar.Novel.Contracts/`
  - Protobuf 契约生成（从 `novel/protos/*.proto` 生成 C# 类型）
  - 目标：跨边界类型的唯一来源（避免手写 DTO 漂移）

- `Aevatar.Novel.Sidecar/`
  - 可运行的 sidecar（ASP.NET Core）
  - 目标：提供 HTTP/gRPC API + 事件流（SSE/WebSocket/IPC）
  - 负责：文件监听、增量索引（SQLite 可重建）、工作流编排入口（Cognitive Mesh）

#### 已实现（当前可用）

- **SSOT ProjectRoot**
  - `GET /api/novel/project-root`
  - `POST /api/novel/project-root`（Protobuf JSON：`{ "projectRoot": "/abs/path" }`）

- **事件流（SSE）**
  - `GET /api/novel/events`
  - 输出 `SidecarEvent`（Protobuf JSON），包括：
    - `fileChanged`
    - `unitTestsCompleted`
    - `writingSessionLogUpdated`
    - `deviationImpactCompleted`
    - `canonChangeRecorded`
    - `rewriteBranchCreated` / `rewriteBranchMerged`
    - `setupPayoffScanCompleted`

- **文件 API（供前端编辑器使用，强制 SSOT + 路径安全）**
  - `POST /api/novel/fs/list`
  - `POST /api/novel/fs/read`
  - `POST /api/novel/fs/write`

- **工作流 F：故事单元测试 + 写作日志（会话聚合）v1**
  - 作者修改 `chapters/*.txt`（或 `artifacts/tests/narrative_tests.md`）后，sidecar 会：
    - 运行 Narrative Tests（由 `NarrativeTestAgent` 负责，Aevatar Local runtime）
    - 产出 `artifacts/tests/<run_id>_test_report.md`
    - 聚合会话日志：`sessions/<session_id>/writing_session_log.md`
    - 产出会话提示词（v1 stub）：`sessions/<session_id>/author_intent_prompt.md`

- **工作流 E：偏离 → 影响分析（deterministic v1）**
  - 作者修改 `chapters/*.txt` 后，sidecar 会（派生文件，可删可重建）：
    - `artifacts/deviation/*_deviation_report.md`
    - `artifacts/deviation/*_deviation_prompt.md`
    - `artifacts/deviation/*_change_impact_report.md`
    - `artifacts/deviation/*_backup_options_report.md`

- **Canon Governance（设定变更治理 v1）**
  - 作者修改 `artifacts/canon/*.md` 后，sidecar 会产出：
    - `artifacts/canon/changes/*_canon_change.md`（要求作者填写 reason/impact/trace-back）

- **Rewrite Branch / Merge（分支与合并 v1）**
  - API：
    - `POST /api/novel/branches/create`
    - `POST /api/novel/branches/list`
    - `POST /api/novel/branches/diff`
    - `POST /api/novel/branches/merge-chapter`
  - 文件布局：
    - `branches/<branch_id>/chapters/*.txt`
    - `branches/_merges/*_merge.md`

- **Setup/Payoff Ledger（伏笔台账 v1 / deterministic）**
  - 触发：
    - 作者修改 `chapters/*.txt`
    - 或修改 `artifacts/ledger/setup_payoff_ledger.md`
  - 产出（派生文件，可删可重建）：
    - `artifacts/ledger/reports/<run_id>_setup_payoff_report.md`
  - SSE：
    - `setupPayoffScanCompleted`（包含 report/ledger 的 `ArtifactRef` + open/paid/broken/dueSoon 计数）

#### Narrative Tests 定义（v1）

在 `artifacts/tests/narrative_tests.md` 中写一个 ` ```json ` 代码块（见 `novel/agent_skills/narrative-tests/SKILL.md`），即可被 v1 runner 执行。


