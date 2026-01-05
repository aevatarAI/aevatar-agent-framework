# AG-UI 集成指南

## 什么是 AG-UI？

AG-UI (Agent UI) 是一个标准化的 Agent 事件流协议，用于在 Web UI 中实时展示 Agent 的执行状态、对话历史和推理过程。它提供了一套统一的事件类型和流式传输机制，让前端能够以一致的方式展示不同 Agent 系统的运行状态。

## 为什么需要 AG-UI？

### 1. **标准化前端对接**

在没有 AG-UI 之前，每个 Agent 系统都需要自定义事件格式和 SSE 端点：

```javascript
// 每个项目都要写一遍类似的代码
app.MapGet("/api/sessions/{id}/events", async (sessionId, ctx) => {
    // 自定义事件格式
    await ctx.Response.WriteAsync($"data: {customJson}\n\n");
});
```

**问题**：
- 前端代码无法复用
- 不同系统的 UI 组件无法通用
- 新项目需要重新实现一遍事件流

**AG-UI 解决方案**：
- 统一的事件类型（`RUN_STARTED`, `TEXT_MESSAGE_CONTENT`, `STATE_SNAPSHOT` 等）
- 标准化的 SSE 端点命名（`/api/sessions/{id}/agui/events`）
- 前端 SDK 可以直接对接任何符合 AG-UI 协议的后端

### 2. **解决重连问题**

传统 SSE 实现面临的核心问题：

```
用户刷新页面 → 重连 SSE → 需要 replay 所有历史事件
```

**问题场景**：
- Agent 运行了 1 小时，产生了 10,000 条 token 事件
- 用户刷新页面，需要等待 10,000 条事件全部重放
- 前端卡顿，用户体验极差

**AG-UI 解决方案**：
- **快照优先策略**：重连时先发送 `MESSAGES_SNAPSHOT` 和 `STATE_SNAPSHOT`
- 只重放关键状态，不重放所有中间事件
- 用户立即看到最新状态，无需等待

### 3. **支持多运行时**

Aevatar Agent Framework 支持三种运行时：
- **Local**: 本地内存执行
- **Orleans**: 分布式虚拟 Actor
- **ProtoActor**: 高性能 Actor 系统

**问题**：
- 不同运行时的 Agent ID 格式不同
- 状态获取方式不同
- 事件传播机制不同

**AG-UI 解决方案**：
- 框架层统一抽象（`Aevatar.Agents.AGUI.AgUiBootstrap`）
- 自动适配不同运行时的 ID 格式
- 通过 `IGAgentActorManager` 统一访问接口
- 前端无需关心后端运行时类型

## AG-UI 带来的好处

### 1. **前端开发效率提升**

**之前**：每个项目需要实现
- 自定义事件解析
- 状态同步逻辑
- 重连处理
- 消息去重

**现在**：使用 AG-UI SDK
```javascript
import { AgUiClient } from '@agui/sdk';

const client = new AgUiClient('/api/sessions/123/agui/events');

client.on('TEXT_MESSAGE_CONTENT', (event) => {
    // 自动处理流式文本
    appendMessage(event.messageId, event.delta);
});

client.on('STATE_SNAPSHOT', (event) => {
    // 自动恢复状态
    restoreState(event.snapshot);
});
```

**收益**：
- 前端代码减少 70%+
- 新项目接入时间从 2 天降至 2 小时
- UI 组件可以在不同项目间复用

### 2. **用户体验提升**

#### 即时状态恢复

```
刷新页面 → 立即显示最新消息和状态 → 无需等待重放
```

#### 流畅的流式输出

```
Token 1 → Token 2 → Token 3 → ... → 完整消息
```

AG-UI 的 `TEXT_MESSAGE_CONTENT` 事件支持增量更新，用户可以实时看到 Agent 的思考过程。

#### 状态同步

```
初始状态 → 增量更新 1 → 增量更新 2 → ... → 最新状态
```

使用 `STATE_SNAPSHOT` + `STATE_DELTA`（JSON Patch），前端可以高效地同步复杂状态。

### 3. **可观测性增强**

AG-UI 提供标准化的事件类型，便于：

#### 统一监控

```javascript
// 所有系统都发送相同格式的事件
monitor.on('RUN_STARTED', trackRun);
monitor.on('RUN_ERROR', alertError);
monitor.on('STEP_FINISHED', logStep);
```

#### 调试友好

- 事件类型清晰（`RUN_STARTED` vs `STEP_STARTED`）
- 时间戳统一（Unix epoch milliseconds）
- 关联 ID 支持（`threadId`, `runId`, `messageId`）

#### 回放支持

标准事件格式使得事件回放变得简单：

```javascript
const events = await loadEventsFromStorage();
const player = new AgUiEventPlayer(events);
player.play(); // 自动重放所有事件
```

### 4. **系统解耦**

#### 前后端解耦

```
前端 ←→ AG-UI Protocol ←→ 后端
```

