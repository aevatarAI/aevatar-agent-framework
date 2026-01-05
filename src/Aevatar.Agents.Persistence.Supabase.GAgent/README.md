# Aevatar.Agents.Persistence.Supabase.GAgent

Supabase persistence adapters (state and/or AI memory) for Aevatar.

## Responsibilities
- Provide storage adapters for state/memory/graph persistence.
- Hide provider-specific concerns behind framework abstractions.
- Keep external dependencies optional and isolated to this package.

## Key features
- Supabase/Postgres-backed implementations for state/memory.
- Provider-specific adapters isolated per package.
- Configuration-driven wiring via DI.
- Designed to be optional (only include what you need).

## Public API highlights
- `SupabasePersistenceOptions`
- `SupabaseSchemaScript`
- `SupabaseEventRouterStore`
- `SupabaseEventRouterStoreFactory`
- `SupabaseConfigStore`
- `SupabaseConfigurationStoreFactory`
- `SupabaseStateStore`
- `SupabaseStateStoreFactory`
- `SupabaseGAgentServiceCollectionExtensions`

## NuGet packaging
- **Recommended**: Optional.
- **Why**: This is an adapter/integration module; publish it if you want consumers to opt in without pulling extra dependencies.
- **Packaging note**: keep external dependencies isolated here; core packages should not depend on it.

## Build

```bash
dotnet build src/Aevatar.Agents.Persistence.Supabase.GAgent/Aevatar.Agents.Persistence.Supabase.GAgent.csproj
```

## Tests

- See `docs/Tests.md` for a coverage review and entry points.

## Documentation

- Project docs: `docs/`
- Repository docs: `docs/` at repo root

## Notes

- Cross-boundary data (state/events/config) should be defined with **Protocol Buffers**.
