# Tasks Document

- [x] 1. 扩展交易系统 Protobuf 契约
  - File: apps/Aevatar.Trading/Aevatar.Trade/trade_messages.proto
  - 新增 Exchange/Trigger/Policy/StartupRiskCheck 相关消息与状态
  - 确保新增字段遵循兼容性规则（只增字段，不复用字段号）
  - Purpose: 为跨边界配置、事件与状态提供统一契约
  - _Leverage: apps/Aevatar.Trading/Aevatar.Trade/trade_messages.proto_
  - _Requirements: 1,2,3,4,7_
  - _Prompt: Implement the task for spec aevatar-trading-refactor, first run spec-workflow-guide to get the workflow guide then implement the task: Role: Protobuf/Backend Engineer | Task: 在 trade_messages.proto 中新增 ExchangeConfig、DecisionTriggerConfig、TradingPolicyConfig、DecisionTriggerEvent、TradingPolicyUpdatedEvent、StartupRiskCheckRequestedEvent/ResultEvent、PolicyManagerState、DecisionTriggerState 等消息，保持现有字段号不变，仅追加新字段 | Restrictions: 跨边界类型必须 Protobuf；不复用/修改旧字段号；保持文件 <800 行 | _Leverage: apps/Aevatar.Trading/Aevatar.Trade/trade_messages.proto | _Requirements: 1,2,3,4,7 | Success: 新消息可生成代码且不破坏旧字段，编译通过 | Workflow: 先将该任务在 tasks.md 标记为 [-]，完成后用 log-implementation 记录实现细节，再标记为 [x]

- [x] 2. 新增交易所抽象接口与能力模型
  - File: apps/Aevatar.Trading/Aevatar.Trade/Infrastructure/Exchanges/IExchangeClient.cs
  - File: apps/Aevatar.Trading/Aevatar.Trade/Infrastructure/Exchanges/ExchangeCapabilities.cs
  - 定义市场/账户/交易客户端接口与能力标识
  - Purpose: 实现交易所可配置切换与能力差异降级
  - _Leverage: apps/Aevatar.Trading/Aevatar.Trade/Infrastructure/WeexApi/IWeexApiClient.cs_
  - _Requirements: 1,7_
  - _Prompt: Implement the task for spec aevatar-trading-refactor, first run spec-workflow-guide to get the workflow guide then implement the task: Role: Backend Architect | Task: 创建交易所抽象接口与能力模型（Market/Account/Trade + Capabilities），为 Weex/OKX 适配器提供统一入口 | Restrictions: 接口只暴露必要能力；避免循环依赖；保持单文件职责清晰 | _Leverage: apps/Aevatar.Trading/Aevatar.Trade/Infrastructure/WeexApi/IWeexApiClient.cs | _Requirements: 1,7 | Success: 新接口/能力模型清晰可用，后续适配器可实现 | Workflow: 先将该任务在 tasks.md 标记为 [-]，完成后用 log-implementation 记录实现细节，再标记为 [x]

