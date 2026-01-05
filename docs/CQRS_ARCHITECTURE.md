# Aevatar CQRS State Projection Architecture

## 概述

CQRS (Command Query Responsibility Segregation) 状态投影模块，用于将 Agent 状态变化同步到 Elasticsearch 等读模型存储，支持复杂查询。

**核心设计原则：CQRS 与 Stream 实现解耦**

## 架构图

```
┌─────────────────────────────────────────────────────────────────────────┐
│                        Agent Layer                                       │
│  ┌─────────────────────────────────────────────────────────────────┐   │
│  │  GAgentBase<TState>                                              │   │
│  │    │                                                             │   │
│  │    ├── State Change (HandleEventAsync)                           │   │
│  │    │         │                                                   │   │
│  │    │         ▼                                                   │   │
│  │    └── OnStateChangedAsync(state)                                │   │
│  │                  │                                               │   │
│  │                  ▼                                               │   │
│  │         ProjectStateAsync(state)                                 │   │
│  │                  │                                               │   │
│  └──────────────────┼───────────────────────────────────────────────┘   │
│                     │                                                    │
└─────────────────────┼────────────────────────────────────────────────────┘
                      │
                      ▼
┌─────────────────────────────────────────────────────────────────────────┐
│                    CQRS Abstraction Layer                                │
│  ┌─────────────────────────────────────────────────────────────────┐   │
│  │  IStateProjector (统一投影接口)                                   │   │
│  │    - ProjectAsync(StateWrapper wrapper)                          │   │
│  └─────────────────────────────────────────────────────────────────┘   │
│                     │                                                    │
│     ┌───────────────┼───────────────┬───────────────┬───────────┐       │
│     │               │               │               │           │       │
│     ▼               ▼               ▼               ▼           ▼       │
│  ┌──────┐    ┌──────────┐    ┌───────────┐  ┌──────────┐  ┌─────────┐  │
│  │Direct│    │Batched   │    │Stream     │  │Composite │  │Logging  │  │
│  │ES    │    │ES(推荐)  │    │Forwarding │  │Projector │  │Projector│  │
│  └──────┘    └──────────┘    └───────────┘  └──────────┘  └─────────┘  │
│       │           │               │               │                     │
│       └───────────┴───────────────┼───────────────┘                     │
│                                   │                                     │
│                                   ▼ (仅 StreamForwarding 使用)          │
│                   ┌─────────────────────────────────┐                  │
│                   │  IMessageStreamProvider          │ ← 已有框架抽象   │
│                   │  (Silo 配置决定具体实现)          │                  │
│                   └─────────────────────────────────┘                  │
│                                   │                                     │
│                   ┌───────────────┴───────────────┐                    │
│                   ▼                               ▼                    │
│         ┌─────────────────┐            ┌─────────────────────┐        │
│         │LocalMessageStream│           │MassTransitMessage   │         │
│         │(内存/Orleans)    │           │StreamProvider       │         │
│         └─────────────────┘            └─────────────────────┘        │
└─────────────────────────────────────────────────────────────────────────┘
                     │
                     ▼
┌─────────────────────────────────────────────────────────────────────────┐
│                    Elasticsearch Index Layer                             │
│  ┌─────────────────────────────────────────────────────────────────┐   │
│  │  IStateIndexService                                              │   │
│  │    - EnsureIndexExistsAsync (动态创建索引+Mapping)                 │   │
│  │    - IndexStateAsync (写入/更新文档)                               │   │
│  │    - QueryAsync (Lucene 语法查询)                                 │   │
│  │    - GetByIdAsync / CountAsync                                    │   │
│  └─────────────────────────────────────────────────────────────────┘   │
│                                                                          │
│  ┌─────────────────────────────────────────────────────────────────┐   │
│  │  ElasticsearchStateIndexService (实现)                           │   │
│  │    - 动态 Mapping 生成 (基于 Protobuf 消息类型反射)                  │   │
│  │    - ScriptedUpsert 乐观并发控制                                   │   │
│  │    - 批量操作支持                                                  │   │
│  └─────────────────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────────────────┘
```

