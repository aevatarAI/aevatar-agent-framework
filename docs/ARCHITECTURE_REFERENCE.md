# 🌌 Aevatar Agent Framework 架构文档

## 📋 架构总览

Aevatar Agent Framework 是一个**多运行时、事件驱动**的智能体框架，支持在不同的运行时环境（Local、ProtoActor、Orleans）中无缝切换，同时提供完整的 EventSourcing 支持。

### 核心理念

> **语言的震动构建现实** —— 每个事件是计算宇宙中的一次震动

框架将 Agent 抽象为独立的计算单元，通过事件的流动和共振实现分布式协作。

## 🏗️ 分层架构

```
┌─────────────────────────────────────────────────────────┐
│                    应用层 (Applications)                 │
│                    Demo.Api / Examples                  │
├─────────────────────────────────────────────────────────┤
│                  运行时抽象层 (IGAgentActor)              │
│            统一的 Actor 接口，隔离具体运行时实现             │
├──────────────┬──────────────────┬───────────────────────┤
│    Local     │   ProtoActor     │      Orleans          │
│   Runtime    │    Runtime       │     Runtime           │
│   (进程内)    │   (Actor模型)     │    (虚拟Actor)        │
├──────────────┴──────────────────┴───────────────────────┤
│               业务逻辑层 (IGAgent)                        │
│           Agent 业务逻辑定义与事件处理                      │
├─────────────────────────────────────────────────────────┤
│                  核心基类 (GAgentBase)                    │
│                事件处理、生命周期、状态管理                  │
├─────────────────────────────────────────────────────────┤
│                 EventSourcing 层                        │
│             事件存储、重放、快照 (可选)                     │
├─────────────────────────────────────────────────────────┤
│              消息序列化层 (Protobuf)                      │
│              统一的消息格式与高效序列化                     │
└─────────────────────────────────────────────────────────┘
```

### 层次职责

1. **应用层**：具体的业务应用实现
2. **运行时抽象层**：屏蔽不同运行时的差异
3. **具体运行时**：三种可选的运行时实现
4. **业务逻辑层**：Agent 的业务逻辑定义
5. **核心基类**：提供通用的 Agent 功能
6. **EventSourcing 层**：可选的事件溯源支持
7. **序列化层**：基于 Protobuf 的高效序列化

## 📦 项目结构

### 核心抽象 (Abstractions)
```
Aevatar.Agents.Abstractions/
├── IGAgent.cs                    # Agent 业务接口
├── IGAgentActor.cs               # Actor 运行时接口
├── IGAgentActorFactory.cs        # Actor 工厂接口
├── IGAgentActorManager.cs        # Actor 管理器接口
├── IGAgentFactory.cs             # Agent 工厂接口（用于 DI）
├── IEventPublisher.cs            # 事件发布接口
├── IMessageStream.cs             # 消息流接口
├── IMessageStreamProvider.cs     # 消息流提供者接口
├── IStreamNotFoundHandler.cs     # 流未找到处理器
├── StreamingOptions.cs           # 流配置
├── abstrations_messages.proto    # Protobuf 消息定义
├── EventSourcing/                # EventSourcing 接口
│   └── IEventStore.cs
├── Persistence/                  # 持久化接口
│   ├── IStateStore.cs
│   └── IConfigStore.cs
└── Attributes/                   # 特性标记
    ├── EventHandlerAttribute.cs
    ├── AllEventHandlerAttribute.cs
    └── ConfigurationAttribute.cs
```

### 核心实现 (Core)
```
Aevatar.Agents.Core/
├── GAgentBase.cs                        # 无状态基类（事件调度、生命周期）
├── GAgentBase.TState.cs                 # 带状态 + EventSourcing 的核心实现
├── GAgentBase.TState.TConfig.cs         # 带状态 + 配置（ConfigStore）
├── GAgentActorBase.cs                   # Actor 包装器基类
├── DependencyInjection/
│   ├── AevatarBuilder.cs                # Fluent 构建器
│   ├── IAevatarBuilder.cs               # 构建器接口
│   └── AevatarBuilderStorageExtensions.cs
├── Extensions/
│   └── ServiceCollectionExtensions.cs   # AddAevatarAgentSystem 扩展
├── EventRouting/                        # 事件路由
├── EventSourcing/                       # EventSourcing 实现
├── Hierarchy/ActorHierarchyCoordinator.cs
├── Persistence/                         # 内存持久化实现
│   ├── InMemoryStateStore.cs
│   ├── InMemoryConfigStore.cs
│   └── InMemoryEventStore.cs
├── StateProtection/                     # 状态保护机制
└── Observability/                       # 可观测性
```

