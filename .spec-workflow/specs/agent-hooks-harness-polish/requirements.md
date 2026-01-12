# Requirements Document

## Introduction

本规格用于对已落地的 `AI.Core Hooks/Harness` 进行一次 **可维护性/可发现性** 的打磨，修复两处“坏味道”：

1. **工具拒绝（deny tool）机制**目前通过 `context.Metadata["deny_tool"] / ["deny_reason"]` 约定传递，属于隐式协议，易踩坑、难重构。
2. **Hook 注入**目前通过反射写入 `AIGAgentBase` 的属性（best-effort），虽然可用但可发现性弱，也不利于 IDE/代码审查定位注入入口。

目标：在不引入 breaking change 的前提下，让 Hook/Harness 具备更强的 **强类型 API** 与 **显式注入路径**，并同步更新文档与示例。

## Alignment with Product Vision

- **框架层治理**：把横切治理（LLM/Tool/稳定性/可控性）集中在 AI.Core 层，减少业务 Agent/Coordinator/Worker 的重复实现。
- **默认安全**：任何变更不得扩大权限（仍遵循 `AllowInternalTools/AllowDangerousTools` defense-in-depth）。
- **best-effort**：注入失败、hook 失败不得阻塞主链路。

## Requirements

### Requirement 1 — 强类型工具拒绝 API（消灭隐式协议）

**User Story:** 作为 Hook 作者/框架维护者，我希望用强类型 API 表达“拒绝执行某个工具”，从而避免通过字符串 key 约定，降低踩坑成本并提升可维护性。

#### Acceptance Criteria

1. WHEN hook 在 `BeforeToolExecuteAsync` 调用 `context.DenyTool(reason)` THEN 系统 SHALL 拒绝执行该工具，并返回一个失败的 `ToolExecutionResult`（best-effort，不抛异常）。
2. WHEN `context.DenyTool(reason)` 被调用 THEN 系统 SHALL 在可观测数据中包含 deny 标记与原因（不泄漏 secrets）。
3. WHEN 现有 hook 仍使用旧的 `context.Metadata[...]` 方式设置 deny 标记 THEN 系统 SHALL 继续兼容（不得破坏已有 hook）。
4. WHEN deny 发生 THEN 系统 SHALL 使用统一的 key/语义（集中定义，避免散落硬编码）。

---

### Requirement 2 — 显式 Hook 注入（去反射，提高可发现性）

**User Story:** 作为框架使用者/维护者，我希望 Hook 注入路径清晰可追踪，并尽可能避免反射写属性，从而提升可发现性、可调试性与长期可维护性。

#### Acceptance Criteria

1. WHEN `AIGAgentFactory` 创建 `AIGAgentBase`（或其派生类）THEN 系统 SHALL 以显式、类型化方式注入：
   - `AevatarAgentHookOptions`（优先 `IOptions<AevatarAgentHookOptions>`）
   - `IEnumerable<IAevatarAgentHook>`（Additional hooks）
2. IF 注入失败或 DI 中未注册任何 hooks/options THEN 系统 SHALL 使用默认值（内置 hooks + 默认 options），且不影响 agent 创建（best-effort）。
3. IF hooks/options 在 agent 创建后被注入 THEN 系统 SHALL 确保 Hook pipeline 的缓存不会导致“注入不生效”（应重建或延迟构建）。
4. 文档 SHALL 更新：不再宣称“通过反射注入”，而应描述实际注入机制与配置入口。

## Non-Functional Requirements

### Code Architecture and Modularity

- **Single Responsibility Principle**：强类型 Deny API 与注入逻辑分离，避免 `AIGAgentBase` 继续膨胀。
- **Backward Compatibility**：不得引入 breaking change（保持旧 metadata 方式可用）。
- **Protobuf 铁律**：本规格不引入新的跨边界类型；若未来扩展事件/消息需跨边界，必须走 `.proto`。

### Performance

- 新增 API/注入逻辑的开销应为常数级；不引入额外反射扫描。

### Security

- Hook 不得扩权；deny 只做“收敛型”能力。
- deny reason/metadata 不得包含 secrets。

### Reliability

- best-effort：注入失败/Hook 失败不阻塞主链路。

### Usability

- Hook 作者能在 30 秒内发现正确用法（`context.DenyTool(reason)`），并快速通过配置禁用 hooks。


