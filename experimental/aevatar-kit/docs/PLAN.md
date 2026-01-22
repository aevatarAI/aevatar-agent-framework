# AevatarKit（对标 mcp-agent-graph）实现计划

> 目标：在 **Aevatar Actor Runtime** 之上做一个“可视化多智能体工作流平台”，让 Graph/Agent/Tool/Model/Memory 变成可配置、可复用、可观测、可治理的产品能力。

---

## 1. 背景与定位

### 1.1 对标对象

- `mcp-agent-graph`（MAG）是一套 **平台**（前端 + 后端 + 存储 + 部署），提供 Agent/Graph/Model/Memory/MCP 的一体化体验，并支持将 Graph 导出为可调用的 Tool（MCP Server Script）。

### 1.2 AevatarKit 的定位

- **AevatarKit = 平台层（Product） + 执行层（Aevatar）**
- AevatarKit 不重新发明“并行/编排/投票/护栏/可观测”，优先复用 Aevatar 现有的 `Aevatar.Agents.Cognitive` 执行内核与 `Aevatar.Agents.AI.Tool.MCP` 的 MCP 集成。

---

## 2. 核心原则（品味与约束）

### 2.1 单一真相源（SSOT）

- Graph 的唯一权威表示应为 **Protobuf GraphDefinition（IR）**。
- YAML 仅作为：导入/导出格式、调试产物、兼容运行时编排层的编译目标。

### 2.2 能消失的分支永远更优雅

- 不在业务层堆 if/else 处理节点特例。
- 通过统一的 `StepDefinition`/参数解析/guardrails 让“异常情况”进入常规路径。

### 2.3 Token-Free First

- 确定性处理（聚合/过滤/去重/事实检索/指标计算）应优先走 token-free primitives（如 `transform/retrieve_facts/hpa`）。
- LLM 只做：生成、推理、证明草案、不可确定的决策。

---

## 3. 系统架构（MVP 版本）

### 3.1 逻辑分层

- **Frontend（Web）**
  - Graph Editor（拖拽编排）
  - Run Timeline（实时事件/流式输出可视化）
  - Agent/Model/MCP 管理页面

- **Backend（AevatarKit API / Control Plane）**
  - Graph/Agent/Prompt/Model/MCP 配置 CRUD
  - Run Orchestrator：创建 Coordinator、创建/扩容 Worker Pool、注入变量、启动执行
  - Event Streaming：将 `WorkflowStepEvent` 推送到前端（SSE/WebSocket）

- **Runtime（Aevatar Data Plane）**
  - `CognitiveCoordinatorGAgent`：执行 YAML/IR 编译后的 workflow
  - `RoleAIGAgent` + `CognitiveStepExecutionHandler`：并行执行 `llm_call`（Actor 真实并行）

### 3.2 数据存储（建议）

- **关系型 DB（PostgreSQL）**：
  - Graph/Run/Version/ACL 等结构化数据
  - **AI Memory（对话/事件/索引）**：append-only + 搜索（FTS），为共享/治理/RBAC 打基础
- **对象存储（S3/MinIO）**：
  - 附件、导出物、超长日志/回放包
  - Memory Attachments（图片/文件/超长文本/证据包）
- **向量存储（V1 再上）**：
  - pgvector（优先，便于统一运维与权限）
  - 或独立向量库（性能/能力驱动）

### 3.3 AI Memory 子系统（MVP+ 核心能力）

> 目标：让 Memory 成为“可配置、可复用、可检索、可共享、可治理”的产品能力，而不是散落在单个 Agent 里的私有实现。

#### 3.3.1 Memory 的数据模型（SSOT）

- **Memory 是资源**：不是“Agent 私有变量”，而是可被多个 Agent/Run 引用的结构化资源。
- **推荐主键（概念）**：
  - `tenant_id`：多租户隔离
  - `memory_id`：Memory 资源 id（稳定引用）
  - `scope`：绑定范围（下文），用于默认隔离与生命周期管理
  - `entry`：append-only 的记忆条目（时间序列）
