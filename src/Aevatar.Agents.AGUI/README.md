# Aevatar.Agents.AGUI

AG-UI (Agent UI) 事件模型与“消息快照”工具。

## 包含内容

- **`AgUiEvents.cs`**：AG-UI 事件类型（RUN/STEP/TEXT/STATE/MESSAGES/CUSTOM）
- **`AgUiBootstrap.cs`**：从多个 Actor 的 `AevatarAIAgentState.History`（可选 AIMemory）收集 **assistant 完整消息快照**

## API

```csharp
public sealed record AgUiActor
{
    public required string ActorId { get; init; }
    public string? ActorTypeName { get; init; } // optional, for Orleans style id
    public required string LaneId { get; init; } // UI grouping key
    public Func<IGAgentActorManager, CancellationToken, Task<IGAgentActor?>>? CreateAsync { get; init; }
}

public sealed record AgUiMessageSnapshotOptions
{
    public required string ThreadId { get; init; }
    public int MaxAssistantMessages { get; init; } = 60;
    public Func<string, IDictionary<string, string>, string, string>? ResolveLaneId { get; init; }
}

public static class AgUiBootstrap
{
    public static Task<IReadOnlyList<AgUiMessage>> CollectAssistantMessagesAsync(
        IGAgentActorManager actorManager,
        IAevatarAIMemoryFactory? memoryFactory,
        IReadOnlyList<AgUiActor> actors,
        AgUiMessageSnapshotOptions options,
        CancellationToken ct = default);
}
```

## 用法示例

```csharp
var actors = new List<AgUiActor>
{
    new()
    {
        ActorId = "raw-id-1",
        ActorTypeName = "MyAgentType",
        LaneId = "lane-0",
        CreateAsync = (mgr, ct) => mgr.CreateAndRegisterAsync<MyAgentType>("raw-id-1", ct)
    }
};

var messages = await AgUiBootstrap.CollectAssistantMessagesAsync(
    actorManager,
    memoryFactory,
    actors,
    new AgUiMessageSnapshotOptions
    {
        ThreadId = sessionId,
        MaxAssistantMessages = 60,
        ResolveLaneId = (stepId, meta, defaultLaneId) => defaultLaneId
    },
    ct);
```

## 相关文档

- `docs/AGUI_INTEGRATION_GUIDE.md`

