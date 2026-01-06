# Project Structure

## Directory Organization

本仓库是一个 **monorepo**：既包含框架核心库，也包含运行时实现、插件、业务 Agent、示例项目与多个完整子系统/应用。

```
project-root/
├── src/                             # 框架核心库（可发布为 NuGet 包）
│   ├── Aevatar.Agents.Abstractions/ # 公共接口 + 事件契约（含 Protobuf 定义）
│   ├── Aevatar.Agents.Core/         # 基础实现（GAgentBase、路由、层级、EventSourcing 等）
│   ├── Aevatar.Agents.Runtime.*     # 三运行时：Local / ProtoActor / Orleans
│   ├── Aevatar.Agents.AI.*          # AI 抽象与 Provider（MEAI / LLMTornado / Tool/MCP 等）
│   └── ...                          # Maker/Cognitive/Persistence 等能力模块
│
├── plugins/                         # 可插拔生态插件（如 MassTransit、CQRS 等）
│
├── agents/                          # 业务 Agent 实现（领域侧，可迁移至任意运行时）
│
├── examples/                        # 示例项目（Quickstart、集成演示、AppHost 示例等）
│
├── apps/                            # Aspire AppHost（本地编排与演示入口）
│   └── *AppHost/
│
├── cognitive-mesh/                  # Cognitive Mesh 系统（独立子系统，含 docs/）
├── scientific-research-assistant/   # 科研助手系统（独立子系统，含 docs/、frontend/）
├── notebook/                        # Notebook 系统（独立子系统）
├── novel/                           # Novel 系统（独立子系统，含 frontend/、protos/）
├── trade/                           # Trade 系统（独立子系统，含 frontend/、docs/）
│
├── docs/                            # 框架级文档（指南/架构/规范/可观测性等）
├── .spec-workflow/                  # Spec Workflow：specs/、steering/、templates/、approvals/
├── Directory.Packages.props         # Central Package Management（统一依赖版本源）
├── *.slnx                           # 多解决方案入口（框架/各系统）
└── README*.md                       # 仓库入口文档
```

## Naming Conventions

### Files
- **Protobuf**: `snake_case.proto`
- **C# Projects/Namespaces**: `Aevatar.Agents.<Capability>` / `Aevatar.Agents.Runtime.<Runtime>` / `Aevatar.Agents.AI.<Module>`
- **Tests**: `test/Aevatar.Agents.<Module>.Tests/`（与被测模块同名对齐）
- **Docs**: 框架级文档放 `docs/`；子系统文档放各自目录下的 `docs/` 与 `README.md`
- **Spec Workflow**:
  - `.spec-workflow/specs/<spec-name>/{requirements,design,tasks}.md`
  - `.spec-workflow/steering/{product,tech,structure}.md`

### Code
- **Agent 类**: `XxxAgent` 或 `XxxGAgent`（必须无参构造），描述方法 `GetDescriptionAsync()` 必须实现
- **State/Event/Config 类型**: 必须由 `.proto` 生成（命名通常为 `XxxState` / `XxxEvent` / `XxxConfig`）
- **事件命名**: 语义优先（通常使用过去式），避免 Command 风格
- **Event Handler**: `async Task`，使用 `[EventHandler]` / `[AllEventHandler]` 或约定名 `HandleAsync`

## Import Patterns

### Import Order
（C# 约定）
1. `System.*`
2. 第三方依赖（Orleans/Proto.Actor/Protobuf/OTel 等）
3. 内部依赖（`Aevatar.*`）

### Module/Package Organization
- 依赖版本统一写入 `Directory.Packages.props`，避免在各 `*.csproj` 内散落版本号
- Protobuf 文件通过构建流程生成代码（不要手写可序列化跨边界类型）

## Code Structure Patterns

### Module/Class Organization
常见文件组织：
1. `using` / `namespace`
2. 公共类型（对外 API）
3. 内部实现与私有 helper

### Function/Method Organization
- **输入校验靠前**，核心逻辑居中，错误处理与日志贯穿
- **Event Handler 不阻塞**：只用 `async/await`，禁止 `Thread.Sleep()`
- **State 修改位置受限**：只在 `OnActivateAsync` 或事件处理器中修改 `State`（不要在构造函数赋值/替换 State）

### File Organization Principles
- 尽量 **一个公共类型一个文件**（尤其在 `src/` 核心库）
- 运行时相关代码放 `Runtime.*`；业务 Agent 与领域契约尽量保持运行时无关

## Code Organization Principles

1. **Single Responsibility**：每个项目/文件明确职责边界（Core/Runtime/Plugins/Agents）
2. **Modularity**：能力通过模块与插件组合，而非在 Core 内引入特例
3. **Testability**：关键路径（事件处理、层级关系、传播方向、跨运行时兼容）必须可测
4. **Consistency**：跨边界类型一律 Protobuf；依赖版本一律集中管理

## Module Boundaries

- **`Aevatar.Agents.Abstractions`**：公共接口与契约（含 Protobuf），是稳定边界
- **`Aevatar.Agents.Core`**：业务侧可复用的核心实现（不依赖具体运行时）
- **`Aevatar.Agents.Runtime.*`**：运行时适配层（依赖 Core/Abstractions；运行时差异收敛在这里）
- **`Aevatar.Agents.AI.*`**：AI 能力与 Provider（尽量保持运行时无关；与 Core 集成）
- **`plugins/`**：外部系统集成（可选依赖，不应反向侵入 Core）
- **`agents/`**：业务 Agent（领域逻辑；事件/状态契约用 `.proto`）
- **`examples/`**：示例演示（允许依赖运行时/宿主；不作为框架稳定 API）
- **子系统目录（`cognitive-mesh/` 等）**：独立应用，按各自 README/docs 管理

## Code Size Guidelines

- **File size**: 单文件不超过 800 行（超过应拆分）
- **Function/Method size**: 函数尽量 ≤ 20 行；超过需反思抽象/职责
- **Nesting depth**: 缩进不超过 3 层；3 个以上 if/else 分支应重构设计而非继续加分支

## Dashboard/Monitoring Structure (if applicable)

- 框架级可观测性以 **OpenTelemetry + Aspire AppHost** 为主；具体系统的 UI/监控放在各自子系统目录下（如 `*/frontend/`、`*/docs/`）。
- **Port Policy**：仓库内**禁止使用 `:5000`** 作为示例/默认端口；`5678` 仅作为推荐示例端口（如 sidecar），如有冲突可使用任意未占用端口。

## Documentation Standards

- `docs/` 存放框架级权威文档；子系统目录内 `README.md` / `docs/` 作为该子系统入口
- **架构级变更必须同步更新文档**（目录结构、模块职责、对外 API、运行时约束）
- Protobuf schema 变更需遵循兼容性规则（可加字段，不改号，不复用字段号）
- Spec Workflow 文档是需求/设计/任务的“可追溯记录”，steering 文档是跨 spec 的“共识锚点”


