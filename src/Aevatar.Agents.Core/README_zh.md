# Aevatar.Agents.Core

Aevatar Agent Framework 的核心实现层：Agent 基类、事件处理/路由、可观测性等。

## 职责
- 基于 Abstractions 提供 Agent 编程模型的核心实现。
- 提供事件处理发现/执行、事件路由等基础能力。
- 提供通用基础设施（观测、CQRS/EventSourcing 辅助等）。

## 主要功能
- 运行时无关设计（多运行时）。
- 事件驱动架构。
- 跨边界类型 Protobuf 优先。

## 公开 API 入口（自动扫描）
- `GAgentActorBase`
- `GAgentBase`
- `EventHandlerMetadata`
- `GAgentManager`
- `EventTypeInfo`
- `IEventHandlerDiscoverer`
- `ReflectionEventHandlerDiscoverer`
- `ProtobufEventTypeResolver`
- `IntervalSnapshotStrategy`
- `ISnapshotStrategy`

## GAgentBase（核心 Agent 基类）
`GAgentBase` 是业务逻辑的标准编程模型，运行在 `GAgentActor` 包装器之上，专注于事件驱动的状态演进。

### 变体
- `GAgentBase<TState>`：带 Protobuf State 的基础 Agent。
- `GAgentBase<TState, TEvent>`：事件类型过滤，仅处理可赋值为 `TEvent` 的事件（减少反序列化开销）。
- `GAgentBase<TState, TEvent, TConfiguration>`：增加 Protobuf 配置对象，便于运行时注入。

### 生命周期与状态
- **State 必须来自 Protobuf 生成**，由运行时创建；不要手动 `State = new ...`。
- 在 `OnActivateAsync` 中初始化 State（先调用 `base`），在 `OnDeactivateAsync` 中清理（最后调用 `base`）。
- 必须提供**无参构造函数**用于激活。
- 必须实现 `GetDescriptionAsync()` 返回 Agent 描述。

### 事件处理模型
- 事件处理器发现方式：
  - `[EventHandler]` 处理具体事件类型。
  - `[AllEventHandler]` 处理完整 `EventEnvelope`。
  - 约定：`HandleAsync` / `HandleEventAsync` 命名方法。
- 处理器签名要求：
  - `public`/`protected`，返回 `Task` 或 `Task<T>`。
  - 仅一个参数（事件消息或 `EventEnvelope`）。
- 执行顺序可通过 **Priority** 控制（数值越小越先执行）。
- 默认不处理自身发布的事件，可按需开启。

### 事件发布
- `PublishAsync(evt, EventDirection.Up|Down|Both)` 控制传播方向：
  - **Up**：向父级流传播（兄弟广播）。
  - **Down**：向子级流传播（层级广播）。
  - **Both**：双向广播。

### 最小示例
```csharp
public class CounterAgent : GAgentBase<CounterState, CounterEvent>
{
    public CounterAgent() : base() { }

    public override Task<string> GetDescriptionAsync() =>
        Task.FromResult($"Counter: {State.Count}");

    public override async Task OnActivateAsync(CancellationToken ct = default)
    {
        await base.OnActivateAsync(ct);
        State.Count = 0;
    }

    [EventHandler]
    public async Task HandleAsync(CounterEvent evt)
    {
        State.Count += evt.Delta;
        await PublishAsync(new CounterUpdatedEvent { Count = State.Count });
    }
}
```

> 注意：`TState`、`TEvent`、`TConfiguration` 若跨越运行时边界，必须使用 **Protocol Buffers** 类型。

## 是否建议发布为 NuGet
- **建议**：是（建议作为独立 NuGet 包发布）。
- **原因**：属于基础能力模块，通常会被下游系统直接引用。
- **打包建议**：核心包保持轻依赖；数据库/Provider 等外部集成拆成独立可选包。

## 构建

```bash
dotnet build src/Aevatar.Agents.Core/Aevatar.Agents.Core.csproj
```

## 单元测试

- 测试覆盖面评审与入口请见 `docs/Tests.md`。

## 文档

- 项目文档：`docs/`
- 仓库级文档：仓库根目录 `docs/`

## 约束/约定

- 跨边界类型（State/Event/Config 等）必须使用 **Protocol Buffers** 定义。
