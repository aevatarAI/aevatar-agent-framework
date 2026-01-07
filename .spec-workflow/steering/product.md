# Product Overview

## Product Purpose
Aevatar Agent Framework 是一个基于 Actor Model 的分布式智能体框架：将 **业务逻辑（GAgent）** 与 **运行时基础设施（GAgentActor）** 解耦，让同一份 Agent 代码在 **Local / ProtoActor / Orleans** 之间切换而无需改动，并以事件驱动的 Stream 体系支撑大规模 Agent 协作。

## Target Users
- **框架使用者（Primary）**：构建多智能体系统/事件驱动系统的 .NET 开发者与平台团队（需要可扩展、可观测、可测试的 Agent 基座）。
- **AI/Agent 工程团队**：需要将 LLM/Tool Calling/MCP 能力安全地纳入 Agent 体系，并可在不同运行时与部署形态间迁移。
- **框架贡献者（Secondary）**：为运行时、持久化、消息系统、AI Provider、示例与文档贡献能力的开发者。

## Key Features
1. **Write Once, Run Anywhere**: 同一份 Agent 代码可在 Local（开发/测试）、ProtoActor（高吞吐）、Orleans（分布式/虚拟 Actor）之间切换。
2. **Event-Driven + Hierarchy Routing**: 以 Stream 为神经系统，支持 Up/Down/Both 传播方向与父子层级自动路由，促进群体协作与解耦。
3. **Protobuf-First Contracts**: 所有跨边界（Stream/网络/存储）的 State/Event/Config 强制用 Protobuf 定义，保证跨运行时兼容、可演进与性能。

## Business Objectives
- 降低分布式 Agent 系统的工程门槛，把“正确的抽象”固化为默认路径（事件驱动、运行时无关、Protobuf 契约）。
- 提供生产可用的能力拼图：运行时、可观测性、（可选）事件溯源、AI 集成与生态插件，缩短从 Demo 到上线的距离。
- 以示例/应用系统验证设计，持续沉淀最佳实践并推动社区协作与复用。

## Success Metrics
- **Time-to-First-Agent**: 新用户从 clone 到跑通 `examples/SimpleDemo/` 并写出第一个 Agent ≤ 10 分钟。
- **Cross-Runtime Confidence**: 核心测试覆盖 Local/ProtoActor/Orleans 的关键路径（事件处理、父子关系、传播方向），CI 通过率 100%。
- **Operational Readiness**: 默认集成 OpenTelemetry（Trace/Metrics/Logs），并能在 Aspire/标准 OTel 后端中稳定观测到关键指标。

## Product Principles
1. **Events are Truth**: 通信以事件为中心，避免隐式共享状态；必要时用 Event Sourcing 提供审计与可回放。
2. **Boundary Types Must Be Protobuf**: 任何跨边界的数据必须是 Protobuf；宁可多写一份 `.proto`，也不要引入运行时不确定性。
3. **Runtime Agnostic by Design**: 业务逻辑不感知运行时；运行时差异收敛在 Actor 包装层与基础设施适配层。

## Monitoring & Visibility (if applicable)
- **Dashboard Type**: 依赖 OpenTelemetry 生态与 Aspire（示例 AppHost）进行可观测性展示。
- **Real-time Updates**: 通过事件流与标准 OTel 导出实现实时性（具体形态由部署环境决定）。
- **Key Metrics Displayed**: 事件吞吐/延迟、Handler 执行耗时、订阅数量、激活/停用、错误率与重试。
- **Sharing Capabilities**: 通过标准 OTel 后端与日志系统共享（例如 Prometheus/Grafana、OTLP、日志平台）。

## Future Vision
让 Aevatar 成为“可组合的 Agent OS”：在保持 **简单、可测、可迁移** 的前提下，持续扩展运行时/AI/插件生态，并以真实应用系统驱动框架演化。

### Potential Enhancements
- **Remote Access**: 提供更一致的远程调试/诊断体验（跨运行时一致的追踪、Dump、事件回放入口）。
- **Analytics**: 对关键指标提供更系统的基准与趋势分析（性能回归、事件分布、热点 Handler）。
- **Collaboration**: 更强的多 Agent 协作工具链（开发期可视化拓扑、事件回放协作、知识/记忆模块标准化）。


