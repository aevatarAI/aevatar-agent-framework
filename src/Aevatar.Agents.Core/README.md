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
