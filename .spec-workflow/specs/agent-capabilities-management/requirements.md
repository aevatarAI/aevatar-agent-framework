# Requirements Document

## Introduction

在科研平台（`scientific-research-assistant/ui`）的 **Agents 卡片**中新增一个“能力管理”入口，用于 **按 Agent 维度**实时管理其可用的：

- MCP Servers（连接/断开/优先级/可用性）
- Skills（skill packs 的安装/启用/禁用/同步）
- AI Tools（可调用工具清单的启用/禁用/分组与搜索）

目标是让操作者在不重启服务的前提下，能够 **实时 CRUD** 每个 agent 的能力配置，并让 `dag_builder` 等 worker agent 在下一轮/下一次请求中基于最新能力做决策与工具调用。

## Alignment with Product Vision

该能力直接支持 steering `product.md` 的：

- **Events are Truth / Runtime Agnostic**：能力配置以清晰的配置源（File-SSoT + API）驱动，避免隐藏状态；不同宿主/运行时保持一致交互。
- **Protobuf-First Contracts**：跨边界的可持久化数据结构保持 schema-first（沿用仓库既有 API 形态，必要处以 Protobuf/JSON 镜像确保可演进）。
- **Operational Readiness**：让 AI 工具与 MCP 能力在平台内可见、可控、可审计，降低“黑盒工具链”带来的风险。

## Requirements

### Requirement 1 — Agents 卡片新增“能力管理”入口

**User Story:** 作为平台使用者，我希望在 Agents 卡片里能打开每个 agent 的能力管理面板，以便快速查看/调整该 agent 的 MCP/Skills/Tools 配置。

#### Acceptance Criteria
1. WHEN 用户在 Agents 卡片中选择某个 agent THEN 系统 SHALL 提供“Capabilities/能力”入口并打开管理面板。
2. WHEN 管理面板打开 THEN 系统 SHALL 显示该 agent 当前启用的 MCP servers、skills、tools 的摘要（数量、状态、最后更新时间）。
3. IF 当前未连接 session THEN 系统 SHALL 禁用能力管理入口并提示需要先连接 session。

### Requirement 2 — MCP Server 能力的实时管理（CRUD）

**User Story:** 作为平台使用者，我希望能为某个 agent 配置其可用的 MCP server（连接/断开/排序/禁用），以便控制该 agent 的工具来源。

#### Acceptance Criteria
1. WHEN 用户添加/更新/删除某个 MCP server 配置 THEN 系统 SHALL 在 1 秒内刷新 UI 状态并展示成功/失败原因。
2. WHEN 用户对 MCP server 执行“Connect/Reconnect/Disconnect” THEN 系统 SHALL 调用后端能力并返回实时状态（connected/connecting/error）。
3. IF MCP server 配置包含敏感字段（如 token/key） THEN 系统 SHALL 不在前端明文回显（仅允许通过安全的 secrets/host 机制注入）。

### Requirement 3 — Skills（Skill Packs）能力的实时管理（CRUD）

**User Story:** 作为平台使用者，我希望能为某个 agent 管理其 skills（安装/卸载/启用/禁用/同步），以便让不同 agent 具备不同工具/知识能力。

#### Acceptance Criteria
1. WHEN 用户对 skills 执行 install/uninstall/enable/disable THEN 系统 SHALL 更新配置并在 UI 中立刻反映（包含进度与错误提示）。
2. WHEN 用户触发“Sync skill packs” THEN 系统 SHALL 复用现有同步机制并在面板内展示日志与结果。
3. IF 当前宿主不支持某些写操作 THEN 系统 SHALL 显示只读模式与原因（例如缺少对应 transport capability）。

### Requirement 4 — AI Tools（可调用工具）能力的实时管理（CRUD）

**User Story:** 作为平台使用者，我希望能控制某个 agent 的可调用工具列表（启用/禁用/搜索/分组），以便最小化权限面与错误调用。

#### Acceptance Criteria
1. WHEN 用户启用/禁用某个 tool THEN 系统 SHALL 在下一次该 agent 发起推理/工具调用时生效（无需重启）。
2. WHEN 工具数量较大 THEN 系统 SHALL 支持搜索与分页/虚拟化（至少支持 top-N + 搜索过滤）。
3. IF 工具来源为 MCP THEN 系统 SHALL 标注来源并支持按 MCP server 过滤。

### Requirement 5 — 配置持久化与回放（File-SSoT + 审计）

**User Story:** 作为平台维护者，我希望 agent 能力配置可持久化与可审计，以便重连/刷新后配置不丢，并可追踪变更。

#### Acceptance Criteria
1. WHEN 用户更新 agent 能力配置 THEN 系统 SHALL 将配置写入 File-SSoT（session scoped），并包含 version/updatedAt。
2. WHEN 前端重连或刷新 THEN 系统 SHALL 从后端加载最新能力配置并渲染一致。
3. WHEN 配置写入失败 THEN 系统 SHALL 回滚 UI 乐观更新并提示错误。

### Requirement 6 — `dag_builder` 能力读取与决策

**User Story:** 作为平台系统，我希望 `dag_builder` 能随时读取当前 DAG 状态与其自身能力配置，以便选择如何加入新的 node（例如是否可用某些工具/skills）。

#### Acceptance Criteria
1. WHEN `dag_builder` 开始执行 THEN 系统 SHALL 在构建请求前读取最新 DAG 快照与该 agent 的能力配置。
2. WHEN 能力配置在运行中被修改 THEN 系统 SHALL 保证下一次 `dag_builder` 执行使用最新配置。
3. IF 能力配置缺失/损坏 THEN 系统 SHALL 使用安全默认值（最小权限：仅基础 tools；MCP 默认关闭），并记录可诊断日志。

## Non-Functional Requirements

### Code Architecture and Modularity
- **Single Responsibility Principle**: UI 的能力管理面板与后端能力配置存储/路由应拆分清晰，避免将复杂逻辑堆进 `SraWorkbenchApp.tsx`。
- **Modular Design**: 能力管理 UI 组件应可复用（未来扩展到其它子系统/宿主）。
- **Dependency Management**: 新增依赖必须走 `Directory.Packages.props`（C#）与既有前端包管理流程（避免引入重型依赖）。
- **Clear Interfaces**: 前后端通过明确 API 合约交互（GET/PUT/POST），并对错误、进度与只读模式有统一语义。

### Performance
- UI 打开能力面板的首屏加载应在 300ms 内完成（本地/缓存命中场景）。
- 大量 tools/skills 时，列表渲染应保持流畅（避免卡顿/冻结）。

### Security
- 不在 UI 明文暴露 secrets；敏感字段通过现有 secrets/host 注入（或仅显示“已配置”状态）。
- 所有写入操作必须验证 `sessionId/agentId/dagId` 等输入，防止路径穿越与越权。

### Reliability
- 配置更新应具备失败回滚与错误可见性。
- 多窗口/多用户同时编辑时，至少保证最后写入可见，并提供版本冲突提示（MVP 可先“last-write-wins + version”）。

### Usability
- 能力管理入口在 Agents 卡片中可直达，不增加学习成本。
- 面板内提供清晰的状态（connected/running/error）、搜索、以及“恢复默认配置”的操作。


