# Requirements Document

## Introduction

本规格定义一个 **“事件处理模块化 + YAML 装配”** 的能力：  
让普通 `RoleAIGAgent` 能通过 Agent YAML 装配成 “Coordinator-like” 行为，
并将 `EventHandler` 从方法抽成可插拔模块。

目标：
- 把 Coordinator 的能力拆分为模块（workflow / step / fanout / vote / trace / primitives 等）
- RoleAIGAgent 作为模块宿主，按需装配
- 支持通过 Agent YAML 配置模块集合与路由

非目标：
- 不实现完整 UI 或新的运行时
- 不改变现有 Cognitive DSL 语义

## Alignment with Product Vision

- **Runtime agnostic**：模块是 Core/AI.Core 级别能力，不依赖 Local/Orleans/ProtoActor。
- **Protobuf-first**：跨边界事件仍必须 Protobuf。
- **Event-driven**：模块通过事件进行协作，不引入新的直接调用链。

## Requirements

### Requirement 1 — 事件模块接口

**User Story:** 作为框架开发者，我希望把 EventHandler 抽成模块，以便复用与装配。

#### Acceptance Criteria
1. WHEN 模块注册到宿主 THEN 系统 SHALL 按确定性顺序调用（priority + name）。
2. WHEN 模块 `CanHandle` 返回 true THEN 系统 SHALL 调用 `HandleAsync`。
3. IF 模块执行失败 THEN 系统 SHALL 记录日志并继续后续模块（best-effort）。

---

### Requirement 2 — RoleAIGAgent 模块宿主

**User Story:** 作为框架使用者，我希望普通 Role agent 也能托管事件模块。

#### Acceptance Criteria
1. WHEN RoleAIGAgent 收到事件 THEN SHALL 通过 `[AllEventHandler]` 转发给模块。
2. IF 未注册任何模块 THEN SHALL 不产生额外行为（零开销）。
3. RoleAIGAgent SHALL 提供 `RegisterEventModule / SetEventModules / GetEventModules` 接口。

---

### Requirement 3 — Step 执行模块适配

**User Story:** 作为 Cognitive 用户，我希望 `ExecuteStepRequestEvent` 能通过模块化 handler 运行。

#### Acceptance Criteria
1. WHEN `SetStepExecutionHandler` 被调用 THEN SHALL 以模块形式接入事件分发。
2. WHEN handler 返回 `StepExecutionResult` THEN SHALL 发布对应响应事件（Up/Down）。

---

### Requirement 4 — YAML 装配（Spec Workflow）

**User Story:** 作为系统设计者，我希望通过 YAML 装配模块与事件路由。

#### Acceptance Criteria
1. WHEN YAML 指定 modules THEN RoleAgentFactory SHALL 装配相应模块。
2. WHEN YAML 指定 routes THEN 模块路由 SHALL 依据规则分发事件。
3. IF YAML 未指定 modules/routes THEN 使用默认模块集合（保持兼容）。

---

## Non-Functional Requirements

### Code Architecture and Modularity
- 单文件职责清晰（模块接口 / 宿主 / 路由拆分）
- 不引入 AI.Core → Cognitive 的反向依赖

### Performance
- 模块分发 O(n)，可预测
- 路由匹配轻量化（避免 heavy reflection）

### Reliability
- 模块失败不得阻断主流程
- 日志包含模块名与 event type

### Usability
- YAML 装配示例必须可用
