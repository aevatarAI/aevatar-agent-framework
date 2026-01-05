# Requirements Document

## Introduction

本规格定义一个可移植的 “Hooks/Harness 中间层”，用于把 oh-my-opencode 的 hook 思路（任务推进、上下文治理、工具输出治理、会话恢复、环境策略等）以 **可插拔、可配置、默认安全** 的方式落地到 Aevatar Agent Framework 的 AI 栈（主要落点：`src/Aevatar.Agents.AI.Core/AIGAgentBase*`）。

目标：
- 为 `AIGAgentBase` 增加一个统一的 Hook 管线（pipeline），让跨切面能力不再散落在业务 Agent / 各 Provider / 各 Tool 实现里。
- 先落 **最小可用版本**（MVP）：围绕 LLM 请求/响应、Tool 执行前后、错误/重试的 hook 点；提供少量内置 hooks 作为样例与默认护栏。

非目标（本规格不做）：
- 不引入新的 UI/Dashboard（Speckit dashboard 仅用于审批工作流，不作为产品功能）。
- 不实现完整 coding-agent 工具链（对应 `docs/coding-agent/TASKS.md` 另有路线），本规格只做 “Hook 思路的移植底座”。

## Alignment with Product Vision

本功能与 Aevatar 的框架方向一致：
- **Runtime agnostic**：Hook 作为 AI.Core 的框架层能力，不应绑定 Local/Orleans/ProtoActor 任一运行时。
- **Event/Trace-first（best-effort）**：Hook 的执行应可观测（日志/事件），失败不能阻塞主链路。
- **安全默认值**：危险能力（例如执行命令、外部写操作）默认关闭；Hook 不得绕过现有 `AllowInternalTools/AllowDangerousTools` 策略。
- **Cross-boundary 必须 Protobuf**：如需新增跨流/跨 runtime 的事件或状态契约，必须以 `.proto` 定义（遵循 `AGENTS.md` 铁律）。

## Requirements

### Requirement 1 — Hook 管线：统一的可插拔扩展点（核心）

**User Story:** 作为框架开发者/高级用户，我希望在 AI Agent 的关键阶段挂载 hooks，以便实现稳定性、上下文治理与工具治理等跨切面能力，而无需修改每个业务 Agent。

#### Acceptance Criteria

1. WHEN 一个 `AIGAgentBase` 生成 LLM 请求 THEN 系统 SHALL 调用 “BeforeLLMRequest” hook 点，允许 hook 读取/修改请求上下文（best-effort）。
2. WHEN `LLMProvider.GenerateAsync` 返回响应 THEN 系统 SHALL 调用 “AfterLLMResponse” hook 点，允许 hook 读取/修改响应（best-effort）。
3. WHEN 进入 tool-call loop 且准备执行某个 tool THEN 系统 SHALL 调用 “BeforeToolExecute” hook 点，允许 hook 拒绝/改写参数/收敛输出预算（best-effort）。
4. WHEN tool 执行完成（成功或失败）THEN 系统 SHALL 调用 “AfterToolExecute” hook 点，允许 hook 截断/清洗/标注结果（best-effort）。
5. IF 任意 hook 执行抛异常 THEN 系统 SHALL 记录可定位日志并继续主流程（hook 失败不得阻塞 LLM/Tool 主链路）。

---

### Requirement 2 — Hook 生命周期：注册、排序、启用/禁用（可治理）

**User Story:** 作为部署者/框架使用者，我希望能在不改代码的情况下启用/禁用某些内置 hooks，并控制它们的执行顺序，以适配不同安全级别与不同业务环境。

#### Acceptance Criteria

1. WHEN 系统启动并装配 AI.Core THEN 系统 SHALL 支持注册多个 hooks，并按确定性顺序执行（例如 priority/排序键）。
2. WHEN 配置禁用某些 hooks THEN 系统 SHALL 不加载/不执行被禁用的 hooks。
3. IF 未提供任何 hook 配置 THEN 系统 SHALL 使用默认 hooks 集合与默认顺序（安全默认值）。
4. WHEN 同名 hook 重复注册 THEN 系统 SHALL 以确定性策略处理（例如：禁止重复或后注册覆盖前者），并有可观测日志。

