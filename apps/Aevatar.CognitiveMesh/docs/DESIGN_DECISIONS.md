# Cognitive Mesh 设计决策记录

> ADR (Architecture Decision Record) 风格的设计决策文档

---

## ADR-001: 策略抽象接口设计

**日期**: 2025-12-03

**状态**: ✅ 已采纳

### 背景

需要统一 MakerSystem 和 CreativeSystem (UoT) 的执行接口，使它们可以在同一框架下运行。

### 决策

采用 `IReasoningStrategy` 接口作为统一抽象：

```csharp
public interface IReasoningStrategy
{
    StrategyKind Kind { get; }
    string DisplayName { get; }
    
    Task<ReasoningResult> ExecuteAsync(
        string problem,
        ReasoningOptions options,
        IProgress<ReasoningProgress>? progress,
        CancellationToken ct);
    
    ValidationResult ValidateOptions(ReasoningOptions options);
}
```

### 理由

1. **单一职责**：每个策略只关注执行逻辑
2. **依赖倒置**：服务层依赖抽象，不依赖具体实现
3. **开闭原则**：添加新策略不需要修改现有代码
4. **可测试性**：可以 mock 策略进行单元测试

### 替代方案

1. **继承层次**：`ReasoningStrategyBase` 基类
   - 否决原因：过于耦合，难以处理策略间的差异

2. **泛型策略**：`IReasoningStrategy<TConfig, TResult>`
   - 否决原因：增加复杂度，前端难以统一处理

### 后果

- ✅ 策略可以独立开发和测试
- ✅ 前端可以统一处理不同策略的进度
- ⚠️ 需要在 `ReasoningProgress` 中包含所有策略的字段

---

## ADR-002: 统一进度模型设计

**日期**: 2025-12-03

**状态**: ✅ 已采纳

### 背景

MAKER 和 UoT 有不同的进度报告需求：
- MAKER：Depth、Voting、Proposal、StreamingToken
- UoT：AnalogiesFound、ThoughtsExtracted、CandidatesGenerated

### 决策

采用单一 `ReasoningProgress` record，包含所有策略的字段（可空）：

```csharp
public sealed record ReasoningProgress
{
    // 通用
    public required string Phase { get; init; }
    public float ProgressPercent { get; init; }
    public string? Message { get; init; }
    
    // MAKER 特有
    public int? Depth { get; init; }
    public VotingProgress? Voting { get; init; }
    
    // UoT 特有
    public int? AnalogiesFound { get; init; }
    public int? CandidatesGenerated { get; init; }
}
```

### 理由

1. **简单性**：前端只需处理一种类型
2. **可扩展性**：添加新字段不破坏现有代码
3. **类型安全**：编译时检查字段名

### 替代方案

1. **多态进度**：`MakerProgress`、`UoTProgress` 继承 `ProgressBase`
   - 否决原因：JSON 反序列化复杂，前端需要 switch 处理

2. **字典式进度**：`Dictionary<string, object>`
   - 否决原因：失去类型安全，容易出错

### 后果

- ✅ 前端统一处理进度事件
- ✅ 添加新策略只需添加新字段
- ⚠️ Progress 类型会随策略增多而增大

---

## ADR-003: SSE 事件多态序列化

**日期**: 2025-12-03

**状态**: ✅ 已采纳

### 背景

SSE 事件流中需要发送不同类型的事件（Progress、Voting、Proposal 等），这些事件继承自 `MeshEvent` 基类。

### 问题

```csharp
// 默认序列化只包含基类字段！
JsonSerializer.Serialize(evt, jsonOptions);
// 输出: {"type":"progress","runId":"..."}
// 丢失: phase, message, depth 等派生类字段
```

### 决策

使用运行时类型进行序列化：

```csharp
JsonSerializer.Serialize(evt, evt.GetType(), jsonOptions);
// 输出: {"type":"progress","runId":"...","phase":"Decomposing","message":"..."}
```

### 理由

1. **完整性**：确保所有字段被序列化
2. **兼容性**：不需要修改事件类定义
3. **简单性**：单行修改解决问题

### 替代方案

1. **JsonDerivedType 特性**：在基类标注所有派生类
   - 否决原因：需要维护特性列表，新增事件容易遗漏

2. **自定义转换器**：实现 `JsonConverter<MeshEvent>`
   - 否决原因：过于复杂，增加维护成本

### 后果

- ✅ 所有事件字段正确序列化
- ✅ 无需修改事件类
- ⚠️ 每次序列化都需要获取运行时类型

---

## ADR-004: 动态内容加载

**日期**: 2025-12-03

**状态**: ✅ 已采纳

### 背景

论文审稿项目需要论文内容，但内容太大不适合硬编码在项目配置中。

### 问题

```csharp
// 内置项目定义
Task = "Review and improve the academic paper for publication quality."
// LLM 收到的只有这句话，没有论文内容！
```

### 决策

在执行时根据项目 ID 动态加载内容：

```csharp
if (project.Id == "paper-review")
{
    var paperPath = Path.Combine(..., "articles", "xxx.md");
    if (File.Exists(paperPath))
    {
        var paperContent = await File.ReadAllTextAsync(paperPath, ct);
        task = $"""
            Review and improve the following academic paper...
            
            {paperContent}
            """;
    }
}
```

### 理由

1. **分离关注点**：项目配置与内容分离
2. **可维护性**：更新论文不需要修改代码
3. **灵活性**：可以支持多种内容来源

### 替代方案

1. **Context 字段**：论文内容放在 `Options.Context["paper"]`
   - 否决原因：Context 设计用于小型键值对，不适合大文本

2. **外部 API**：从文件服务加载
   - 否决原因：增加外部依赖，原型阶段过于复杂

### 后果

