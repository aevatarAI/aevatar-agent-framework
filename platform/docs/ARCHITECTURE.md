# Aevatar Platform — 架构（骨架）

> 目标：Platform 是 **Agent OS / Workbench**。CLI/TUI 体验对标 OpenCode，但能力不局限编程场景。

## 目录结构

```
platform/
├── docs/
│   ├── PRD.md
│   ├── FEASIBILITY.md
│   └── ARCHITECTURE.md        # 本文件：Platform 代码骨架说明
├── test/
│   └── Aevatar.Platform.Tests/ # 平台测试（CLI/DSL/Session/Policy）
└── src/
    ├── Aevatar.Platform.slnx        # Platform 子解决方案（CLI + Core + Contracts）
    ├── Aevatar.Platform.Contracts/  # 平台跨边界 Protobuf 合同
    ├── Aevatar.Platform.Cli/        # CLI/TUI 入口（OpenCode parity）
    │   ├── Commands/                # CLI 命令面（System.CommandLine）
    │   └── Tui/                     # TUI 交互（Spectre.Console）
    ├── Aevatar.Platform.Server/     # serve/web/attach 后端（HTTP + SSE）
    └── Aevatar.Platform.Core/       # 平台核心（Profiles/Packs、Workflow、Sessions、Tools…）
        ├── Config/            # ~/.aevatar 配置与 secrets 加载（严格不泄露 secrets）
        ├── Packs/             # Profiles/Packs：多领域能力装配层（coding/vibe/worldbuilding…）
        ├── Sessions/          # 会话事件存储与导入导出（file-backed）
        ├── Tools/             # 工具注册与安全策略（allowlist/timeout/deny）
        └── Workflow/          # Mesh DSL 编译与执行计划（MVP stub）
```

## 模块职责（一句话）

- `Aevatar.Platform.Cli`: 负责命令行解析与终端 UI；不承载业务编排逻辑。
- `Aevatar.Platform.Cli/Commands`: 负责 CLI 命令面与 OpenCode parity 的命令/参数集合。
- `Aevatar.Platform.Cli/Tui`: 负责 TUI 交互输入解析与基础渲染。
- `Aevatar.Platform.Cli/Tui/TuiApp`: TUI 事件循环与运行时组装入口。
- `Aevatar.Platform.Cli/Tui/InputParser`: 解析 `/` 命令、`!` shell、`@` 附件与普通消息。
- `Aevatar.Platform.Cli/Tui/TuiHandlers`: 封装命令与消息处理逻辑（会话/工作流/输出渲染）。
- `Aevatar.Platform.Cli/Tui/ShellRunner`: 执行 shell 命令（受工具策略约束）。
- `Aevatar.Platform.Cli/Tui/AttachmentResolver`: 解析本地路径与模糊匹配。
- `Aevatar.Platform.Server`: 提供 serve/web/attach 的 HTTP + SSE 后端与鉴权。
- `Aevatar.Platform.Contracts`: 跨边界 Protobuf 合同（session state/events 等）。
- `Aevatar.Platform.Tests`: 平台核心行为测试（CLI 解析、DSL 编译、会话回放、策略拒绝）。
- `Aevatar.Platform.Core`: 负责配置、会话、DSL 编排、工具策略等核心能力；CLI/TUI/Server 共用。
- `Aevatar.Agents.AI.Core`: 提供 role 驱动 Agent 与 YAML 装配能力（RoleAIGAgent / RoleAgentFactory）。
- `Aevatar.Platform.Core/Packs`: 负责发现 packs、解析 profiles，并把“领域助手”变成数据驱动装配（workflow+roles+policy）。
- `Aevatar.Platform.Core/Sessions`: 负责 file-backed 事件存储与会话导入导出。
- `Aevatar.Platform.Core/Tools`: 负责工具注册与策略过滤（dangerous/internal/allowlist/timeout）。
- `Aevatar.Platform.Core/Workflow`: 负责 Mesh DSL 编译（JSON/YAML）与执行计划；执行细节在后续任务补全。

## 关键约束（硬规则）

- **跨边界类型必须 Protobuf**：会话事件、stream/attach 传输、持久化事件等一律 `.proto`。
- **禁止使用 `:5000`**：仓库内任何示例/默认监听端口不得为 `5000`（如需默认端口，优先 `5678`）。

## 开发规范（节选）

- 平台层只做组合与编排，不在此层重写 `AIGAgentBase` 行为。
- role YAML 的读取与应用统一走框架层（`GlobalAgentYamlRegistry` + `AgentYamlConfigApplier`）。

## 架构决策

- RoleAIGAgent/RoleAgentFactory 上移到 AI.Core，Platform 只依赖框架能力，避免重复实现与语义漂移。

## 变更日志

- 2026-01-15：Platform Core 移除 role agents，改为复用 AI.Core 的 role 装配能力。


