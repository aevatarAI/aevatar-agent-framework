# Requirements Document

## Introduction

本规格定义一套 **原生（非 MCP）LSP（Language Server Protocol）工具链**，让 Aevatar 的 AI Agent 获得“结构化代码理解/重构”的能力（definition/references/diagnostics/rename/code actions 等），并以 **新项目（new project）** 的形式交付，保持与现有 `Aevatar.Agents.AI.Core` 的边界清晰、可选依赖、可控启用。

范围聚焦在“把 LSP 作为可配置的语言服务器后端池，并以 Tool 形式提供给 agent 调用”，而不是做 IDE 产品本身。

目标（MVP）：
- 提供 **可配置** 的 LSP server 池（按扩展名/优先级选择，优先 `--stdio`）。
- 提供 **只读语义查询工具**（definition/references/hover/diagnostics）。
- 提供 **重构预览工具**（rename/code actions → workspace edit 预览）。
- 提供 **受控的应用编辑工具**（apply workspace edit，危险操作，必须可被策略一票否决）。
- 通过现有 Hook/Harness 与 Tool Policy，实现输出治理、权限收敛与可观测性。

非目标（本规格不做）：
- 不实现 UI/IDE 插件（VSCode/JetBrains 等）。
- 不内置/下载/打包各语言的 LSP server（由宿主环境提供安装与命令）。
- 不引入网络端口作为默认通讯方式（避免端口管理与安全问题；仓库也禁止 `:5000`）。
- 不承诺在没有语言服务器可执行文件的环境中提供语义能力（必须 graceful degrade）。

## Alignment with Product Vision

该能力与 Aevatar 的框架方向一致：
- **Runtime agnostic**：LSP 作为工具链能力，不绑定 Local/Orleans/ProtoActor 任一运行时；在不同运行时中均可“可选启用”，不可用时必须可降级。
- **Protobuf-first contracts**：若新增任何跨边界（Stream/存储/网络）的 State/Event/Config 契约，必须以 `.proto` 定义并生成代码（遵循 `AGENTS.md` 铁律）。
- **默认安全**：LSP 工具不得绕过 `AllowInternalTools/AllowDangerousTools`、tool allowlist 与 hook policy；重构写操作必须显式启用。
- **可观测/可测试**：工具执行必须可定位（日志/耗时/错误原因），并且可用单测验证协议/编辑应用逻辑。

## Requirements

### Requirement 1 — 新项目交付：LSP 能力以独立 project 形式提供

**User Story:** 作为框架维护者，我希望 LSP 能力以独立 project 提供，以便保持 AI.Core 体积可控、依赖边界清晰，并允许宿主按需引用与启用。

#### Acceptance Criteria

1. WHEN 交付该能力 THEN 仓库 SHALL 新增一个位于 `src/` 下的 C# project（命名遵循 `Aevatar.Agents.AI.<Module>` 约定，例如 `Aevatar.Agents.AI.Lsp`）。
2. IF 新 project 引入新增依赖 THEN 依赖版本 SHALL 统一声明在 `Directory.Packages.props`（不得在 `*.csproj` 内散落版本号）。
3. WHEN LSP project 被移除/未引用 THEN `Aevatar.Agents.AI.Core` SHALL 不受影响（可选依赖，不强绑定）。

---

### Requirement 2 — LSP Server 配置：可配置的后端池（extensions + priority + disabled）

**User Story:** 作为部署者/平台团队，我希望可以通过配置声明多语言 LSP server 的启动命令与适用文件扩展名，并能禁用/调整优先级，以适配不同语言、不同运行环境与不同安全策略。

#### Acceptance Criteria