- **记忆分层（从快到慢）**：
  - Conversation / Timeline（原始对话与关键事件，append-only）
  - Working Memory（运行中短期上下文，按 runId 生命周期）
  - Long-term Memory（可回收、可总结、可打标签）
  - Retrieval Index（FTS/Embedding 索引）

#### 3.3.2 Memory Scope：共享的第一性设计

AI Agent **可以共享 memory**，但共享必须有边界与治理。AevatarKit 将 Memory 做成可声明的 scope：

- `private(agent)`：单 Agent 私有（默认）
- `session(sessionId)`：同一会话共享（例如多 Agent 协作会话）
- `run(runId)`：同一次运行共享（便于回放与审计）
- `graph(graphId@version)`：同一 Graph 版本共享（作为“技能包”的记忆基底）
- `tenant(shared)`：租户级共享（组织知识库/事实库）

> 实现要点：共享依赖 **append-only + 幂等写入**，避免“最后写赢”的状态覆盖；总结/提纯走异步投影。

#### 3.3.3 权限与安全（RBAC + Policy）

- Memory 需要与 Graph/Run 同等级的 **ACL/RBAC**：
  - Read / Write / Admin（删除、导出、策略变更）
  - Tool/Agent 的写入必须可审计（谁写了什么）
- 存储层（Postgres/Supabase）建议：
  - Backend 使用 service 账户直连写入（最小暴露）
  - 若暴露给 PostgREST，则启用 RLS + policy（按 tenant/scope/role 控制）

#### 3.3.4 在 AevatarKit 中的管理体验（必须“方便”）

- **Memory Explorer**（Web UI）：
  - 列表/过滤：按 scope、agent、run、标签、时间范围
  - 检索：FTS / 语义检索（V1）
  - 操作：pin、tag、merge、summarize、export、purge（保留策略）
- **Memory Profile**（配置项）：
  - 为 Graph/Agent 绑定：使用哪些 memory source（读写/只读）
  - 策略：保留天数、最大条目、是否写入原文、是否自动总结


---

## 4. 运行模型（把“图”跑起来）

### 4.1 Graph → Workflow 的编译路径

- MVP：`GraphDefinition(IR)` → 编译为 `WorkflowDefinition(YAML DSL)` → 由 `CognitiveCoordinatorGAgent` 执行。
- V1：IR 直接执行（避免 YAML 作为中间层）；YAML 仅用于导入/导出。

### 4.2 观测与回放

- 统一使用 `WorkflowStepEvent` 做：
  - 实时可视化（Running/Completed/Failed/Streaming）
  - 运行历史回放（按 runId 拉取事件流）
  - 审计（哪个节点调用了什么工具、耗时、token、失败原因）

### 4.3 可靠性护栏

- 复用并推广现有 DSL 护栏字段：
  - `timeout_seconds` / `idle_timeout_seconds`
  - `max_length` / `strict_parse`
  - `include_failures`（失败也进入结果列表，进度不会“卡死”）

---

## 5. MCP（工具生态）

### 5.1 MVP：MCP Client

- 复用 `Aevatar.Agents.AI.Tool.MCP`（官方 C# MCP SDK）。
- 平台侧提供：
  - MCP Server Registry（npx/uvx/stdio，后续扩展 http transport）
  - Tool Catalog（列出工具 schema）
  - Graph 节点：ToolCall Node（toolName + args 模板）

### 5.2 V2：Graph as Tool（对标 MAG 导出）

- 目标：把“已发布 Graph”暴露成一个 Tool：
  - 方案 A：实现 MCP Server Host（Graph → MCP tool）
  - 方案 B：提供 HTTP Tool Gateway，再由 MCP server 封装

### 5.3 Agent Skills（技能包）与生态（对标“SKILL.md”标准）

> 观察：社区正在用 **SKILL.md** 把“能力包”做成可安装、可复用的单元（并有 marketplace.json 支持一键安装）。
> AevatarKit 可以把“Graph/Tool/Prompt/MemoryProfile”打包成 Skill，形成内部与外部生态。

