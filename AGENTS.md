# AGENTS.md

Aevatar Agent Framework - 基于 Actor Model 的分布式智能体框架

## 🔴 核心铁律：Protocol Buffers

> **任何跨越边界的类型必须使用 Protobuf 定义！**

### 必须用 Protobuf 的类型
- Agent State (`TState` in `GAgentBase<TState>`)
- Event Messages (任何通过 Stream 传输的消息)
- Event Sourcing Events
- Configuration Objects

### 可以用普通 C# 的类型
- Internal helper classes
- Local-only data structures
- Temporary computation results

### 违规示例 ❌
```csharp
// WRONG - 手动定义 State 类会在运行时崩溃
public class MyAgentState 
{
    public string Name { get; set; }
    public decimal Balance { get; set; } // decimal 无法序列化!
}
```

### 正确示例 ✅
```protobuf
// my_agent.proto
message MyAgentState {
    string name = 1;
    double balance = 2;  // 用 double 替代 decimal
    google.protobuf.Timestamp last_update = 3;
}
```

---

## 构建与测试

### 环境要求
- .NET 10 SDK
- 推荐 macOS/Linux (Windows 需确保 Protobuf 工具链正常)

### 常用命令
```bash
# 构建整个解决方案
dotnet build

# 运行所有测试
dotnet test

# 运行特定项目测试
dotnet test test/Aevatar.Agents.Core.Tests/

# 运行 SimpleDemo
cd examples/SimpleDemo && dotnet run

# 生成 Protobuf 代码 (自动在 build 时执行)
dotnet build src/Aevatar.Agents.Core/
```

### Aspire 应用启动
```bash
# Paper Review 系统
cd apps/Aevatar.PaperReview/PaperReview.AppHost && dotnet run

# Maker 系统
cd apps/Aevatar.MakerSystem/MakerSystem.AppHost && dotnet run

# Cognitive Mesh
cd apps/Aevatar.CognitiveMesh/CognitiveMesh.AppHost && dotnet run
```

---

## 项目结构

```
src/                          # 核心库
├── Aevatar.Agents.Abstractions/    # 接口 & 事件契约
├── Aevatar.Agents.Core/            # 基础实现 & EventSourcing
├── Aevatar.Agents.Runtime.Local/   # 本地运行时 (开发/测试)
├── Aevatar.Agents.Runtime.Orleans/ # Orleans 运行时 (分布式)
├── Aevatar.Agents.Runtime.ProtoActor/ # ProtoActor 运行时 (高性能)
├── Aevatar.Agents.AI.*/            # AI 集成 (MEAI, LLMTornado)
├── Aevatar.Agents.Maker/           # MAKER 框架
├── Aevatar.Agents.Cognitive/       # 认知推理 Agent
├── Aevatar.Agents.Cognitive.Core/  # 认知策略与执行模型
├── Aevatar.Agents.Cognitive.Content/ # 内容加载/项目存储
├── Aevatar.Agents.Cognitive.Streaming/ # 流式广播工具
└── Aevatar.CognitiveMesh.Dsl/      # Cognitive DSL 编译器

agents/                       # 业务 Agent 实现
├── Aevatar.Agents.Chat/
├── Aevatar.Agents.Twitter/
└── Aevatar.Agents.Workflow/

apps/                         # 稳定应用
├── Aevatar.Trading/
├── Aevatar.VibeResearching/
├── Aevatar.AxiomReasoning/
├── Aevatar.CognitiveMesh/
└── Aevatar.PaperReview/

experimental/                 # 规划/开发中应用
├── notebook/
├── learning/
├── novel/
└── aevatar-kit/

examples/                     # 示例项目
├── SimpleDemo/               # 5分钟入门
├── EventSourcingDemo/        # EventSourcing 示例
├── AIAgentWithToolDemo/      # AI Tool Calling
└── Demo.Agents/              # 各类 Agent 实现

test/                         # 测试项目
└── Aevatar.Agents.*.Tests/
```

---

## Agent 开发规范

### 1. 创建新 Agent 的标准流程

**Step 1: 定义 Proto 消息**
```protobuf
// my_agent.proto
syntax = "proto3";
package myagent;

import "google/protobuf/timestamp.proto";

message MyAgentState {
    string id = 1;
    int32 count = 2;
}

message MyEvent {
    string event_id = 1;
    string content = 2;
}
```

**Step 2: 实现 Agent 类**
```csharp
public class MyAgent : GAgentBase<MyAgentState>
{
    // 必须：无参构造函数
    public MyAgent() : base() { }
    
    // 必须：实现描述方法
    public override Task<string> GetDescriptionAsync() =>
        Task.FromResult($"MyAgent: {State.Count}");
    
    // 在 OnActivateAsync 中初始化 State 属性
    public override async Task OnActivateAsync(CancellationToken ct = default)
    {
        await base.OnActivateAsync(ct);
        State.Id = Id.ToString("N")[..8];
        State.Count = 0;
    }
    
    // 事件处理器
    [EventHandler]
    public async Task HandleMyEvent(MyEvent evt)
    {
        State.Count++;
        await PublishAsync(new ResponseEvent { ... });
    }
}
```

