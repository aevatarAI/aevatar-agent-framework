# Agent Context Design Document

## Overview

Runtime-agnostic request context mechanism for passing contextual data (language, region, user metadata) across agent boundaries without modifying method signatures.

## Architecture

```
┌─────────────────────────────────────────────────────────────┐
│                    Application Layer                         │
│   (Controllers - Set context before agent calls)             │
└─────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────┐
│              Aevatar.Agents.Abstractions                     │
│   - IAgentContext / IAgentContextAccessor                    │
│   - AgentContextKey<T> (type-safe key)                       │
│   - AgentContextPropagationOptions                           │
│   - EventEnvelope.ContextMetadata (proto field)              │
└─────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────┐
│                 Aevatar.Agents.Core                          │
│   - AsyncLocalAgentContext (ConcurrentDictionary)            │
│   - AgentContextPropagator (inject/extract)                  │
│   - AgentContextSerializer (protobuf ContextValue)           │
│   - AgentContextScope (scoped restore)                       │
└─────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────┐
│              Runtime Implementations                         │
│   Local:   AsyncLocalAgentContext directly                   │
│   Orleans: OrleansAgentContextBridge → RequestContext        │
│   Proto:   Falls back to AsyncLocal (future: message headers)│
└─────────────────────────────────────────────────────────────┘
```

## Core API

### Type-Safe Keys

```csharp
// Framework-provided keys
AgentContextKeys.CorrelationId  // AgentContextKey<string>
AgentContextKeys.UserId         // AgentContextKey<string>
AgentContextKeys.Language       // AgentContextKey<string>, default: "en"
AgentContextKeys.IsCN           // AgentContextKey<bool>, default: false

// Custom keys
public static class MyContextKeys
{
    public static readonly AgentContextKey<string> OrderId = new("OrderId");
    // NOTE: 金额类数据建议用最小货币单位(long)跨边界传播，避免 decimal/浮点跨语言语义差异
    public static readonly AgentContextKey<long> AmountCents = new("AmountCents");
}
```

### Context Access

```csharp
// In Controller - Set context
var context = _contextAccessor.GetOrCreate();
context.Set(AgentContextKeys.CorrelationId, Guid.NewGuid().ToString());
context.Set(AgentContextKeys.Language, "zh-CN");
context.Set(MyContextKeys.OrderId, "ORD-12345");

// In Agent - Read context
[EventHandler]
public async Task Handle(MyEvent evt)
{
    var correlationId = Context?.Get(AgentContextKeys.CorrelationId);
    var language = Context?.Get(AgentContextKeys.Language) ?? "en";
    var orderId = Context?.Get(MyContextKeys.OrderId);
    
    // Context auto-propagates when publishing events
    await PublishAsync(new ResponseEvent { ... });
}
```

## Propagation Options

```csharp
public class AgentContextPropagationOptions
{
    public int MaxKeys { get; set; } = 32;
    public int MaxTotalBytes { get; set; } = 8192;
    public HashSet<string>? AllowedKeys { get; set; }  // null = allow all
    public HashSet<string>? DeniedKeys { get; set; }   // takes precedence

    // Fluent API
    public AgentContextPropagationOptions Allow(params string[] keys);
    public AgentContextPropagationOptions Deny(params string[] keys);
}
```

## DI Registration

```csharp
// Local Runtime - default (propagate all keys)
services.AddAevatarAgentSystem(builder =>
{
    builder.UseLocalRuntime();
    builder.AddAgentContext();
});

// Local Runtime - with allowlist
services.AddAevatarAgentSystem(builder =>
{
    builder.UseLocalRuntime();
    builder.AddAgentContext(options => options
        .Allow("CorrelationId", "UserId", "Language"));
});

// Local Runtime - with denylist
services.AddAevatarAgentSystem(builder =>
{
    builder.UseLocalRuntime();
    builder.AddAgentContext(options => options
        .Deny("Password", "SecretToken"));
});

// Orleans Runtime
services.AddAevatarAgentSystem(builder =>
{
    builder.UseOrleansRuntime();
    builder.AddOrleansAgentContext();
});

// Orleans Runtime - with options
services.AddAevatarAgentSystem(builder =>
{
    builder.UseOrleansRuntime();
    builder.AddOrleansAgentContext(options => options
        .Allow("CorrelationId", "Language", "IsCN"));
});
```

## How It Works

### Publish Side (GAgentActorBase)

```csharp
public async Task<string> PublishEventAsync<TEvent>(TEvent evt, ...)
    {
        var envelope = CreateEnvelope(evt, direction);
        
        // Inject current context into envelope
    ContextPropagator?.InjectContext(envelope);

    await PublishToStream(envelope);
}
```

### Receive Side (GAgentBase)

```csharp
protected virtual async Task HandleEventCoreAsync(EventEnvelope envelope, ...)
{
    // Create scoped context (auto-restores previous on dispose)
    using var contextScope = ContextAccessor?.CreateScope(envelope) 
        ?? AgentContextScope.Empty;

    // Context property available during handler execution
    await InvokeHandlers(envelope, ct);
}
```

### Serialization Format

Uses protobuf `ContextValue` oneof for type-safe, extensible serialization:

```protobuf
message ContextValue {
  oneof value {
    string string_value = 1;
    bool bool_value = 2;
    int64 int_value = 3;
    double double_value = 4;
    string datetime_iso = 5;  // ISO8601 format
    string guid_string = 6;   // Standard GUID format
    // Extensible: add new types without breaking compatibility
  }
}

message EventEnvelope {
  // ...
  map<string, ContextValue> context_metadata = 16;
}
```

**Supported Types:**

| CLR Type | Protobuf Field | Notes |
|----------|----------------|-------|
| string | `string_value` | Direct mapping |
| bool | `bool_value` | Direct mapping |
| int/long | `int_value` | int64 |
| float/double | `double_value` | float 会退化为 double |
| DateTime | `datetime_iso` | ISO8601 string |
| Guid | `guid_string` | "D" format |
| Others | `string_value` | ToString() fallback |

> 说明：`ContextValue` 使用 `int64/double` 承载数值，反序列化后可能出现类型退化（例如 `int -> long`、`float -> double`）。
> 框架在 `Get<T>` 时会做 best-effort 转换，但如果你追求“零歧义”，跨边界请优先使用 `long/double/string`。

## Migration from Orleans RequestContext

```csharp
// Before (Orleans-specific)
RequestContext.Set("IsCN", isCN);
var value = RequestContext.Get("IsCN");

// After (Runtime-agnostic, type-safe)
context.Set(AgentContextKeys.IsCN, isCN);
var value = context.Get(AgentContextKeys.IsCN);
```

## Key Features

- ✅ **Runtime Agnostic** - Works across Local, Orleans, ProtoActor
- ✅ **Type Safe** - `AgentContextKey<T>` with compile-time checking
- ✅ **Auto Propagation** - Context flows through `EventEnvelope.ContextMetadata`
- ✅ **Extensible** - Protobuf oneof allows adding types without breaking compatibility
- ✅ **Cross-Language** - Standard protobuf serialization (Java/Go/Python compatible)
- ✅ **Configurable** - Allow/Deny lists, size limits
- ✅ **Scoped** - `AgentContextScope` prevents context leakage
- ✅ **High Performance** - `ConcurrentDictionary`, native protobuf serialization

---

*Last updated: 2025-12 | .NET 10 | Aevatar Agent Framework*
