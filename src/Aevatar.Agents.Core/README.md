# Aevatar.Agents.Core

Core implementations for agents, event handling, routing, and observability built on top of the Abstractions layer.

## Responsibilities
- Implement the base agent programming model on top of Abstractions.
- Provide event handling discovery/execution and routing primitives.
- Offer shared utilities (observability, CQRS/EventSourcing helpers where applicable).

## Key features
- Runtime-agnostic design (multi-runtime).
- Event-driven architecture.
- Protobuf-first for cross-boundary data.

## Public API highlights
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

## GAgentBase (core agent base class)
`GAgentBase` is the canonical programming model for business logic. It runs inside a `GAgentActor` wrapper and focuses on event-driven state changes.

### Variants
- `GAgentBase<TState>`: Base agent with a protobuf state.
- `GAgentBase<TState, TEvent>`: Adds type filtering so only events assignable to `TEvent` are processed (reduces deserialization overhead).
- `GAgentBase<TState, TEvent, TConfiguration>`: Adds protobuf configuration for runtime setup.

### Lifecycle and state
- **State must be protobuf-generated** and is created by the runtime; do not assign `State = new ...`.
- Initialize state in `OnActivateAsync` (call `base` first), cleanup in `OnDeactivateAsync` (call `base` last).
- Provide a **parameterless constructor** for activation.
- Implement `GetDescriptionAsync()` to describe the agent instance.

### Event handling model
- Handlers are discovered via:
  - `[EventHandler]` for specific event types.
  - `[AllEventHandler]` for full `EventEnvelope` processing.
  - Conventions: `HandleAsync` / `HandleEventAsync` methods.
- Handler signature requirements:
  - `public`/`protected`, return `Task` or `Task<T>`.
  - Single parameter (event message or `EventEnvelope`).
- Execution order can be controlled with handler **Priority** (lower runs first).
- Self-published events are ignored by default; opt in when needed.

### Event publishing
- Use `PublishAsync(evt, EventDirection.Up|Down|Both)` to control propagation:
  - **Up**: to parent stream (sibling broadcast).
  - **Down**: to child streams (hierarchical broadcast).
  - **Both**: bi-directional broadcast.

### Minimal example
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

> Note: `TState`, `TEvent`, and `TConfiguration` must be **Protocol Buffers** types when they cross runtime boundaries.

## NuGet packaging
- **Recommended**: Yes (as an independent NuGet package).
- **Why**: This is a foundational module that downstream systems commonly reference.
- **Packaging note**: keep optional integrations (databases/providers) in separate packages.

## Build

```bash
dotnet build src/Aevatar.Agents.Core/Aevatar.Agents.Core.csproj
```

## Tests

- See `docs/Tests.md` for a coverage review and entry points.

## Documentation

- Project docs: `docs/`
- Repository docs: `docs/` at repo root

## Notes

- Cross-boundary data (state/events/config) should be defined with **Protocol Buffers**.