### 2. 关键约束

| 约束 | 正确做法 | 错误做法 |
|------|----------|----------|
| 构造函数 | 无参构造函数 | 带参数的构造函数 |
| State 初始化 | 在 `OnActivateAsync` 中修改属性 | 在构造函数中赋值或 `State = new()` |
| 事件处理器返回类型 | `async Task` | `void` 或同步方法 |
| 阻塞操作 | `await Task.Delay()` | `Thread.Sleep()` |

### 3. 事件传播方向

```csharp
// 向上传播：Child → Parent Stream → All Siblings
await PublishAsync(evt, EventDirection.Up);

// 向下传播：Parent → Own Stream → All Children
await PublishAsync(evt, EventDirection.Down);

// 双向传播
await PublishAsync(evt, EventDirection.Both);
```

---

## 代码风格

### 文件规模
- 每文件不超过 800 行
- 每文件夹不超过 8 个文件

### 命名规范
- Agent 类: `XxxAgent` 或 `XxxGAgent`
- State 类: `XxxState` (Proto 生成)
- Event 类: `XxxEvent` (Proto 生成)
- Proto 文件: `snake_case.proto`

### 注释风格
- 中文 + ASCII 分块注释
- 关键业务逻辑必须注释

### 避免的代码坏味道
- 循环依赖
- 数据泥团 (多个数据项总一起出现应组合为对象)
- 超过 3 层缩进
- 超过 3 个 if/else 分支

---

## 运行时选择

| 场景 | 推荐运行时 | 原因 |
|------|-----------|------|
| 开发/测试 | Local | 最快反馈循环，无网络开销 |
| 高性能服务 | ProtoActor | 最高吞吐量 (350K msg/s) |
| 分布式系统 | Orleans | 虚拟 Actor、自动故障转移 |

切换运行时只需一行配置：
```csharp
services.AddAevatarAgentSystem(builder => {
    builder.UseLocalRuntime();     // 或
    builder.UseOrleansRuntime();   // 或
    builder.UseProtoActorRuntime();
});
```

---

## 测试指南

### 测试覆盖要求
- Event Handler 发现与执行
- Parent-Child 关系建立与事件传播
- 跨运行时兼容性

### 测试铁律
- **永远不要删除失败的测试** - 修复编译错误而非删除测试
- 使用 Protobuf 消息进行测试
- 测试 Happy Path 和 Error Path

### 运行测试
```bash
# 所有测试
dotnet test

# 带详细输出
dotnet test --logger "console;verbosity=detailed"

# 特定测试类
dotnet test --filter "FullyQualifiedName~MyAgentTests"
```

---

## 依赖版本管理

所有包版本在 `Directory.Packages.props` 集中管理：
- .NET: 10.0
- Orleans: 9.2.1
- Proto.Actor: 1.8.0
- Google.Protobuf: 3.33.0
- Microsoft.Extensions.AI: 10.0.0

添加新依赖时，先在 `Directory.Packages.props` 中定义版本。

---

## 常见问题排查

### Proto 生成失败
```bash
# 检查 Grpc.Tools 是否正确安装
dotnet restore
dotnet build --no-incremental
```

### Orleans Grain 激活失败
- 确认 State 类型是 Protobuf 生成的
- 检查 Orleans Silo 配置

### 事件未被处理
- 确认 `[EventHandler]` 属性存在
- 检查事件类型匹配
- 验证 Parent-Child 订阅关系

---

## 文档入口

| 文档 | 内容 |
|------|------|
| [docs/AEVATAR_FRAMEWORK_GUIDE.md](docs/AEVATAR_FRAMEWORK_GUIDE.md) | **主指南**: 架构、开发、AI、运行时 |
| [docs/ARCHITECTURE_REFERENCE.md](docs/ARCHITECTURE_REFERENCE.md) | 深度架构参考 |
| [docs/CONSTITUTION.md](docs/CONSTITUTION.md) | 设计哲学 |
| [apps/Aevatar.CognitiveMesh/docs/](apps/Aevatar.CognitiveMesh/docs/) | Cognitive Mesh 系统文档 |

---

## 大型 Monorepo 导航

如果在子项目中工作，优先查看该目录下的 README.md：
- `apps/Aevatar.CognitiveMesh/README.md` - Cognitive Mesh 系统入口
- `apps/Aevatar.MakerSystem/examples/MakerSystem/README.md` - MAKER 框架说明
- `src/Aevatar.Agents.Maker/docs/` - MAKER 详细设计

---

## PR 规范

- 标题格式: `[模块名] 简要描述`
- 运行 `dotnet build && dotnet test` 确保通过
- Proto 文件变更需同时更新相关 Agent 代码
- 架构变更需同步更新 docs/ 目录

---

*最后更新: 2025-12-12 | .NET 10 | Aevatar Agent Framework*