### 持久化模块（Backend-first 命名）

持久化相关项目采用 **后端优先** 的命名与分层（Backend = 稳定边界；Capability = 可插拔能力），以保持与 MongoDB/Supabase 现有结构一致，并避免 “Graph/Backend 两套命名轴混用” 的长期混乱。

详见：`docs/PERSISTENCE_PROJECT_LAYOUT.md`

### 运行时实现

#### Local Runtime (进程内)
```
Aevatar.Agents.Runtime.Local/
├── LocalGAgentActor.cs               # In-memory actor实现
├── LocalGAgentActorFactory.cs
├── LocalGAgentActorManager.cs
├── LocalMessageStream.cs             # 基于 Channel 的消息流
├── LocalMessageStreamRegistry.cs
├── LocalStreamNotFoundHandler.cs     # 流未找到处理器
├── Subscription/LocalSubscriptionManager.cs
└── ServiceCollectionExtensions.cs    # AddAevatarLocalRuntime
```

#### ProtoActor Runtime (Actor 模型)
```
Aevatar.Agents.Runtime.ProtoActor/
├── ProtoActorGAgentActor.cs
├── ProtoActorGAgentActorFactory.cs
├── ProtoActorGAgentActorManager.cs
├── ProtoActorMessageStream.cs
├── AgentActor.cs                     # Proto.Actor IActor 实现
└── ServiceCollectionExtensions.cs
```

#### Orleans Runtime (虚拟 Actor)
```
Aevatar.Agents.Runtime.Orleans/
├── OrleansGAgentGrain.cs             # 运行在 Silo 内的 Grain
├── OrleansGAgentActor.cs             # Actor 包装器
├── OrleansGAgentActorFactory.cs
├── OrleansGAgentActorManager.cs
├── OrleansMessageStream.cs
├── OrleansStreamNotFoundHandler.cs   # 自动唤醒支持
└── Subscription/

### AI 模块
```
Aevatar.Agents.AI.Core/
├── AIGAgentBase.cs                   # AI Agent 基类
├── AIGAgentBase.Tools.cs             # 工具/Function Calling 能力（已合并进 AIGAgentBase）
├── AIGAgentBase.AgentSkills.cs       # Agent Skills（SKILL.md）按需加载 + 可选导入 dotnet-file tools
├── AIGAgentBase.TCustomState.cs      # 自定义状态
├── AIGAgentBase.TCustomState.TCustomConfig.cs
├── AIGAgentFactory.cs                # AI Agent 工厂
├── ConversationHistoryManager.cs     # 对话历史管理
├── Embeddings/                       # Embedding 支持
├── WithTool/                         # 工具系统（原 Aevatar.Agents.AI.WithTool，已并入 AI.Core）
│   ├── Abstractions/                 # 工具接口 & 定义
│   ├── Tools/                        # ToolManager + 内置/核心/自定义工具
│   ├── MCP/                          # Model Context Protocol 支持
│   └── tool_messages.proto           # Tool 相关事件/消息（Protobuf）
└── ai_messages.proto

NOTE:
- 为了保持兼容性，工具相关类型仍使用命名空间 `Aevatar.Agents.AI.WithTool.*`，
  但它们现在由 `Aevatar.Agents.AI.Core` 程序集提供（不再存在独立的 `Aevatar.Agents.AI.WithTool` 工程）。

Aevatar.Agents.AI.WithProcessStrategy/
├── AIGAgentWithProcessStrategy.cs    # 带处理策略的 AI Agent
└── Strategies/                       # CoT, ReAct 等策略
```

### 插件模块
```
Aevatar.Agents.Plugins.MassTransit/
├── MassTransitMessageStream.cs       # MassTransit 消息流
├── MassTransitMessageStreamProvider.cs
├── StreamMessageDispatcher.cs        # 消息分发器
└── ServiceCollectionExtensions.cs    # AddMassTransitStreamPlugin
```

## 🔄 事件系统

### EventEnvelope (Protobuf)
```protobuf
message EventEnvelope {
  string id = 1;
  google.protobuf.Timestamp timestamp = 2;
  int64 version = 3;
  google.protobuf.Any payload = 4;

  string correlation_id = 5;
  string publisher_id = 6;
  EventDirection direction = 7;
  bool should_stop_propagation = 8;
  int32 max_hop_count = 9;
  int32 current_hop_count = 10;
  int32 min_hop_count = 11;
  string message = 12;
  repeated string publishers = 13;
}
```

### 事件路由方向

- **Down**: 向子 Agent 传播
- **Up**: 向父 Agent 传播（同时广播给兄弟）
- **Both**: 同时向上、向下各发送一条 Envelope

### 事件处理器

```csharp
// 特定事件处理器
[EventHandler(Priority = 100)]
public async Task HandleMyEvent(MyEvent evt) { }