---

### Requirement 3 — 内置 hooks（MVP）：输出截断、上下文预算、会话恢复（样例 + 护栏）

**User Story:** 作为框架使用者，我希望开箱即用地获得一组最常用的工程化护栏（类似 oh-my-opencode 的 hook 默认值），减少长任务失败与 token/输出爆炸。

#### Acceptance Criteria

1. WHEN tool 返回的内容超过预算（字符/字节上限）THEN 系统 SHALL 通过内置 hook 截断并保持可理解性（例如保留头尾、附加 “truncated” 提示）。
2. WHEN 预计上下文接近阈值（token/消息数/字符数）THEN 系统 SHALL 通过内置 hook 提供 best-effort 的降级策略入口（例如提示/触发摘要/减少可见工具集）。
3. IF LLM 调用因上下文过长或模型特定约束失败 THEN 系统 SHALL 允许通过内置 hook 触发重试/降级（例如：移除工具 schema、缩短附加块、插入必要字段修复）。

---

### Requirement 4 — 安全边界：Hook 不能扩大权限，只能收敛权限

**User Story:** 作为安全审阅者，我希望 hooks 不会成为绕过工具安全策略与内部访问控制的后门。

#### Acceptance Criteria

1. WHEN `AllowDangerousTools=false` THEN 系统 SHALL 不允许任何 hook 使危险工具变得可见或可执行。
2. WHEN `AllowInternalTools=false` THEN 系统 SHALL 不允许任何 hook 使内部工具变得可见或可执行。
3. IF hook 试图执行未授权的 side-effect 行为 THEN 系统 SHALL 拒绝并记录日志（best-effort），且不影响其他非危险流程。

---

### Requirement 5 — 可观测性：Hook 执行可追踪（best-effort）

**User Story:** 作为开发者/运维，我希望能定位“某次请求为什么被截断/为什么降级/为什么重试”，以便调试与审计。

#### Acceptance Criteria

1. WHEN hook 执行 THEN 系统 SHALL 记录可定位日志（至少包含 hook 名称、阶段、耗时、请求关联 id）。
2. IF hook 修改了 LLM 请求/响应或 tool 结果 THEN 系统 SHALL 以可追踪的方式标注（例如 metadata 或日志字段），且不泄漏 secrets。
3. IF 观测/上报失败 THEN 系统 SHALL 不影响主链路（best-effort）。

## Non-Functional Requirements

### Code Architecture and Modularity

- **Single Responsibility Principle**: Hook 接口、Hook 管线、内置 hooks 分文件拆分，避免 `AIGAgentBase` 继续膨胀
- **Modular Design**: hooks 不直接依赖具体业务 Agent；通过抽象上下文（request/response/tool context）交互
- **Dependency Management**: 默认 hooks 与可选 hooks 通过 DI/Options 注册，不在业务代码里写 if/else
- **Clear Interfaces**: 若新增跨边界事件/消息，必须由 `.proto` 定义并生成代码（`AGENTS.md`）

### Performance

- Hook 管线的开销必须可控（每阶段 O(hookCount)），默认 hooks 不能引入明显额外延迟
- 任何截断/统计必须有界（例如最大扫描字节数），避免对大 payload 做全量拷贝

### Security

- 默认不启用危险 hooks（与外部写、命令执行相关的能力必须显式开启）
- Hook 产生的日志/事件不得包含 secrets（token/key），必须支持脱敏
- 仓库内示例/默认配置不得使用 `:5000` 端口（遵循仓库 Port Policy）

### Reliability

- 任意 hook 失败必须可降级（best-effort），不得阻塞主链路
- 重试/降级策略必须有上限（避免无限重试）

### Usability

- 提供清晰的启用/禁用方式（类似 `disabled_hooks` 的体验），并能快速定位当前启用的 hooks 列表
- 提供最小示例：一个 agent 如何启用 hooks、如何替换默认 hooks