1. WHEN 宿主提供 LSP 配置 THEN 系统 SHALL 支持声明多个 server（每个 server 包含 `command[]`、`extensions[]`、`priority`、`disabled` 等字段）。
2. WHEN 某文件扩展名匹配多个 server THEN 系统 SHALL 选择 `priority` 更高（数值更小优先）的 server；相同优先级时 SHALL 有确定性 tie-break（例如按 name 排序）。
3. IF server 标记为 `disabled=true` THEN 系统 SHALL 不启动/不选择该 server。
4. WHEN 启动语言服务器 THEN 系统 SHALL 优先使用 stdio 模式（例如 `--stdio`），并 SHALL 不使用固定端口；仓库示例/默认配置 SHALL 不出现 `:5000`。

---

### Requirement 3 — 会话与进程管理：池化、重启、并发与超时（best-effort）

**User Story:** 作为运维/开发者，我希望 LSP server 能够被复用与自动恢复，以获得可控的性能与稳定性，并避免长任务中频繁冷启动造成延迟与失败。

#### Acceptance Criteria

1. WHEN 第一次调用某个 LSP server THEN 系统 SHALL 启动对应进程并完成 LSP `initialize` 握手。
2. WHEN 后续调用同一 server 且进程健康 THEN 系统 SHALL 复用既有会话（避免重复启动）。
3. IF server 进程崩溃/退出/协议异常 THEN 系统 SHALL 返回结构化失败结果，并在后续调用中 best-effort 尝试重启（有上限，避免无限重试）。
4. WHEN tool 调用带 cancellation/timeout THEN 系统 SHALL 在超时后取消等待并返回失败（不得无限挂起）。
5. IF 并发调用同一 server THEN 系统 SHALL 以有界方式串行化关键协议阶段（例如初始化/文档同步），并避免无界队列增长。

---

### Requirement 4 — 只读语义查询工具（不改文件）

**User Story:** 作为 coding-agent/AI 工程师，我希望 agent 能以工具调用形式获取符号定义、引用、悬停类型信息与诊断信息，从而进行可信的“结构化代码理解”。

#### Acceptance Criteria

1. WHEN agent 调用只读 LSP 工具（如 definition/references/hover/diagnostics）THEN 系统 SHALL 通过已选择的 LSP server 返回结构化结果（JSON）。
2. IF 请求的文件不在 workspace 或路径非法 THEN 系统 SHALL 返回明确错误（不得访问 workspace 外部文件）。
3. IF 没有匹配的 LSP server 或 server 不可用 THEN 系统 SHALL 返回明确错误，并提供可操作的 remediation 提示（例如缺少配置/缺少可执行文件）。
4. WHEN 结果过大（例如 references 数量极多）THEN 系统 SHALL 进行有界返回（limit + 截断标记 + 总量信息），避免 tool 输出撑爆上下文。

---

### Requirement 5 — 重构预览工具：rename / code actions 生成 WorkspaceEdit（不直接落盘）

**User Story:** 作为用户/审阅者，我希望 agent 先给出可审阅的重构计划（workspace edit 预览），再决定是否应用，以降低自动改代码带来的风险。

#### Acceptance Criteria

1. WHEN 调用 `rename` 或 `code actions` 预览工具 THEN 系统 SHALL 返回一个 workspace edit（跨文件的文本编辑集合），且 SHALL 不对文件系统产生任何修改。
2. WHEN 预览结果返回 THEN 系统 SHALL 提供至少：受影响文件数量、编辑数量、以及可追踪的截断标记（若被限幅）。
3. IF 语言服务器不支持某请求（capability 缺失）THEN 系统 SHALL 返回结构化失败结果（包含 capability 缺失原因），并建议降级路径。

---

### Requirement 6 — 应用编辑工具：受策略约束的 WorkspaceEdit 落盘（危险操作）

**User Story:** 作为用户，我希望在明确允许的情况下，让 agent 将已预览/已审批的 workspace edit 应用到代码库中，并确保失败时不会把仓库改成半残状态。

#### Acceptance Criteria