// 所有事件处理器
[AllEventHandler(AllowSelfHandling = false)]
protected async Task ForwardAllEvents(EventEnvelope envelope) { }

// 默认处理器（方法名约定）
public async Task HandleAsync(GeneralConfigEvent evt) { }
```

## 💾 EventSourcing 支持

### 事件存储抽象
```csharp
public interface IEventStore
{
    Task<long> AppendEventsAsync(
        Guid agentId,
        IEnumerable<AgentStateEvent> events,
        long expectedVersion,
        CancellationToken ct = default);

    Task<IReadOnlyList<AgentStateEvent>> GetEventsAsync(
        Guid agentId,
        long? fromVersion = null,
        long? toVersion = null,
        int? maxCount = null,
        CancellationToken ct = default);

    Task<long> GetLatestVersionAsync(Guid agentId, CancellationToken ct = default);
    Task SaveSnapshotAsync(Guid agentId, AgentSnapshot snapshot, CancellationToken ct = default);
    Task<AgentSnapshot?> GetLatestSnapshotAsync(Guid agentId, CancellationToken ct = default);
}
```

### 使用示例
```csharp
public class BankAccountAgent : GAgentBaseWithEventSourcing<BankAccountState>
{
    protected override Task ApplyStateChangeEventAsync<TEvent>(TEvent evt)
    {
        switch (evt)
        {
            case MoneyDeposited deposited:
                _state.Balance += deposited.Amount;
                break;
            case MoneyWithdrawn withdrawn:
                _state.Balance -= withdrawn.Amount;
                break;
        }
        return Task.CompletedTask;
    }
}
```

## 📝 序列化规则 (关键约束)

> **🔴 强制要求：所有需要序列化的类型必须使用 Protobuf 定义！**

这是框架的核心约束，不是可选项：

### 必须使用 Protobuf 的类型
1. **Agent State** - 所有 `IGAgent<TState>` 的 State 类型
2. **Event Messages** - 所有通过事件系统传递的消息
3. **Event Sourcing Events** - 所有需要持久化的事件

### 为什么强制使用 Protobuf？
- **Orleans Streaming** 使用 `byte[]` 传输，需要可靠的序列化
- **跨运行时兼容** - Local/ProtoActor/Orleans 之间无缝切换
- **版本演进** - Protobuf 提供向后兼容性保证
- **性能优越** - 比 JSON 快 3-5 倍，体积小 2-3 倍

### 项目配置示例
```xml
<ItemGroup>
  <PackageReference Include="Google.Protobuf" Version="3.28.3" />
  <PackageReference Include="Grpc.Tools" Version="2.67.0">
    <PrivateAssets>all</PrivateAssets>
    <IncludeAssets>runtime; build; native; contentfiles; analyzers</IncludeAssets>
  </PackageReference>
</ItemGroup>

<ItemGroup>
  <Protobuf Include="*.proto" />
</ItemGroup>
```

### 正确示例
```protobuf
// my_agent.proto
message MyAgentState {
    string agent_id = 1;
    int32 version = 2;
    repeated string child_ids = 3;
}

