# Requirements Document

## Introduction

本 spec 将 `platform/docs/PRD.md` 的产品需求收敛为可实施、可验收的“Platform（对标 OpenCode 的 CLI/TUI 体验的多智能体工作台）”需求文档。

Platform 的目标是一套 **CLI/TUI 交互式 Agent OS / Workbench**：
- **基础 UX 标准：对标 OpenCode 的 CLI/TUI 体验**：对话式交互、工具调用、文件/命令/Git/LSP/搜索、流式输出、会话管理（并支持脚本化）。
- **差异化增强：多 Agent + DSL 编排**：用 Cognitive Mesh DSL 声明式定义 workflow（拓扑/数据流/约束），用 role YAML 定义每个角色的模型/提示词/tools/skills。
- **不局限编程**：在同一套 CLI/TUI 底座上，通过 profile/pack 的组合把编程助手扩展到 vibe researching、小说世界观设定等多领域助手。
- **本地优先**：所有敏感配置与用户数据默认只落在本地 `~/.aevatar/`（含 secrets）。

## Alignment with Product Vision

与 steering 文档一致：
- **Events are Truth**：核心交互（用户输入、Agent 输出、工具执行、workflow 状态变化、会话持久化）以事件模型组织，便于回放与诊断。
- **Boundary Types Must Be Protobuf**：跨边界（stream / 存储 / 运行时）类型必须由 Protobuf 定义，避免运行时序列化不确定性。
- **Runtime agnostic by design**：业务 Agent 逻辑与运行时解耦；MVP 以 Local 跑通，后续可迁移到 ProtoActor/Orleans。
- **安全默认值**：危险能力（shell/写文件/外部网络/MCP）应可配置、可审计、默认收敛。

## Requirements

### Requirement 1 — CLI/TUI 必须 1:1 对标 OpenCode（命令/flags/交互语法）

**User Story:** 作为开发者，我希望 `aevatar` 的 CLI/TUI 在行为与接口上完全对标 OpenCode，以便零学习成本迁移使用习惯，并支撑脚本化/自动化集成。

#### Acceptance Criteria

#### 1.1 默认行为（tui）

