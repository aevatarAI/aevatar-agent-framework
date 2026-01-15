# Aevatar Platform — 架构（骨架）

> 目标：Platform 是 **Agent OS / Workbench**。CLI/TUI 体验对标 OpenCode，但能力不局限编程场景。

## 目录结构

```
platform/
├── docs/
│   ├── PRD.md
│   ├── FEASIBILITY.md
│   └── ARCHITECTURE.md        # 本文件：Platform 代码骨架说明
└── src/
    ├── Aevatar.Platform.slnx  # Platform 子解决方案（CLI + Core）
    ├── Aevatar.Platform.Cli/  # CLI/TUI 入口（后续实现 OpenCode parity）
    └── Aevatar.Platform.Core/ # 平台核心（Profiles/Packs、Workflow、Sessions、Tools…）
        ├── Agents/            # Role agents 与 factory（RoleAIGAgent / RoleAgentFactory）
        ├── Config/            # ~/.aevatar 配置与 secrets 加载（严格不泄露 secrets）
        └── Packs/             # Profiles/Packs：多领域能力装配层（coding/vibe/worldbuilding…）
        └── Workflow/          # Mesh DSL 编译与执行计划（MVP stub）
```

## 模块职责（一句话）

- `Aevatar.Platform.Cli`: 负责命令行解析与终端 UI；不承载业务编排逻辑。
- `Aevatar.Platform.Core`: 负责配置、会话、DSL 编排、role agents、工具策略等核心能力；CLI/TUI/Server 共用。
- `Aevatar.Platform.Core/Agents`: 负责创建 role 驱动的通用 AI Agent，并应用 YAML 配置（tools/skills/system prompt）。
- `Aevatar.Platform.Core/Packs`: 负责发现 packs、解析 profiles，并把“领域助手”变成数据驱动装配（workflow+roles+policy）。
- `Aevatar.Platform.Core/Workflow`: 负责 Mesh DSL 编译（JSON/YAML）与执行计划；执行细节在后续任务补全。

## 关键约束（硬规则）

- **跨边界类型必须 Protobuf**：会话事件、stream/attach 传输、持久化事件等一律 `.proto`。
- **禁止使用 `:5000`**：仓库内任何示例/默认监听端口不得为 `5000`（如需默认端口，优先 `5678`）。