message MyEvent {
    string event_id = 1;
    google.protobuf.Timestamp timestamp = 2;
    google.protobuf.Any payload = 3;
}
```

### 错误示例
```csharp
// ❌ 永远不要这样做！
public class MyAgentState  // 手动定义的类无法正确序列化
{
    public string AgentId { get; set; }
    public int Version { get; set; }
}
```

> **记住：如果数据需要跨运行时边界传输，就必须使用 Protobuf 定义它！**
>
> 详细规则请查看 [全能指南 - 序列化](AEVATAR_FRAMEWORK_GUIDE.md#defining-state--events-protobuf)

## 🧠 Embedding 通道

- `LLMProviderConfig` 新增 `Embeddings` 节点，描述 `model/deployment/apiKey/endpoint/dimensions` 等向量模型参数。配置缺失或未显式启用时，Agent 将不具备向量能力。这是遵循 **"Pay for what you use"** 原则，避免未声明的外部依赖初始化带来的资源开销。
- `IAIAgentEmbeddingFactory` 负责把配置翻译成 `IEmbeddingGenerator<string, Embedding<float>>`（默认实现 `MEAIEmbeddingFactory` 使用 Microsoft.Extensions.AI OpenAI/Azure SDK，并复用统一的 `ClientLoggingOptions`）。
- `AIGAgentBase` 在 `InitializeAsync` 时读取 `Embeddings` 配置并注入生成器，提供 `GenerateEmbeddingAsync`、`GenerateEmbeddingsAsync` 与 `CosineSimilarity` 等受保护 API，方便派生 Agent 做记忆召回、工具排序等语义操作。
- `AIGAgentFactory` 会自动注入 `IAIAgentEmbeddingFactory`（类似 LLM Provider Factory），因此业务 Agent 只需声明 `LLMProviders:Providers:<name>:Embeddings` 即可获得统一的 embedding 管道。

## 🔌 消息流 (Streaming)

每个 Agent 拥有独立的消息流，支持异步消息传递和背压控制。

### Local: Channel-based
```csharp
Channel.CreateBounded<byte[]>(new BoundedChannelOptions(100)
{
    FullMode = BoundedChannelFullMode.Wait,
    SingleReader = false,
    SingleWriter = false
});
```

### ProtoActor: EventStream
```csharp
_actorSystem.EventStream.Subscribe<T>(handler);
```

### Orleans: Streams
```csharp
var stream = streamProvider.GetStream<byte[]>(StreamId.Create("AgentStream", agentId));
```

## 📊 性能指标

> 以下数值为当前内部压测的目标区间，用于指导优化方向，并非公开基准。

| 指标 | Local | ProtoActor | Orleans |
|-----|-------|-----------|---------|
| 启动时间 | < 1ms | ~10ms | ~100ms |
| 消息延迟 | < 0.1ms | < 1ms | < 5ms |
| 吞吐量 | 100K/s | 50K/s | 20K/s |
| 内存占用 | ~50KB | ~100KB | ~500KB |
| 分布式 | ❌ | ✅ | ✅ |
| 持久化 | 内存 | 可选 | 完整 |

## 🔄 与旧架构对比

### 旧架构 (old/framework)

**特点：**
- ❌ **强依赖 Orleans**：所有 Agent 必须是 Orleans Grain
- ❌ **复杂的依赖**：集成 ABP Framework
- ✅ **功能丰富**：插件系统、权限管理
- ❌ **重量级**：启动慢、资源占用高
- ❌ **灵活性差**：难以在非 Orleans 环境运行

**架构：**
```
Application → Orleans Grain → ABP Framework → MongoDB
                ↓
         JournaledGrain
                ↓
         Event Sourcing
```

### 新架构 (当前)

**特点：**
- ✅ **运行时无关**：支持 Local/ProtoActor/Orleans
- ✅ **轻量级**：最小依赖，快速启动
- ✅ **灵活切换**：相同代码，不同运行时
- ✅ **渐进式**：按需选择功能
- ✅ **标准化**：Protobuf 消息格式

**架构：**
```
Application → IGAgentActor → [Local|ProtoActor|Orleans]
                ↓
            IGAgent (业务逻辑)
                ↓
          GAgentBase (通用功能)
                ↓
         [可选] EventSourcing
```

## 🎯 核心改进

### 1. 解耦 Orleans
- **旧**：GAgentBase 直接继承 JournaledGrain
- **新**：Orleans 只是三种运行时之一

### 2. 抽象分层
- **旧**：业务逻辑与 Orleans 混合
- **新**：清晰的接口层次（IGAgent → IGAgentActor）

### 3. 消息传递
- **旧**：Orleans Stream 硬编码
- **新**：IMessageStream 抽象，多种实现

### 4. 序列化
- **旧**：Orleans 序列化器
- **新**：统一 Protobuf（跨平台、高性能）

### 5. 测试友好
- **旧**：必须启动 Orleans Silo
- **新**：Local 运行时直接测试

### 6. 资源占用
- **旧**：~2GB 内存（Orleans + ABP）
- **新**：~50MB 内存（Local 运行时）

## 📈 迁移路径

### 从旧架构迁移

1. **Agent 定义迁移**
```csharp
// 旧
[GAgent]
public class MyAgent : GAgentBase<MyState, MyEvent>
{
    // Orleans 特定代码
}

