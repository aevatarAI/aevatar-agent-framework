# Requirements Document

## Introduction

本规格定义：在 `examples/` 下新增一个 **专门面向 `ProviderType = "claude_agent_sdk"` 的 Demo 工程**，用“可离线运行 + 可对照”的方式，充分展示它区别于其他 provider（典型：HTTP 直连模型 provider）的一组能力：

- **项目级配置（File-SSoT）**：通过 `projectRoot` 读取 demo 项目的 `.claude/*` 配置并影响行为
- **插件体系**：通过 `plugins` 目录加载插件并影响输出
- **权限收敛**：默认最小权限；只有显式允许的 `allowedTools` 才能执行对应能力（如读文件/写文件）
- **进程隔离**：用外部 runner（Node/Python/…）承载编排与插件生态，保持 .NET 侧依赖树干净
- **流式输出**：通过 marker 协议展示 Aevatar streaming 的增量体验（best-effort）
- **tool-loop 边界**：Demo 必须验证 `claude_agent_sdk` provider 不会触发 Aevatar tool-loop（忽略 Functions、不会返回 `AevatarFunctionCall`）

Demo 的 runner **不依赖网络**，默认使用仓库内提供的“mock runner”（仅用于演示集成协议与行为差异）；同时提供“如何替换为真实 Claude Agent SDK runner”的说明。

目标（MVP）：
- 新增一个可运行的示例工程 `examples/ClaudeAgentSdkProviderDemo/`，一键运行能看到上述差异点。
- Demo 必须离线可跑（不需要真实 LLM 调用），输出可复现。

非目标（本规格不做）：
- 不在仓库内自动安装 Claude Agent SDK / Node 包依赖（运行环境由开发者准备）。
- 不引入新的跨 runtime/agent 边界消息契约（因此不新增 `.proto`）。
- 不提供 Web UI；以 Console demo 为主。

## Alignment with Product Vision

本 Demo 与 Aevatar 的工程方向一致：
- **Pluggable**：用 `LLMProviders` 的 `ProviderType` 选择体现 provider 可插拔架构。
- **Safe by default**：默认最小权限；演示“显式允许才开放能力”的实践。
- **File-SSoT**：通过 `.claude/*` 与插件目录展示“文件即真相源、可审计”的协作与配置方式。
- **Runtime-agnostic**：demo 聚焦 provider 边界与 I/O 协议，不侵入 Orleans/ProtoActor 等运行时。

## Requirements

### Requirement 1 — Demo 工程：可离线运行、输出可复现

**User Story:** 作为框架使用者，我希望有一个可以离线运行的 demo，用来理解 `claude_agent_sdk` provider 的工作方式与价值。

#### Acceptance Criteria

1. WHEN 开发者执行 `dotnet run`（在 demo 目录下）THEN Demo SHALL 在不访问外网、不依赖真实 API Key 的情况下完成运行，并输出可复现结果。
2. IF 机器未安装 Node/Python（runner 不可用）THEN Demo SHALL 给出清晰提示（缺什么、怎么装、如何切换到 mock runner/替换 runner）。

---

### Requirement 2 — `.claude/*`：展示项目级配置对行为的影响

**User Story:** 作为开发者，我希望 demo 能展示 `projectRoot` + `.claude/*` 如何影响 Claude Agent SDK 路线的行为，从而体现其 File-SSoT 与可审计特性。

#### Acceptance Criteria

1. WHEN demo 使用 `claude_agent_sdk` provider 且配置 `projectRoot` THEN runner SHALL 读取该目录下的 `.claude/*`（demo 自带样例文件）并将其影响体现在输出中（例如：persona/策略/子任务分解模板）。
2. IF `.claude` 目录不存在 THEN demo SHALL 仍可运行（退化为默认行为），并在输出中说明发生了降级。

---

### Requirement 3 — Plugins：展示插件加载与扩展点

**User Story:** 作为开发者，我希望 demo 能展示 `plugins` 目录如何被加载，以及插件如何改变最终输出，从而说明 Claude Agent SDK 的工程扩展能力。

#### Acceptance Criteria

1. WHEN demo 配置一个或多个 plugin 路径 THEN runner SHALL 加载这些插件并在输出中明确标注“加载了哪些插件、插件做了什么变换/贡献”。
2. IF plugin 加载失败（路径错误/模块错误）THEN demo SHALL 给出可读错误，不得静默失败。

---

### Requirement 4 — 权限收敛：默认最小权限，显式允许才生效

**User Story:** 作为安全审阅者，我希望 demo 能直观展示 “默认最小权限 / 显式 allow-list 才开放能力”。

#### Acceptance Criteria

1. WHEN `allowedTools` 为空（默认）THEN runner SHALL 拒绝执行受控能力（例如读文件/写文件），并在输出中说明“被拒绝的原因与如何开启”。
2. WHEN `allowedTools` 显式包含 `filesystem_read` THEN runner SHALL 允许读取 demo 项目内指定文件并把内容反映在输出中。
3. WHEN `allowedTools` 显式包含 `filesystem_write` THEN runner SHALL 允许写入 demo 输出文件到 demo 目录下的指定位置（不写入 repo 根目录）。

---

### Requirement 5 — Streaming：展示流式增量输出（best-effort）

**User Story:** 作为产品开发者，我希望 demo 能展示 `claude_agent_sdk` provider 的 streaming 体验（通过 marker 协议），以及无法流式时的降级行为。

#### Acceptance Criteria

1. WHEN demo 以 streaming 方式调用 provider THEN Demo SHALL 显示增量输出（逐 chunk 打印），并在末尾打印 “stream complete”。
2. IF runner 未输出 streaming marker THEN Demo SHALL 降级为单 chunk 输出，并在输出中说明降级原因。

---

### Requirement 6 — 对照组：展示其区别于“普通 provider”的能力边界

**User Story:** 作为学习者，我希望 demo 能把 `claude_agent_sdk` 与“普通 provider（对照组）”放在同一套输入下对比输出，让差异一眼可见。

#### Acceptance Criteria

1. WHEN demo 运行 THEN Demo SHALL 至少执行两条路径并对比输出：
   - `claude_agent_sdk` provider（有 `.claude`/plugins/权限）
   - 对照 provider（不读取 `.claude`，不加载 plugins，不具备 runner 侧权限系统）
2. Demo 输出 SHALL 明确指出差异来源（项目配置/插件/权限/进程隔离/流式），而不是只给两段文本。

---

### Requirement 7 — 安全与端口策略：不引入默认监听、禁止 `:5000`

**User Story:** 作为仓库维护者，我希望 demo 不会引入误导性的端口默认值或隐式网络访问。

#### Acceptance Criteria

1. Demo SHALL NOT 默认启动任何对外监听服务；不得使用 `:5000` 作为示例或默认端口。
2. IF 将来需要 sidecar（非本规格）THEN 文档示例 SHALL 建议默认端口为 `:5678`。

## Non-Functional Requirements

### Code Architecture and Modularity

- Demo 工程目录结构清晰：runner、plugins、demo projectRoot（含 `.claude`）分开。
- 单文件不超过 800 行；单目录文件数量遵循仓库规范（每层不超过 8 个文件）。

### Performance

- Demo 输出应快速完成（离线 mock runner），不引入长时间等待。

### Security

- 不提交 secrets；不在日志中打印 `ANTHROPIC_API_KEY` 或任何 token。

### Reliability

- 缺失依赖/配置错误时提供明确错误信息（可自助修复）。

### Usability

- 提供 `README.md`：包含运行步骤、预期输出说明、如何替换为真实 Claude Agent SDK runner。


