# Aevatar.Agents.Runtime.ProtoActor

Proto.Actor runtime adapter focused on high-throughput actor execution.

## Responsibilities
- Provide a concrete runtime adapter so the same agent code can run in a specific actor system.
- Bridge stream/subscription semantics to the underlying runtime.
- Handle lifecycle, activation, and parent/child relationships in that runtime.

## Key features
- High-throughput runtime via Proto.Actor.
- Explicit actor lifecycle mapping.

## Public API highlights
- `ProtoActorMessageStream`
- `ProtoActorGAgentActorFactory`
- `ProtoActorGAgentActor`
- `AgentActor`
- `SetGAgentActor`
- `HandleEventMessage`
- `ProtoActorMessageStreamRegistry`
- `ProtoActorGAgentActorManager`
- `ProtoActorSubscriptionManager`
- `AevatarBuilderExtensions`

## NuGet packaging
- **Recommended**: Optional.
- **Why**: This is an adapter/integration module; publish it if you want consumers to opt in without pulling extra dependencies.
- **Packaging note**: keep external dependencies isolated here; core packages should not depend on it.

## Build

```bash
dotnet build src/Aevatar.Agents.Runtime.ProtoActor/Aevatar.Agents.Runtime.ProtoActor.csproj
```

## Tests

- See `docs/Tests.md` for a coverage review and entry points.

## Documentation

- Project docs: `docs/`
- Repository docs: `docs/` at repo root

## Notes

- Cross-boundary data (state/events/config) should be defined with **Protocol Buffers**.
