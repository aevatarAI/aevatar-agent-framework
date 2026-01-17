# Requirements Document

## Introduction

本次重构目标是让 Aevatar.Trading 成为“多交易所可配置 + 事件驱动风控 + AI 决策 + 新版前端”的交易系统：以统一的 API 抽象接入 Weex/OKX 等交易所，通过系统级行情拉取与价格变化检测来触发 AI 决策，而不是固定时间间隔调用 AI；前端以仓位与 AI 决策为核心重新布局，弱化旧策略模块依赖。

## Alignment with Product Vision

该重构遵循框架的 Event-Driven 与 Protobuf-First 原则，通过事件流驱动价格变化与 AI 决策，保持跨运行时一致性；同时通过可配置的交易所适配层保持运行时/平台无关与可扩展性，符合产品“Write Once, Run Anywhere”的目标。

## Requirements

### Requirement 1

**User Story:** 作为交易系统运维者，我希望通过配置切换交易所（Weex/OKX 等），从而不改代码也能切换接入。

#### Acceptance Criteria
1. WHEN 配置文件指定交易所类型 THEN 系统 SHALL 使用对应交易所适配器完成交易所连接与行情/仓位查询。
2. IF 交易所能力与统一抽象不完全一致 THEN 系统 SHALL 通过能力标识/降级策略保证运行可用并记录差异。
3. WHEN 配置切换交易所 THEN 系统 SHALL 在启动时加载新适配器，并输出可观察日志/事件。

### Requirement 2

**User Story:** 作为交易系统操作者，我希望系统自动拉取币价并监测变化，从而只在达到条件时才触发 AI 分析。

#### Acceptance Criteria
1. WHEN 系统拉取到最新价格 THEN 系统 SHALL 生成价格快照/变化事件用于后续处理。
2. IF 价格变化达到阈值或满足触发条件 THEN 系统 SHALL 触发 AI 分析请求事件。
3. WHEN 未达到触发条件 THEN 系统 SHALL 不触发 AI 分析（避免固定时间间隔调用 AI）。

### Requirement 3

**User Story:** 作为风险控制负责人，我希望系统启动时 AI 先做一次仓位风险检查，从而第一时间决定是否止损/止盈/减仓。

#### Acceptance Criteria
1. WHEN 系统启动并完成基础依赖初始化 THEN 系统 SHALL 触发 AI 的启动风险检查。
2. IF AI 给出平仓/止损/止盈/调仓建议 THEN 系统 SHALL 生成决策事件并记录可追溯日志。
3. WHEN 无仓位或 AI 判断无需动作 THEN 系统 SHALL 记录“无操作”决策并保持稳定运行。

### Requirement 4

**User Story:** 作为 AI Agent，我希望可以调整系统触发条件与风险参数，从而让系统在我认为重要的条件下提醒我决策。

#### Acceptance Criteria
1. WHEN AI 通过 Tool 调整触发条件/阈值/冷却时间等参数 THEN 系统 SHALL 校验并持久化这些参数。
2. WHEN 参数被更新 THEN 后续的价格变化检测 SHALL 使用最新参数生效。
3. IF AI 设置的参数不合法或超出安全边界 THEN 系统 SHALL 拒绝并返回可理解的错误信息。

### Requirement 5

**User Story:** 作为系统设计者，我希望策略模块不是系统运行的前置条件，从而让核心流程聚焦在仓位与 AI 决策。

#### Acceptance Criteria
1. WHEN 系统启动 THEN 核心流程 SHALL 不依赖旧策略模块也能完成行情拉取、仓位检查与 AI 决策。
2. IF 需要使用策略模块 THEN 系统 SHALL 以可选组件方式接入，而不是默认必需。

### Requirement 6

**User Story:** 作为前端使用者，我希望界面突出仓位、风险与 AI 决策，从而更直观地理解系统状态与动作。

#### Acceptance Criteria
1. WHEN 打开交易系统前端 THEN 系统 SHALL 优先展示仓位概览、风险指标与 AI 决策摘要。
2. WHEN AI 决策产生 THEN 前端 SHALL 实时更新决策卡片并关联价格变化与仓位影响。
3. IF 交易所连接或行情拉取异常 THEN 前端 SHALL 明确展示状态与告警信息。

### Requirement 7

**User Story:** 作为工程负责人，我希望系统内的跨边界类型与事件有统一契约，从而保证跨运行时兼容与可演进。

#### Acceptance Criteria
1. WHEN 定义新的 State/Event/Config THEN 类型 SHALL 使用 Protobuf 定义并遵循字段兼容规则。
2. IF 需要新增/删除字段 THEN 系统 SHALL 仅添加可选字段或删除字段且不复用字段号。

## Non-Functional Requirements

### Code Architecture and Modularity
- **Single Responsibility Principle**: 交易所适配、行情拉取、触发条件、AI 决策、前端展示各自独立
- **Modular Design**: 交易所适配器与触发条件/参数管理可插拔
- **Dependency Management**: 依赖版本统一在 `Directory.Packages.props`
- **Clear Interfaces**: 交易所 API 与 AI Tool 使用清晰契约与参数验证

### Performance
- 行情拉取与价格变化检测不应阻塞主线程，需异步与可取消
- AI 调用具备冷却/去抖机制，避免过度触发

### Security
- AI Tool 更新参数需校验范围与权限，避免异常或危险配置
- 日志与事件中避免暴露敏感密钥或交易所私密信息

### Reliability
- 交易所连接失败需可重试并暴露健康状态
- 价格变化检测失败不得导致系统崩溃，应降级为继续拉取

### Usability
- 前端以仓位与 AI 决策为核心信息层级，状态/告警清晰可见
- 系统配置切换交易所具备明确的可观测反馈
