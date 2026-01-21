# Aevatar.Agents.Persistence.SQLite.Memory

SQLite-backed AI Memory adapters (IMemoryStore / IMemoryVectorIndex).

## Responsibilities
- Provide SQLite persistence for AI memory store and vector index.
- Hide provider-specific concerns behind framework abstractions.
- Keep external dependencies optional and isolated to this package.

## Key features
- SQLite-backed implementations for IMemoryStore + IMemoryVectorIndex.
- Brute-force cosine similarity for vector search (in-process).
- Configuration-driven wiring via DI.

## Public API highlights
- `SQLiteMemoryStore`
- `SQLiteMemoryVectorIndex`
- `SQLiteMemoryServiceCollectionExtensions`

## Build

```bash
dotnet build src/Aevatar.Agents.Persistence.SQLite.Memory/Aevatar.Agents.Persistence.SQLite.Memory.csproj
```

## Documentation

- Project docs: `docs/`
- Repository docs: `docs/` at repo root

## Notes

- Cross-boundary data (state/events/config) should be defined with **Protocol Buffers**.