## 测试验证结果 ✅

| 运行时 | 消息系统 | 状态 | 测试通过 |
|--------|----------|:----:|:--------:|
| Local | In-Memory Stream | ✅ | 2025-11-28 |
| Orleans | Orleans Memory Stream | ✅ | 2025-11-28 |
| MassTransit | Kafka | ✅ | 2025-11-28 |

## 核心组件

### 1. IStateProjector (投影接口)

**位置**: `Aevatar.Agents.Abstractions/CQRS/IStateProjector.cs`

```csharp
public interface IStateProjector
{
    /// <summary>
    /// 投影状态到读模型
    /// </summary>
    Task ProjectAsync(StateWrapper wrapper, CancellationToken ct = default);
}
```

**职责**：
- 接收 StateWrapper（包含序列化的状态数据）
- 将状态投影到目标存储（ES、MongoDB 等）
- 与具体的 Stream 实现无关

### 2. IStateIndexService (ES 索引服务接口)

**位置**: `Aevatar.Agents.Abstractions/CQRS/IStateIndexService.cs`

```csharp
public interface IStateIndexService
{
    // ============ 写入操作 ============
    Task EnsureIndexExistsAsync(string agentType, Type? stateType = null, CancellationToken ct = default);
    Task IndexStateAsync(StateIndexDocument document, CancellationToken ct = default);
    Task IndexStateBatchAsync(IEnumerable<StateIndexDocument> documents, CancellationToken ct = default);
    Task DeleteStateAsync(string agentType, string agentId, CancellationToken ct = default);
    
    // ============ 查询操作 ============
    Task<StateQueryResult?> GetByIdAsync(string agentType, string agentId, CancellationToken ct = default);
    Task<PagedStateQueryResult> QueryAsync(StateQuery query, CancellationToken ct = default);
    Task<long> CountAsync(string agentType, string? queryString = null, CancellationToken ct = default);
}
```

### 3. StateWrapper (状态包装器)

**位置**: `Aevatar.Agents.Abstractions/abstrations_messages.proto`

```protobuf
message StateWrapper {
  string agent_id = 1;           // Agent ID
  string agent_type = 2;         // Agent 类型全名
  google.protobuf.Any state_data = 3;  // Protobuf Any 包装的状态
  int64 version = 4;             // 版本号（使用 DateTime.UtcNow.Ticks）
  google.protobuf.Timestamp published_at = 5;  // 发布时间
  map<string, string> metadata = 6;  // 扩展元数据
}
```

## 投影器实现

### 1. ElasticsearchStateProjector (直接投影) ✅ 已实现

直接将状态写入 Elasticsearch，不经过 Stream。

**位置**: `Aevatar.Agents.Core/CQRS/ElasticsearchStateProjector.cs`

```csharp
public class ElasticsearchStateProjector : IStateProjector
{
    private readonly IStateIndexService _indexService;
    
    public async Task ProjectAsync(StateWrapper wrapper, CancellationToken ct = default)
    {
        // 1. 解包 Protobuf Any 获取原始状态
        var stateType = ResolveStateType(wrapper.AgentType, wrapper.StateData);
        var state = UnpackState(wrapper.StateData, stateType);
        
        // 2. 确保索引存在（动态创建 Mapping）
        await _indexService.EnsureIndexExistsAsync(wrapper.AgentType, stateType, ct);
        
        // 3. 转换为索引文档
        var document = ConvertToIndexDocument(wrapper, state, stateType);
        
        // 4. 索引到 ES
        await _indexService.IndexStateAsync(document, ct);
    }
}
```

### 2. StreamForwardingProjector (Stream 转发) ✅ 已实现

**位置**: `Aevatar.Agents.Core/CQRS/StreamForwardingProjector.cs`

将状态变化转发到 Stream，由消费者处理。使用 `IMessageStreamProvider` 抽象，具体实现由 Silo 配置决定。