1. WHEN 用户执行 `aevatar` 且不带任何参数 THEN 系统 SHALL 默认启动 TUI（对标 `opencode` 默认进入 TUI）并进入可交互状态（见 [OpenCode CLI](https://opencode.ai/docs/cli/)）。
2. WHEN 用户执行 `aevatar tui [project]` THEN 系统 SHALL 在指定 project（或当前目录）启动 TUI（对标 `opencode [project]`）。

#### 1.2 `tui` flags（对标）

1. WHEN 用户以 `aevatar tui` 启动 THEN 系统 SHALL 支持以下 flags，并与 OpenCode 语义一致（见 [OpenCode CLI](https://opencode.ai/docs/cli/)）：
   - `--continue` / `-c`：继续上次会话
   - `--session` / `-s`：继续指定会话
   - `--prompt`：指定 prompt
   - `--model` / `-m`：以 `provider/model` 形式指定模型
   - `--agent`：指定 agent
   - `--port`、`--hostname`：监听地址（用于本地 server/web/attach 形态；默认不得绑定 `:5000`）

#### 1.3 CLI commands（对标）

1. WHEN 用户执行 `aevatar run [message..]` THEN 系统 SHALL 以非交互模式运行（对标 `opencode run`），并支持以下 flags（见 [OpenCode CLI](https://opencode.ai/docs/cli/)）：
   - `--command`（command 名称，message 作为 args）
   - `--continue` / `-c`
   - `--session` / `-s`
   - `--share`
   - `--model` / `-m`（`provider/model`）
   - `--agent`
   - `--file` / `-f`（附加文件）
   - `--format`（default/json）
   - `--title`
   - `--attach`（附加到运行中的 server）
   - `--port`（本地 server 端口；默认随机/可配置；不得默认使用 `:5000`）
2. WHEN 用户执行 `aevatar serve` THEN 系统 SHALL 启动 headless server（对标 `opencode serve`），并支持 `--port/--hostname/--cors/--mdns` 等关键 flags（语义对标，细节在 design 明确；见 [OpenCode CLI](https://opencode.ai/docs/cli/)）。
3. WHEN 用户执行 `aevatar web` THEN 系统 SHALL 启动带 Web UI 的 server（对标 `opencode web`），并支持 `--port/--hostname/--cors/--mdns` 等关键 flags（见 [OpenCode CLI](https://opencode.ai/docs/cli/)）。
4. WHEN 用户执行 `aevatar attach [url]` THEN 系统 SHALL 将 TUI 附加到已运行的 server（对标 `opencode attach`），并支持 `--dir` 与 `--session/-s` flags（见 [OpenCode CLI](https://opencode.ai/docs/cli/)）。
5. WHEN 用户执行 `aevatar agent ...` THEN 系统 SHALL 提供 agent 管理命令（至少 `create` 与 `list`，语义对标；见 [OpenCode CLI](https://opencode.ai/docs/cli/)）。
6. WHEN 用户执行 `aevatar auth ...` THEN 系统 SHALL 提供 provider 凭证管理命令（至少 `login/list/logout`，语义对标；见 [OpenCode CLI](https://opencode.ai/docs/cli/)）。
7. WHEN 用户执行 `aevatar mcp ...` THEN 系统 SHALL 提供 MCP server 管理命令（至少 `add/list/auth/logout/debug`，语义对标；见 [OpenCode CLI](https://opencode.ai/docs/cli/)）。
8. WHEN 用户执行 `aevatar models [provider]` THEN 系统 SHALL 列出可用模型并支持 `--refresh` 与 `--verbose`（语义对标；见 [OpenCode CLI](https://opencode.ai/docs/cli/)）。
9. WHEN 用户执行 `aevatar session ...` THEN 系统 SHALL 提供会话管理（至少 `list`，语义对标；见 [OpenCode CLI](https://opencode.ai/docs/cli/)）。
10. WHEN 用户执行 `aevatar stats` THEN 系统 SHALL 输出 token/cost 统计并支持 `--days/--tools/--models/--project` 等过滤（语义对标；见 [OpenCode CLI](https://opencode.ai/docs/cli/)）。
11. WHEN 用户执行 `aevatar export [sessionId]` THEN 系统 SHALL 导出会话为 JSON（语义对标；见 [OpenCode CLI](https://opencode.ai/docs/cli/)）。
12. WHEN 用户执行 `aevatar import <file-or-url>` THEN 系统 SHALL 从本地文件或 share URL 导入会话（语义对标；见 [OpenCode CLI](https://opencode.ai/docs/cli/)）。
13. WHEN 用户执行 `aevatar acp` THEN 系统 SHALL 提供 ACP server（stdin/stdout nd-JSON）并支持 `--cwd/--port/--hostname`（语义对标；见 [OpenCode CLI](https://opencode.ai/docs/cli/)）。
14. WHEN 用户执行 `aevatar uninstall` THEN 系统 SHALL 支持卸载并提供 `--keep-config/-c`、`--keep-data/-d`、`--dry-run`、`--force/-f`（语义对标；见 [OpenCode CLI](https://opencode.ai/docs/cli/)）。
15. WHEN 用户执行 `aevatar upgrade [target]` THEN 系统 SHALL 支持升级到最新或指定版本并提供 `--method/-m`（语义对标；见 [OpenCode CLI](https://opencode.ai/docs/cli/)）。

#### 1.4 Global flags & env vars（对标）

1. WHEN 用户执行任意命令 THEN 系统 SHALL 支持全局 flags `--help/-h`、`--version/-v`、`--print-logs`、`--log-level`（语义对标；见 [OpenCode CLI](https://opencode.ai/docs/cli/)）。
2. WHEN 用户通过环境变量配置 THEN 系统 SHALL 提供等价能力来覆盖 config 路径/目录、禁用自动更新/裁剪、server basic auth、实验特性等（语义对标；详项在 design 中列清单，并参考 [OpenCode CLI](https://opencode.ai/docs/cli/) 的 env vars 部分）。

#### 1.5 TUI 交互语法（对标）

1. WHEN 用户在 TUI 输入消息中使用 `@` 引用文件 THEN 系统 SHALL 提供模糊文件搜索与附加能力（语义对标；见 [OpenCode TUI](https://opencode.ai/docs/tui/)）。
2. WHEN 用户在 TUI 输入以 `!` 开头的内容 THEN 系统 SHALL 以 shell 命令执行并将输出纳入会话上下文（语义对标；见 [OpenCode TUI](https://opencode.ai/docs/tui/)）。
3. WHEN 用户在 TUI 输入以 `/` 开头的命令 THEN 系统 SHALL 提供 TUI 内快捷命令入口（至少包含帮助、会话切换、新会话、主题、外部编辑器等；具体命令集以 [OpenCode TUI](https://opencode.ai/docs/tui/) 为准）。
4. WHEN 用户执行 `/editor`（或等价命令）THEN 系统 SHALL 调起外部编辑器编辑输入内容，并使用 `EDITOR` 环境变量选择编辑器（语义对标；见 [OpenCode TUI](https://opencode.ai/docs/tui/)）。
5. WHEN 用户使用 TUI 快捷键（例如存在前导键，如 `ctrl+x`）THEN 系统 SHALL 提供同等能力与一致的可发现性（快捷键列表/帮助面板；见 [OpenCode TUI](https://opencode.ai/docs/tui/)）。

---

### Requirement 2 — `~/.aevatar/` 配置体系（主配置 + secrets + role agents + workflows + mcp）

**User Story:** 作为开发者，我希望 Platform 以 `~/.aevatar/` 为配置与数据根目录，以便本地优先、可迁移、可审计且不泄露敏感信息。

#### Acceptance Criteria

1. WHEN Platform 启动 THEN 系统 SHALL 读取 `~/.aevatar/config.yaml` 作为主配置（允许不存在：不存在时使用安全默认值）。
2. WHEN Platform 启动 THEN 系统 SHALL 读取 `~/.aevatar/secrets.yaml` 作为敏感信息来源；IF secrets 不存在 THEN 系统 SHALL 仍可运行但相应 provider/MCP 必须不可用并给出可理解提示。
3. WHEN Platform 启动 THEN 系统 SHALL 扫描 `~/.aevatar/agents/*.yaml` 并发现 role 配置（role 作为工作流节点类型的配置来源）。
4. WHEN 用户使用自定义 workflow THEN 系统 SHALL 从 `~/.aevatar/workflows/`（或显式路径）读取 DSL 文件。
5. WHEN 用户启用 MCP THEN 系统 SHALL 从 `~/.aevatar/mcp/servers.yaml`（或等价配置入口）加载 MCP 服务器配置。

---

### Requirement 3 — Role YAML 驱动的 Agent 配置应用（与 workflow 解耦）

**User Story:** 作为开发者，我希望 workflow 只描述拓扑/数据流/约束，而每个 role 的 model/tools/skills/prompt 由 role YAML 管理，以便复用与可组合。

#### Acceptance Criteria

1. WHEN workflow 节点 `node.type = <role>` THEN 系统 SHALL 以 `<role>` 在 agent registry 中查找 `~/.aevatar/agents/<role>.yaml` 并应用到该节点对应的 Agent 实例（provider/model/system_prompt/tools/skills 等）。
2. IF 找不到 `<role>.yaml` THEN 系统 SHALL 使用应用/框架默认配置（best-effort），并在日志/输出中提示该 role 未配置。
3. WHEN role YAML 中指定 tools allowlist THEN 系统 SHALL 将其作为 baseline allowlist 参与 LLM request 与 tool execution 的双重约束（defense in depth）。
4. WHEN role YAML 中 `skills` 非空 THEN 系统 SHALL 启用 `~/.aevatar/skills` 并允许 skills 相关工具（具体工具集在 design 明确），且必须受 allowlist/安全策略约束。

---

### Requirement 4 — Cognitive Mesh DSL：workflow 声明式编排（MVP）

**User Story:** 作为开发者，我希望用 Cognitive Mesh DSL（JSON）声明式定义多 Agent 的工作流拓扑与约束，以便可视化、可复用、可扩展。

#### Acceptance Criteria

1. WHEN 用户选择一个 workflow DSL THEN 系统 SHALL 解析 DSL 并验证 schema（至少：dsl_version、nodes、edges、constraints 的基本合法性）。
2. WHEN DSL 中定义节点与边 THEN 系统 SHALL 生成可执行计划（execution plan），并以事件/日志可观测地展示执行进度。
3. IF DSL 校验失败 THEN 系统 SHALL 给出可定位错误（包含失败字段路径/原因），且不启动执行。
4. WHEN DSL 指定 `strategy` 与 `constraints`（例如 max_iterations / token_limit）THEN 系统 SHALL 在执行时强制执行这些约束，且不得出现无限循环。

---

### Requirement 5 — 多 Agent 协作执行（Router / Coder / Reviewer / Tester / Debug / Search / Docs / Judge）

**User Story:** 作为开发者，我希望任务能被路由并由多个专业化 Agent 协作完成，以便获得更可靠的实现-审查-测试闭环。

#### Acceptance Criteria

1. WHEN workflow 包含 `RouterAgent` THEN 系统 SHALL 根据 routing rules 将任务路由到对应 role 的 Agent。
2. WHEN workflow 包含 `Coder → Reviewer` THEN 系统 SHALL 支持基本的代码审查回路（Reviewer 给出问题 → Coder 修订 → 可在约束内重复）。
3. WHEN workflow 包含 `TesterAgent` THEN 系统 SHALL 支持测试编写与运行（具体执行策略在 design 中定义），并将测试结果反馈给 Coder/Reviewer。
4. WHEN workflow 包含 `JudgeAgent` THEN 系统 SHALL 支持决策/投票阈值（如 voting_threshold）并对“是否继续迭代/是否接受变更”给出可追溯结果。

---

### Requirement 6 — Tool Calling：文件操作 / Bash / Git / 搜索 / LSP / MCP（对标 OpenCode）

**User Story:** 作为开发者，我希望 Agent 能安全地调用工具完成真实工程操作，以便达到 OpenCode 级别的“能动手”的助手体验。

#### Acceptance Criteria

1. WHEN Agent 需要读取/写入/删除文件 THEN 系统 SHALL 提供文件系统相关工具，并对可访问路径进行限制（白名单/根目录约束由配置决定）。
2. WHEN Agent 需要执行命令 THEN 系统 SHALL 提供 shell 工具，且必须支持命令白名单与超时（例如 allowed_commands/timeout_seconds）。
3. WHEN Agent 需要 Git 操作 THEN 系统 SHALL 提供常用 Git 工具（status/diff/commit 等），并能输出可读的结果摘要。
4. WHEN Agent 需要代码搜索 THEN 系统 SHALL 提供 grep/（可选）ast-grep 与 glob 能力，并返回有界结果（避免输出爆炸）。
5. WHEN 用户/Agent 启用 MCP THEN 系统 SHALL 支持通过 MCP Bridge 将外部工具接入，并确保 MCP 工具同样受安全策略与 allowlist 约束。
6. IF `AllowDangerousTools=false`（或等价策略）THEN 系统 SHALL 隐藏/拒绝执行危险工具（例如 shell、web_search、脚本执行等，具体集合在 design 明确）。

---

### Requirement 7 — 流式输出与可观测运行（首字节体验）

**User Story:** 作为开发者，我希望 AI 输出可流式呈现，并能看到多 Agent/多步骤的执行进度，以便获得良好的交互体验与可控性。

#### Acceptance Criteria

1. WHEN Agent 生成响应 THEN 系统 SHALL 支持流式输出（首字节延迟目标见 NFR）。
2. WHEN workflow 运行 THEN 系统 SHALL 在 CLI/TUI 中显示当前活动节点、工具调用开始/结束、错误摘要等关键进度信息。
3. IF 任意 Agent/tool 失败 THEN 系统 SHALL 以 best-effort 继续运行其它步骤（除非 constraint/策略要求停止），并输出可定位的失败原因。

---

### Requirement 8 — 会话管理：恢复、历史查看、Event Sourcing 持久化（对标 OpenCode）

**User Story:** 作为开发者，我希望可以恢复上次会话、查看会话历史，并将关键状态可回放地持久化，以便长任务与中断后继续。

#### Acceptance Criteria

1. WHEN 用户执行 `aevatar --resume` THEN 系统 SHALL 恢复最近一次会话（选择规则在 design 明确），并继续在同一上下文中工作。
2. WHEN 用户执行 `aevatar sessions list` THEN 系统 SHALL 列出已存在会话及其摘要信息（id、时间、工作目录、workflow）。
3. WHEN 用户执行 `aevatar sessions show <session-id>` THEN 系统 SHALL 展示该会话的关键事件/摘要（有界输出）。
4. WHEN 会话持久化启用 THEN 系统 SHALL 使用事件溯源（Event Sourcing）记录会话与关键状态变更，并支持重放恢复。

---

### Requirement 9 — Provider 支持：云端与离线（Ollama）

**User Story:** 作为开发者，我希望能在云端 provider 与本地 Ollama 之间切换，以便在不同网络/成本/隐私条件下工作。

#### Acceptance Criteria

1. WHEN 配置中启用 `ollama` provider THEN 系统 SHALL 支持连接本地 Ollama endpoint，并允许选择默认模型。
2. WHEN provider API key 缺失 THEN 系统 SHALL 明确提示并禁止对该 provider 发起请求（不允许静默失败）。
3. WHEN 用户通过 CLI 覆盖模型/provider THEN 系统 SHALL 在本次运行中生效并可观测（输出/日志可确认）。

---

### Requirement 10 — 预置 workflows：standard / code-review / tdd / maker / debug

**User Story:** 作为新用户，我希望开箱即用地选择一组预置 workflow，以便快速上手并在不同任务类型间切换。

#### Acceptance Criteria

1. WHEN 用户执行 `aevatar --workflow standard` THEN 系统 SHALL 以单 Agent 模式运行（最小闭环）。
2. WHEN 用户执行 `aevatar --workflow code-review` THEN 系统 SHALL 运行 Coder → Reviewer 的基本协作流程。
3. WHEN 用户执行 `aevatar --workflow maker` THEN 系统 SHALL 支持多专家共识（至少包含 Router/Experts/Judge 的骨架与阈值约束）。
4. IF 预置 workflow 不存在或不可用 THEN 系统 SHALL 返回可理解错误，并列出可用 workflows。

---

### Requirement 11 — Profiles/Packs：多领域助手的装配与切换（不局限编程）

**User Story:** 作为开发者/创作者/研究者，我希望在同一套 `aevatar` CLI/TUI 上选择不同 profile（coding/vibe/worldbuilding…），从而切换默认 workflow、角色集合与工具策略，以便把 Platform 从“编程助手”扩展为通用多智能体工作台。

#### Acceptance Criteria

1. WHEN 用户在启动时选择 profile（例如 `--profile vibe`）THEN 系统 SHALL 使用该 profile 的默认 workflow 与默认 roles/tools policy（具体映射由 pack 定义）。
2. WHEN 用户未显式选择 profile THEN 系统 SHALL 使用默认 profile（例如 `coding`），且该默认值可由 `~/.aevatar/config.yaml` 覆盖。
3. WHEN profile 为 `vibe` THEN 系统 SHALL 允许该 profile 绑定的角色集合包含非编程向角色（例如 `research_assistant/planner/reasoner/librarian/verifier/...`），并仍通过 role YAML 管理其模型/提示词/tools/skills。
4. WHEN profile 为 `worldbuilding` THEN 系统 SHALL 允许该 profile 绑定的角色集合包含世界观向角色（例如 `worldbuilder/lore_keeper/critic/...`），并仍通过 role YAML 管理其模型/提示词/tools/skills。
5. WHEN 用户安装/提供一个 pack（workflow + role templates + tool/policy presets）THEN 系统 SHALL 能发现并列出可用 packs，并允许将其作为 profile 的来源（pack 的发现机制在 design 中细化）。

## Non-Functional Requirements

### Code Architecture and Modularity
- **Single Responsibility Principle**：CLI、workflow engine（DSL compiler/orchestrator）、tools、安全策略、会话存储拆分清晰，避免单文件/单类膨胀。
- **Modular Design**：Platform 应复用 `src/` 中现有 AI.Core 能力（Tool/Hooks/MCP/Agent YAML），wiring 放在 Platform 层而非反向侵入框架层。
- **Dependency Management**：新增依赖版本必须集中到 `Directory.Packages.props`。
- **Protobuf-first**：任何跨边界（state/event/config/stream）的类型必须由 `.proto` 定义并生成代码。

### Performance
- **Startup time**: WHEN 启动 CLI THEN 系统 SHALL 在 < 1s 内进入可交互状态（不以 provider 连接/MCP 初始化阻塞 REPL）。
- **First-token latency**: WHEN 进行流式输出 THEN 系统 SHALL 目标 < 3s 输出首字节（在可用 provider 条件下）。
- **Memory**: 在基础运行（单会话、少量文件操作）场景，进程内存占用目标 < 200MB（具体预算与测量方法在 design 明确）。

### Security
- **Local-first data**：所有 secrets 必须位于 `~/.aevatar/secrets.yaml`（或等价机制），不得写入日志与会话事件。
- **Tool safety**：危险工具默认关闭且需要显式配置；所有工具输出必须有界（截断/摘要），避免把敏感/大输出灌入上下文。
- **Port policy**：仓库内示例/默认配置 **禁止使用 `:5000`**；如需示例监听端口，优先 `:5678` 且可配置。

### Reliability
- **Best-effort**：非关键附属能力（hooks/MCP/可选 provider）失败不应阻塞主链路；但必须可观测（日志/错误摘要）。
- **Bounded loops**：workflow/agent/tool loop 必须受约束与预算上限，防止无限循环与输出爆炸。

### Usability
- **Time-to-first-task**：新用户从安装到跑通一次 `aevatar -c "<task>"` 的闭环目标 < 5 分钟（前置条件：provider 可用或 Ollama 已启动）。
- **Predictable output**：CLI 输出必须结构化、可读、可复制（尤其是审查结论、测试结果、文件变更摘要）。


