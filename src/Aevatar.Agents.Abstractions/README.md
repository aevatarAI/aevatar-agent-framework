# Aevatar.Agents.Abstractions

Runtime-agnostic contracts (interfaces, attributes, and Protobuf messages) for the Aevatar Agent Framework.

## Responsibilities
- Define core contracts for agents/actors (interfaces, attributes, envelopes).
- Provide Protobuf schemas for cross-boundary data (state, events, configs).
- Keep the surface area runtime-agnostic to support multiple runtimes.

## Key features
- Runtime-agnostic design (multi-runtime).
- Event-driven architecture.
- Protobuf-first for cross-boundary data.

## Public API highlights
- `ResourceContext`
- `ResourceMetadata`
- `StreamingOptions`
- `IStreamNotFoundHandler`
- `IStateGAgent`
- `IGAgentActorManager`
- `ActorHealthStatus`
- `ActorManagerStatistics`
- `IEventDeduplicator`
- `DeduplicationStatistics`

## NuGet packaging
- **Recommended**: Yes (as an independent NuGet package).
- **Why**: This is a foundational module that downstream systems commonly reference.
- **Packaging note**: keep optional integrations (databases/providers) in separate packages.

## Build

```bash
dotnet build src/Aevatar.Agents.Abstractions/Aevatar.Agents.Abstractions.csproj
```

## Tests

- See `docs/Tests.md` for a coverage review and entry points.

## Documentation

- Project docs: `docs/`
- Repository docs: `docs/` at repo root

## Notes

- Cross-boundary data (state/events/config) should be defined with **Protocol Buffers**.