// 新
public class MyAgent : GAgentBase<MyState>
{
    // 运行时无关代码
}
```

2. **事件处理迁移**
```csharp
// 旧：Orleans 特定
protected override void TransitionState(MyState state, MyEvent @event)

// 新：通用处理器
[EventHandler]
public async Task HandleMyEvent(MyEvent evt)
```

3. **依赖注入配置**
```csharp
// 旧：Orleans 配置
builder.Host.UseOrleans(siloBuilder => { /* 复杂配置 */ });

// 新：简单切换
services.AddSingleton<IGAgentActorFactory, LocalGAgentActorFactory>();
// 或
services.AddSingleton<IGAgentActorFactory, ProtoActorGAgentActorFactory>();
// 或
services.AddOrleansAgents();
```

## 🚀 使用建议

### 运行时选择

| 场景 | 推荐运行时 | 理由 |
|-----|----------|------|
| 开发测试 | Local | 无需配置，快速迭代 |
| 高性能单机 | ProtoActor | Actor 模型，高吞吐 |
| 分布式生产 | Orleans | 成熟方案，自动故障恢复 |
| 微服务 | ProtoActor | 轻量级，易集成 |
| 企业应用 | Orleans | 完整功能，运维友好 |

### 最佳实践

1. **强制 Protobuf**：所有 State/Event/Message 必须用 .proto 定义，不要手写 C# 类
2. **从 Local 开始**：开发时使用 Local 运行时，测试通过后切换到其他运行时
3. **渐进式采用**：先基础功能，后 EventSourcing
4. **事件优先**：使用事件驱动而非直接调用
5. **合理分层**：业务逻辑放在 IGAgent 层，运行时细节隔离在 IGAgentActor 层
6. **类型安全**：利用 Protobuf 的强类型，避免 dynamic 或 object

## 📊 架构决策记录 (ADR)

### ADR-001: 多运行时支持
- **状态**：已实现
- **决策**：支持 Local、ProtoActor、Orleans 三种运行时
- **理由**：不同场景需要不同的运行时特性
- **后果**：增加了抽象层，但提供了极大的灵活性

### ADR-002: Protobuf 序列化 (强制要求)
- **状态**：已实现
- **决策**：所有需要序列化的类型必须使用 Google Protobuf 定义
- **理由**：
  - Orleans Streaming 使用 byte[] 传输，需要可靠的序列化机制
  - 跨运行时兼容：Local/ProtoActor/Orleans 之间无缝切换
  - 高性能：比 JSON 快 3-5 倍，体积小 2-3 倍
  - 版本兼容：Protobuf 提供向后兼容性保证
- **约束**：
  - 禁止手动定义 State/Event/Message 的 C# 类
  - 必须通过 .proto 文件生成所有序列化类型
  - decimal 类型需要转换为 double 或使用整数表示（如分）
- **后果**：增加了 proto 文件维护成本，但确保了系统的可靠性和互操作性

### ADR-003: EventSourcing 可选
- **状态**：已实现
- **决策**：EventSourcing 作为可选功能，不强制使用
- **理由**：不是所有场景都需要事件溯源
- **后果**：框架更灵活，但需要明确选择

### ADR-004: 事件路由机制
- **状态**：已实现
- **决策**：支持 Up/Down/UpThenDown/Bidirectional 四种路由
- **理由**：覆盖层级 Agent 间的所有通信模式
- **后果**：强大的事件传播能力，需要防止循环

## 🎊 总结

Aevatar Agent Framework 通过**震动的共振**实现了：

1. **运行时自由**：一份代码，三种运行时
2. **架构优雅**：清晰分层，职责单一
3. **性能卓越**：从进程内到分布式的平滑扩展
4. **开发友好**：简单起步，渐进增强
5. **生产就绪**：完整的 EventSourcing 和监控支持

> **从 Orleans 的枷锁中解放，在多运行时的宇宙中自由震动** 🌌

---

**架构的本质是语言的结构显现**  
**每一层都是震动的不同频率**  
**在共振中，系统获得生命**

*Built with ❤️ by HyperEcho*
