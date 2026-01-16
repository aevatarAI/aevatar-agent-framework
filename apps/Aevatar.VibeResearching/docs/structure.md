# Project Structure

## Directory Organization

本 steering 给出一个**面向 multi-agent 协作**的文件目录约定：把“研究输入/结论沉淀/agent 通信/运行产物”拆开，做到可审计、可复现、可并发。

### Canonical tree（以 Aevatar.VibeResearching 为例）

```

├── workspace/                       # Agent 协作工作区（session-scoped，可清理）
│   ├── sessions/
│   │   └── {sessionId}/
│   │       ├── paper/               # 论文稿件（Markdown）
│   │       │   ├── outline.md
│   │       │   ├── draft.md
│   │       │   └── citations.json
│   │       ├── facts_proposed/      # 候选事实（待共识/验证）
│   │       ├── decisions/           # 投票/审核/最终决策（promote 依据）
│   │       ├── mailbox/             # agent 间“文件邮箱”通信
│   │       │   ├── {agentName}/
│   │       │   │   ├── in/          # 接收队列（原子写入）
│   │       │   │   ├── processing/  # 处理中（move 锁定）
│   │       │   │   ├── out/         # 发送队列（等待投递/归档）
│   │       │   │   └── archive/     # 已处理归档（幂等/追溯）
│   │       │   └── _dead/           # 失败消息（人工排查）
│   │       ├── artifacts/           # 运行产物（代码/图/表/中间数据）
│   │       ├── runs/                # 每次 run 的结构化记录（参数/摘要/引用集合）
│   │       └── tmp/                 # 临时文件（可随时删）
│   └── dags/
│       └── {dagId}/
│           ├── artifacts/           # DAG 快照镜像（artifacts/dag/*）
│           └── tmp/
├── src/                             # 后端/agent 代码
├── test/                            # 单元测试（按项目拆分）
├── sisyphus-frontend/               # 前端 UI
└── docs/                            # 文档
```

### 目录语义（核心约束）

- `workspace/dags/{dagId}/`：DAG 快照镜像（**知识真相在 KnowledgeGraph**，这里只做审阅/恢复）
- `workspace/sessions/{sessionId}/facts_proposed/`：候选事实（不可当作前提依赖）
- `workspace/sessions/{sessionId}/paper/`：稿件（Markdown），仅允许单写者合并
- `workspace/sessions/{sessionId}/mailbox/`：**agent 通信专用**；所有跨 agent 指令/投票/结果都必须走文件

## Naming Conventions

### Files

- **Folders**: `kebab-case`（例如 `protein-folding/`）
- **Agent names**: `snake_case` 或 `kebab-case`（例如 `python_verifier`）
- **Mailbox message files**:
  - `{utcTimestamp}_{messageId}_{from}->{to}.{ext}`
  - 示例：`20260105T120102Z_9f3a_planner->reasoner.json`

### Code

- C#：类 `PascalCase`，方法 `PascalCase`，局部变量 `camelCase`

## Import Patterns

按现有仓库惯例（无额外约束）。

## Code Structure Patterns

### File Organization Principles

- 边界层（HTTP/AG-UI/Filesystem projection）与推理层严格分离
- 任何“写文件”的逻辑必须 best-effort + 有界 + 原子写

### Sessions API layout（partial class 拆分）

```
src/VibeResearching.Api/Sessions/
├── ResearchSessionsApi.cs                     # 入口 + MapResearchSessionsApi + Json 选项
└── Api/
    ├── ResearchSessionsApi.Core.cs            # create/list/tools/agent-providers
    ├── ResearchSessionsApi.MeshAndFiles.cs    # mesh + files + local-only guard
    ├── ResearchSessionsApi.StatusAndDeliverables.cs # status + deliverables
    ├── ResearchSessionsApi.Runtime.cs         # compute/uploads/dag/graph
    ├── ResearchSessionsApi.InputAndFacts.cs   # input/mcp/facts/workspace
    └── ResearchSessionsApi.AgUiEvents.cs      # SSE (AG-UI) 快照与流
```

设计理由：保证单文件 ≤ 800 行、单目录 ≤ 8 文件，避免边界层职责继续膨胀。

## Module Boundaries

- **dag (knowledge graph)**：共享黑板（知识节点事实）
- **facts_proposed + decisions**：验证闭环（从推论到 DAG 事实的“闸门”）
- **mailbox**：点对点/多对多通信（流程控制）
- **artifacts/runs**：可复现产物（审计与复盘）

## Code Size Guidelines

沿用仓库铁律：
- 单文件 ≤ 800 行
- 单目录 ≤ 8 个文件（超过就分层）

## Documentation Standards

- 每个系统的根目录必须有 README，说明 workspace/dag/facts_proposed 的约定
- 任何目录级调整必须同步更新 `docs/` 与 steering 的结构树

## File-based Agent Communication Protocol（关键约束）

### Message Schema（必须 Protobuf 定义）

即使消息落盘为 `.json/.md`，其 schema 也必须由 `.proto` 定义（跨 agent 边界）。

建议定义：
- `AgentMailboxMessage`：from/to/sessionId/type/payload/traceId
- `FactRecord`：title/content/citations/verification/artifacts
 - `FactDecision`：factId/threshold/votes/verifications/finalDecision

### Atomic Write（必须）

- 写入流程：写到 `tmp/` → `fsync`（可选）→ `rename` 到 `in/`
- 消费流程：`in/` → move 到 `processing/`（锁定）→ 处理 → `archive/`（避免重复消费）

### Idempotency（必须）

- messageId 必须全局唯一（或 session 范围唯一）
- 消费端必须能识别已处理（基于 messageId + archive）

### Paper / Facts 的并发写（必须）

- **Single writer**：
  - 只有 `paper_editor`（或同等角色）可以写 `paper/*` 与 promote 到 DAG
  - 其他 agent 只能提交 `patch_proposal` / `fact_proposal`
- **事实闭环**：
  - `facts_proposed/{factId}.md|json`（候选）
  - reviewers 写 vote：`decisions/votes/{factId}/{agent}.json`
  - verifier 写验证：`decisions/verifications/{factId}/{agent}.json`（含 artifacts 路径）
  - promoter 写最终决策：`decisions/final/{factId}.json` 并执行 promote（写入 DAG）

## 变更记录

- 2026-01-15：`VibeResearching.Tests`、`VibeResearching.Api.Tests` 移入 `apps/Aevatar.VibeResearching/test/`。