- [x] 3. 实现 Weex 适配器与 OKX 占位适配器
  - File: apps/Aevatar.Trading/Aevatar.Trade/Infrastructure/Exchanges/WeexExchangeClient.cs
  - File: apps/Aevatar.Trading/Aevatar.Trade/Infrastructure/Exchanges/OkxExchangeClient.cs
  - 将现有 IWeexApiClient/WeexWebSocketClient 封装为新接口实现；OKX 提供可降级的 NotSupported 行为
  - Purpose: 通过配置切换交易所且运行可用（能力不足时降级）
  - _Leverage: apps/Aevatar.Trading/Aevatar.Trade/Infrastructure/WeexApi/*_
  - _Requirements: 1,2,7_
  - _Prompt: Implement the task for spec aevatar-trading-refactor, first run spec-workflow-guide to get the workflow guide then implement the task: Role: Backend Integrations Engineer | Task: 实现 WeexExchangeClient 封装现有 Weex API；新增 OkxExchangeClient 作为占位实现（明确能力不足与降级路径） | Restrictions: 不改动现有 Weex API 行为；OKX 不完整能力需显式 NotSupported；日志要清晰 | _Leverage: apps/Aevatar.Trading/Aevatar.Trade/Infrastructure/WeexApi/* | _Requirements: 1,2,7 | Success: 适配器可被 DI 选择，Weex 正常运行，OKX 选择时不崩溃 | Workflow: 先将该任务在 tasks.md 标记为 [-]，完成后用 log-implementation 记录实现细节，再标记为 [x]

- [x] 4. 重构 DI 注册与配置绑定（支持 Exchange/Trigger/Policy）
  - File: apps/Aevatar.Trading/Aevatar.Trade/ServiceCollectionExtensions.cs
  - File: apps/Aevatar.Trading/Aevatar.Trade.Api/Program.cs
  - File: apps/Aevatar.Trading/Aevatar.Trade.Api/appsettings.json
  - 新增 ExchangeConfig/DecisionTriggerConfig/TradingPolicyConfig 绑定与适配器选择
  - Purpose: 通过配置切换交易所与触发策略
  - _Leverage: apps/Aevatar.Trading/Aevatar.Trade/ServiceCollectionExtensions.cs_
  - _Requirements: 1,2,4,7_
  - _Prompt: Implement the task for spec aevatar-trading-refactor, first run spec-workflow-guide to get the workflow guide then implement the task: Role: .NET DI Engineer | Task: 将 AddWeexTradingServices 重构为通用 AddTradingExchangeServices，绑定 Exchange/Trigger/Policy 配置并在 Program.cs 中选择 Weex/OKX 适配器，同时仅在 Weex 模式桥接 WEEX_* 环境变量 | Restrictions: 不使用 :5000 端口；配置不包含 secrets 回显；遵循 Central Package Management | _Leverage: apps/Aevatar.Trading/Aevatar.Trade/ServiceCollectionExtensions.cs | _Requirements: 1,2,4,7 | Success: DI 可根据配置切换交易所并完成配置注入 | Workflow: 先将该任务在 tasks.md 标记为 [-]，完成后用 log-implementation 记录实现细节，再标记为 [x]

- [x] 5. 新增 PolicyManagerAgent 与 AI 参数工具
  - File: apps/Aevatar.Trading/Aevatar.Trade/Agents/Policy/PolicyManagerAgent.cs
  - File: apps/Aevatar.Trading/Aevatar.Trade/Agents/Policy/TradingPolicyTools.cs
  - 提供策略参数读写与校验，并通过 Tool 暴露给 AI
  - Purpose: 让 AI 动态调整触发与风控参数
  - _Leverage: src/Aevatar.Agents.AI.Core/AIGAgentBase.Tools.cs, apps/Aevatar.VibeResearching/src/*/Tools/*_
  - _Requirements: 3,4,7_
  - _Prompt: Implement the task for spec aevatar-trading-refactor, first run spec-workflow-guide to get the workflow guide then implement the task: Role: AI Tools Developer | Task: 新增 PolicyManagerAgent 与工具类（如 get_trading_policy / update_trading_policy），校验参数合法性并发布 TradingPolicyUpdatedEvent | Restrictions: 状态/事件必须使用 Protobuf；工具参数需校验范围；避免三层以上缩进 | _Leverage: src/Aevatar.Agents.AI.Core/AIGAgentBase.Tools.cs, apps/Aevatar.VibeResearching/src/*/Tools/* | _Requirements: 3,4,7 | Success: AI 能读写策略参数且更新事件可被下游消费 | Workflow: 先将该任务在 tasks.md 标记为 [-]，完成后用 log-implementation 记录实现细节，再标记为 [x]

- [x] 6. 新增 DecisionTriggerAgent（价格变化触发）
  - File: apps/Aevatar.Trading/Aevatar.Trade/Agents/Triggers/DecisionTriggerAgent.cs
  - 根据 DecisionTriggerConfig 计算价格变化并发布 DecisionTriggerEvent
  - Purpose: 以“价格变化”驱动 AI 决策，不再固定 interval 触发
  - _Leverage: apps/Aevatar.Trading/Aevatar.Trade/Agents/Data/DataCollectorAgent.cs_
  - _Requirements: 2,4,7_
  - _Prompt: Implement the task for spec aevatar-trading-refactor, first run spec-workflow-guide to get the workflow guide then implement the task: Role: Backend Engineer | Task: 实现 DecisionTriggerAgent，维护价格窗口/冷却时间，触发 DecisionTriggerEvent，并响应 TradingPolicyUpdatedEvent 更新配置 | Restrictions: 不阻塞事件处理；使用 Protobuf 状态；复杂分支需简化 | _Leverage: apps/Aevatar.Trading/Aevatar.Trade/Agents/Data/DataCollectorAgent.cs | _Requirements: 2,4,7 | Success: 价格变化达到阈值时触发事件，冷却生效 | Workflow: 先将该任务在 tasks.md 标记为 [-]，完成后用 log-implementation 记录实现细节，再标记为 [x]

- [x] 7. DataCollectorAgent 改用交易所抽象
  - File: apps/Aevatar.Trading/Aevatar.Trade/Agents/Data/DataCollectorAgent.cs
  - 使用 IExchangeMarketDataClient/可选流式接口替代 IWeexApiClient
  - Purpose: 去 Weex 耦合并保持行情拉取能力
  - _Leverage: apps/Aevatar.Trading/Aevatar.Trade/Infrastructure/Exchanges/IExchangeClient.cs_
  - _Requirements: 1,2_
  - _Prompt: Implement the task for spec aevatar-trading-refactor, first run spec-workflow-guide to get the workflow guide then implement the task: Role: Backend Engineer | Task: 让 DataCollectorAgent 依赖交易所抽象接口，保留 WS/REST 降级逻辑与 MarketTick/Kline 事件发布 | Restrictions: 保持现有行为与日志语义；避免新阻塞；不引入 5000 端口 | _Leverage: apps/Aevatar.Trading/Aevatar.Trade/Infrastructure/Exchanges/IExchangeClient.cs | _Requirements: 1,2 | Success: DataCollector 可在 Weex/OKX 模式运行（OKX 至少 REST） | Workflow: 先将该任务在 tasks.md 标记为 [-]，完成后用 log-implementation 记录实现细节，再标记为 [x]

- [x] 8. Coordinator 仅在 DecisionTriggerEvent 上决策
  - File: apps/Aevatar.Trading/Aevatar.Trade/Agents/Coordinator/TradingCoordinatorAgent.cs
  - 将决策入口从 MarketTickEvent 改为 DecisionTriggerEvent，支持策略参数更新
  - Purpose: 将 AI 决策频率交给 Trigger 控制
  - _Leverage: apps/Aevatar.Trading/Aevatar.Trade/Agents/Coordinator/TradingCoordinatorAgent.cs_
  - _Requirements: 2,4_
  - _Prompt: Implement the task for spec aevatar-trading-refactor, first run spec-workflow-guide to get the workflow guide then implement the task: Role: AI Agent Engineer | Task: 调整 TradingCoordinatorAgent 仅在 DecisionTriggerEvent 时触发 AI 决策，并接收 TradingPolicyUpdatedEvent 更新阈值/权重 | Restrictions: 不在事件处理链路 await LLM；保持 single-flight 逻辑 | _Leverage: apps/Aevatar.Trading/Aevatar.Trade/Agents/Coordinator/TradingCoordinatorAgent.cs | _Requirements: 2,4 | Success: 决策触发由 Trigger 控制，仍保持稳定性 | Workflow: 先将该任务在 tasks.md 标记为 [-]，完成后用 log-implementation 记录实现细节，再标记为 [x]

- [x] 9. 风控与执行改用交易所抽象并支持启动风险检查
  - File: apps/Aevatar.Trading/Aevatar.Trade/Agents/RiskControl/RiskManagerAgent.cs
  - File: apps/Aevatar.Trading/Aevatar.Trade/Agents/Execution/ExecutorAgent.cs
  - 添加 StartupRiskCheck 处理与策略更新
  - Purpose: 启动即进行风险检查，并解耦 Weex
  - _Leverage: apps/Aevatar.Trading/Aevatar.Trade/Agents/RiskControl/RiskManagerAgent.cs_
  - _Requirements: 1,3,4_
  - _Prompt: Implement the task for spec aevatar-trading-refactor, first run spec-workflow-guide to get the workflow guide then implement the task: Role: Risk/Execution Engineer | Task: 为 RiskManagerAgent 增加 StartupRiskCheck 事件处理与策略更新应用；ExecutorAgent 改用交易所抽象下单 | Restrictions: AI 不直接下单；执行路径保持可审计；遵循 Protobuf 状态 | _Leverage: apps/Aevatar.Trading/Aevatar.Trade/Agents/RiskControl/RiskManagerAgent.cs | _Requirements: 1,3,4 | Success: 启动风险检查可运行，执行层不依赖 Weex 具体实现 | Workflow: 先将该任务在 tasks.md 标记为 [-]，完成后用 log-implementation 记录实现细节，再标记为 [x]

- [x] 10. TradingSystem 编排更新（新增 Trigger/Policy）
  - File: apps/Aevatar.Trading/Aevatar.Trade/TradingSystem.cs
  - 初始化新 Agent，建立层级关系，启动时触发风险检查，并提供 API 所需方法
  - Purpose: 系统级编排与入口调整
  - _Leverage: apps/Aevatar.Trading/Aevatar.Trade/TradingSystem.cs_
  - _Requirements: 1,2,3,4,5_
  - _Prompt: Implement the task for spec aevatar-trading-refactor, first run spec-workflow-guide to get the workflow guide then implement the task: Role: System Orchestration Engineer | Task: 在 TradingSystem 中新增 PolicyManager/DecisionTrigger，并更新层级拓扑、Start 流程（启动风险检查）、以及用于 API 的策略/触发/仓位方法 | Restrictions: 不破坏现有审计链路；避免阻塞启动流程；保持 Agent 单父约束 | _Leverage: apps/Aevatar.Trading/Aevatar.Trade/TradingSystem.cs | _Requirements: 1,2,3,4,5 | Success: 系统启动正常且新链路可工作 | Workflow: 先将该任务在 tasks.md 标记为 [-]，完成后用 log-implementation 记录实现细节，再标记为 [x]

- [x] 11. 新增策略/触发/仓位 API（交易所无关）
  - File: apps/Aevatar.Trading/Aevatar.Trade.Api/Controllers/PolicyController.cs
  - File: apps/Aevatar.Trading/Aevatar.Trade.Api/Controllers/DecisionController.cs
  - File: apps/Aevatar.Trading/Aevatar.Trade.Api/Controllers/PositionsController.cs
  - 提供 Policy 读写、手工触发与仓位查询接口
  - Purpose: 前端/AI 操作入口解耦 Weex
  - _Leverage: apps/Aevatar.Trading/Aevatar.Trade.Api/Controllers/TradingController.cs_
  - _Requirements: 2,3,4,6_
  - _Prompt: Implement the task for spec aevatar-trading-refactor, first run spec-workflow-guide to get the workflow guide then implement the task: Role: API Developer | Task: 添加 Policy/Decision/Positions 控制器并调用 TradingSystem 提供的方法，返回结构化响应与错误 | Restrictions: 不暴露敏感配置；保持 REST 语义；不绑定 5000 端口 | _Leverage: apps/Aevatar.Trading/Aevatar.Trade.Api/Controllers/TradingController.cs | _Requirements: 2,3,4,6 | Success: 新 API 可被前端调用且与交易所无关 | Workflow: 先将该任务在 tasks.md 标记为 [-]，完成后用 log-implementation 记录实现细节，再标记为 [x]

- [x] 12. Meta 接口与前端 DTO 抽象化
  - File: apps/Aevatar.Trading/Aevatar.Trade.Api/Controllers/MetaController.cs
  - File: apps/Aevatar.Trading/frontend/src/types.ts
  - 输出 Exchange/Trigger/Policy 摘要，前端 DTO 去 Weex 强耦合
  - Purpose: 支持 OKX 等交易所与新布局需求
  - _Leverage: apps/Aevatar.Trading/Aevatar.Trade.Api/Controllers/MetaController.cs_
  - _Requirements: 1,6_
  - _Prompt: Implement the task for spec aevatar-trading-refactor, first run spec-workflow-guide to get the workflow guide then implement the task: Role: Full-stack Engineer | Task: 扩展 Meta 返回字段（exchange/type/capabilities/trigger/policy 摘要），并更新前端 types.ts 对应 DTO | Restrictions: 不泄露 secrets；保持字段兼容性；仅添加新字段 | _Leverage: apps/Aevatar.Trading/Aevatar.Trade.Api/Controllers/MetaController.cs | _Requirements: 1,6 | Success: 前端可获取新摘要信息且旧字段保持可用 | Workflow: 先将该任务在 tasks.md 标记为 [-]，完成后用 log-implementation 记录实现细节，再标记为 [x]

- [x] 13. 前端 API 与状态模型更新
  - File: apps/Aevatar.Trading/frontend/src/api.ts
  - File: apps/Aevatar.Trading/frontend/src/types.ts
  - 接入 Policy/Decision/Positions 新接口
  - Purpose: 为新 UI 提供数据源
  - _Leverage: apps/Aevatar.Trading/frontend/src/api.ts_
  - _Requirements: 6_
  - _Prompt: Implement the task for spec aevatar-trading-refactor, first run spec-workflow-guide to get the workflow guide then implement the task: Role: Frontend Engineer | Task: 添加新 API 调用与类型（policy/decision/positions），保持统一错误处理与同源请求 | Restrictions: 不引入新 UI 库；保持轻量风格 | _Leverage: apps/Aevatar.Trading/frontend/src/api.ts | _Requirements: 6 | Success: 前端可拉取新接口数据并复用 apiFetch | Workflow: 先将该任务在 tasks.md 标记为 [-]，完成后用 log-implementation 记录实现细节，再标记为 [x]

- [x] 14. 交易控制台重排布局（突出仓位/风险/AI 决策）
  - File: apps/Aevatar.Trading/frontend/src/pages/TradingPage.tsx
  - File: apps/Aevatar.Trading/frontend/src/App.tsx
  - 重构 UI 层级与模块展示，弱化旧策略页
  - Purpose: UI 聚焦仓位与 AI 决策
  - _Leverage: apps/Aevatar.Trading/frontend/src/pages/TradingPage.tsx_
  - _Requirements: 6_
  - _Prompt: Implement the task for spec aevatar-trading-refactor, first run spec-workflow-guide to get the workflow guide then implement the task: Role: Frontend UI Engineer | Task: 重排 TradingPage 与 App Tabs，突出仓位、风险、AI 决策与触发条件，保留调试入口但降级为次级区域 | Restrictions: 不引入新依赖；保持响应式与性能 | _Leverage: apps/Aevatar.Trading/frontend/src/pages/TradingPage.tsx | _Requirements: 6 | Success: UI 重点清晰，仓位/决策信息优先展示 | Workflow: 先将该任务在 tasks.md 标记为 [-]，完成后用 log-implementation 记录实现细节，再标记为 [x]

- [x] 15. 更新架构与前端文档
  - File: apps/Aevatar.Trading/docs/ARCHITECTURE.md
  - File: apps/Aevatar.Trading/docs/FRONTEND.md
  - File: apps/Aevatar.Trading/docs/TRADING_WORKFLOW.md
  - 同步新的模块边界、事件流与 UI 结构
  - Purpose: 架构变更同步文档
  - _Leverage: apps/Aevatar.Trading/docs/ARCHITECTURE.md_
  - _Requirements: 1,2,3,6_
  - _Prompt: Implement the task for spec aevatar-trading-refactor, first run spec-workflow-guide to get the workflow guide then implement the task: Role: Tech Writer / Architect | Task: 更新交易系统架构/前端/流程文档，加入 Exchange 抽象、Trigger/Policy/StartupRiskCheck 与新 UI 布局说明 | Restrictions: 文档精炼；树形结构清晰；避免与实现不一致 | _Leverage: apps/Aevatar.Trading/docs/ARCHITECTURE.md | _Requirements: 1,2,3,6 | Success: 文档准确反映新架构与流程 | Workflow: 先将该任务在 tasks.md 标记为 [-]，完成后用 log-implementation 记录实现细节，再标记为 [x]

- [x] 16. 新增核心逻辑单元测试
  - File: apps/Aevatar.Trading/test/Aevatar.Trade.Tests/Aevatar.Trade.Tests.csproj
  - File: apps/Aevatar.Trading/test/Aevatar.Trade.Tests/DecisionTriggerAgentTests.cs
  - File: apps/Aevatar.Trading/test/Aevatar.Trade.Tests/PolicyManagerAgentTests.cs
  - 覆盖触发阈值与策略更新校验
  - Purpose: 防止触发逻辑回归
  - _Leverage: test/Aevatar.Agents.*.Tests (patterns), Directory.Packages.props_
  - _Requirements: 2,4_
  - _Prompt: Implement the task for spec aevatar-trading-refactor, first run spec-workflow-guide to get the workflow guide then implement the task: Role: Test Engineer | Task: 新建测试项目并覆盖 DecisionTriggerAgent 与 PolicyManagerAgent 的关键路径（阈值/冷却/校验） | Restrictions: 不删除失败测试；使用 Protobuf 消息；测试 happy + error path | _Leverage: test/Aevatar.Agents.*.Tests, Directory.Packages.props | _Requirements: 2,4 | Success: 测试可运行且覆盖关键逻辑 | Workflow: 先将该任务在 tasks.md 标记为 [-]，完成后用 log-implementation 记录实现细节，再标记为 [x]
