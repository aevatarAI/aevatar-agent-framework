# Technology Stack

## Project Type
以 **.NET 10** 为核心的分布式智能体框架（Library-first），提供多运行时（Local/ProtoActor/Orleans）能力，并在 monorepo 中包含示例项目、应用宿主（Aspire AppHost）与若干业务系统/实验性子项目。

## Core Technologies

### Primary Language(s)
- **Language**: C# / .NET 10
- **Runtime/Compiler**: .NET SDK 10.0.x
- **Language-specific tools**: `dotnet build`, `dotnet test`, Central Package Management（`Directory.Packages.props`）

### Key Dependencies/Libraries
- **Google.Protobuf (3.33.0)**: 跨边界数据契约与序列化（State/Event/Config 强制 Protobuf）
- **Microsoft Orleans (9.2.1)**: 分布式虚拟 Actor 运行时与 Streaming 支撑
- **Proto.Actor / Proto.Remote (1.8.0)**: 高性能 Actor 运行时与远程通信
- **Microsoft.Extensions.AI (10.0.0)**: AI 能力抽象层（配合 Provider：OpenAI/Azure OpenAI/LLMTornado 等）
- **OpenTelemetry (1.12.0) + Aspire (9.5.2)**: 可观测性与本地编排/示例宿主
- **MassTransit (8.3.0)**: 消息系统/流插件生态（Kafka/RabbitMQ 等）
- **MongoDB.Driver (3.5.2)**: MongoDB 持久化（状态/事件存储等模块化支持）
- **xUnit (2.9.2) + Moq (4.20.72) / FluentAssertions (7.1.0)**: 测试体系

### Application Architecture
- **Actor Model + Event-Driven**：业务逻辑写在 `GAgent`，运行时/网络/路由写在 `GAgentActor`；Agent 间交互通过事件（Stream）传播完成。
- **Runtime Abstraction**：同一 Agent 逻辑可在 Local/ProtoActor/Orleans 运行时之间切换，差异收敛在 Actor 包装与基础设施适配层。
- **Hierarchy + Stream Routing**：父子层级关系 + Up/Down/Both 传播方向，支撑群体广播与协作模式。

### Data Storage (if applicable)
- **Primary storage**: 以可插拔方式提供（示例包含 InMemory 与 MongoDB 模块；Orleans 生态可接入多种 Provider）
- **Caching**: 以运行时/宿主配置为准（可通过 `Microsoft.Extensions.*` 与具体实现扩展）
- **Data formats**: **Protocol Buffers**（跨边界统一格式），配置/宿主层常见为 JSON

### External Integrations (if applicable)
- **APIs**: OpenAI / Azure OpenAI / 其他 LLM Provider（通过 `Microsoft.Extensions.AI` 或 Provider 包接入）
- **Protocols**: Stream 事件传播；Proto.Remote（gRPC/网络传输）；HTTP（示例/应用系统）
- **Authentication**: 业务应用侧可选 ABP / OpenIddict 体系（框架层尽量保持运行时无关）

### Monitoring & Dashboard Technologies (if applicable)
- **Dashboard Framework**: 以 OpenTelemetry 生态与 Aspire 观测面板为主（具体 UI 由部署环境决定）
- **Real-time Communication**: OTel 导出与后端聚合（实时性取决于后端/采样策略）
- **Visualization Libraries**: 由 OTel 后端决定（Prometheus/Grafana/OTLP 等）
- **State Management**: 框架内部状态必须 Protobuf；运行时/存储实现负责跨边界一致性

## Development Environment

### Build & Development Tools
- **Build System**: `dotnet` + 多项目解决方案（`.slnx`）
- **Package Management**: NuGet（Central Package Management：`Directory.Packages.props`）
- **Development workflow**: 本地运行示例（`examples/*`）、AppHost（`apps/*AppHost`）驱动端到端验证

### Code Quality Tools
- **Static Analysis**: 以 .NET SDK/编译器分析为主（可按团队标准补充 analyzers）
- **Formatting**: 以仓库既定风格为准（建议将格式化/分析器规则沉淀到统一配置）
- **Testing Framework**: xUnit + Mocking/Assertion 工具；覆盖运行时兼容性与事件传播关键路径
- **Documentation**: `docs/` + 各子系统 README；Spec Workflow 用于需求/设计/任务与 steering 文档

### Version Control & Collaboration
- **VCS**: Git
- **Branching Strategy**: 以仓库实践为准（建议 PR 驱动）
- **Code Review Process**: PR 规范（模块名 + 简述），提交前确保 `dotnet build && dotnet test`

### Dashboard Development (if applicable)
- **Live Reload**: 依赖具体子项目实现（Web/前端目录存在于部分子系统）
- **Port Management**: **禁止使用 `:5000`**；`5678` 仅作为推荐示例端口（如 sidecar），如有冲突可使用任意未占用端口（端口应可配置）
- **Multi-Instance Support**: 以 AppHost/多项目并行运行策略为准（端口显式配置）

## Deployment & Distribution (if applicable)
- **Target Platform(s)**: macOS/Linux 优先（Windows 需保证 Protobuf 工具链），运行时可本地/单机/集群/分布式部署
- **Distribution Method**: NuGet 包（核心库/运行时/插件）+ 源码仓库示例与应用系统
- **Installation Requirements**: .NET 10 SDK，Protobuf 工具链（随构建触发生成）
- **Update Mechanism**: 语义化版本与 Protobuf 的前后向兼容策略（新增字段/不复用字段号）

## Technical Requirements & Constraints

### Performance Requirements
- **高吞吐/低延迟目标**：在 ProtoActor 运行时优先；Local 用于最快反馈；Orleans 用于分布式鲁棒性。
- **约束**：跨边界数据必须 Protobuf，以避免运行时序列化失败与性能回退。

### Compatibility Requirements
- **Platform Support**: .NET 10 支持的平台；分布式场景按 Orleans/ProtoActor 支持矩阵部署
- **Dependency Versions**: 统一由 `Directory.Packages.props` 管理（Central Package Management）
- **Standards Compliance**: Protobuf schema 演进规则（可加字段，不改号，不复用）

### Security & Compliance
- **Security Requirements**: 由宿主/应用层注入（例如密钥管理、认证授权）；框架侧默认不在契约层泄漏敏感信息
- **Threat Model**: 事件注入、消息放大、配置泄露与越权发布等（应用层需做事件校验与隔离）

### Scalability & Reliability
- **Expected Load**: 大规模 Agent 交互（事件驱动）与横向扩展（Orleans/ProtoActor）
- **Availability Requirements**: Orleans 集群能力与可观测性/告警体系支撑
- **Growth Projections**: 以插件化方式扩展存储/消息系统/AI Provider

## Technical Decisions & Rationale

### Decision Log
1. **Protobuf-First**: 任何跨边界类型（State/Event/Config）强制 Protobuf，换取跨运行时一致性与可演进契约。
2. **GAgent / GAgentActor 分离**: 业务逻辑不感知运行时，基础设施可替换，避免“运行时绑死业务”。
3. **Central Package Management**: 统一版本管理，降低 monorepo 依赖漂移与升级成本。
4. **OTel + Aspire**: 默认可观测性通路与示例编排，让“能跑”升级为“可观测地跑”。

## Known Limitations
- **Monorepo 复杂度**: 子系统众多，首次阅读成本较高；需依赖 `docs/` 与 steering/structure 来降低认知负担。
- **运行时差异不可完全消除**: 不同运行时在生命周期、网络与存储上仍有细节差异，需要通过测试矩阵持续收敛。


