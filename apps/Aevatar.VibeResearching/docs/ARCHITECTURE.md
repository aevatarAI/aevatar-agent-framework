## 架构总览（Vibe Researching）

### 模块边界

- **`sisyphus-frontend/`（Web 前端）**：Sisyphus 研究编排 UI（React + Vite + Tailwind）。
  - 通过 Vite proxy 将 `/api` 指向本地后端
  - 基于 AG-UI SSE 展示 DAG、workers 与交互流
- **`src/VibeResearching.Api/`**：ASP.NET Core API（**AG-UI**），提供 session API + `/agui/events` SSE（快照优先）。
- **`src/VibeResearching/`**：`ResearchAgent`（基于 `AIGAgentBase`），支持：
  - 连接 Claude Scientific Skills 的 MCP 工具（可选）
  - 加载本地 Agent Skills（SKILL.md + scripts/references/assets），并通过 skills_* 工具完成“发现→加载→执行”闭环
- **`src/VibeResearching.Contracts/`**：**Protobuf 合约**（mailbox / fact proposals / DAG / paper patch），所有跨 agent 边界的文件消息都以此为 schema。
- **`src/VibeResearching.Api/Workspace|Facts|Paper|Dag/`**：文件协作基础设施（workspace 目录、fact lifecycle、DAG 快照镜像、Markdown 稿件）。
- **`src/VibeResearching.Api/Infrastructure/SkillPacksSync*.cs`**：启动时 best-effort 同步 GitHub skills repos（clone/pull），为本地 Agent Skills 提供可更新的 skill packs（支持多个 repo）。
- **`src/VibeResearching.Api/Infrastructure/SkillsMp*.cs`**：SkillsMP 市场集成（loopback-only）：
  - 搜索 skills（`/api/skillsmp/search` / `/api/skillsmp/ai-search`）
  - 将 repo 追加到 `skillpacks.json` 并触发同步（`/api/skillsmp/install`）
- **`src/VibeResearching/Vibe/*`**：vibe researching 多智能体角色：
  - `VibePlannerAgent`：生成研究计划（假设/未知/验证路径）
  - `VibeReasonerAgent`：基于 materials（DAG facts）推理（可选 `python_exec` 验证）
- **`workspace/`（运行时目录）**：会话级协作工作区（paper、facts_proposed、decisions、mailbox、runs、artifacts）
- **`workspace/dags/`**：DAG 快照镜像（artifacts/dag/*）

### 事件流（用户输入 → Agent 执行 → AG-UI SSE）

1. **前端创建会话**：`POST /api/sessions` → 得到 `sessionId`
2. **前端订阅 AG-UI**：`GET /api/sessions/{id}/agui/events`
   - 先发送 `MESSAGES_SNAPSHOT`（快照优先）
   - 再进入 live stream（RUN/STEP/TEXT/CUSTOM）
3. **前端提交输入**：`POST /api/sessions/{id}/input`
   - body: `{ "message": "...", "mode": "chat" | "vibe" }`（默认 `chat`）
4. **后端触发 run（fire-and-forget）**：
   - 发送 `RUN_STARTED`
   - 发送 `STEP_STARTED(chat)` 或 `STEP_STARTED(vibe.*)`
   - 发送 `TEXT_MESSAGE_*`（user + assistant streaming）
  - vibe 模式会额外发送 `STATE_SNAPSHOT`（workspace：DAG facts 计数与预览等）
   - tool calling 期间发送 `CUSTOM`（tool_start/tool_end）
   - 结束时发送 `STEP_FINISHED(chat)` → `RUN_FINISHED`（失败则 `RUN_ERROR`）

### AG-UI 重连策略（快照优先）

- **不依赖 replay**：SSE 重连时不重放 token/tool spam。
- **连接即快照**：
  - `MESSAGES_SNAPSHOT` 来自后端 `ResearchSession` 的 **server-side message log**（兼容 multi-agent）。
  - `STATE_SNAPSHOT`（workspace）用于展示 materials / 运行阶段等可视化状态。
  - 额外发送 `CUSTOM`：
    - `aevatar.scientific.session`：会话元信息
    - `aevatar.scientific.tools_snapshot`：工具列表（含 MCP 标记）

### Vibe Researching（DAG facts → 多智能体推论）

vibe 模式的最小闭环（MVP）：

- `vibe.materials`：从 DAG knowledge nodes 构建 bounded context
- `vibe.plan`：`VibePlannerAgent` 输出研究计划（可执行步骤）
- `vibe.reason`：`VibeReasonerAgent` 进行推理与引用；如启用 Python，可用 `python_exec` 做计算验证

### Paper Collaboration（File-SSoT）

目标：让多个 agents 协作写论文，但 **文件是唯一真相**，且 agents 之间只通过文件沟通。

核心目录（每个 session）：

- `workspace/sessions/{sessionId}/paper/`：`outline.md`、`draft.md`
- `workspace/sessions/{sessionId}/facts_proposed/`：候选事实（未通过共识/验证前不可当作前提依赖）
- `workspace/sessions/{sessionId}/decisions/`：votes/verifications/final（promote 的依据）
- `workspace/sessions/{sessionId}/mailbox/`：文件邮箱（in → processing → archive，失败进 _dead）

写作策略：

- 非写者 agents 只提交 patch proposal（mailbox → `paper_editor`）
- 单写者合并 patch 并原子写入 `paper/*`，同时写入 `runs/{runId}/` 作为审计日志

### Tool 调用可视化（MCP skills）

核心思路：**不改 tool 本身**，只在边界层做“协议投影”。

- `ResearchAgent` 使用 `ResearchToolManager` 包装工具执行：
  - 在 `ExecuteToolAsync` 前后通过 `AsyncLocal` sink 发出 tool_start/tool_end
- API 在执行一次 run 时把 `ResearchStreamEventContext.Current` 绑定为 `AgUiResearchStreamEventSink`：
  - 输出为 `CUSTOM` 事件：
    - `aevatar.scientific.tool_start`
    - `aevatar.scientific.tool_end`
  - 并标注 `isMcp`，让 UI 可以明确区分 Claude Scientific Skills 的调用

### 架构决策

- 前端统一为 `sisyphus-frontend/`，避免双实现漂移，减少维护面。
- 移除旧的 `frontend/`、`frontend_old/`、`ui/`、`obsidian-plugin/` 以降低构建噪音与依赖复杂度。
- Sessions API 拆分为 `partial class`，落在 `Sessions/Api/`，避免单文件过大并保持边界层职责清晰。

### 变更记录

- 2026-01-15：统一前端为 `sisyphus-frontend/`，清理旧前端与 Obsidian host。
- 2026-01-15：移除 `facts/` 与 `sources/` 目录，统一以 DAG knowledge nodes 作为事实来源。
- 2026-01-15：`ResearchSessionsApi` 拆分为 partial class，并新增 `Sessions/Api/` 子目录。