```csharp
public class StreamForwardingProjector : IStateProjector
{
    private readonly IMessageStreamProvider _streamProvider;
    public const string StateProjectionCategory = "StateProjection";
    
    public async Task ProjectAsync(StateWrapper wrapper, CancellationToken ct = default)
    {
        var agentId = Guid.Parse(wrapper.AgentId);
        var stream = _streamProvider.GetStream(agentId, StateProjectionCategory);
        await stream.ProduceAsync(wrapper, ct);
    }
}
```

### 3. CompositeStateProjector (组合投影) ✅ 已实现

组合多个投影器，同时执行。

```csharp
public class CompositeStateProjector : IStateProjector
{
    private readonly IEnumerable<IStateProjector> _projectors;
    
    public async Task ProjectAsync(StateWrapper wrapper, CancellationToken ct = default)
    {
        foreach (var projector in _projectors)
        {
            await projector.ProjectAsync(wrapper, ct);
        }
    }
}
```

### 4. LoggingStateProjector (日志投影) ✅ 已实现

用于开发调试，仅记录状态变化日志。

```csharp
public class LoggingStateProjector : IStateProjector
{
    public Task ProjectAsync(StateWrapper wrapper, CancellationToken ct = default)
    {
        _logger.LogInformation(
            "[State Projected] AgentId: {AgentId}, Type: {AgentType}, Version: {Version}",
            wrapper.AgentId, wrapper.AgentType, wrapper.Version);
        return Task.CompletedTask;
    }
}
```

### 5. BatchedStateProjector (批量投影) ✅ 已实现

**位置**: `Aevatar.Agents.Core/CQRS/BatchedStateProjector.cs`

批量累积状态变化，定期刷新到 ES，优化高并发场景。

```csharp
public class BatchedStateProjector : IStateProjector, IDisposable
{
    private readonly ConcurrentDictionary<string, StateIndexDocument> _pendingDocuments = new();
    private readonly Timer _flushTimer;
    
    public Task ProjectAsync(StateWrapper wrapper, CancellationToken ct = default)
    {
        // 只保留最新版本
        _pendingDocuments.AddOrUpdate(
            wrapper.AgentId,
            _ => document,
            (_, existing) => document.Version > existing.Version ? document : existing
        );
        
        // 批大小或超时触发刷新
        if (ShouldFlush())
        {
            return FlushInternalAsync(ct);
        }
        return Task.CompletedTask;
    }
}
```

**特性**：
- 只保留每个 Agent 的最新状态版本
- 批大小 / 超时自动触发刷新
- 内存感知的动态批大小
- 指数退避重试
- Dispose 时最终刷新

## 版本控制

使用 `DateTime.UtcNow.Ticks` 作为版本号，确保：
- 每次状态变化都有唯一递增的版本
- 无需依赖 EventStore 配置
- 支持乐观并发控制

```csharp
// GAgentBase.TState.cs
var wrapper = new StateWrapper
{
    AgentId = Id.ToString(),
    AgentType = GetType().FullName ?? GetType().Name,
    StateData = Any.Pack(state),
    Version = DateTime.UtcNow.Ticks,  // 唯一递增版本
    PublishedAt = Timestamp.FromDateTime(DateTime.UtcNow)
};
```

## ScriptedUpsert 乐观并发

使用 ES Script 实现版本检查，只更新版本更高的文档：

```csharp
var script = new Script
{
    Source = @"
        if (ctx.op == 'create' || 
            ctx._source.version == null || 
            params.version >= ctx._source.version) 
        { 
            ctx._source = params.doc; 
        } 
        else 
        { 
            ctx.op = 'noop'; 
        }",
    Params = new Dictionary<string, object>
    {
        ["version"] = document.Version,
        ["doc"] = document
    }
};
```

## 配置示例

### 实际配置（HttpApi.Host）

