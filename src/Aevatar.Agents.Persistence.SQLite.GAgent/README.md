# Aevatar.Agents.Persistence.SQLite.GAgent

SQLite persistence adapters (state/config/event router) for Aevatar GAgent.

## Responsibilities
- Provide storage adapters for state/config/event routing persistence.
- Hide provider-specific concerns behind framework abstractions.
- Keep external dependencies optional and isolated to this package.

## Key features
- SQLite-backed implementations for state/config/event routing.
- Provider-specific adapters isolated per package.
- Configuration-driven wiring via DI.
- Designed to be optional (only include what you need).

## Public API highlights
- `SQLiteStateStore`
- `SQLiteConfigStore`
- `SQLiteEventRouterStore`
- `SQLiteGAgentServiceCollectionExtensions`

## Build

```bash
dotnet build src/Aevatar.Agents.Persistence.SQLite.GAgent/Aevatar.Agents.Persistence.SQLite.GAgent.csproj
```

## Documentation

- Project docs: `docs/`
- Repository docs: `docs/` at repo root

## Notes

- Cross-boundary data (state/events/config) should be defined with **Protocol Buffers**.