- **Skill = 可分发资产包**（建议结构）：
  - `SKILL.md`：说明、触发条件、输入输出、权限、示例
  - `graph.pb`/`graph.yaml`：GraphDefinition（IR）与可选导入导出格式
  - `tools/`：MCP server 配置（npx/uvx/http）与 tool schema
  - `prompts/`：system prompt / templates
  - `memory_profile.json`：memory sources + 策略（保留/总结/检索）
- **平台能力**：
  - Skill Catalog：搜索/安装/启停/更新/审计（企业内“技能市场”）
  - Graph/Skill 双向转换：Graph 发布 → Skill；Skill 安装 → Graph/Tool/MemoryProfile
- **安全基线**：
  - 第三方 Skill 默认只读导入（需显式授权才能启用 tool/写入 memory）
  - 依赖与脚本执行需沙箱/签名/白名单（防供应链风险）

---

## 6. 目录结构（计划中的代码骨架）

> 注意：这是目标结构树（用于后续实现），当前仅创建 docs。

```text
experimental/aevatar-kit/
  docs/
    PLAN.md
  src/
    AevatarKit.Api/              # REST/SSE/WebSocket
    AevatarKit.Core/             # Graph IR、编译器、校验器
    AevatarKit.Runtime/          # Orchestrator：Coordinator/Worker 生命周期管理
    AevatarKit.Storage/          # DB/对象存储适配
  frontend/
    ...                          # Graph Editor + Run UI
```

---

## 7. 里程碑（可执行拆解）

### M0（1 周）：能跑 + 能看

- Graph 先用固定 YAML（无需 UI）
- API：启动一次 run，实时输出 `WorkflowStepEvent`
- 前端：Timeline 能显示步骤状态与流式输出
- Memory（最小闭环）：把 run 的关键输出/对话以 append-only 方式落库，支持回放与审计

### MVP（2–4 周）：能编排 + 能复盘

- Graph CRUD（最小字段）+ YAML Import/Export
- 前端 Graph Editor（有限节点集：llm_call/conditional/vote/fan_out/transform/workflow_call）
- Run History + 回放（按 runId 拉取 StepEvent）
- MCP Server 连接（npx/uvx）+ Tool 节点调用
- Memory（可用）：Memory Explorer（列表/搜索/导出）+ Memory Profile（Graph/Agent 绑定）

### V1（4–8 周）：平台化

- 多租户 + RBAC
- Graph versioning（draft/published）
- Scheduler（定时/周期跑 Graph）
- Memory（进阶）：
  - 共享 memory（session/run/graph/tenant scope）
  - 自动总结（投影）+ embedding 检索（pgvector）
  - 保留与清理策略（可审计）

### V2：对外可调用 + 生态

- Graph as MCP Tool / HTTP Tool
- Subgraph Marketplace（内部共享/发布）
- 配额/成本治理（并发、token、工具调用）
- Skill Marketplace：
  - Graph/Tool/Prompt/MemoryProfile 打包分发（SKILL.md）
  - Skill 安装与治理（审计、权限、版本、回滚）

---

## 8. 风险与对策

- **多租户与安全**：工具调用必须做鉴权/隔离/审计；MCP env/secrets 需要加密存储。
- **记忆安全与合规**：共享 memory 需要 RBAC、保留策略、可删除与可导出；写入来源必须可追溯。
- **“看起来卡死”**：所有并行/流式必须具备终态判定与超时策略；失败也必须进入可视化与统计。
- **Graph 演进**：IR 必须版本化；Protobuf schema 演进遵循“只加字段不改号”。

---

## 9. 变更日志

- 2025-12-18：初始化版本（创建 AevatarKit 计划文档）。
- 2025-12-21：扩展 Memory 子系统设计（共享/治理/管理 UI）与 Skill 生态（SKILL.md 打包与市场化）。