```csharp
// AgentRuntimeExtensions.cs
private static void RegisterCQRSServices(IServiceCollection services)
{
    var config = services.BuildServiceProvider().GetRequiredService<IConfiguration>();
    var esUrl = config.GetValue<string>("Elasticsearch:Url") ?? "http://localhost:9200";
    var esPrefix = config.GetValue<string>("Elasticsearch:IndexPrefix") ?? "aevatar-state";
    
    // Elasticsearch client
    services.AddSingleton(sp =>
    {
        var settings = new ElasticsearchClientSettings(new Uri(esUrl));
        return new ElasticsearchClient(settings);
    });
    
    // State Index Service
    services.AddSingleton<IStateIndexService>(sp =>
    {
        var client = sp.GetRequiredService<ElasticsearchClient>();
        var logger = sp.GetRequiredService<ILogger<ElasticsearchStateIndexService>>();
        var options = new ElasticsearchOptions { IndexPrefix = esPrefix };
        return new ElasticsearchStateIndexService(client, logger, options);
    });
    
    // State Query Service
    services.AddScoped<IStateQueryService, StateQueryService>();
    
    // State Projector
    services.AddSingleton<IStateProjector>(sp =>
    {
        var indexService = sp.GetRequiredService<IStateIndexService>();
        var logger = sp.GetRequiredService<ILogger<ElasticsearchStateProjector>>();
        return new ElasticsearchStateProjector(indexService, logger);
    });
}
```

### 使用 CQRSOptionsBuilder

```csharp
// 直接投影模式（默认）
services.AddCQRS(options =>
{
    options.UseElasticsearch(es =>
    {
        es.Url = "http://localhost:9200";
        es.IndexPrefix = "aevatar";
    });
    options.UseDirectProjection();
});

// 批量投影模式（高并发场景推荐）
services.AddCQRS(options =>
{
    options.UseElasticsearch(es =>
    {
        es.Url = "http://localhost:9200";
        es.IndexPrefix = "aevatar";
    });
    options.UseBatchedProjection(batch =>
    {
        batch.BatchSize = 15;              // 默认批大小
        batch.BatchTimeoutSeconds = 1;     // 超时强制刷新
        batch.MaxBatchSize = 100;          // 积压时最大批大小
        batch.MinBatchSize = 5;            // 内存压力下最小批大小
        batch.MaxRetryCount = 3;           // 最大重试次数
        batch.RetryBaseDelaySeconds = 2;   // 指数退避基础延迟
        batch.FlushMinPeriodInMs = 1000;   // 定时器周期
    });
});

// 快捷方式
services.AddBatchedElasticsearchCQRS(
    "http://localhost:9200",
    "aevatar-state",
    batch => { batch.BatchSize = 50; });

// 日志模式（开发调试）
services.AddCQRS(options =>
{
    options.UseLoggingProjection();
});

// Stream 转发模式
services.AddCQRS(options =>
{
    options.UseElasticsearch(es => { ... });
    options.UseStreamForwarding();
});

// 组合模式
services.AddCQRS(options =>
{
    options.UseElasticsearch(es => { ... });
    options.UseCompositeProjection(
        typeof(LoggingStateProjector),
        typeof(ElasticsearchStateProjector));
});
```

### appsettings.json

```json
{
  "Elasticsearch": {
    "Url": "http://localhost:9200",
    "IndexPrefix": "aevatar-state"
  }
}
```

## 查询 API

### HTTP Controller

```
GET  /api/states/{agentType}/{agentId}     - 按 ID 查询
POST /api/states/query                      - Lucene 语法查询
GET  /api/states/{agentType}/count          - 计数
```

### 查询示例

```json
// POST /api/states/query
{
  "agentType": "UserAgentState",
  "queryString": "username:alice* AND isActive:true",
  "pageIndex": 0,
  "pageSize": 20,
  "sortFields": ["loginCount:desc", "createdAt:asc"]
}
```

## 动态数据解析

### 基础类型直接存储

