# IEventModule（事件模块）指南

> 目标：为 `RoleAIGAgent` 提供**可装配、可路由、可扩展**的事件处理模块（不侵入业务 Agent）。

## 1) 概览

IEventModule 是一个**事件处理插件**，通过 YAML `extensions.event_modules` 装配：

- 模块收到 `EventEnvelope`，可做**过滤、处理、发布新事件**；
- `RoleAIGAgent` 以 **AllEventHandler** 方式统一分发；
- 适用于**跨应用/跨运行时**的 role agent 装配（YAML）。

## 2) 核心接口

- `IEventModule`
  - `bool CanHandle(EventEnvelope envelope)`
  - `Task HandleAsync(EventEnvelope envelope, IEventModuleHost host, CancellationToken ct)`
- `IEventModuleHost`
  - `AgentId` / `Logger`
  - `Agent`：当前 agent 实例（`AIGAgentBase`）
  - `PublishAsync(...)`：发布新事件（内部统一走 EventPublisher）
- `IEventModuleFactory`
  - `TryCreate(string name, out IEventModule module)`：基于 YAML 名称创建模块实例
- `IEventRouteEvaluator`
  - `TryGetEventType(EventEnvelope ...)`
  - `TryGetStepType(EventEnvelope ...)`（用于 Cognitive step 路由）
- `IRouteBypassModule`
  - 标记接口：**不受 `event_routes` 过滤**（例如 StepExecution 类模块）

## 3) 装配方式（YAML）

### 3.1 event_modules

```yaml
extensions:
  event_modules: "demo_chat_trace,step_execution_handler"
```

- 逗号分隔，大小写不敏感；
- 由 `RoleAgentFactory` 调用 `IEventModuleFactory` 创建。

### 3.2 event_routes

`event_routes` 是可选路由规则，支持 YAML list 或行式 DSL：

```yaml
extensions:
  event_modules: "demo_chat_trace,step_execution_handler"
  event_routes: |
    - when: event.type == "aevatar.agents.ai.core.ChatRequestEvent"
      to: demo_chat_trace
    - when: event.type == "aevatar.agents.ai.core.ChatResponseEvent"
      to: demo_chat_trace
```

**规则语义**

- `event.type`：来自 `EventEnvelope.Payload.TypeUrl` 的最后段（完整 proto name）
- `event.step_type`：由 `IEventRouteEvaluator` 提供（Cognitive 用于 `ExecuteStepRequestEvent.StepType`）

如果配置了 `event_routes`：

- 模块会被 `RoutedEventModule` 包装，只有命中的路由才会触发；
- 实现 `IRouteBypassModule` 的模块**不会被路由过滤**。

## 4) 分发顺序

`RoleAIGAgent` 内部使用 **排序 + lock‑free 快照**：

- 排序键：`Priority` → `Name`
- 每个模块独立 try/catch（最佳努力，不影响其它模块）

## 5) 常见模块

### 5.1 StepExecution（Cognitive）

- `CognitiveEventModuleFactory` 提供：
  - `step_execution_handler` / `step_executor`
  - `coordinator_*`（workflow/vote/llm 等）
- `CognitiveEventRouteEvaluator` 支持 `event.step_type`

### 5.2 Demo 模块

示例：`demo_chat_trace` 监听 `ChatRequestEvent/ChatResponseEvent`，转成 `ExecutionTraceEvent`。

## 6) 注意事项

- **跨边界事件必须 Protobuf**（IEventModule 只处理 `EventEnvelope` / Protobuf payload）。
- 模块逻辑应保持**幂等 & 轻量**，避免阻塞。
- 复杂流程建议拆分多个模块，通过 `event_routes` 精准路由。

## 7) 相关代码入口

- `RoleAIGAgent`：事件分发与模块注册
- `RoleAgentFactory`：YAML 装配入口
- `EventRoute` / `RoutedEventModule`：路由规则解析/过滤

