# AG-UI Integration

## Overview

AG-UI (Agent UI) 是一个标准化的 Agent 事件流协议，用于在 Web UI 中实时展示 Agent 的执行状态和对话历史。

## 架构

```
┌─────────────────────────────────────────────────────────┐
│              Aevatar.Agents.AGUI                        │
│  (独立框架层项目)                                        │
│                                                          │
│  ┌──────────────────────────────────────────────────┐   │
│  │  AgUiEvents.cs                                   │   │
│  │  - AgUiEvent (base)                              │   │
│  │  - RunStartedEvent / RunFinishedEvent            │   │
│  │  - StepStartedEvent / StepFinishedEvent         │   │
│  │  - TextMessageStart/Content/End                   │   │
│  │  - StateSnapshotEvent / StateDeltaEvent          │   │
│  │  - MessagesSnapshotEvent                          │   │
│  │  - CustomEvent                                    │   │
│  └──────────────────────────────────────────────────┘   │
│                                                          │
│  ┌──────────────────────────────────────────────────┐   │
│  │  AgUiBootstrap.cs                                │   │
│  │  - CollectAssistantMessagesAsync()               │   │
│  └──────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────┘
                          │
                          │ used by
                          ▼
┌─────────────────────────────────────────────────────────┐
│         Aevatar.AxiomReasoning.AgUi                     │
│                                                          │
│  ┌──────────────────────────────────────────────────┐   │
│  │  AxiomAgUiBootstrap.cs                           │   │
│  │  - BuildMessagesSnapshotAsync()                  │   │
│  │  - TryBuildGraphSnapshotAsync()                  │   │
│  └──────────────────────────────────────────────────┘   │
│                                                          │
│  ┌──────────────────────────────────────────────────┐   │
│  │  AxiomAgUiEventStream.cs                        │   │
│  │  - BuildAsync()                                  │   │
│  │  - AxiomEvent → AgUiEvent 转换                  │   │
│  └──────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────┘
```

## 核心组件

### AgUiEvents.cs (在 Aevatar.Agents.AGUI 项目中)

定义了 AG-UI 协议的标准事件类型：

- **Run Lifecycle**: `RunStartedEvent`, `RunFinishedEvent`, `RunErrorEvent`
- **Step Lifecycle**: `StepStartedEvent`, `StepFinishedEvent`
- **Text Streaming**: `TextMessageStartEvent`, `TextMessageContentEvent`, `TextMessageEndEvent`
- **State Sync**: `StateSnapshotEvent`, `StateDeltaEvent`
- **Message Sync**: `MessagesSnapshotEvent`, `AgUiMessage`
- **Extension**: `CustomEvent`

### AgUiBootstrap.cs

提供“assistant 消息快照”的通用实现：从多个 Actor 的 `State.History` 收集每个 `step_id` 的最终 assistant 内容，并生成 `MESSAGES_SNAPSHOT` 需要的 `AgUiMessage[]`。

## 使用示例

### 在 AxiomReasoning 中使用

```csharp
// 1. 构建消息快照
var bootstrap = await AxiomAgUiBootstrap.BuildMessagesSnapshotAsync(
    session,
    actorManager,
    maxAssistantMessages: 60,
    ct);

// 2. 构建图快照
var initialGraph = await AxiomAgUiBootstrap.TryBuildGraphSnapshotAsync(
    graphStore,
    sessionId,
    ct);

// 3. 转换事件流
await foreach (var evt in AxiomAgUiEventStream.BuildAsync(
    session,
    session.EventHub.SubscribeAsync(replay: false, ct: ct),
    bootstrap.Messages,
    initialGraph,
    bootstrap.ExtraEvents,
    ct))
{
    // 发送到 SSE
    await ctx.Response.WriteAsync($"data: {json}\n\n", ct);
}
```

## 设计原则

1. **协议标准化**: 所有事件遵循 AG-UI Protocol
2. **快照优先**: 重连时发送快照而非重放所有事件
3. **Best-effort**: 所有操作都是 best-effort，不影响主流程
4. **运行时无关**: 支持 Local/Orleans/ProtoActor 运行时

## 扩展点

- **CustomEvent**: 用于应用特定的扩展事件
- **StateDeltaEvent**: 使用 JSON Patch (RFC 6902) 进行增量更新
- **消息元数据**: 通过 CustomEvent 传递 Worker/Step 元数据

