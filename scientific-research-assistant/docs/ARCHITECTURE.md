## 架构总览（Scientific Research Assistant）

### 模块边界

- **`ui/`（共享 UI Core）**：React + TypeScript 的 **单一 UI 来源**（host-agnostic），包含：
  - `SraWorkbenchApp`：完整 Workbench UI（chat + panels + files/dag 视图）
  - `useWorkbenchController`：共享 controller（transport 驱动，Web/Obsidian 行为一致）
  - `SraTransport`：通信抽象（HTTP + SSE + uploads + capability gating）
- **`frontend/`（Web host）**：React + Vite + Tailwind 的宿主壳。
  - 通过 `WebTransport` 注入 `SraTransport`，渲染共享 `ui/` 的 `SraWorkbenchApp`
  - 旧的 `frontend/src/app/*` / `frontend/src/panels/*` 多数已变为 re-export（避免重复与漂移）
- **`obsidian-plugin/`（Obsidian host）**：Obsidian Desktop 插件宿主壳（React view + Tailwind scoped CSS）。
  - 通过 `ObsidianTransport` 注入 `SraTransport`，渲染共享 `ui/` 的 `SraWorkbenchApp`
  - 通过 capability gating（loopback-only）安全降级本地敏感能力（files/secrets/key reveal 等）
- **`src/ScientificResearchAssistant.Api/`**：ASP.NET Core API（**AG-UI**），提供 session API + `/agui/events` SSE（快照优先）。
- **`src/ScientificResearchAssistant/`**：`ResearchAgent`（基于 `AIGAgentBase`），支持：
  - 连接 Claude Scientific Skills 的 MCP 工具（可选）
  - 加载本地 Agent Skills（SKILL.md + scripts/references/assets），并通过 skills_* 工具完成“发现→加载→执行”闭环
- **`src/ScientificResearchAssistant.Contracts/`**：**Protobuf 合约**（mailbox / facts / paper patch），所有跨 agent 边界的文件消息都以此为 schema。
- **`src/ScientificResearchAssistant.Api/Workspace|Facts|Paper/`**：文件协作基础设施（workspace 目录、facts 生命周期、Markdown 稿件）。
- **`src/ScientificResearchAssistant.Api/Infrastructure/SkillPacksSync*.cs`**：启动时 best-effort 同步 GitHub skills repos（clone/pull），为本地 Agent Skills 提供可更新的 skill packs（支持多个 repo）。
- **`src/ScientificResearchAssistant/Vibe/*`**：vibe researching 多智能体角色：
  - `VibePlannerAgent`：生成研究计划（假设/未知/验证路径）
  - `VibeReasonerAgent`：基于 materials（sources）推理（可选 `python_exec` 验证）
- **`sources/`**：来源资料（可引用，不要求写进去就为真）
- **`facts/`**：已验证结论（可当作事实依赖）
- **`workspace/`（运行时目录）**：会话级协作工作区（paper、facts_proposed、decisions、mailbox、runs、artifacts）

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
   - vibe 模式会额外发送 `STATE_SNAPSHOT`（workspace：facts/sources 计数与预览等）
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

### 双宿主（Web / Obsidian）与 Transport 抽象

核心原则：**UI 与宿主解耦**，所有跨边界 IO 通过 `SraTransport` 完成。

- **WebTransport（浏览器）**：
  - HTTP：`fetch`
  - SSE：`EventSource`
  - baseUrl：通常通过 Vite proxy 指向本地 sidecar（默认 `localhost:5678`）
- **ObsidianTransport（桌面插件）**：
  - HTTP：Obsidian `requestUrl`（规避 CORS）
  - SSE：Node-style SSE client（支持断线重连）
  - 能力：通过 loopback 检测启用/禁用本地敏感 API（files/secrets/key reveal）

### Capability gating（本地/远程安全降级）

共享 UI 根据 `transport.capabilities` 进行功能开关，避免在 remote baseUrl 场景误导用户触发 localhost-only API：

- `filesApi`：控制 Files 相关 UI
- `secretsEnabled` / `revealApiKey`：控制 secrets/LLM provider 配置与 key reveal

### Vibe Researching（facts + sources → 多智能体推论）

vibe 模式的最小闭环（MVP）：

- `vibe.materials`：读取 `facts/` + `sources/` 构建 bounded context（facts 优先，其次 sources relevance-ranked）
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


