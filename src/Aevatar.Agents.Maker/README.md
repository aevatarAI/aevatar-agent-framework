# Aevatar.Agents.Maker

MAKER framework modules for building and orchestrating agents/skills.

## Responsibilities
- Provide MAKER framework building blocks for agent/skill orchestration.
- Expose extension points that integrate with the Aevatar agent runtime.

## Key features
- Skill/tool orchestration primitives.
- Composition patterns for building agent systems.

## Public API highlights
- `MakerServiceCollectionExtensions`
- `TaskCheckpoint`
- `ExecutionCheckpoint`
- `RecoveryResult`
- `ICheckpointStore`
- `InMemoryCheckpointStore`
- `FileCheckpointStore`
- `TaskCheckpointManager`
- `ExecutionMode`
- `ContextIsolationMode`

## NuGet packaging
- **Recommended**: Optional.
- **Why**: This is an adapter/integration module; publish it if you want consumers to opt in without pulling extra dependencies.
- **Packaging note**: keep external dependencies isolated here; core packages should not depend on it.

## Build

```bash
dotnet build src/Aevatar.Agents.Maker/Aevatar.Agents.Maker.csproj
```

## Tests

- See `docs/Tests.md` for a coverage review and entry points.

## Documentation

- Project docs: `docs/`
- Repository docs: `docs/` at repo root

## Notes

- Cross-boundary data (state/events/config) should be defined with **Protocol Buffers**.
