# Aevatar.Agents.Cognitive 架构文档

## 目录结构

```
Aevatar.Agents.Cognitive/
├── Agents/                      # Agent 实现
│   ├── CognitiveCoordinatorGAgent.cs                     # CoordinatorAgent: state + lifecycle + step dispatcher
│   ├── CognitiveCoordinatorGAgent.Workflow.cs            # Workflow start/fail/output build
│   ├── CognitiveCoordinatorGAgent.Parallel.cs            # fan_out/parallel + worker completion
│   ├── CognitiveCoordinatorGAgent.Llm.cs                 # Coordinator-side LLM (via AIGAgentBase chat pipeline)
│   ├── CognitiveCoordinatorGAgent.Vote.cs                # vote consensus (semantic clustering + red-flag)
│   ├── CognitiveCoordinatorGAgent.StepEvents.cs          # step events for UI/observability
│   ├── CognitiveCoordinatorGAgent.Parameters.cs          # output parsing + parameter helpers + red-flag config
│   ├── CognitiveCoordinatorGAgent.Tools.cs               # tool_call/tool_evolve/tool_validate
│   └── (removed) CognitiveWorkerGAgent.cs                # Worker removed; RoleAIGAgent handles step requests
│   └── Shared/
│       └── CognitiveAIGAgentBase.cs                      # Shared: stateless LLM request + step history metadata
├── Engine/                      # 工作流引擎
│   └── WorkflowParser.cs              # YAML 工作流解析
├── Execution/                   # 执行器
│   ├── TransformExecutor.cs            # transform 原语执行器（token-free）
│   ├── RetrieveFactsExecutor.cs        # retrieve_facts 原语执行器（token-free）
│   └── CognitiveStepExecutionHandler.cs # RoleAIGAgent step handler (ExecuteStepRequestEvent)
│   └── WorkflowOrchestrator.cs         # workflow 编排主循环（抽离自 Coordinator）
│   └── CognitiveStepExecutor.cs         # Step 选择与事件派发（抽离自 Coordinator）
├── Primitives/                  # DSL 原语
│   ├── IPrimitive.cs                  # 原语上下文 + PrimitiveResult + 参数扩展
│   ├── WorkflowDefinition.cs          # WorkflowDefinition/StepDefinition/InputParameter + IWorkflowRegistry
│   └── WorkflowResult.cs              # 执行结果 + InMemoryWorkflowRegistry
├── Template/                    # 模板引擎
│   ├── TemplateEngine.cs              # 模板渲染
│   └── OutputParser.cs                # 输出解析
├── Utilities/                   # 工具类
│   ├── ProtoValueConverter.cs         # Protobuf 值转换
├── DependencyInjection/         # DI 扩展
│   └── ServiceCollectionExtensions.cs
├── workflows/                   # 内置工作流定义
│   ├── direct.yaml                    # 直接执行
│   ├── maker.yaml                     # MAKER 系统（投票 + 红旗 + 递归）
│   ├── uot-combinational.yaml         # UoT 组合式创意推理
│   ├── axiom_theorem_loop.yaml        # 公理 → 定理发现循环（Coordinator 提出，Workers 证明）
│   ├── axiom_reasoning.yaml           # 公理驱动逐步推理（每步 vote 共识）
│   ├── hypothesis_promotion_loop.yaml # 假设升级定理循环（HPL）
└── cognitive_messages.proto     # Protobuf 消息定义
```

## 核心组件

### 1. CoordinatorAgent（原 CognitiveCoordinatorGAgent）
**职责**: 工作流协调与执行

- 解析并执行 YAML 定义的工作流
- 协调 Worker 执行并行任务
- 管理投票共识流程
- 发送步骤事件供前端可视化

**实现形态**：`partial` 拆分（避免巨型文件、降低耦合）

- `CognitiveCoordinatorGAgent.Workflow.cs`（class=`CoordinatorAgent`）：启动/失败/输出构建/主循环
- `CognitiveCoordinatorGAgent.Parallel.cs`（class=`CoordinatorAgent`）：`fan_out`/`parallel` + Worker 完成事件聚合
- `CognitiveCoordinatorGAgent.Llm.cs`（class=`CoordinatorAgent`）：Coordinator LLM 调用（复用 `AIGAgentBase.ChatAsync/ChatStreamAsync`，含 streaming & 超时护栏）
- `CognitiveCoordinatorGAgent.Vote.cs`（class=`CoordinatorAgent`）：投票共识（语义聚类 + 红旗）
- `CognitiveCoordinatorGAgent.StepEvents.cs`（class=`CoordinatorAgent`）：步骤事件（UI/回放）
- `CognitiveCoordinatorGAgent.Parameters.cs`（class=`CoordinatorAgent`）：输出解析 + 参数/红旗配置解析

### 2. RoleAIGAgent + CognitiveStepExecutionHandler
**职责**: 并行任务执行

- RoleAIGAgent 作为执行体（YAML 角色驱动）
- `CognitiveStepExecutionHandler` 处理 `ExecuteStepRequestEvent`
- 执行 LLM 调用或 tool_call（复用 `AIGAgentBase.ChatAsync/ChatStreamAsync` 与 `ExecuteToolForWorkflowAsync`）
- 向上报告执行结果