前端只需要知道 AG-UI 协议，不需要了解：
- 后端使用的运行时（Local/Orleans/ProtoActor）
- Agent 的具体实现细节
- 事件如何生成和传播

#### 多系统集成

不同 Agent 系统可以共享同一套前端：

```
┌─────────────────┐
│   AG-UI SDK     │
└────────┬────────┘
         │
    ┌────┴────┐
    │         │
┌───▼───┐ ┌──▼────┐
│Axiom  │ │Paper  │
│Reason │ │Review │
└───────┘ └───────┘
```

### 5. **扩展性**

#### Custom 事件支持

AG-UI 提供 `CustomEvent` 类型，支持系统特定的扩展：

```csharp
new CustomEvent
{
    Name = "aevatar.axiom.progress",
    Value = new { phase, progress, tokens }
}
```

**好处**：
- 保持协议兼容性
- 支持系统特定需求
- 不影响标准事件流

#### 版本兼容

AG-UI 协议设计考虑了版本兼容：
- 新事件类型向后兼容
- 可选字段支持渐进式升级
- 前端可以优雅降级

## 集成示例

### 后端集成（AxiomReasoning）

```csharp
// 1. 使用框架层提供的 Bootstrap
var bootstrap = await AxiomAgUiBootstrap.BuildMessagesSnapshotAsync(
    session,
    actorManager,
    memoryFactory,
    maxAssistantMessages: 60,
    ct);

// 2. 转换事件流
await foreach (var evt in AxiomAgUiEventStream.BuildAsync(
    session,
    session.EventHub.SubscribeAsync(replay: false, ct: ct),
    bootstrap.Messages,
    initialGraph,
    bootstrap.ExtraEvents,
    ct))
{
    // 3. 发送标准 AG-UI 事件
    var json = JsonSerializer.Serialize(evt, jsonOptions);
    await ctx.Response.WriteAsync($"data: {json}\n\n", ct);
}
```

### 前端集成

```javascript
// 使用 AG-UI SDK
import { AgUiClient } from '@agui/sdk';

const client = new AgUiClient('/api/sessions/123/agui/events');

// 监听标准事件
client.on('MESSAGES_SNAPSHOT', (event) => {
    // 恢复消息历史
    event.messages.forEach(msg => renderMessage(msg));
});

client.on('TEXT_MESSAGE_CONTENT', (event) => {
    // 流式更新消息
    appendText(event.messageId, event.delta);
});

client.on('STATE_SNAPSHOT', (event) => {
    // 恢复状态
    restoreGraph(event.snapshot.graph);
});

client.on('STATE_DELTA', (event) => {
    // 增量更新状态
    applyPatch(event.delta);
});
```

## 最佳实践

### 1. **快照优先**

重连时优先发送快照，避免重放大量中间事件：

```csharp
// ✅ 正确：先发快照
yield return new MessagesSnapshotEvent { Messages = messages };
yield return new StateSnapshotEvent { Snapshot = state };

// ❌ 错误：重放所有事件
await foreach (var evt in eventHub.SubscribeAsync(replay: true))
    yield return evt;
```

### 2. **增量更新**

使用 `STATE_DELTA` 而非完整快照：

```csharp
// ✅ 正确：增量更新
yield return new StateDeltaEvent 
{ 
    Delta = BuildJsonPatch(prevState, newState) 
};

// ❌ 错误：每次都发完整状态
yield return new StateSnapshotEvent { Snapshot = newState };
```

### 3. **流式文本**

使用 `TEXT_MESSAGE_CONTENT` 而非完整消息：

```csharp
// ✅ 正确：流式输出
yield return new TextMessageContentEvent 
{ 
    MessageId = messageId,
    Delta = token 
};

// ❌ 错误：等待完整消息
yield return new MessagesSnapshotEvent 
{ 
    Messages = [completeMessage] 
};
```

### 4. **错误处理**

使用 `RUN_ERROR` 而非抛出异常：

```csharp
// ✅ 正确：发送错误事件
yield return new RunErrorEvent 
{ 
    Message = ex.Message,
    Code = "AXIOM_SESSION_ERROR"
};

// ❌ 错误：直接抛出异常
throw new Exception(ex.Message);
```

## 总结

AG-UI 集成带来的核心价值：

1. **标准化**：统一事件格式，前端代码可复用
2. **性能**：快照优先策略，重连无需等待
3. **解耦**：前后端通过协议通信，系统解耦
4. **可观测**：标准化事件便于监控和调试
5. **扩展性**：支持 Custom 事件，保持兼容性

**推荐**：所有新的 Agent 系统都应该集成 AG-UI，以获得更好的前端开发体验和用户体验。

## 相关文档

- [AG-UI 技术架构](./src/Aevatar.Agents.Cognitive/docs/AGUI.md) - 详细的技术实现
- [AxiomReasoning 架构](./cognitive-mesh/Aevatar.AxiomReasoning/docs/ARCHITECTURE.md) - 实际应用案例

