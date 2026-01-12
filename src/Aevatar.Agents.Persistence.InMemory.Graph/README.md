# Aevatar.Agents.Persistence.InMemory.Graph

In-memory graph persistence implementation for development and tests.

## Responsibilities
- Provide storage adapters for state/memory/graph persistence.
- Hide provider-specific concerns behind framework abstractions.
- Keep external dependencies optional and isolated to this package.

## Key features
- Zero-dependency in-memory graph store.
- Provider-specific adapters isolated per package.
- Configuration-driven wiring via DI.
- Designed to be optional (only include what you need).

## Public API highlights
- `GraphInMemoryServiceCollectionExtensions`

## NuGet packaging
- **Recommended**: Optional.
- **Why**: This is an adapter/integration module; publish it if you want consumers to opt in without pulling extra dependencies.
- **Packaging note**: keep external dependencies isolated here; core packages should not depend on it.

## Build

```bash
dotnet build src/Aevatar.Agents.Persistence.InMemory.Graph/Aevatar.Agents.Persistence.InMemory.Graph.csproj
```

## Tests

- See `docs/Tests.md` for a coverage review and entry points.

## Documentation

- Project docs: `docs/`
- Repository docs: `docs/` at repo root

## Notes

- Cross-boundary data (state/events/config) should be defined with **Protocol Buffers**.
