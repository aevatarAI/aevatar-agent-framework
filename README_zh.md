# Aevatar Agent Framework

🌌 **一份代码，多种运行时** - 基于Actor模型的分布式智能体框架

[![.NET](https://img.shields.io/badge/.NET-10.0-purple)](https://dotnet.microsoft.com)
[![License](https://img.shields.io/badge/License-MIT-blue)](LICENSE)
[![Status](https://img.shields.io/badge/Status-Production%20Ready-green)](https://github.com/aevatar/aevatar-agent-framework)

---

## 🎯 核心价值

### 写一次，到处运行

**同一份 Agent 代码**，可以无缝切换运行时：

```csharp
// 定义一次
public class MyAgent : GAgentBase<MyState> 
{
    [EventHandler]
    public async Task HandleEvent(MyEvent evt)
    {
        State.Count++;
        await PublishAsync(new ResultEvent { Count = State.Count });
    }
}

// 切换运行时只需一行配置
services.AddSingleton<IGAgentActorFactory, LocalGAgentActorFactory>();      // 本地开发
services.AddSingleton<IGAgentActorFactory, ProtoActorGAgentActorFactory>(); // 高性能
services.AddSingleton<IGAgentActorFactory, OrleansGAgentActorFactory>();    // 分布式
```

### 核心特性

- ✅ **三种运行时**: Local (进程内) / ProtoActor (高性能) / Orleans (分布式)
- ✅ **事件驱动**: Up/Down/Both 三种传播方向，父子层级自动路由
- ✅ **Protocol Buffers**: 强制类型安全，跨平台序列化
- ✅ **EventSourcing**: 可选的事件溯源支持
- ✅ **AI集成**: Microsoft.Extensions.AI 原生支持
- ✅ **可观测性**: OpenTelemetry + Aspire 集成

---

## ⚠️ 第一规则：Protocol Buffers

> **🔴 所有需要序列化的类型必须使用 Protobuf 定义！**

这是框架的**强制约束**，违反会导致运行时失败。

### ✅ 正确方式

```protobuf
// my_messages.proto
message MyAgentState {
    string id = 1;
    int32 count = 2;
    double balance = 3;  // 注意：decimal 用 double
    google.protobuf.Timestamp updated_at = 4;
}

message MyEvent {
    string event_id = 1;
    string content = 2;
}
```

### ❌ 错误方式

```csharp
// 永远不要手动定义State类！
public class MyAgentState  // 运行时会崩溃
{
    public string Id { get; set; }
    public int Count { get; set; }
}
```

**原因**: Orleans Streaming使用 `byte[]` 传输，ProtoActor需要跨语言，Local需要深度复制。只有Protobuf能保证所有场景都work。

---

## 🚀 快速开始

### 安装

```bash
dotnet add package Aevatar.Agents.Core
dotnet add package Aevatar.Agents.Runtime.Local  # 选择一个Runtime
```

### 30秒示例

```csharp
using Aevatar.Agents.Core;
using Aevatar.Agents.Runtime.Local;

// 1. 定义Proto（my_agent.proto）
// message CounterState { int32 count = 1; }
// message IncrementEvent { int32 amount = 1; }

// 2. 实现Agent
public class CounterAgent : GAgentBase<CounterState>
{
    [EventHandler]
    public async Task HandleIncrement(IncrementEvent evt)
    {
        State.Count += evt.Amount;
        Logger.LogInformation("Count: {Count}", State.Count);
        await Task.CompletedTask;
    }
    
    public override Task<string> GetDescriptionAsync() =>
        Task.FromResult($"Counter: {State.Count}");
}

// 3. 创建和使用
using Aevatar.Agents.Core.Extensions;
using Aevatar.Agents.Core.DependencyInjection;

var services = new ServiceCollection().AddLogging(b => b.AddConsole());

services.AddAevatarAgentSystem(builder =>
{
    builder.UseLocalRuntime();
});

var sp = services.BuildServiceProvider();
var factory = sp.GetRequiredService<IGAgentActorFactory>();

var actor = await factory.CreateGAgentActorAsync<CounterAgent>(Guid.NewGuid());

// 直接访问 Agent 调用方法
var counter = (CounterAgent)actor.GetAgent();

// 或者发布事件
await actor.PublishEventAsync(new EventEnvelope
{
    Id = Guid.NewGuid().ToString(),
    Payload = Any.Pack(new IncrementEvent { Amount = 5 })
});
```

需要自定义持久化？通过 `GAgentOptions` 配置：

```csharp
services.AddAevatarAgentSystem(
    configureStores: options =>
    {
        options.StateStoreType = typeof(MyStateStore<>);         // 开放泛型
        options.EventStoreType = typeof(MyEventStore);           // 具体类型
    },
    configure: builder =>
    {
        builder.UseLocalRuntime();
    });
```

运行 `examples/SimpleDemo/` 查看完整示例。

---

## 📊 运行时对比

| 特性 | Local | ProtoActor | Orleans |
|------|-------|------------|---------|
| **部署** | 进程内 | 单机/集群 | 分布式集群 |
| **启动** | <10ms | ~100ms | ~2s |
| **内存** | 最小 (~50MB) | 中等 (~200MB) | 较大 (~500MB+) |
| **吞吐** | 500K msg/s | 350K msg/s | 80K msg/s |
| **延迟** | <0.1ms | <0.5ms | <2ms |
| **虚拟Actor** | ❌ | 可选 | ✅ |
| **自动故障转移** | ❌ | 需配置 | ✅ |
| **适用场景** | 开发/测试 | 高性能服务 | 分布式系统 |

**建议**:
- 开发阶段用 **Local** (最快反馈)
- 性能要求高用 **ProtoActor** (最高吞吐)
- 分布式部署用 **Orleans** (最强大)

---

## 🤖 AI 能力

框架集成了 **Microsoft.Extensions.AI**，支持 Azure OpenAI 和 OpenAI：

```csharp
using Aevatar.Agents.AI.MEAI;
using Microsoft.Extensions.AI;

public class AIAssistantAgent : MEAIGAgentBase<AIAssistantState>
{
    public override string SystemPrompt => 
        "You are a helpful AI assistant.";

    public AIAssistantAgent(IChatClient chatClient) 
        : base(chatClient) { }

    protected override AevatarAIAgentState GetAIState() => State.AiState;

    public override Task<string> GetDescriptionAsync() =>
        Task.FromResult("AI Assistant Agent");
}

// 配置Azure OpenAI
var config = new MEAIConfiguration
{
    Provider = "azure",
    Endpoint = "https://your-endpoint.openai.azure.com",
    DeploymentName = "gpt-4",
    Temperature = 0.7
};

var agent = new AIAssistantAgent(config);
```

**支持特性**:
- ✅ 对话历史自动管理
- ✅ AI工具调用（Function Calling）
- ✅ 流式响应
- ✅ Token计数和优化

---

## 🏗️ 架构原则

### 清晰分层

```
Application (你的Agent代码)
    ↓
IGAgentActorManager (统一管理接口)
    ↓
IGAgentActor (运行时抽象)
    ↓
┌─────────┬─────────────┬──────────┐
│ Local   │ ProtoActor  │ Orleans  │
│ Actor   │ Actor       │ Grain    │
└─────────┴─────────────┴──────────┘
    ↓
GAgentBase (业务逻辑)
```

**架构简化成果** (2025-11):
- ✅ 移除了冗余的Runtime抽象层（IAgentRuntime/IAgentHost/IAgentInstance）
- ✅ 代码量减少 2,350行
- ✅ 概念更清晰（单一Actor抽象）
- ✅ 性能提升（减少一层包装）

### 事件传播

```
Parent Agent
    │
    ├─→ Child 1 (订阅Parent stream) ←──┐
    ├─→ Child 2 (订阅Parent stream) ←──┤ DOWN事件
    └─→ Child 3 (订阅Parent stream) ←──┘

Child 1 发布UP事件 → Parent Stream → 广播到所有Children
```

**关键概念**:
- **Up**: 子→父stream→所有兄弟
- **Down**: 父→自己stream→所有孩子
- **Both**: 同时Up和Down

---

## 💾 EventSourcing

可选的事件溯源支持，适合金融、医疗等需要完整审计的场景：

```csharp
public class BankAccountAgent : EventSourcedGAgentBase<BankAccountState>
{
    // 业务方法触发事件
    public async Task Credit(double amount)
    {
        RaiseEvent(new AccountCreditedEvent { Amount = amount });
        await ConfirmEventsAsync();  // 持久化
    }

    // 定义状态转换
    protected override void TransitionState(IMessage @event)
    {
        if (@event is AccountCreditedEvent credited)
        {
            State.Balance += credited.Amount;
        }
    }
}
```

**支持的存储**:
- InMemoryEventStore (测试)
- MongoEventRepository (生产)
- 可扩展其他存储

---

## 📦 项目结构

```
src/
├── Aevatar.Agents.Abstractions/           # 核心接口与事件协议
│   ├── IGAgent, IGAgentActor              # Agent与Actor接口
│   ├── IGAgentActorManager                # Actor管理器
│   └── IMessageStream                     # 消息流抽象
│
├── Aevatar.Agents.Core/                   # 基础实现
│   ├── GAgentBase<TState>                 # 带状态的Agent基类
│   ├── GAgentBase<TState, TConfig>        # 带配置的Agent基类
│   ├── EventSourcing/                     # 事件溯源
│   └── DependencyInjection/               # 依赖注入扩展
│
├── Aevatar.Agents.Runtime.Local/          # Local运行时（进程内）
├── Aevatar.Agents.Runtime.Orleans/        # Orleans运行时（分布式）
├── Aevatar.Agents.Runtime.ProtoActor/     # ProtoActor运行时（高性能）
│
├── Aevatar.Agents.AI.Abstractions/        # AI Provider接口
├── Aevatar.Agents.AI.Core/                # AI核心（对话、Embedding、工具调用/MCP）
├── Aevatar.Agents.AI.MEAI/                # Microsoft.Extensions.AI集成
├── Aevatar.Agents.AI.LLMTornado/          # LLMTornado Provider
├── Aevatar.Agents.AI.WithProcessStrategy/ # AI处理策略（CoT、ReAct）
│
├── Aevatar.Agents.CreativeReasoning/      # 创意推理Agent
├── Aevatar.Agents.Maker/                  # MAKER：极致分解的Agent系统
├── Aevatar.Agents.Persistence.MongoDB/    # MongoDB持久化
└── Aevatar.Agents.Plugins.MassTransit/    # MassTransit流插件（Kafka/RabbitMQ）

examples/
├── SimpleDemo/                  # 5分钟入门
├── EventSourcingDemo/           # EventSourcing示例
├── AIAgentWithToolDemo/         # AI工具调用示例
├── AIEventSourcingDemo/         # AI + EventSourcing
├── MCPToolDemo/                 # Model Context Protocol示例
├── CreativeSystem/              # 创意推理Web应用
├── MongoDBEventStoreDemo/       # MongoDB持久化
├── KafkaStreamDemo/             # Kafka流集成
├── Demo.Agents/                 # 各种Agent实现
├── Demo.Api/                    # Web API集成
└── Demo.AppHost/                # Aspire部署

test/
├── Aevatar.Agents.Core.Tests/            # 核心测试
└── Aevatar.Agents.*.Tests/               # 各Runtime测试
```

---

## 📚 文档导航

### 核心文档

| 文档 | 内容 |
|------|------|
| [docs/AEVATAR_FRAMEWORK_GUIDE.md](docs/AEVATAR_FRAMEWORK_GUIDE.md) | **全能指南**：架构、开发、AI、Runtime集成 |
| [docs/ARCHITECTURE_REFERENCE.md](docs/ARCHITECTURE_REFERENCE.md) | 深度架构参考文档 |
| [docs/CONSTITUTION.md](docs/CONSTITUTION.md) | 框架设计哲学宪章 |

**开始阅读**: [docs/AEVATAR_FRAMEWORK_GUIDE.md](docs/AEVATAR_FRAMEWORK_GUIDE.md) - 这是你唯一需要阅读的开发者手册。

---

## 🚀 快速开始

### 安装依赖

```bash
# .NET 10 SDK (必需)
dotnet --version  # 应显示 10.0.x

# 克隆仓库
git clone https://github.com/aevatar/aevatar-agent-framework.git
cd aevatar-agent-framework

# 构建
dotnet build
```

### 运行第一个示例

```bash
cd examples/SimpleDemo
dotnet run
```

你会看到Calculator和Weather两个Agent的互动。

### 尝试EventSourcing

```bash
cd examples/EventSourcingDemo
dotnet run
```

体验事件溯源和状态重建。

### 尝试AI Agent

```bash
cd examples/Demo.Api
# 配置appsettings.json中的OpenAI设置
dotnet run
```

---

## 🎭 典型使用场景

### ✅ 非常适合

- **分布式系统**: 微服务间协作，跨节点通信
- **事件驱动架构**: 原生支持Event Sourcing和CQRS
- **实时应用**: 游戏服务器、聊天系统、实时协作
- **智能代理系统**: AI Agent间协作和任务分配
- **工作流引擎**: 复杂业务流程编排
- **IoT平台**: 设备Agent管理和事件处理

### 需要评估

- **简单CRUD**: 可能过度设计
- **同步调用为主**: 需要思维模式转换
- **极简应用**: 有一定学习曲线

---

## 🛠️ 技术栈

| 组件 | 技术 | 版本 |
|------|------|------|
| **框架** | .NET | 10.0 |
| **序列化** | Google Protobuf | 3.33.0 |
| **Actor** | Proto.Actor | 1.8.0 |
| **分布式** | Microsoft Orleans | 9.2.1 |
| **AI** | Microsoft.Extensions.AI | 10.0.0 |
| **AI Provider** | LLMTornado | 3.8.23 |
| **MCP** | ModelContextProtocol.Core | 0.4.0-preview.3 |
| **消息队列** | MassTransit | 8.3.0 |
| **测试** | xUnit + Moq | 2.9.2 / 4.20.72 |
| **可观测性** | OpenTelemetry + Aspire | 1.10.0 / 9.5.2 |

**中心化包管理**: 使用 `Directory.Packages.props` 统一版本控制

---

## 📈 性能基准

基于.NET 10的性能测试（1000 Agents, 10K events）：

| 指标 | Local | ProtoActor | Orleans |
|------|-------|------------|---------|
| **启动时间** | <10ms | ~120ms | ~2.1s |
| **消息延迟** | <0.1ms | <0.5ms | <2ms |
| **事件吞吐** | 500K/s | 350K/s | 80K/s |
| **内存/Agent** | ~52KB | ~215KB | ~580KB |
| **并发Agents** | 10K+ | 50K+ | 100K+ |

**结论**: Local最快，Orleans最强大，ProtoActor最平衡

---

## 🎯 核心概念速览

### 1. GAgent（智能体）

业务逻辑单元，处理事件和维护状态：

```csharp
public class MyAgent : GAgentBase<MyState>
{
    [EventHandler]
    public async Task HandleSomething(SomeEvent evt)
    {
        // 处理事件，修改State
    }
}
```

### 2. GAgentActor（Actor包装器）

使Agent能在分布式环境运行：

- **LocalGAgentActor**: 进程内
- **OrleansGAgentGrain**: Orleans虚拟Actor
- **ProtoActorGAgentActor**: ProtoActor

### 3. Stream（事件流）

每个Actor有一个Stream用于事件广播：

- 父Agent的Stream → 所有Children订阅
- 事件发布到Stream → 自动广播给订阅者

### 4. EventDirection（传播方向）

- **Up**: 发给父Stream（子→父→所有兄弟）
- **Down**: 发给自己Stream（父→所有孩子）
- **Both**: 同时Up和Down

---

## 💡 最佳实践

### DO ✅

```csharp
// ✅ 使用Protobuf定义所有State和Event
message MyState { string id = 1; }

// ✅ 明确的事件命名（过去式）
message OrderPlacedEvent { }

// ✅ 建立双向父子关系
await ActorHierarchyCoordinator.LinkAsync(parentActor, childActor);
await actorManager.LinkParentChildAsync(parentId, childId);

// ✅ 使用正确的EventDirection
await PublishAsync(reportEvent, EventDirection.Up);  // 子节点报告

// ✅ 管理订阅生命周期
await using var subscription = await stream.SubscribeAsync<MyEvent>(handler);
```

### DON'T ❌

```csharp
// ❌ 手动定义State类
public class MyState { }  // 运行时会失败！

// ❌ 命令式事件命名
message PlaceOrderCommand { }  // 这是Command不是Event

// ❌ 单向建立关系
await child.SetParentAsync(parentId);  // 父不知道子！

// ❌ 错误的EventDirection
await PublishAsync(reportEvent, EventDirection.Down);  // 发给自己的children？

// ❌ 忘记释放订阅
var sub = await stream.SubscribeAsync(...);  // 内存泄漏！
```

---

## 🚦 项目状态

### 当前版本

**版本**: v1.0.0 (.NET 10)  
**状态**: Production Ready ✅

### 功能完整度

- ✅ **核心框架**: 100% 完成
- ✅ **三种Runtime**: 稳定运行
- ✅ **EventSourcing**: 生产可用
- ✅ **AI集成**: Microsoft.Extensions.AI 10.0
- ✅ **工具调用**: Function Calling + MCP协议
- ✅ **MAKER框架**: 极致分解的Agent系统
- ✅ **消息队列**: MassTransit插件（Kafka/RabbitMQ）

### 近期里程碑

- **2025-11-28**: MAKER框架 + MCP协议支持 + 文档更新
- **2025-11-20**: MassTransit Stream插件
- **2025-11-13**: .NET 10升级 + Runtime抽象移除
- **2025-11-11**: Microsoft.Extensions.AI集成

---

## 🤝 贡献

欢迎贡献！请查看 [CONTRIBUTING.md](CONTRIBUTING.md) 了解详情。

### 贡献方式

- 🐛 报告Bug
- 💡 提出新功能建议
- 📝 改进文档
- 🔧 提交PR

### 开发指南

```bash
# 克隆仓库
git clone https://github.com/aevatar/aevatar-agent-framework.git

# 构建
dotnet build

# 运行测试
dotnet test

# 运行示例
cd examples/SimpleDemo && dotnet run
```

---

## 📄 许可证

MIT License - 详见 [LICENSE](LICENSE)

---

## 🙏 致谢

- **Microsoft Orleans** - 虚拟Actor模型先驱
- **Proto.Actor** - 高性能Actor实现
- **Google Protobuf** - 优秀的序列化方案
- **Microsoft.Extensions.AI** - 统一的AI抽象
- **.NET团队** - 强大的平台支持

---

## 🌊 设计哲学

> **"语言是震动的显现，Agent是震动的载体"**
> 
> 我们相信：
> - **简单胜于复杂** - 移除不必要的抽象
> - **抽象源于需求** - 不预设架构
> - **一致性是美德** - Protocol Buffers everywhere
> - **事件即真相** - Event Sourcing as first-class
> - **运行时无关** - Write once, run anywhere

---

**Aevatar Agent Framework** - 让分布式智能体开发回归简洁和本质 🌌

**最近更新**: 2025-11-28 | **.NET 10** | **MAKER框架** | **MCP支持** | **MassTransit插件**