### 2.1 CognitiveAIGAgentBase（Shared）
**职责**: 统一 Cognitive 系列 AI Agent 的“LLM 请求形态 + 历史落盘策略”

- **复用 AIGAgentBase chat 管线**：避免 Coordinator/Worker 各自手写 `LLMProvider.Generate*`
- **无状态 prompt**：`BuildLLMRequest` 只发送当前 step 的 user message（不 replay `State.History`）
- **UI hydration 专用 history**：通过 step-scope metadata（`step_id/step_type/agent_kind/...`）落盘到 `State.History`
- **禁用隐藏 LLM 总结**：history compaction 不触发 summary LLM call（返回 `null`）

### 3. ProtoValueConverter
**职责**: Protobuf 转换

- C# 对象 ↔ Protobuf Value
- Dictionary ↔ Protobuf Struct

```csharp
// 使用示例
var protoValue = ProtoValueConverter.ToProto(myObject);
var csharpObject = ProtoValueConverter.FromProto(protoValue);
```

### 4. 参数/聚合辅助（Coordinator 内聚）
**职责**：把“参数解析 / 红旗配置 / 输出解析 / reduce 聚合”等易分叉逻辑收敛到 Coordinator 内部，避免重复实现与语义漂移。

- `CognitiveCoordinatorGAgent.Parameters.cs`：参数解析 + red-flag 配置 + 输出解析
- `CognitiveCoordinatorGAgent.cs`：`ConvertToList` / `ApplyReducer`（fan_out reduce）

## 设计原则

1. **单一职责**: 每个类只做一件事
2. **依赖注入**: 通过构造函数注入依赖
3. **事件驱动**: Actor 间通过 Protobuf 事件通信
4. **可测试性**: 核心逻辑可独立测试
5. **静态工具优先**: 无状态操作使用静态方法

## DSL 可靠性护栏（关键约定）

- **结构化输出可用**：Worker 上报 *raw assistant response*，Coordinator 会按 `output` 类型解析为 Dictionary/List，供 `transform` 做 0-token 聚合与控制流决策。
- **可控超时**：`llm_call.timeout_seconds/idle_timeout_seconds` 与 `fan_out.timeout_seconds` 避免 hard hang。
- **失败可观测**：`fan_out.include_failures=true` 时失败子任务也会进入结果列表（`success=false`），便于统计与红旗记录。
- **defaults 注入**：workflow 顶层 `defaults` 可为 `llm_call/vote/fan_out` 注入默认参数，避免每步重复写。

## 数据流

```
YAML Workflow
     │
     ▼
┌─────────────────────┐
│   WorkflowParser    │ ──解析──▶ WorkflowDefinition
└─────────────────────┘
     │
     ▼
┌──────────────────────────────┐
│ CoordinatorAgent              │
│ (Workflow/Parallel/LLM/Vote)  │ ──协调──▶ 步骤执行
└──────────────────────────────┘
     │                 │
     │                 ├── fan_out / parallel ──▶ Workers
     │                 │
     └── EmitStepEvent ┴────────────────────────▶ 前端可视化
```

## 组件依赖关系

```
CoordinatorAgent
├── TemplateEngine          # 模板渲染
├── OutputParserFactory     # 输出解析
├── ProtoValueConverter     # Protobuf 转换
└── VoteEngine (MAKER)      # 投票共识
```

## 扩展点

1. **自定义步骤类型**: 在 `ExecuteStepAsync` 中添加新的 case
2. **自定义输出解析器**: 实现 `IOutputParser` 接口
3. **自定义 Red-Flag 策略**: 实现 `IRedFlagStrategy` 接口
4. **自定义聚合器**: 在 `CoordinatorAgent.ApplyReducer` 中添加（fan_out.reduce）

## 变更日志

- 2025-12: 新增 token-free 原语 `transform` / `retrieve_facts`，用于把确定性数据处理与相关事实选择从 LLM 中剥离，减少 token 浪费。
- 2025-12: DSL 支持 workflow-level `defaults` + `max_length/strict_parse/timeout_seconds/idle_timeout_seconds/include_failures` 护栏，使配置不再“写了但不生效”。 
- 2025-12: 新增工作流 `hypothesis_promotion_loop.yaml`（HPL：Hypothesis→验证→升级定理）。
- 2025-12: Coordinator 去味：移除未被引用的 `StepEventEmitter/FanOutExecutor`，并将 `CoordinatorAgent`（原 `CognitiveCoordinatorGAgent`）拆分为多个 `partial` 文件以控制复杂度。
- 2025-12: 去味：移除未被引用的 `ParameterResolver/*Primitive.cs`，补齐 DSL 数据模型（`WorkflowDefinition/StepDefinition`），并修正 Worker streaming 中间态事件的统计累加语义（只在终态累计 tokens/calls）。
- 2026-01: 合并版本化工作流，统一为 `maker` / `uot-combinational`。