- ✅ 论文内容正确传递给 LLM
- ⚠️ 目前是硬编码的特殊处理，未来需要通用化
- 📋 计划：支持用户上传内容

---

## ADR-005: 策略注册机制

**日期**: 2025-12-03

**状态**: ✅ 已采纳

### 背景

需要在运行时查找和调用不同的策略实现。

### 决策

使用 `StrategyRegistry` 封装 DI 注入的策略集合（框架层：`src/Aevatar.Agents.Cognitive.Core/Strategies/StrategyRegistry.cs`）：

```csharp
public class StrategyRegistry
{
    private readonly Dictionary<StrategyKind, IReasoningStrategy> _strategies;
    
    public StrategyRegistry(IEnumerable<IReasoningStrategy> strategies)
    {
        _strategies = strategies.ToDictionary(s => s.Kind);
    }
    
    public IReasoningStrategy? Get(StrategyKind kind) => 
        _strategies.GetValueOrDefault(kind);
}
```

DI 注册：

```csharp
services.AddSingleton<IReasoningStrategy, MakerStrategy>();
services.AddSingleton<IReasoningStrategy, UoTCombinationalStrategy>();
services.AddSingleton<StrategyRegistry>();
```

### 理由

1. **自动发现**：新策略只需注册 DI，Registry 自动收集
2. **类型安全**：按 `StrategyKind` 枚举查找
3. **单例生命周期**：策略无状态，可以复用

### 替代方案

1. **工厂模式**：`IReasoningStrategyFactory.Create(kind)`
   - 否决原因：增加抽象层，策略是无状态的不需要工厂

2. **反射发现**：扫描程序集查找实现类
   - 否决原因：隐式依赖，难以调试

### 后果

- ✅ 添加策略只需注册 DI
- ✅ 编译时检查策略类型
- ⚠️ 策略必须实现为单例

---

## ADR-006: 配置工厂方法

**日期**: 2025-12-03

**状态**: ✅ 已采纳

### 背景

`ReasoningOptions` 有很多字段，直接构造容易出错。

### 决策

提供策略特定的工厂方法：

```csharp
public sealed record ReasoningOptions
{
    // 工厂方法
    public static ReasoningOptions ForMaker(
        MakerReliability reliability = MakerReliability.Medium,
        int maxLlmCalls = 500,
        string providerName = "deepseek") => new()
    {
        ProviderName = providerName,
        MakerReliability = reliability,
        MakerConsensusK = reliability switch { ... },
        MaxLlmCalls = maxLlmCalls
    };
    
    public static ReasoningOptions ForUotCombinational(
        string? domainHint = null,
        int maxAnalogies = 5,
        string providerName = "claude") => new()
    {
        ProviderName = providerName,
        UotDomainHint = domainHint,
        UotMaxAnalogies = maxAnalogies
    };
}
```

### 理由

1. **引导正确使用**：工厂方法设置合理默认值
2. **隐藏复杂性**：用户不需要了解所有字段
3. **防止错误**：如 Reliability 自动计算 ConsensusK

### 后果

- ✅ 简化配置创建
- ✅ 默认 Provider 避免空值错误
- ⚠️ 每种策略需要维护一个工厂方法

---

## ADR-007: 前端 UI 架构

**日期**: 2025-12-03

**状态**: ✅ 已采纳

### 背景

需要一个统一的前端来展示不同策略的执行过程。

### 决策

采用原生 HTML/CSS/JS，无框架：

```
wwwroot/
├── index.html    # 单页 HTML
├── styles.css    # 样式
└── app.js        # 逻辑
```

### 理由

1. **简单性**：原型阶段不需要复杂框架
2. **快速迭代**：修改即时生效
3. **无构建步骤**：直接运行
4. **学习成本低**：任何人都能修改

### 替代方案

1. **React/Vue/Svelte**：现代前端框架
   - 否决原因：增加构建复杂度，原型阶段不必要

2. **Blazor**：C# 全栈
   - 否决原因：WASM 加载慢，调试体验差

### 后果

- ✅ 零配置启动
- ✅ 快速原型迭代
- ⚠️ 代码量增大后可能需要重构
- 📋 计划：稳定后考虑迁移到 Svelte

---

## ADR-008: SSE vs WebSocket

**日期**: 2025-12-03

**状态**: ✅ 已采纳

### 背景

需要实时推送执行进度到前端。

### 决策

使用 Server-Sent Events (SSE)：

```csharp
app.MapGet("/api/projects/{id}/events", async (ctx, ...) =>
{
    ctx.Response.Headers.ContentType = "text/event-stream";
    await foreach (var evt in svc.GetEventStreamAsync(id, ct))
    {
        await ctx.Response.WriteAsync($"data: {json}\n\n");
    }
});
```

### 理由

1. **简单性**：HTTP 原生支持，无需额外协议
2. **单向足够**：只需服务器 → 客户端推送
3. **自动重连**：浏览器 `EventSource` 内置重连
4. **调试友好**：可以用 curl 测试

### 替代方案

1. **WebSocket**：双向通信
   - 否决原因：目前不需要客户端 → 服务器的实时消息

2. **轮询**：定时 GET 请求
   - 否决原因：延迟高，浪费带宽

### 后果

- ✅ 实现简单
- ✅ 浏览器原生支持
- ⚠️ 未来需要干预命令时可能需要 WebSocket

---

## 待决策

### TBD-001: 持久化存储方案

需要决定运行历史、项目配置的持久化方案：
- SQLite
- MongoDB
- 文件系统

### TBD-002: 用户认证方案

需要决定多用户支持的认证方案：
- API Key
- OAuth
- JWT

### TBD-003: 策略组合编排

需要决定如何支持多策略组合：
- 声明式 DSL
- 代码编排
- 图形化编辑器

---

*Last Updated: 2025-12-03*