```csharp
private static bool IsBasicType(Type type)
{
    var underlyingType = Nullable.GetUnderlyingType(type) ?? type;
    
    if (underlyingType.IsPrimitive) return true;
    
    if (underlyingType == typeof(string) ||
        underlyingType == typeof(DateTime) ||
        underlyingType == typeof(decimal) ||
        underlyingType == typeof(Guid) ||
        underlyingType == typeof(Google.Protobuf.WellKnownTypes.Timestamp))
        return true;
    
    return false;
}
```

### 复杂类型序列化为 JSON

```csharp
if (!IsBasicType(property.PropertyType))
{
    // 使用 System.Text.Json 序列化复杂类型
    data[propertyName] = JsonSerializer.Serialize(value, new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReferenceHandler = ReferenceHandler.IgnoreCycles
    });
}
```

## BatchProjectorOptions 配置

```csharp
public class BatchProjectorOptions
{
    public int BatchSize { get; set; } = 15;              // 默认批大小
    public int BatchTimeoutSeconds { get; set; } = 1;     // 超时强制刷新
    public int MaxBatchSize { get; set; } = 100;          // 积压时最大批大小
    public int MinBatchSize { get; set; } = 5;            // 内存压力下最小批大小
    public long HighMemoryThreshold { get; set; } = 1GB;  // 内存阈值
    public int MaxRetryCount { get; set; } = 3;           // 最大重试次数
    public int RetryBaseDelaySeconds { get; set; } = 2;   // 指数退避基础延迟
    public int MaxRetryDelaySeconds { get; set; } = 30;   // 最大重试延迟
    public int FlushMinPeriodInMs { get; set; } = 1000;   // 定时器周期
}
```

## 文件结构

```
src/
  Aevatar.Agents.Abstractions/
    CQRS/
      IStateProjector.cs              # 投影接口
      IStateIndexService.cs           # 索引服务接口
      IStateQueryService.cs           # 查询门面接口（HTTP/Tools 统一依赖）
    abstrations_messages.proto        # StateWrapper 定义
    
  Aevatar.Agents.Core/
    CQRS/
      StateQueryService.cs            # 查询门面默认实现（基于 IStateIndexService）
      
  Aevatar.Agents.Core/
    GAgentBase.TState.cs              # 状态钩子 OnStateChangedAsync
    Helpers/StateProjectorInjector.cs # 投影器注入

plugins/
  Aevatar.Agents.Plugins.CQRS/
    Elasticsearch/
      ElasticsearchStateProjector.cs      # ES 直接投影
      ElasticsearchStateIndexService.cs   # ES 索引实现
      ElasticsearchOptions.cs             # ES 配置
    Batching/
      BatchedStateProjector.cs            # 批量投影（高并发）
      BatchProjectorOptions.cs            # 批量配置选项
    Forwarding/
      StreamForwardingProjector.cs        # Stream 转发投影
    DependencyInjection/
      CQRSServiceExtensions.cs            # DI 配置扩展（注册 IStateIndexService + IStateQueryService）

apps/Aevatar.App/
  src/Aevatar.App.HttpApi.Host/
    Extensions/AgentRuntimeExtensions.cs # CQRS 配置
  src/Aevatar.App.HttpApi/
    Controllers/StateQueryController.cs # HTTP API
```

## 总结

| 特性 | 说明 | 状态 |
|------|------|:----:|
| **解耦设计** | CQRS 核心与 Stream 实现完全解耦 | ✅ |
| **直接投影** | ElasticsearchStateProjector | ✅ |
| **批量投影** | BatchedStateProjector (高并发) | ✅ |
| **Stream 转发** | StreamForwardingProjector | ✅ |
| **组合投影** | CompositeStateProjector | ✅ |
| **日志投影** | LoggingStateProjector | ✅ |
| **动态 Mapping** | 基于 Protobuf 类型自动生成 | ✅ |
| **乐观并发** | ScriptedUpsert 版本控制 | ✅ |
| **内存感知** | 动态批大小调整 | ✅ |
| **指数退避** | 失败重试策略 | ✅ |
| **Lucene 查询** | 支持 ES Query String 语法 | ✅ |
| **多运行时支持** | Local / Orleans / MassTransit | ✅ |
