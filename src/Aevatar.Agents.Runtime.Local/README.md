# Aevatar.Agents.Runtime.Local

Local (in-process) runtime for fast development and unit tests.

## Responsibilities
- Provide a concrete runtime adapter so the same agent code can run in a specific actor system.
- Bridge stream/subscription semantics to the underlying runtime.
- Handle lifecycle, activation, and parent/child relationships in that runtime.

## Key features
- In-memory runtime with minimal overhead.
- Great for development and unit tests.

## Public API highlights
- `LocalGAgentActor`
- `LocalStreamNotFoundHandler`
- `LocalGAgentActorManager`
- `LocalMessageStreamRegistry`
- `LocalMessageStream`
- `LocalGAgentActorFactory`
- `LocalSubscriptionManager`
- `AevatarBuilderExtensions`
- `ServiceCollectionExtensions`

## NuGet packaging
- **Recommended**: Optional.
- **Why**: This is an adapter/integration module; publish it if you want consumers to opt in without pulling extra dependencies.
- **Packaging note**: keep external dependencies isolated here; core packages should not depend on it.

## Build

```bash
dotnet build src/Aevatar.Agents.Runtime.Local/Aevatar.Agents.Runtime.Local.csproj
```

## Tests

- See `docs/Tests.md` for a coverage review and entry points.

## Documentation

- Project docs: `docs/`
- Repository docs: `docs/` at repo root

## Notes

- Cross-boundary data (state/events/config) should be defined with **Protocol Buffers**.