1. WHEN 调用“应用 workspace edit”工具 THEN 系统 SHALL 视为危险操作，并 SHALL 被 `AllowDangerousTools` / tool allowlist / hooks policy 之一票否决。
2. IF `AllowDangerousTools=false` THEN 系统 SHALL 拒绝执行并返回明确原因（不得落盘）。
3. WHEN 应用 edits THEN 系统 SHALL 以 best-effort 的原子性策略执行（例如：预检查范围与版本、失败时回滚已写入部分，或保证“要么全部成功，要么全部失败”）。
4. WHEN 应用完成 THEN 系统 SHALL 返回结构化结果（成功/失败、写入文件列表、写入字节/行数统计、错误原因）。

---

### Requirement 7 — 安全与治理：输出限幅、路径边界、命令边界、可观测性

**User Story:** 作为安全审阅者/平台团队，我希望 LSP 工具链默认安全且可审计：不会泄漏敏感信息、不会越权访问文件、不会让输出失控。

#### Acceptance Criteria

1. WHEN 任何 LSP 工具执行 THEN 系统 SHALL 遵守 workspace 边界（禁止访问 workspace 外部路径）。
2. WHEN 启动 LSP server 进程 THEN 系统 SHALL 只允许执行来自配置的命令（不得让模型动态拼接任意命令），并对不可执行/不存在命令返回清晰错误。
3. WHEN tool 输出超出预算 THEN 系统 SHALL 截断并标注（与现有 `ToolOutputTruncationHook` 对齐），且不得把 secrets 写入日志/metadata。
4. WHEN 工具执行（成功或失败）THEN 系统 SHALL 记录可定位日志（至少包含 tool 名、server 名、耗时、request id），并保持 best-effort（观测失败不阻塞主链路）。

---

### Requirement 8 — 测试与兼容：可在 CI 中验证，无需外部语言服务器依赖

**User Story:** 作为维护者，我希望该能力能在 CI 中稳定测试，不依赖机器上预装的 tsserver/csharp-ls 等外部二进制，从而避免“环境问题导致测试脆弱”。

#### Acceptance Criteria

1. WHEN 运行单元测试 THEN 系统 SHALL 使用内置的 fake/mock LSP server（或最小可执行测试 server）来验证 JSON-RPC/LSP 往返、超时、重启与错误处理。
2. IF 没有真实 LSP server 可执行文件 THEN 测试 SHALL 仍然通过（不以外部安装为前提）。
3. WHEN 在不同运行时（Local/Orleans/ProtoActor）中启用该工具 THEN 系统 SHALL 至少做到：不可用时 graceful degrade（返回结构化失败，不影响 agent 主链路）。

## Non-Functional Requirements

### Code Architecture and Modularity
- **Single Responsibility Principle**: LSP 协议层、进程/会话管理层、Tool 适配层、配置绑定层分文件/分目录组织，避免单文件膨胀（单文件 < 800 行）。
- **Modular Design**: LSP project 不反向侵入 Core；以清晰 API 暴露给 AI.Core/宿主层调用与注册。
- **Dependency Management**: 新增依赖必须走 Central Package Management（`Directory.Packages.props`）。
- **Clear Interfaces**: 如需新增跨边界事件/状态/配置契约，必须使用 Protobuf（`.proto`）并遵循兼容性规则。

### Performance
- LSP 请求必须有界（超时/并发上限/结果 limit），避免无界队列与超大输出。
- 默认复用 LSP server 会话，避免冷启动成为瓶颈。

### Security
- 默认不启用写操作工具；写操作必须显式开启且可被策略拒绝。
- 命令执行边界固定为配置提供的命令；模型不得注入任意命令。
- 仓库内任何示例/默认配置不得使用 `:5000` 端口。

### Reliability
- 进程崩溃/协议异常必须 best-effort 自愈（重启有上限）并可观测。
- 失败必须返回结构化错误，且不得拖垮 tool loop/LLM 主链路。

### Usability
- 提供清晰的启用/禁用与配置方式（多 server、extensions、priority、disabled）。
- 工具返回结构化 JSON，易于 agent 消化与二次调用（预览 → 应用的两阶段流程）。


